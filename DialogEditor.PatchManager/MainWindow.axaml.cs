using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.ViewModels;

namespace DialogEditor.PatchManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new PatchManagerViewModel(
            new AvaloniaFolderPicker(this),
            new AvaloniaFilePicker(this));
        DataContext = vm;
    }

    public void LoadPatchList(string path) =>
        ((PatchManagerViewModel)DataContext!).LoadFromFile(path);

    private void Settings_Click(object? sender, RoutedEventArgs e) =>
        new PatchManagerSettingsWindow().ShowDialog(this);
}
