using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Input.TextInput;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Rambutan.Core.Diagnostics;
using UTDotNet.Avalonia;
using UTDotNet.Core;
using UTDotNet.Diagnostics;
using UTDotNet.UbuntuTouch;

namespace Rambutan.App.Services;

/// <summary>AXAML opt-in; one bridge for the window group, one lease per attached editor.</summary>
public sealed class PhoneInput : AvaloniaObject
{
    public static readonly AttachedProperty<bool> EnabledProperty =
        AvaloniaProperty.RegisterAttached<PhoneInput, TextBox, bool>("Enabled");
    public static readonly AttachedProperty<InputType> TypeProperty =
        AvaloniaProperty.RegisterAttached<PhoneInput, TextBox, InputType>("Type");
    private static readonly Dictionary<TextBox, FieldLifetime> Fields = [];
    private static readonly Dictionary<Window, WindowLifetime> Windows = [];
    private static readonly DiagnosticLog Log = new();
    private static MaliitInputBridge? _bridge;

    static PhoneInput()
    {
        EnabledProperty.Changed.AddClassHandler<TextBox>((field, e) =>
        {
            if (e.NewValue is true)
            {
                field.AttachedToVisualTree += OnAttached;
                field.DetachedFromVisualTree += OnDetached;
                AttachField(field);
            }
            else
            {
                field.AttachedToVisualTree -= OnAttached;
                field.DetachedFromVisualTree -= OnDetached;
                DetachField(field);
            }
        });
        Log.EntryAdded += entry => AppLog.Write("touch/" + entry.Category, entry.Message);
    }

    public static void SetEnabled(TextBox field, bool value) => field.SetValue(EnabledProperty, value);
    public static bool GetEnabled(TextBox field) => field.GetValue(EnabledProperty);
    public static void SetType(TextBox field, InputType value) => field.SetValue(TypeProperty, value);
    public static InputType GetType(TextBox field) => field.GetValue(TypeProperty);

    private static void OnAttached(object? sender, VisualTreeAttachmentEventArgs e) => AttachField((TextBox)sender!);
    private static void OnDetached(object? sender, VisualTreeAttachmentEventArgs e) => DetachField((TextBox)sender!);

    internal static InputRequest RequestFor(TextBox field) => new()
    {
        InputType = field.PasswordChar != '\0' || TextInputOptions.GetIsSensitive(field)
            ? InputType.Password : GetType(field),
        Multiline = field.AcceptsReturn,
        AutoCorrect = !field.AcceptsReturn && GetType(field) == InputType.Text,
        AutoCapitalization = false,
    };

    private static void AttachField(TextBox field)
    {
        if (!UbuntuTouchPlatform.IsUbuntuTouch || Fields.ContainsKey(field) ||
            TopLevel.GetTopLevel(field) is not Window window) return;
        AttachWindow(window);
        var request = RequestFor(field).Normalize();
        Fields.Add(field, new FieldLifetime(field, request, window));
    }

    private static void DetachField(TextBox field)
    {
        if (Fields.Remove(field, out var lifetime)) lifetime.Dispose();
    }

    public static void PrepareDialog(Window window)
    {
        if (!UbuntuTouchPlatform.UsePhoneLayout) return;
        ConfigurePhoneDialog(window);
    }

    internal static void ConfigurePhoneDialog(Window window)
    {
        window.Classes.Add("phone");
        window.SizeToContent = SizeToContent.Manual;
        window.MinWidth = 0;
        window.MinHeight = 0;
        window.WindowDecorations = WindowDecorations.None;
        window.WindowStartupLocation = WindowStartupLocation.Manual;
        // Lomiri does not reliably maximize transient X11 windows. Their actual
        // size and origin must follow the owner's client, in logical units.
        window.WindowState = WindowState.Normal;
        window.Width = 360;
        window.Height = 640;
        Window? owner = null;
        void Fit()
        {
            if (owner is null || owner.ClientSize.Width <= 0 || owner.ClientSize.Height <= 0) return;
            window.MaxWidth = owner.ClientSize.Width;
            window.MaxHeight = owner.ClientSize.Height;
            window.Width = owner.ClientSize.Width;
            window.Height = owner.ClientSize.Height;
            window.Position = owner.PointToScreen(default);
        }
        void OnSize(object? sender, SizeChangedEventArgs args) => Fit();
        void OnPosition(object? sender, PixelPointEventArgs args) => Fit();
        window.Opened += (_, _) =>
        {
            owner = window.Owner as Window;
            if (owner is not null)
            {
                owner.SizeChanged += OnSize;
                owner.PositionChanged += OnPosition;
                Fit();
            }
            AttachWindow(window);
            AppLog.Write("touch/dialog", $"{window.GetType().Name}: client={window.ClientSize}; owner={owner?.ClientSize}; scale={window.RenderScaling}.");
        };
        window.Closed += (_, _) =>
        {
            if (owner is null) return;
            owner.SizeChanged -= OnSize;
            owner.PositionChanged -= OnPosition;
        };
    }

