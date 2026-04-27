namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Platform-agnostic folder picker abstraction.
/// WPF: backed by Microsoft.Win32.OpenFolderDialog.
/// Avalonia: backed by IStorageProvider.OpenFolderPickerAsync.
/// </summary>
public interface IFolderPicker
{
    /// <summary>Returns the selected path, or null if the user cancelled.</summary>
    Task<string?> PickFolderAsync(string title);
}
