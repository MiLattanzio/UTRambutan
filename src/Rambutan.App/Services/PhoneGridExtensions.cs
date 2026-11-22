using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace Rambutan.App.Services;

// Grid definitions are CLR collections rather than styled properties. These
// extensions keep both layouts in AXAML, selected by the same app form factor.
public sealed class PhoneRowsExtension : MarkupExtension
{
    public string Phone { get; set; } = "*";
    public string Desktop { get; set; } = "*";
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new RowDefinitions(UbuntuTouchPlatform.UsePhoneLayout ? Phone : Desktop);
}

public sealed class PhoneColumnsExtension : MarkupExtension
{
    public string Phone { get; set; } = "*";
    public string Desktop { get; set; } = "*";
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        new ColumnDefinitions(UbuntuTouchPlatform.UsePhoneLayout ? Phone : Desktop);
}
