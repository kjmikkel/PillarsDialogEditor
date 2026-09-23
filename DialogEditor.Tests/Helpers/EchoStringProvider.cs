using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

/// Like StubStringProvider (every key echoes itself), except for the keys given real
/// values — for tests that must see through a composing format such as "{0} [{1}]".
public sealed class EchoStringProvider(Dictionary<string, string> overrides) : IStringProvider
{
    public string Get(string key) => overrides.TryGetValue(key, out var v) ? v : key;
    public bool TryGet(string key, out string value) { value = Get(key); return true; }
}
