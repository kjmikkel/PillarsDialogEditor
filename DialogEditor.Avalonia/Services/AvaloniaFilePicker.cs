using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Services;

public sealed class AvaloniaFilePicker(TopLevel topLevel) : IFilePicker
{
    public async Task<string?> PickOpenFileAsync(
        string title, string extension, string extensionDescription)
    {
        var results = await topLevel.StorageProvider.OpenFilePickerAsync(
            new FilePickerOpenOptions
            {
                Title         = title,
                AllowMultiple = false,
                FileTypeFilter =
                [
                    new FilePickerFileType(extensionDescription)
                        { Patterns = [$"*{extension}"] },
                    FilePickerFileTypes.All,
                ],
            });
        return results.FirstOrDefault()?.Path.LocalPath;
    }

    public async Task<string?> PickSaveFileAsync(
        string title, string suggestedName, string extension, string extensionDescription)
    {
        var result = await topLevel.StorageProvider.SaveFilePickerAsync(
            new FilePickerSaveOptions
            {
                Title               = title,
                SuggestedFileName   = suggestedName,
                DefaultExtension    = extension.TrimStart('.'),
                FileTypeChoices =
                [
                    new FilePickerFileType(extensionDescription)
                        { Patterns = [$"*{extension}"] },
                ],
            });
        return result?.Path.LocalPath;
    }
}
