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

    /// <summary>
    /// Plural-aware lookup: resolves "{key}_{CldrCategory}" for the current UI
    /// language (set by CoreLocale.SetCulture), falling back to "{key}_Other",
    /// then bare "{key}" (unmigrated legacy). Count is always {0}; extraArgs
    /// follow as {1}… . See docs/superpowers/specs/2026-07-04-pluralisation-design.md.
    /// </summary>
    public static string FormatCount(string key, int count, params object[] extraArgs)
    {
        var lang     = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var category = PluralRules.Category(lang, count);

        if (!Provider.TryGet($"{key}_{category}", out var value)
            && !Provider.TryGet($"{key}_Other", out value))
            value = Provider.Get(key);

        var args = new object[extraArgs.Length + 1];
        args[0] = count;
        extraArgs.CopyTo(args, 1);
        return string.Format(value, args);
    }
}
