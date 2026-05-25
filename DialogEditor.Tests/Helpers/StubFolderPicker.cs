using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

public sealed class StubFolderPicker(string? result = null) : IFolderPicker
{
    public Task<string?> PickFolderAsync(string title) => Task.FromResult(result);
}
