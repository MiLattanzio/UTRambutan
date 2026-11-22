using Avalonia;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Metadata;

namespace Rambutan.App.Controls;

/// <summary>A persistent phone close action, outside the scrolling dialog body.</summary>
public partial class DialogFrame : UserControl
{
    public static readonly StyledProperty<Control?> BodyProperty =
        AvaloniaProperty.Register<DialogFrame, Control?>(nameof(Body));

    [Content]
    public Control? Body { get => GetValue(BodyProperty); set => SetValue(BodyProperty, value); }

    public DialogFrame() => InitializeComponent();

    private void OnClose(object? sender, RoutedEventArgs e) => (TopLevel.GetTopLevel(this) as Window)?.Close();
}
