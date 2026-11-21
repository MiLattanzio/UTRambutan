using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rambutan.App.Services;
using Rambutan.App.ViewModels;
using Rambutan.App.Views;
using UTDotNet.Core;

namespace Rambutan.App.Tests;

public class UbuntuTouchTests
{
    [Theory]
    [InlineData(null, "/home/phablet/.local/share/rambutan.milattanzio")]
    [InlineData("/home/phablet/.local/share", "/home/phablet/.local/share/rambutan.milattanzio")]
    [InlineData("/home/phablet/.local/share/rambutan.milattanzio", "/home/phablet/.local/share/rambutan.milattanzio")]
    [InlineData("relative", "/home/phablet/.local/share/rambutan.milattanzio")]
    public void DataStaysInTheClickNamespaceWithoutDuplicatingIt(string? xdg, string expected) =>
        Assert.Equal(expected, UbuntuTouchPlatform.ResolveDataDirectory(xdg, "/home/phablet"));

    [Fact]
    public void SensitiveAndSearchEditorsKeepTheirNativeHints() => Headless.Run(() =>
    {
        var password = new TextBox { PasswordChar = '●' };
        Assert.Equal(InputType.Password, PhoneInput.RequestFor(password).Normalize().InputType);
        Assert.False(PhoneInput.RequestFor(password).Normalize().AutoCorrect);
        var credential = new TextBox { AcceptsReturn = true };
        TextInputOptions.SetIsSensitive(credential, true);
        Assert.Equal(InputType.Password, PhoneInput.RequestFor(credential).InputType);
        var search = new TextBox();
        PhoneInput.SetType(search, InputType.Search);
        Assert.Equal(InputType.Search, PhoneInput.RequestFor(search).InputType);
        Assert.True(PhoneInput.RequestFor(new TextBox { AcceptsReturn = true }).Multiline);
    });

    [Theory]
    [InlineData(360, 640, 0)]
    [InlineData(408, 865, 300)]
    [InlineData(900, 384, 220)]
    public void SearchRemainsReachableWithPhoneGeometryAndKeyboard(double width, double height, double inset) => Headless.Run(() =>
    {
        ShellResources.Ensure();
        var vm = new MainViewModel();
        vm.Navigation.GoTo(new SearchPageViewModel());
        var window = new PhoneWindow { DataContext = vm, Width = width, Height = height };
        try
        {
            window.Show();
            window.Classes.Set("keyboard", inset > 0);
            ((Control)window.Content!).Margin = new Thickness(0, 0, 0, inset);
            window.UpdateLayout();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var field = window.GetVisualDescendants().OfType<TextBox>().Single(f => f.Name == "SearchBox");
            Assert.True(field.Bounds.Height >= 48);
            var origin = field.TranslatePoint(default, window)!.Value;
            Assert.True(origin.X >= 0 && origin.X + field.Bounds.Width <= width + 1);
            Assert.True(origin.Y >= 0 && origin.Y + field.Bounds.Height <= height - inset + 1);
            // This test runs on a desktop: style opt-in must not disable Avalonia's IME.
            Assert.True(InputMethod.GetIsInputMethodEnabled(field));
            Assert.Equal(InputType.Search, PhoneInput.GetType(field));
        }
        finally { window.Close(); }
    });
}
