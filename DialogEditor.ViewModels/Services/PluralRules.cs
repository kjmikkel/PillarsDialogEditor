namespace DialogEditor.ViewModels.Services;

/// <summary>CLDR plural category of a cardinal count.</summary>
public enum PluralCategory { Zero, One, Two, Few, Many, Other }

/// <summary>
/// CLDR cardinal plural rules, integer counts only (every pluralised UI string
/// counts discrete things). Multi-form languages (pl/ru/ar) ship now as the
/// reference implementations a future translation plugs into — the mechanism
/// is proven by tests, not hoped generalisable. Unknown languages use the
/// English rule; Loc.FormatCount's _Other fallback absorbs the rest.
/// Rules source: CLDR cardinal rules (see unicode.org CLDR plural rules chart).
/// </summary>
public static class PluralRules
{
    public static PluralCategory Category(string langTwoLetter, int n)
        => langTwoLetter.ToLowerInvariant() switch
        {
            "fr" => n is 0 or 1 ? PluralCategory.One : PluralCategory.Other,
            "pl" => Polish(n),
            "ru" => Russian(n),
            "ar" => Arabic(n),
            _    => n == 1 ? PluralCategory.One : PluralCategory.Other, // en, de, …
        };

    private static PluralCategory Polish(int n)
    {
        if (n == 1) return PluralCategory.One;
        if (n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) return PluralCategory.Few;
        return PluralCategory.Many;
    }

    private static PluralCategory Russian(int n)
    {
        if (n % 10 == 1 && n % 100 != 11) return PluralCategory.One;
        if (n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) return PluralCategory.Few;
        return PluralCategory.Many;
    }

    private static PluralCategory Arabic(int n) => n switch
    {
        0 => PluralCategory.Zero,
        1 => PluralCategory.One,
        2 => PluralCategory.Two,
        _ when n % 100 is >= 3 and <= 10  => PluralCategory.Few,
        _ when n % 100 is >= 11 and <= 99 => PluralCategory.Many,
        _ => PluralCategory.Other,
    };
}
