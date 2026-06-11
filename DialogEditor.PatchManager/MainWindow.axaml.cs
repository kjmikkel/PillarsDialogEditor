using Avalonia.Controls;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
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
        ThemePicker.DataContext = new ThemePickerViewModel(new ThemeApplier());
    }

    public void LoadPatchList(string path) =>
        ((PatchManagerViewModel)DataContext!).LoadFromFile(path);
}
