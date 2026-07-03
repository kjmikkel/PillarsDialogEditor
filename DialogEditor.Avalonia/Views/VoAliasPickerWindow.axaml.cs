using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

/// <summary>
/// Modal picker for the ExternalVO alias UX (Task 7): lets the user browse every
/// conversation/node pair in the loaded game data and reuse an existing recording
/// instead of importing a new one. Hosts a <see cref="VoAliasPickerViewModel"/>
/// (Task 6) and returns the chosen alias via <see cref="ResultAlias"/> after the
/// window closes, mirroring the ShowDialog-then-read-a-property pattern already
/// used by VoImportDialog/GitConflictResolutionWindow in MainWindow.axaml.cs.
/// </summary>
public partial class VoAliasPickerWindow : Window
{
    public string? ResultAlias { get; private set; }

    public VoAliasPickerWindow() => InitializeComponent();   // XAML previewer

    public VoAliasPickerWindow(VoAliasPickerViewModel vm) : this()
        => DataContext = vm;

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        ResultAlias = (DataContext as VoAliasPickerViewModel)?.ResultAlias;
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void Rows_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if ((DataContext as VoAliasPickerViewModel)?.ResultAlias is not null)
            Ok_Click(sender, e);
    }
}
