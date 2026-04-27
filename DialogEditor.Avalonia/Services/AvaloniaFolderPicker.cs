using Avalonia.Controls;
using Avalonia.Platform.Storage;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Services;

public sealed class AvaloniaFolderPicker(TopLevel topLevel) : IFolderPicker
{
    public async Task<string?> PickFolderAsync(string title)
    {
        var results = await topLevel.StorageProvider.OpenFolderPickerAsync(
            new FolderPickerOpenOptions { Title = title, AllowMultiple = false });
        return results.FirstOrDefault()?.Path.LocalPath;
    }
}
