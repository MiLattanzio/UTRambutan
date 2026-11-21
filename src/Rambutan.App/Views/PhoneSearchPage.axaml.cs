using Avalonia.Controls;
using Avalonia.Input;
using Rambutan.App.ViewModels;

namespace Rambutan.App.Views;

public partial class PhoneSearchPage : UserControl
{
    public PhoneSearchPage() => InitializeComponent();
    private void OnSearchKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchPageViewModel vm)
        {
            vm.SearchCommand.Execute(null);
            e.Handled = true;
        }
    }
}
