using System.Text.Json;
using Rambutan.App.Services;
using Rambutan.App.ViewModels;
using Rambutan.InnerTube;

namespace Rambutan.App.Tests;

public sealed class BrowserCookieSignInTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "rambutan-browser-test-" + Guid.NewGuid());
    private static object Cookie(string domain = ".youtube.com", string name = "SAPISID", string value = "synthetic", string path = "/", long expires = 0) =>
        new { domain, name, value, path, expires };
    private static string Payload(params object[] cookies) => JsonSerializer.Serialize(new { cookies });
    private static InnerTubeSession? Parse(params object[] cookies) => BrowserCookieSignIn.ParseCookies(Payload(cookies), DateTimeOffset.UtcNow);

    [Fact]
    public void ImportsOnlyYouTubeMusicCookies_AndPrefersTheExactHost()
    {
        var session = Parse(Cookie(), Cookie("music.youtube.com", value: "exact"), Cookie("accounts.google.com", "SID", "google-secret"), Cookie(".youtube.com", "SID", "youtube-session"));
        Assert.NotNull(session);
        Assert.Equal("exact", session.Sapisid);
        Assert.Contains("SID=youtube-session", session.CookieHeader);
        Assert.DoesNotContain("google-secret", session.CookieHeader);
        Assert.DoesNotContain("SAPISID=synthetic", session.CookieHeader);
    }

    [Theory]
    [InlineData(".youtube.com.attacker.invalid")]
    [InlineData("notyoutube.com")]
    [InlineData("accounts.google.com")]
    [InlineData("..youtube.com")]
    public void UnrelatedDomainsCannotSupplyAuthentication(string domain) => Assert.Null(Parse(Cookie(domain)));

    [Theory]
    [InlineData("bad; SID=injected")]
    [InlineData("bad\r\nAuthorization: injected")]
    [InlineData("bad\u0000")]
    public void CookieHeaderInjectionIsRejected(string value) => Assert.Null(Parse(Cookie(value: value)));

    [Fact]
    public void ExpiredAndPathSpecificCookiesAreExcluded()
    {
        Assert.Null(Parse(Cookie(expires: DateTimeOffset.UtcNow.AddMinutes(-1).ToUnixTimeSeconds())));
        Assert.Null(Parse(Cookie(path: "/other")));
        Assert.NotNull(Parse(Cookie(name: "__Secure-3PAPISID")));
    }

    [Fact]
    public void OversizedMessagesAreRejected()
    {
        Assert.Throws<InvalidDataException>(() => BrowserCookieSignIn.ParseCookies(new string('x', 65537), DateTimeOffset.UtcNow));
        Assert.Throws<InvalidDataException>(() => Parse(Enumerable.Range(0, 65).Select(_ => Cookie()).ToArray()));
    }

    [Fact]
    public async Task ProcessProtocol_RejectsAnInvalidSessionBeforeAcceptingTheNextOne()
    {
        if (!OperatingSystem.IsLinux()) return;
        var executable = Script($"""
            test "$RAMBUTAN_BROWSER_PARENT_XID" = 1234 || exit 3
            printf '%s\n' '{Payload(Cookie(value: "first"))}'
            read -r answer
            test "$answer" = rejected || exit 3
            printf '%s\n' '{Payload(Cookie(value: "second"))}'
            read -r answer
            test "$answer" = accepted || exit 3
            """);
        var checks = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new BrowserCookieSignIn(executable).SignInAsync((session, _) =>
        {
            checks++;
            return Task.FromResult(new SessionInfo(session.Sapisid == "second" ? SessionState.Valid : SessionState.Expired, "Fixture"));
        }, timeout.Token, (nint)1234);
        Assert.Equal(2, checks);
        Assert.Equal("second", result!.Session.Sapisid);
    }

    [Fact]
    public async Task TransientNetworkFailureCanRetryTheSameCookiesWithoutAnotherLogin()
    {
        if (!OperatingSystem.IsLinux()) return;
        var payload = Payload(Cookie());
        var executable = Script($"""
            printf '%s\n' '{payload}'
            read -r answer
            test "$answer" = offline || exit 3
            printf '%s\n' '{payload}'
            read -r answer
            test "$answer" = accepted || exit 3
            """);
        var checks = 0;
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var result = await new BrowserCookieSignIn(executable).SignInAsync((_, _) =>
            Task.FromResult(new SessionInfo(++checks == 1 ? SessionState.Unreachable : SessionState.Valid, "Fixture")), timeout.Token);
        Assert.Equal(2, checks);
        Assert.Equal("synthetic", result!.Session.Sapisid);
    }

    [Fact]
    public async Task ClosingTheBrowserDoesNotCreateASession()
    {
        if (!OperatingSystem.IsLinux()) return;
        var result = await new BrowserCookieSignIn(Script("exit 0")).SignInAsync((_, _) => throw new InvalidOperationException(), default);
        Assert.Null(result);
    }

    [Fact]
    public async Task CancellingStopsTheBrowserAndDoesNotAcceptPendingVerification()
    {
        if (!OperatingSystem.IsLinux()) return;
        var executable = Script($"printf '%s\\n' '{Payload(Cookie())}'\nread -r answer");
        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new BrowserCookieSignIn(executable).SignInAsync((_, _) =>
        {
            cancel.Cancel();
            return Task.FromResult(new SessionInfo(SessionState.Valid, "Fixture"));
        }, cancel.Token));
    }

    [Fact]
    public async Task DialogAcceptsVerifiedBrowserCookiesWithoutGoogleCookieImport()
    {
        var vm = new SignInDialogViewModel((_, _) => Task.FromResult(new SessionInfo(SessionState.Valid, "Fixture")),
            Task.FromResult(CredentialStorage.PlainFile), async (verify, ct, parent) =>
            {
                Assert.Equal((nint)1234, parent);
                var session = InnerTubeSession.FromCookieString("SAPISID=synthetic")!;
                return new BrowserSignInResult(session, await verify(session, ct));
            });
        Assert.True(vm.SupportsBrowser);
        Assert.False(vm.ManualImportOpen);
        vm.BrowserParentWindow = (nint)1234;
        var accepted = false;
        vm.Succeeded += (session, google, info) =>
        { accepted = true; Assert.Null(google); Assert.Equal("synthetic", session.Sapisid); Assert.Equal(SessionState.Valid, info.State); };
        await vm.BrowserSignInCommand.ExecuteAsync(null);
        Assert.True(accepted);
        Assert.False(vm.IsChecking);
    }

    [Fact]
    public void ManualImportRemainsAvailableWhenTheNativeBrowserIsAbsent()
    {
        var vm = new SignInDialogViewModel((_, _) => throw new InvalidOperationException());
        Assert.False(vm.SupportsBrowser);
        Assert.True(vm.ManualImportOpen);
    }

    private string Script(string body)
    {
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "browser-fixture");
        File.WriteAllText(path, "#!/bin/sh\nset -eu\n" + body + "\n");
        if (OperatingSystem.IsLinux()) File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return path;
    }
    public void Dispose() { if (Directory.Exists(_directory)) Directory.Delete(_directory, true); }
}
