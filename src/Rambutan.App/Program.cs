using Avalonia;
using Avalonia.Media;
using System;
using System.Linq;
using Rambutan.Integrations;

namespace Rambutan.App;

sealed class Program
{
    public static SingleInstanceGuard? Instance { get; private set; }

    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        Services.UbuntuTouchPlatform.Configure();

        // A launch from a rambutan:// link keeps only the link, before
        // anything reads the arguments.
        args = Core.Links.DeepLinkArgs.Sanitize(args);

        // Velopack install/update hooks; exits the process when the installer
        // invoked us, so it must precede the single-instance guard.
        if (!Services.UbuntuTouchPlatform.IsUbuntuTouch) UpdateService.Startup(args);

        // Read before anything else runs: a bad demo setup should stop here, loudly,
        // not start a demo that quietly reaches for the network.
        App.Demo = Services.Demo.DemoOptions.FromEnvironment();

        // One instance per data folder: a demo, or a RAMBUTAN_DATA_DIR run, starts
        // beside the real app. Each profile is a data folder of its own.
        App.Profile = Services.ProfileLaunch.Resolve(
            Services.AppDataLocation.FromEnvironment(App.Demo is not null),
            Services.ProfileLaunch.FindArgument(args));
        App.DataLocation = App.Profile.Location;
        using var instance = Services.UbuntuTouchPlatform.IsUbuntuTouch ? null : SingleInstanceGuard.Acquire(App.DataLocation.InstanceName);
        if (instance is { IsPrimary: false })
        {
            instance.NotifyPrimary(args);
            return;
        }

        Instance = instance;
        // Only the instance that runs it: a plain launch opens it next time.
        App.Profile.Store.SetLastUsed(App.Profile.Profile.Id);
        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        // A profile switch starts the app again once this run has saved everything.
        App.AfterExit?.Invoke();
        // Last of all: the updater kills this process 60 s after it starts, so it
        // starts only once everything is saved. Nothing happens when the line
        // above already installed the update.
        App.Updates?.ApplyOnExit();
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        var builder = AppBuilder.Configure<App>().UsePlatformDetect();
        if (Services.UbuntuTouchPlatform.IsUbuntuTouch)
            builder = builder.With(new X11PlatformOptions { OverlayPopups = true });
        else
        {
#pragma warning disable AVALONIA_X11_CSD
            builder = builder.UseWaylandWithFallback()
                .With(new X11PlatformOptions { EnableDrawnDecorations = true })
                .With(WaylandOptions());
#pragma warning restore AVALONIA_X11_CSD
        }
        return builder.WithInterFont()
            .With(new FontManagerOptions { DefaultFamilyName = DefaultFamily, FontFallbacks = FontFallbacks })
            .LogToTrace();
    }

    internal static WaylandPlatformOptions WaylandOptions() => new()
    {
#pragma warning disable AVALONIA_WAYLAND_FORCE_CSD
        ForceDrawnDecorations = true,
#pragma warning restore AVALONIA_WAYLAND_FORCE_CSD
        // On NVIDIA's driver the default path renders in software.
        UseDmabufSwapchain = Services.WaylandGraphics.DmabufSwapchain(),
        // A dock matches a window to its desktop file by the app id, and COSMIC
        // has no protocol for a window to send its own icon. Unset, Avalonia
        // sends the entry assembly's name, which no desktop file carries.
        AppId = Services.WaylandToplevel.AppId,
    };

    /// <summary>
    /// Figtree (Assets/Fonts, SIL OFL 1.1, OFL.txt beside it) is the UI face: five
    /// static weights, since the variable font leaves weight selection to the
    /// renderer. It covers Latin only, so Inter, which ships with Avalonia, is the
    /// first fallback: Greek, Cyrillic and Vietnamese titles render in Inter rather
    /// than in whatever system font a machine happens to have.
    /// </summary>
    private const string DefaultFamily = "avares://Rambutan.App/Assets/Fonts#Figtree";

    /// <summary>
    /// Where glyphs Figtree and Inter lack come from: CJK and emoji,
    /// in lyrics above all. The chain was proved on Windows and Linux, widened to Korean and Chinese:
    /// Windows' own fonts first, then the Noto families Linux distributions ship
    /// (noto-fonts-cjk, noto-fonts-emoji), then WenQuanYi Zen Hei, a pan-CJK font
    /// common on Debian and Ubuntu. Families a machine doesn't have are skipped,
    /// so one list serves both platforms. Japanese comes before Chinese, so shared
    /// Han characters take Japanese forms: the likelier reading in a music
    /// library, and a choice to revisit if listeners say otherwise.
    /// </summary>
    private static readonly FontFallback[] FontFallbacks =
    [
        new FontFallback { FontFamily = new FontFamily("fonts:Inter#Inter") },
        .. new[]
        {
            // Windows
            "Yu Gothic UI", "Meiryo", "Malgun Gothic", "Microsoft YaHei UI", "Microsoft JhengHei UI",
            "Segoe UI Emoji", "Segoe UI Symbol",
            // Linux
            "Noto Sans CJK JP", "Noto Sans CJK KR", "Noto Sans CJK SC", "Noto Sans CJK TC",
            "WenQuanYi Zen Hei", "Noto Color Emoji",
        }.Select(name => new FontFallback { FontFamily = new FontFamily(name) }),
    ];
}
