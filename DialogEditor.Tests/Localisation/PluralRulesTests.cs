using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

// Reference cases from the CLDR cardinal plural rules (integers only).
public class PluralRulesTests
{
    [Theory]
    [InlineData("en", 1, PluralCategory.One)]
    [InlineData("en", 0, PluralCategory.Other)]
    [InlineData("en", 2, PluralCategory.Other)]
    [InlineData("de", 1, PluralCategory.One)]
    [InlineData("de", 5, PluralCategory.Other)]
    public void EnglishAndGerman_OneOther(string lang, int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category(lang, n));

    [Theory]
    [InlineData(0, PluralCategory.One)]   // French: 0 and 1 are both One
    [InlineData(1, PluralCategory.One)]
    [InlineData(2, PluralCategory.Other)]
    public void French_ZeroAndOneAreOne(int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category("fr", n));

    [Theory]
    [InlineData(1,  PluralCategory.One)]
    [InlineData(2,  PluralCategory.Few)]
    [InlineData(4,  PluralCategory.Few)]
    [InlineData(22, PluralCategory.Few)]   // 2..4 but NOT 12..14
    [InlineData(5,  PluralCategory.Many)]
    [InlineData(12, PluralCategory.Many)]  // teens are Many
    [InlineData(0,  PluralCategory.Many)]
    public void Polish(int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category("pl", n));

    [Theory]
    [InlineData(1,  PluralCategory.One)]
    [InlineData(21, PluralCategory.One)]   // ends in 1, not 11
    [InlineData(11, PluralCategory.Many)]
    [InlineData(2,  PluralCategory.Few)]
    [InlineData(3,  PluralCategory.Few)]
    [InlineData(5,  PluralCategory.Many)]
    [InlineData(14, PluralCategory.Many)]
    public void Russian(int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category("ru", n));

    [Theory]
    [InlineData(0,   PluralCategory.Zero)]
    [InlineData(1,   PluralCategory.One)]
    [InlineData(2,   PluralCategory.Two)]
    [InlineData(3,   PluralCategory.Few)]
    [InlineData(10,  PluralCategory.Few)]
    [InlineData(11,  PluralCategory.Many)]
    [InlineData(99,  PluralCategory.Many)]
    [InlineData(100, PluralCategory.Other)]
    public void Arabic(int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category("ar", n));

    [Theory]
    [InlineData("xx", 1, PluralCategory.One)]    // unknown language → en rule
    [InlineData("xx", 3, PluralCategory.Other)]
    [InlineData("EN", 1, PluralCategory.One)]    // case-insensitive
    public void UnknownOrUppercase_FallsBackToEnglishRule(string lang, int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category(lang, n));
}
