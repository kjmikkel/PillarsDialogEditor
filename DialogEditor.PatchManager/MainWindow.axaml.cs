using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Patch;
using DialogEditor.ViewModels;

namespace DialogEditor.PatchManager;

public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
        var vm = new PatchManagerViewModel(
            new AvaloniaFolderPicker(this),
            new AvaloniaFilePicker(this))
        {
            Host = PatchManagerHost.Standalone,
        };
        PatchManagerDialogs.Attach(vm, this);
        DataContext = vm;
    }

    public void LoadPatchList(string path) =>
        ((PatchManagerViewModel)DataContext!).LoadFromFile(path);

    private void Settings_Click(object? sender, RoutedEventArgs e) =>
        new PatchManagerSettingsWindow().ShowDialog(this);

    private void About_Click(object? sender, RoutedEventArgs e) =>
        new PatchManagerAboutWindow(new PatchManagerAboutViewModel(AppVersion.Current)).ShowDialog(this);
}
