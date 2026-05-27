using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

public sealed class StubFilePicker(
    string? openResult  = null,
    string? saveResult  = null,
    IReadOnlyList<string>? multiResult = null) : IFilePicker
{
    public Task<string?> PickOpenFileAsync(string title, string extension, string extensionDescription)
        => Task.FromResult(openResult);

    public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<(string Extension, string Description)> fileTypes)
        => Task.FromResult(openResult);

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension, string extensionDescription)
        => Task.FromResult(saveResult);

    public Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        IReadOnlyList<(string Extension, string Description)> fileTypes)
        => Task.FromResult(saveResult);

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, string extension, string extensionDescription)
        => Task.FromResult(multiResult ?? (IReadOnlyList<string>)Array.Empty<string>());
}
