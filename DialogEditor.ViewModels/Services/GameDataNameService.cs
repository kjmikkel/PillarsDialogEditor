namespace DialogEditor.ViewModels.Services;

public static class GameDataNameService
{
    private static readonly Dictionary<string, IReadOnlyList<NamedEntry>> _registry = new();

    public static void Register(string kind, IReadOnlyList<NamedEntry> entries)
        => _registry[kind] = entries;

    public static IReadOnlyList<NamedEntry> Get(string kind)
        => _registry.TryGetValue(kind, out var e) ? e : [];

    public static void Clear() => _registry.Clear();
}
