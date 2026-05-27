namespace DialogEditor.ViewModels.Services;

public interface IFilePicker
{
    /// <summary>Returns the selected path, or null if the user cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title, string extension, string extensionDescription);

    /// <summary>Returns the chosen save path, or null if the user cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension, string extensionDescription);

    /// <summary>
    /// Returns the chosen save path, or null if the user cancelled.
    /// Allows specifying multiple file types for the save dialog.
    /// </summary>
    Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        IReadOnlyList<(string Extension, string Description)> fileTypes);

    /// <summary>Returns selected paths (multi-select), or empty list if cancelled.</summary>
    Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, string extension, string extensionDescription);
}
