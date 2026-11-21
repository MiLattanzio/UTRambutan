using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using Rambutan.Core.Diagnostics;

namespace Rambutan.App.Services;

/// <summary>Explicit development probe: renders only this app, never other apps or the compositor.</summary>
internal static class RenderProbe
{
    public static void Attach(Window window)
    {
        if (Environment.GetEnvironmentVariable("RAMBUTAN_RENDER_SNAPSHOT") is not { Length: > 0 } path) return;
        var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        timer.Tick += (_, _) =>
        {
            try
            {
                var pixels = PixelSize.FromSize(window.ClientSize, 1);
                if (pixels.Width <= 0 || pixels.Height <= 0) return;
                using var bitmap = new RenderTargetBitmap(pixels, new Vector(96, 96));
                bitmap.Render(window);
                bitmap.Save(path, PngBitmapEncoderOptions.Default);
            }
            catch (Exception ex) { AppLog.Write("render-probe", ex.GetType().Name); }
        };
        window.Opened += (_, _) => timer.Start();
        window.Closed += (_, _) => timer.Stop();
    }
}
