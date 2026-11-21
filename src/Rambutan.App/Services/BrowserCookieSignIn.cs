using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Rambutan.Core.Diagnostics;
using Rambutan.InnerTube;

namespace Rambutan.App.Services;

public sealed record BrowserSignInResult(InnerTubeSession Session, SessionInfo Info);

/// <summary>A private browser profile, created by this app, passes only YouTube
/// cookies through inherited pipes. Nothing reads Morph's profile or a password.</summary>
public sealed class BrowserCookieSignIn(string executable)
{
    public bool IsAvailable => OperatingSystem.IsLinux() && File.Exists(executable);

    public async Task<BrowserSignInResult?> SignInAsync(
        Func<InnerTubeSession, CancellationToken, Task<SessionInfo>> verify, CancellationToken ct, nint parentWindow = 0)
    {
        var start = new ProcessStartInfo(executable)
        {
            UseShellExecute = false, RedirectStandardInput = true,
            RedirectStandardOutput = true, RedirectStandardError = true,
            StandardOutputEncoding = Encoding.UTF8, StandardErrorEncoding = Encoding.UTF8,
        };
        // The native helper uses the system Qt5 libraries, not the app's FFmpeg
        // library directory; no URL or credential is placed in its command line.
        start.Environment.Remove("LD_LIBRARY_PATH");
        start.Environment.Remove("QT_PLUGIN_PATH");
        start.Environment.Remove("QML2_IMPORT_PATH");
        start.Environment.Remove("QTWEBENGINE_REMOTE_DEBUGGING");
        if (parentWindow != 0)
            start.Environment["RAMBUTAN_BROWSER_PARENT_XID"] = parentWindow.ToString(System.Globalization.CultureInfo.InvariantCulture);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(15));
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start the sign-in browser.");
        AppLog.Write("browser", "Private sign-in browser started.");
        var diagnostics = DiscardDiagnostics(process.StandardError, deadline.Token);
        try
        {
            while (await ReadBoundedLine(process.StandardOutput, deadline.Token) is { } line)
            {
                var candidate = ParseCookies(line, DateTimeOffset.UtcNow);
                if (candidate is null)
                {
                    await process.StandardInput.WriteLineAsync("rejected");
                    await process.StandardInput.FlushAsync(deadline.Token);
                    continue;
                }
                var info = await verify(candidate, deadline.Token);
                AppLog.Write("browser", $"Session verification: {info.State}.");
                deadline.Token.ThrowIfCancellationRequested();
                if (info.State == SessionState.Valid)
                {
                    await process.StandardInput.WriteLineAsync("accepted");
                    await process.StandardInput.FlushAsync(deadline.Token);
                    try { await process.WaitForExitAsync(deadline.Token).WaitAsync(TimeSpan.FromSeconds(3), deadline.Token); }
                    catch (TimeoutException) { Stop(process); }
                    AppLog.Write("browser", "Verified session returned to the sign-in dialog.");
                    return new(candidate, info);
                }
                await process.StandardInput.WriteLineAsync(info.State == SessionState.Unreachable ? "offline" : "rejected");
                await process.StandardInput.FlushAsync(deadline.Token);
            }
            await process.WaitForExitAsync(deadline.Token);
            AppLog.Write("browser", $"Private browser exited: {process.ExitCode}.");
            return process.ExitCode switch
            {
                0 or 1 => null,
                2 => throw new TimeoutException("The sign-in browser expired."),
                _ => throw new InvalidOperationException("The sign-in browser could not be opened."),
            };
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        { throw new TimeoutException("The sign-in browser expired."); }
        finally
        {
            Stop(process);
            await deadline.CancelAsync();
            try { await diagnostics; } catch (OperationCanceledException) { }
        }
    }

    internal static InnerTubeSession? ParseCookies(string json, DateTimeOffset now)
    {
        if (json.Length > 65536) throw new InvalidDataException("Cookie message is too large.");
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { MaxDepth = 8 });
        var cookies = doc.RootElement.GetProperty("cookies");
        if (cookies.ValueKind != JsonValueKind.Array || cookies.GetArrayLength() > 64)
            throw new InvalidDataException("Invalid browser cookie message.");
        var chosen = new Dictionary<string, (string Value, int Rank)>(StringComparer.Ordinal);
        foreach (var cookie in cookies.EnumerateArray())
        {
            var domain = cookie.GetProperty("domain").GetString()?.ToLowerInvariant();
            if (domain?.StartsWith('.') == true) domain = domain[1..];
            if (domain is not ("youtube.com" or "music.youtube.com") || cookie.GetProperty("path").GetString() != "/") continue;
            var expiry = cookie.GetProperty("expires").GetInt64();
            if (expiry != 0 && expiry <= now.ToUnixTimeSeconds()) continue;
            var name = cookie.GetProperty("name").GetString();
            var value = cookie.GetProperty("value").GetString();
            if (string.IsNullOrEmpty(name) || name.Length > 127 || value is null || value.Length > 8192 ||
                name.Any(c => !char.IsAsciiLetterOrDigit(c) && !"!#$%&'*+-.^_`|~".Contains(c)) ||
                value.Any(c => c < 0x21 || c > 0x7e || c is '"' or ',' or ';' or '\\')) continue;
            var rank = domain == "music.youtube.com" ? 1 : 0;
            if (!chosen.TryGetValue(name, out var previous) || rank > previous.Rank) chosen[name] = (value, rank);
        }
        return InnerTubeSession.FromCookieString(string.Join("; ", chosen.Select(p => p.Key + "=" + p.Value.Value)));
    }

    private static async Task<string?> ReadBoundedLine(StreamReader reader, CancellationToken ct)
    {
        var result = new StringBuilder();
        var character = new char[1];
        while (await reader.ReadAsync(character.AsMemory(), ct) != 0)
        {
            if (character[0] == '\n') return result.ToString();
            if (result.Length == 65536) throw new InvalidDataException("Cookie message is too large.");
            result.Append(character[0]);
        }
        if (result.Length != 0) throw new InvalidDataException("Incomplete browser cookie message.");
        return null;
    }
    private static async Task DiscardDiagnostics(StreamReader reader, CancellationToken ct)
    {
        var buffer = new char[2048];
        try { while (await reader.ReadAsync(buffer.AsMemory(), ct) != 0) { } }
        catch (IOException) { }
    }
    private static void Stop(Process process)
    {
        try { if (!process.HasExited) process.Kill(entireProcessTree: true); }
        catch (InvalidOperationException) { }
    }
}
