using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Resolves a game language code (e.g. "fr", "pt-BR") to a friendly, localized
/// name. Known PoE1/PoE2 codes map to resource keys; unknown codes fall back to
/// the raw code. Mirrors SpeakerNameService's resolve-or-fallback shape.
/// </summary>
public static class LanguageNameResolver
{
    private static readonly IReadOnlyDictionary<string, string> KnownKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"]    = "Language_Name_en",
            ["fr"]    = "Language_Name_fr",
            ["de"]    = "Language_Name_de",
            ["es"]    = "Language_Name_es",
            ["it"]    = "Language_Name_it",
            ["pl"]    = "Language_Name_pl",
            ["ru"]    = "Language_Name_ru",
            ["pt-BR"] = "Language_Name_ptBR",
            ["zh-CN"] = "Language_Name_zhCN",
            ["ko"]    = "Language_Name_ko",
            ["ja"]    = "Language_Name_ja",
        };

    /// <summary>Friendly name for a code, or the raw code if it is not a known language.</summary>
    public static string Resolve(string code) =>
        KnownKeys.TryGetValue(code, out var key) ? Loc.Get(key) : code;
}
