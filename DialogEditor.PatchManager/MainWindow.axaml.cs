using Avalonia.Controls;
using DialogEditor.PatchManager.Services;
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

    /// Opens the window with a pre-loaded .patchlist file (e.g. from a command-line argument).
    public void LoadPatchList(string path) =>
        ((PatchManagerViewModel)DataContext!).LoadFromFile(path);
}
