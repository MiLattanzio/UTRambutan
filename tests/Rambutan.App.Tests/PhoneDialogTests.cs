using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rambutan.App.Services;
using Rambutan.App.Views;

namespace Rambutan.App.Tests;

public class PhoneDialogTests
{
    [Theory]
    [InlineData(408, 865, 0)]
    [InlineData(408, 865, 350)]
    [InlineData(320, 568, 280)]
    [InlineData(865, 408, 220)]
    public void NewPlaylistHasReachableInputCancelAndClose(double width, double height, double inset) => Headless.Run(() =>
    {
        ShellResources.Ensure();
        var owner = new Window { Width = width, Height = height };
        var dialog = new TextPromptDialog("New playlist", "");
        PhoneInput.ConfigurePhoneDialog(dialog);
        owner.Show();
        var result = dialog.ShowDialog<string?>(owner);
        try
        {
            ((Control)dialog.Content!).Margin = new Thickness(0, 0, 0, inset);
            Layout(dialog);
            Assert.Equal(owner.ClientSize, dialog.ClientSize);
            var input = dialog.FindControl<TextBox>("InputBox")!;
            Reachable(input, dialog, height - inset);
            foreach (var button in dialog.GetVisualDescendants().OfType<Button>().Where(b => b.IsEffectivelyVisible))
                Reachable(button, dialog, height - inset);
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsCancel);
            Assert.True(input.TranslatePoint(default, dialog)!.Value.Y + input.Bounds.Height <=
                cancel.TranslatePoint(default, dialog)!.Value.Y);
            var close = dialog.GetVisualDescendants().OfType<Button>().Single(b => AutomationProperties.GetName(b) == "Close");
            close.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(result.IsCompletedSuccessfully);
            Assert.Null(result.Result);
        }
        finally { dialog.Close(); owner.Close(); }
    });

    [Fact]
    public void ConfirmationScrollsItsMessageAndTracksOwnerResize() => Headless.Run(() =>
    {
        ShellResources.Ensure();
        var owner = new Window { Width = 408, Height = 865 };
        var dialog = new ConfirmDialog(string.Join('\n', Enumerable.Repeat("A long confirmation message", 80)));
        PhoneInput.ConfigurePhoneDialog(dialog);
        owner.Show();
        var result = dialog.ShowDialog<bool>(owner);
        try
        {
            owner.Width = 320;
            owner.Height = 400;
            Layout(owner);
            Layout(dialog);
            Assert.Equal(owner.ClientSize, dialog.ClientSize);
            var scroll = dialog.GetVisualDescendants().OfType<ScrollViewer>().First();
            Assert.True(scroll.Extent.Height > scroll.Viewport.Height);
            var cancel = dialog.GetVisualDescendants().OfType<Button>().Single(b => b.IsCancel);
            Reachable(cancel, dialog, dialog.ClientSize.Height);
            cancel.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            Assert.True(result.IsCompletedSuccessfully);
            Assert.False(result.Result);
        }
        finally { dialog.Close(); owner.Close(); }
    });

    private static void Layout(Window window)
    {
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
    }
    private static void Reachable(Control control, Window window, double bottom)
    {
        var point = control.TranslatePoint(default, window)!.Value;
        Assert.True(control.Bounds.Height >= 40, $"{control.Name}: height {control.Bounds.Height}");
        Assert.True(point.X >= 0 && point.Y >= 0 &&
            point.X + control.Bounds.Width <= window.ClientSize.Width + 1 &&
            point.Y + control.Bounds.Height <= bottom + 1,
            $"{control.Name}: {point}, {control.Bounds.Size}; viewport {window.ClientSize.Width} × {bottom}");
    }
}
