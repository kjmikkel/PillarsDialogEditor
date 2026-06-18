namespace DialogEditor.ViewModels.Services;

/// <summary>One selectable language: its persisted <paramref name="Id"/> (BCP-47 code,
/// e.g. "en") and the localisation key for its display name.</summary>
public sealed record LanguageOption(string Id, string DisplayNameKey);

/// <summary>
/// Framework-agnostic seam for runtime language switching. The Avalonia implementation
/// injects an overlay ResourceDictionary (last-merged wins, English base covers untranslated
/// keys); tests inject a stub. Mirrors <see cref="IThemeApplier"/>.
/// </summary>
public interface ILanguageApplier
{
    /// <summary>The languages the user may choose between, in display order (default first).</summary>
    IReadOnlyList<LanguageOption> Available { get; }

    /// <summary>Apply the language with the given BCP-47 <paramref name="id"/> to the running app.</summary>
    void Apply(string id);
}
