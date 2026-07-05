using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class ExceptionReportWindow : Window
{
    public ExceptionReportWindow()
    {
        InitializeComponent();
    }

    public ExceptionReportWindow(ExceptionReportViewModel viewModel) : this()
        => DataContext = viewModel;

    private async void Copy_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ExceptionReportViewModel vm) return;
        var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
        if (clipboard is null) return;
        await clipboard.SetTextAsync(vm.CopyText);
    }

    private void IssuesLink_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ExceptionReportViewModel vm) return;
        try { System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(vm.IssuesUrl) { UseShellExecute = true }); }
        catch (Exception ex) { AppLog.Warn($"ExceptionReportWindow: could not open issues link — {ex.Message}"); }
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
