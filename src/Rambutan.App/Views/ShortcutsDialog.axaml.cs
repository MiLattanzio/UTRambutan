using Avalonia.Controls;
using Avalonia.Interactivity;

namespace Rambutan.App.Views;

public partial class ShortcutsDialog : Window
{
    public ShortcutsDialog()
    {
        InitializeComponent();
        Rambutan.App.Services.PhoneInput.PrepareDialog(this);
    }

    private void OnClose(object? sender, RoutedEventArgs e) => Close();
}
