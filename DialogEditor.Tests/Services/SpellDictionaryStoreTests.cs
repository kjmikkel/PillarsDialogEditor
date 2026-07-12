using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class SpellDictionaryStoreTests : IDisposable
{
    private readonly string _dir;
    private readonly string _userDict;

    public SpellDictionaryStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"spell_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _userDict = Path.Combine(_dir, "user-dictionary.txt");
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static string FixtureRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        return Path.Combine(dir!.FullName, "DialogEditor.Tests", "Fixtures", "spell");
    }

    /// Copies the fixture pair in under the "en" prefix (en_US.aff/en_US.dic).
    private void InstallFixtureEn()
    {
        File.Copy(Path.Combine(FixtureRoot(), "test_en.aff"), Path.Combine(_dir, "en_US.aff"));
        File.Copy(Path.Combine(FixtureRoot(), "test_en.dic"), Path.Combine(_dir, "en_US.dic"));
    }

    private SpellDictionaryStore NewStore() => new(_dir, _userDict);

    [Fact]
    public void Discovery_MapsFilenamePrefixToLanguage()
    {
        InstallFixtureEn();
        var store = NewStore();
        Assert.Contains("en", store.AvailableLanguages);
        Assert.True(store.HasDictionary("en"));
        Assert.False(store.HasDictionary("fr"));
    }

    [Fact]
    public void AffixedForm_IsAccepted()
    {
        InstallFixtureEn();
        var store = NewStore();
        Assert.True(store.IsCorrect("cat", "en"));
        Assert.True(store.IsCorrect("cats", "en"));  // via the S suffix rule — real Hunspell
        Assert.True(store.IsCorrect("ships", "en"));
    }

    [Fact]
    public void UnknownWord_Incorrect_WithSuggestion()
    {
        InstallFixtureEn();
        var store = NewStore();
        Assert.False(store.IsCorrect("captian", "en"));
        Assert.Equal("captain", store.Suggest("captian", "en"));
    }

    [Fact]
    public void CorruptPair_TreatedAsAbsent()
    {
        File.WriteAllText(Path.Combine(_dir, "de_DE.aff"), "\0\0garbage");
        File.WriteAllText(Path.Combine(_dir, "de_DE.dic"), "\0\0garbage");
        var store = NewStore();
        // Either not discovered or lookup degrades — never a throw.
        Assert.False(store.HasDictionary("de") && store.IsCorrect("und", "de"));
    }

    [Fact]
    public void Lexicon_AcceptsWord_CaseTolerant()
    {
        InstallFixtureEn();
        var store = NewStore();
        store.RegisterLexicon("en", ["adra"]);
        Assert.True(store.IsCorrect("adra", "en"));
        Assert.True(store.IsCorrect("Adra", "en"));
    }

    [Fact]
    public void UserDictionary_AddWord_PersistsAcrossInstances()
    {
        InstallFixtureEn();
        var store = NewStore();
        Assert.False(store.IsCorrect("Xoti", "en"));
        store.AddWord("Xoti");
        Assert.True(store.IsCorrect("Xoti", "en"));

        var second = NewStore();
        Assert.True(second.IsCorrect("Xoti", "en"));
    }

    [Fact]
    public void MissingAffOrDic_NotDiscovered()
    {
        File.WriteAllText(Path.Combine(_dir, "it_IT.dic"), "1\nciao\n"); // no .aff
        var store = NewStore();
        Assert.False(store.HasDictionary("it"));
    }
}
