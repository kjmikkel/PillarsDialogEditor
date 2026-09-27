using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using DialogEditor.ViewModels;
using DialogEditor.Core.Logging;
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

    // Thin shell call only — the URL is built (and scrubbed) by the view-model. If the shell
    // refuses the long pre-filled URL, retry with the plain issues list. ExternalLauncher
    // logs each failure via AppLog.Warn.
    private void IssuesLink_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ExceptionReportViewModel vm) return;
        if (!ExternalLauncher.Open(vm.ReportUrl) && vm.ReportUrl != vm.IssuesUrl)
            ExternalLauncher.Open(vm.IssuesUrl);
    }

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
