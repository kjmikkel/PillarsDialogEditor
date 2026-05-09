using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels.Resources;

/// <summary>
/// Static helper that delegates to an IStringProvider.
/// Call Loc.Configure(provider) once at startup before any ViewModel is created.
/// </summary>
public static class Loc
{
    private static IStringProvider? _provider;

    private static IStringProvider Provider =>
        _provider ?? throw new InvalidOperationException(
            "Loc.Configure(provider) must be called at startup before any string lookup.");

    public static void Configure(IStringProvider provider) => _provider = provider;

    public static string Get(string key) => Provider.Get(key);

    public static string Format(string key, params object[] args) =>
        string.Format(Provider.Get(key), args);
}
