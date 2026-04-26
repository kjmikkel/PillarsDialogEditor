using DialogEditor.WPF.Services;

namespace DialogEditor.WPF.Resources;

/// <summary>
/// Static helper that delegates to an IStringProvider.
/// Call Loc.Configure(provider) once at startup before any ViewModel is created.
/// WPF default: WpfStringProvider (Application.Current.Resources).
/// Avalonia: configure with an AvaloniaStringProvider.
/// </summary>
public static class Loc
{
    private static IStringProvider? _provider;

    // Lazy default so Application.Current is always ready when first accessed
    private static IStringProvider Provider =>
        _provider ??= new WpfStringProvider();

    public static void Configure(IStringProvider provider) => _provider = provider;

    public static string Get(string key) => Provider.Get(key);

    public static string Format(string key, params object[] args) =>
        string.Format(Provider.Get(key), args);
}
