using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

public sealed class StubFolderPicker(string? result = null) : IFolderPicker
{
    /// Dialog titles this picker was asked for, in call order (see StubFilePicker).
    public List<string> Titles { get; } = [];

    public Task<string?> PickFolderAsync(string title)
    {
        Titles.Add(title);
        return Task.FromResult(result);
    }
}
