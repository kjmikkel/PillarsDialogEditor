using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.Services;

public class LanguagePickerTests
{
    private static readonly IReadOnlyList<string> EnOnly    = ["en"];
    private static readonly IReadOnlyList<string> MultiLang = ["de", "en", "fr", "pl"];
    private static readonly IReadOnlyList<string> NoEn      = ["de", "fr", "pl"];

    [Fact]
    public void Pick_PreferredInList_ReturnsPreferred()
    {
        Assert.Equal("fr", LanguagePicker.Pick(MultiLang, "fr"));
    }

    [Fact]
    public void Pick_PreferredNotInList_FallsBackToEn()
    {
        Assert.Equal("en", LanguagePicker.Pick(MultiLang, "zh"));
    }

    [Fact]
    public void Pick_PreferredNull_FallsBackToEn()
    {
        Assert.Equal("en", LanguagePicker.Pick(MultiLang, null));
    }

    [Fact]
    public void Pick_PreferredEmpty_FallsBackToEn()
    {
        Assert.Equal("en", LanguagePicker.Pick(MultiLang, ""));
    }

    [Fact]
    public void Pick_NoEnAndPreferredNull_ReturnsFirstAvailable()
    {
        Assert.Equal("de", LanguagePicker.Pick(NoEn, null));
    }

    [Fact]
    public void Pick_NoEnAndPreferredAbsent_ReturnsFirstAvailable()
    {
        Assert.Equal("de", LanguagePicker.Pick(NoEn, "en"));
    }

    [Fact]
    public void Pick_OnlyEnglishAvailable_ReturnsEn()
    {
        Assert.Equal("en", LanguagePicker.Pick(EnOnly, null));
    }
}
