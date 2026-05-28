using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class ExportConversationsWindow : Window
{
    public ExportConversationsWindow() => InitializeComponent();

    public ExportConversationsWindow(ExportConversationsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void FormatRadio_Checked(object? sender, RoutedEventArgs e)
    {
        if (sender is RadioButton { Tag: string format } &&
            DataContext is ExportConversationsViewModel vm)
            vm.SelectedFormat = format;
    }

    private void CloseButton_Click(object? sender, RoutedEventArgs e)
        => Close();
}
