using System.Globalization;
using UTDotNet.Core;
using UTDotNet.Diagnostics;

namespace Rambutan.App.Services;

/// <summary>Platform decisions are made before Avalonia creates its first screen.</summary>
public static class UbuntuTouchPlatform
{
    public const string PackageName = "rambutan.milattanzio";
    public static bool IsUbuntuTouch { get; } =
        new PlatformDetector().Detect().MobileEnvironment == LinuxMobileEnvironment.UbuntuTouch;
    // Preview changes the layout only; it never enables Maliit on a desktop.
    public static bool UsePhoneLayout => IsUbuntuTouch ||
        Environment.GetEnvironmentVariable("RAMBUTAN_PHONE_PREVIEW") == "1";

    public static void Configure()
    {
        if (!IsUbuntuTouch) return;
        var data = ResolveDataDirectory(Environment.GetEnvironmentVariable("XDG_DATA_HOME"),
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile));
        Environment.SetEnvironmentVariable(AppDataLocation.OverrideVariable,
            Environment.GetEnvironmentVariable(AppDataLocation.OverrideVariable) ?? data);
        Environment.SetEnvironmentVariable(AppDataLocation.ToolsOverrideVariable,
            Environment.GetEnvironmentVariable(AppDataLocation.ToolsOverrideVariable) ?? Path.Combine(AppContext.BaseDirectory, "tools"));

        // Lomiri's inherited .cache/confined TMPDIR is not writable under this
        // Click profile. .NET, yt-dlp's bundled Python and its format checks all
        // need a private writable scratch directory, also on a cold first play.
        var temporary = Path.Combine(
            Environment.GetEnvironmentVariable(AppDataLocation.OverrideVariable)!, "cache", "tmp");
        Directory.CreateDirectory(temporary);
        Environment.SetEnvironmentVariable("TMPDIR", temporary);

        if (Environment.GetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS") is not null ||
            Environment.GetEnvironmentVariable("AVALONIA_GLOBAL_SCALE_FACTOR") is not null) return;
        var grid = Environment.GetEnvironmentVariable("GRID_UNIT_PX");
        if (grid is null)
        {
            var result = new CommandRunner(TimeSpan.FromSeconds(2))
                .RunAsync("device-info", ["get", "GridUnit"], CancellationToken.None).GetAwaiter().GetResult();
            if (result.Succeeded) grid = result.Output.Trim();
        }
        if (PhoneDisplayScale.FromGridUnit(grid, LinuxMobileEnvironment.UbuntuTouch) is { } scale)
            Environment.SetEnvironmentVariable("AVALONIA_SCREEN_SCALE_FACTORS", scale.ToString(CultureInfo.InvariantCulture));
    }

    internal static string ResolveDataDirectory(string? xdgDataHome, string home)
    {
        var root = !string.IsNullOrWhiteSpace(xdgDataHome) && Path.IsPathRooted(xdgDataHome)
            ? xdgDataHome : Path.Combine(home, ".local", "share");
        return Path.GetFileName(Path.TrimEndingDirectorySeparator(root)) == PackageName
            ? root : Path.Combine(root, PackageName);
    }
}