    public static void AttachWindow(Window window)
    {
        if (!UbuntuTouchPlatform.IsUbuntuTouch || Windows.ContainsKey(window)) return;
        if (_bridge is null)
        {
            _bridge = new MaliitInputBridge(Log);
            _bridge.InputAreaChanged += (target, area) =>
            {
                if (Windows.TryGetValue(target, out var lifetime)) lifetime.SetArea(area);
            };
        }
        Windows.Add(window, new WindowLifetime(window));
    }

    public static void DisposeAll()
    {
        foreach (var field in Fields.Values.ToArray()) field.Dispose();
        Fields.Clear();
        foreach (var window in Windows.Values.ToArray()) window.Dispose();
        Windows.Clear();
        _bridge?.Dispose();
        _bridge = null;
    }

    private sealed class FieldLifetime : IDisposable
    {
        private readonly TextBox _field;
        private readonly TextInputAdapter _adapter;
        private readonly IDisposable _registration;
        private readonly PhoneTextMenu _menu;
        private readonly Window _window;
        private TextClipboardBridge? _clipboard;
        public FieldLifetime(TextBox field, InputRequest request, Window window)
        {
            _field = field;
            _window = window;
            _adapter = new(field, field.Name ?? "editor", request, Log);
            _registration = _bridge!.Register(field, request);
            _menu = new(field);
            EnsureClipboard();
            window.Opened += OnOpened;
            field.GotFocus += OnFocus;
            field.LostFocus += OnBlur;
        }
        private void OnFocus(object? sender, FocusChangedEventArgs e)
        {
            RefreshWindowLayout(_window);
            BringFocusedIntoView();
        }
        private void OnBlur(object? sender, RoutedEventArgs e) => RefreshWindowLayout(_window);
        private void OnOpened(object? sender, EventArgs e) => EnsureClipboard();
        private void EnsureClipboard()
        {
            if (_clipboard is null && _window.TryGetPlatformHandle()?.HandleDescriptor == "XID")
                _clipboard = new(_field, Log);
        }
        public void Dispose()
        {
            _field.GotFocus -= OnFocus;
            _field.LostFocus -= OnBlur;
            _window.Opened -= OnOpened;
            _clipboard?.Dispose();
            _menu.Dispose();
            _registration.Dispose();
            _adapter.Dispose();
            RefreshWindowLayout(_window);
        }
    }

    private static void RefreshWindowLayout(Window window) => Dispatcher.UIThread.Post(() =>
    {
        if (Windows.TryGetValue(window, out var lifetime)) lifetime.ApplyInset();
    }, DispatcherPriority.Loaded);

    private static void BringFocusedIntoView() => Dispatcher.UIThread.Post(() =>
    {
        foreach (var field in Fields.Keys)
            if (field.IsFocused && field.IsEffectivelyVisible) field.BringIntoView();
    }, DispatcherPriority.Loaded);

    private sealed class WindowLifetime : IDisposable
    {
        private readonly Window _window;
        private readonly Control? _root;
        private readonly Thickness _margin;
        private InputArea _area;
        private bool _disposed;
        public WindowLifetime(Window window)
        {
            _window = window;
            _root = window.Content as Control;
            _margin = _root?.Margin ?? default;
            window.SizeChanged += OnSize;
            window.PositionChanged += OnPosition;
            window.Closed += OnClosed;
        }
        public void SetArea(InputArea area) { _area = area; ApplyInset(); }
        private void OnSize(object? sender, SizeChangedEventArgs e) => ApplyInset();
        private void OnPosition(object? sender, PixelPointEventArgs e) => ApplyInset();
        public void ApplyInset()
        {
            if (_disposed || _root is null) return;
            // A detached editor may stop receiving Maliit's final empty area.
            // Reserve space only while this window still has a focused editor.
            // Keep the last area so switching fields does not lose the inset.
            var editing = Fields.Keys.Any(field => field.IsFocused && field.IsEffectivelyVisible &&
                TopLevel.GetTopLevel(field) == _window);
            var inset = 0d;
            if (editing && _area.Width > 0 && _area.Height > 0)
            {
                var viewport = new PixelRect(_window.PointToScreen(default),
                    PixelSize.FromSize(_window.ClientSize, _window.RenderScaling));
                inset = MaliitLayout.BottomInset(_area, viewport, _window.RenderScaling);
            }
            _root.Margin = new(_margin.Left, _margin.Top, _margin.Right, _margin.Bottom + inset);
            // A compositor may already have shrunk the client: the inset is then
            // zero, but accessory chrome still needs to yield space to the editor.
            _window.Classes.Set("keyboard", editing && _area.Width > 0 && _area.Height > 0);
            BringFocusedIntoView();
        }
        private void OnClosed(object? sender, EventArgs e)
        {
            foreach (var field in Fields.Keys.Where(f => TopLevel.GetTopLevel(f) == _window).ToArray()) DetachField(field);
            Windows.Remove(_window);
            Dispose();
        }
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _window.SizeChanged -= OnSize;
            _window.PositionChanged -= OnPosition;
            _window.Closed -= OnClosed;
            if (_root is not null) _root.Margin = _margin;
        }
    }
}
