using Microsoft.Win32;

namespace DialogEditor.WPF.Services;

public sealed class WpfFolderPicker : IFolderPicker
{
    // ShowDialog must be called on the UI thread; Task.FromResult keeps it synchronous
    public Task<string?> PickFolderAsync(string title)
    {
        var dialog = new OpenFolderDialog { Title = title };
        var result = dialog.ShowDialog() == true ? dialog.FolderName : null;
        return Task.FromResult<string?>(result);
    }
}
