using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

public sealed class StubFilePicker(
    string? openResult  = null,
    string? saveResult  = null,
    IReadOnlyList<string>? multiResult = null) : IFilePicker
{
    /// Dialog titles this picker was asked for, in call order. Localisation tests
    /// assert on these — a picker title is user-visible text like any other.
    public List<string> Titles { get; } = [];

    public Task<string?> PickOpenFileAsync(string title, string extension, string extensionDescription)
    {
        Titles.Add(title);
        return Task.FromResult(openResult);
    }

    public Task<string?> PickOpenFileAsync(string title, IReadOnlyList<(string Extension, string Description)> fileTypes)
    {
        Titles.Add(title);
        return Task.FromResult(openResult);
    }

    public Task<string?> PickSaveFileAsync(string title, string suggestedName, string extension, string extensionDescription)
    {
        Titles.Add(title);
        return Task.FromResult(saveResult);
    }

    public Task<string?> PickSaveFileAsync(
        string title,
        string suggestedName,
        IReadOnlyList<(string Extension, string Description)> fileTypes)
    {
        Titles.Add(title);
        return Task.FromResult(saveResult);
    }

    public Task<IReadOnlyList<string>> PickOpenFilesAsync(string title, string extension, string extensionDescription)
    {
        Titles.Add(title);
        return Task.FromResult(multiResult ?? (IReadOnlyList<string>)Array.Empty<string>());
    }
}
