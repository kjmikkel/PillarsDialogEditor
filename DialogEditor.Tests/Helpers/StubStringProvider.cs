using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

public sealed class StubStringProvider : IStringProvider
{
    public string Get(string key) => key;

    // Every key "exists" and echoes itself, so FormatCount(key, n) in VM tests
    // deterministically yields "key_One" / "key_Other" etc.
    public bool TryGet(string key, out string value) { value = key; return true; }
}
