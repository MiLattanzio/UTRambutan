using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Rambutan.App.Services;
using Rambutan.App.ViewModels;
using Rambutan.Core.Diagnostics;
using UTDotNet.Avalonia;

namespace Rambutan.App.Views;

public partial class PhoneWindow : Window
{
    public PhoneWindow()
    {
        InitializeComponent();
        SizeChanged += (_, _) => Classes.Set("compact", ClientSize.Height < 460);
        if (UbuntuTouchPlatform.IsUbuntuTouch) WindowState = WindowState.Maximized;
        Opened += (_, _) =>
        {
            PhoneInput.AttachWindow(this);
            AppLog.Write("touch", $"window={ClientSize}; scale={RenderScaling}; backend={TryGetPlatformHandle()?.HandleDescriptor}; native={UbuntuTouchPlatform.IsUbuntuTouch}");
        };
        DataContextChanged += (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            vm.IsQueueOpen = false;
            vm.IsSidebarOpen = false;
            vm.IsMiniPlayer = false;
            vm.Session.SignInRequested += () => _ = new SignInDialog(vm.Session.CreateSignInDialog()).ShowDialog<bool>(this);
            vm.Actions.PlaylistPicker = options => new PlaylistPickDialog(options).ShowDialog<PlaylistPickResult?>(this);
            vm.Actions.TextPrompt = (title, initial) => new TextPromptDialog(title, initial).ShowDialog<string?>(this);
            vm.Actions.Confirm = message => new ConfirmDialog(message).ShowDialog<bool>(this);
            vm.NowPlaying.Lyrics.OpenEditor = editor => new LyricsEditorDialog(editor).ShowDialog(this);
            vm.Actions.TryCopyToClipboard = async text => Clipboard is { } clipboard &&
                await ClipboardTextWriter.TryWriteAsync(clipboard, text, new UTDotNet.Diagnostics.DiagnosticLog());
            vm.Navigation.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(NavigationService.Current)) vm.IsQueueOpen = false;
            };
        };
        Activated += (_, _) => { if (DataContext is MainViewModel vm) _ = vm.Session.OnActivatedAsync(); };
        SeekSlider.AddHandler(PointerPressedEvent, (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.Player.IsSeeking = true;
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        SeekSlider.AddHandler(PointerReleasedEvent, async (_, _) =>
        {
            if (DataContext is not MainViewModel vm) return;
            try { await vm.Player.SeekToAsync(SeekSlider.Value); }
            finally { vm.Player.IsSeeking = false; }
        }, RoutingStrategies.Bubble, handledEventsToo: true);
        SeekSlider.PointerCaptureLost += (_, _) =>
        {
            if (DataContext is MainViewModel vm) vm.Player.IsSeeking = false;
        };
    }

    private void OnHome(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) { vm.IsQueueOpen = false; vm.NowPlaying.IsOpen = false; vm.Navigation.GoHome(); } }
    private void OnSearch(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) { vm.IsQueueOpen = false; vm.NowPlaying.IsOpen = false; vm.Navigation.GoSearch(); } }
    private void OnLibrary(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) { vm.IsQueueOpen = false; vm.NowPlaying.IsOpen = false; vm.Navigation.GoLibrary(); } }
    private void OnQueue(object? sender, RoutedEventArgs e) { if (DataContext is MainViewModel vm) { vm.NowPlaying.IsOpen = false; vm.IsQueueOpen = !vm.IsQueueOpen; } }
}
