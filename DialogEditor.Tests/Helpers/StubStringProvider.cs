using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

public sealed class StubStringProvider : IStringProvider
{
    public string Get(string key) => key;
}
