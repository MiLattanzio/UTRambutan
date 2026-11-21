// Adapted from UTDotNet-Bridge, MIT; see licenses/utdotnet.txt.
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;

namespace Rambutan.App.Services;

/// <summary>Keep Avalonia's text menus and selection behavior, with phone-sized targets.</summary>
public sealed class PhoneTextMenu : IDisposable
{
    private readonly TextBox _field;
    private MenuFlyout? _menu;

    public PhoneTextMenu(TextBox field)
    {
        _field = field;
        field.PropertyChanged += OnChanged;
        ObserveMenu();
    }

    private void OnChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    { if (e.Property == Control.ContextFlyoutProperty) ObserveMenu(); }

    private void ObserveMenu()
    {
        if (_menu is not null) _menu.Opened -= OnOpened;
        _menu = _field.ContextFlyout as MenuFlyout;
        if (_menu is not null) _menu.Opened += OnOpened;
    }

    private void OnOpened(object? sender, EventArgs e)
    {
        // Fluent shares flyouts between fields. Only their current target configures them.
        if (_menu?.Popup.PlacementTarget != _field) return;
        _menu.ShowMode = FlyoutShowMode.Transient;
        foreach (var item in _menu.Items.OfType<MenuItem>())
        {
            item.MinHeight = 48; item.MinWidth = 64; item.FontSize = 16;
        }
    }

    public void Dispose()
    {
        _field.PropertyChanged -= OnChanged;
        if (_menu is not null) _menu.Opened -= OnOpened;
        _menu = null;
    }
}
