using Avalonia.Controls;
using Avalonia.Interactivity;
using Rambutan.App.Services;
using Rambutan.Core.Diagnostics;

namespace Rambutan.App.Views;

public partial class FileImportDialog : Window
{
    private readonly CancellationTokenSource _closed = new();

    public FileImportDialog()
    {
        InitializeComponent();
        PhoneInput.PrepareDialog(this);
        Closed += (_, _) => _closed.Cancel();
    }

    public FileImportDialog(string title) : this()
    {
        Title = title;
        Status.Text = title;
        Opened += async (_, _) =>
        {
            try
            {
                var result = await UbuntuTouchFilePicker.PickAsync(this, _closed.Token);
                if (IsVisible) Close(result);
                else result?.Dispose();
            }
            catch (OperationCanceledException) when (_closed.IsCancellationRequested) { }
            catch (Exception ex)
            {
                AppLog.Write("content-hub", $"File selection failed: {ex.GetType().Name}.");
                Status.Text = ex.Message;
            }
        };
    }

    private void OnCancel(object? sender, RoutedEventArgs e) => Close();
}
