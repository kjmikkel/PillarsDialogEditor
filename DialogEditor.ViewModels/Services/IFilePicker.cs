namespace DialogEditor.ViewModels.Services;

public interface IFilePicker
{
    /// <summary>Returns the selected path, or null if the user cancelled.</summary>
    Task<string?> PickOpenFileAsync(string title, string extension, string extensionDescription);

    /// <summary>Returns the chosen save path, or null if the user cancelled.</summary>
    Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension, string extensionDescription);

    /// <summary>Returns selected paths (multi-select), or empty list if cancelled.</summary>
    Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, string extension, string extensionDescription);
}
