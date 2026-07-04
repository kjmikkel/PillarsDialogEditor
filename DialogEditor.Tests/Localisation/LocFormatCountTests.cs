using System.Globalization;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

public class LocFormatCountTests : IDisposable
{
    private sealed class MapProvider(Dictionary<string, string> map) : IStringProvider
    {
        public string Get(string key) => map.TryGetValue(key, out var v) ? v : $"[{key}]";
        public bool TryGet(string key, out string value)
        {
            if (map.TryGetValue(key, out var v)) { value = v; return true; }
            value = string.Empty; return false;
        }
    }

    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _originalCulture;

    private static void UseEnglish() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    [Fact]
    public void FormatCount_PicksCategorySuffix()
    {
        UseEnglish();
        Loc.Configure(new MapProvider(new()
        {
            ["X_One"]   = "1 match",
            ["X_Other"] = "{0} matches",
        }));
        Assert.Equal("1 match",   Loc.FormatCount("X", 1));
        Assert.Equal("3 matches", Loc.FormatCount("X", 3));
    }

    [Fact]
    public void FormatCount_MissingCategory_FallsBackToOther()
    {
        // Polish Few requested, only _Other provided — a partially translated
        // overlay must not crash.
        CultureInfo.CurrentUICulture = new CultureInfo("pl-PL");
        Loc.Configure(new MapProvider(new() { ["X_Other"] = "{0} plików" }));
        Assert.Equal("2 plików", Loc.FormatCount("X", 2));   // Few → falls to Other
    }

    [Fact]
    public void FormatCount_NoSuffixedKeys_FallsBackToBareKey()
    {
        UseEnglish();
        Loc.Configure(new MapProvider(new() { ["X"] = "{0} legacy" }));
        Assert.Equal("7 legacy", Loc.FormatCount("X", 7));
    }

    [Fact]
    public void FormatCount_ExtraArgsFollowCount()
    {
        UseEnglish();
        Loc.Configure(new MapProvider(new()
        {
            ["X_Other"] = "Exported {0} conversations to {1}.",
        }));
        Assert.Equal("Exported 4 conversations to out.csv.",
            Loc.FormatCount("X", 4, "out.csv"));
    }

    [Fact]
    public void FormatCount_PolishFew_UsesFewKeyWhenPresent()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pl-PL");
        Loc.Configure(new MapProvider(new()
        {
            ["X_Few"]   = "{0} pliki",
            ["X_Other"] = "{0} plików",
        }));
        Assert.Equal("3 pliki", Loc.FormatCount("X", 3));
    }
}
