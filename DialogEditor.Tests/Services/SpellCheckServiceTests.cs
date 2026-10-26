using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class SpellCheckServiceTests : IDisposable
{
    private readonly string _dir;
    private readonly SpellDictionaryStore _store;
    private readonly SpellCheckService _svc;

    public SpellCheckServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"spellsvc_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        var fixtures = FixtureRoot();
        File.Copy(Path.Combine(fixtures, "test_en.aff"), Path.Combine(_dir, "en_US.aff"));
        File.Copy(Path.Combine(fixtures, "test_en.dic"), Path.Combine(_dir, "en_US.dic"));
        _store = new SpellDictionaryStore(_dir, Path.Combine(_dir, "user.txt"));
        _store.RegisterLexicon("en", ["adra"]);
        _svc = new SpellCheckService(_store);
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

    [Fact]
    public void CorrectText_NoIssues()
        => Assert.Empty(_svc.Check("captain ships cats", "en")); // fixture-dic words only

    [Fact]
    public void AffixedAndLexiconWords_Pass()
        => Assert.Empty(_svc.Check("cats ships adra Adra captain", "en"));

    [Fact]
    public void Misspelling_Flagged_WithSuggestion()
    {
        var issue = Assert.Single(_svc.Check("captian", "en"));
        Assert.Equal("captian", issue.Word);
        Assert.Equal("captain", issue.Suggestion);
        Assert.Equal(0, issue.Position);
    }

    [Fact]
    public void TokensAndMarkup_NeverChecked()
        => Assert.Empty(_svc.Check("[Playerx Namex] <xnotatag> cat", "en"));

    [Fact]
    public void MarkupContent_IsChecked()
    {
        var issue = Assert.Single(_svc.Check("<i>captian</i>", "en"));
        Assert.Equal("captian", issue.Word);
    }

    [Fact]
    public void NumbersAndSingleLetters_Skipped()
        => Assert.Empty(_svc.Check("cat 42 x 3rd", "en"));

    [Fact]
    public void UnknownLanguage_Empty()
        => Assert.Empty(_svc.Check("zzzz qqqq", "fr"));

    [Fact]
    public void DuplicateMisspelling_ReportedOnce()
        => Assert.Single(_svc.Check("captian cat captian", "en"));

    [Fact]
    public void UserDictionaryWord_PassesAfterAdd()
    {
        Assert.Single(_svc.Check("Xoti", "en"));
        _store.AddWord("Xoti");
        Assert.Empty(_svc.Check("Xoti", "en"));
    }

    // ── FindMisspellings: positions for the inline squiggles (issue 9) ───────

    [Fact]
    public void FindMisspellings_ReportsEveryOccurrence()
        => Assert.Equal([new SpellSpan(0, 7), new SpellSpan(13, 7)],
                        _svc.FindMisspellings("captian ship captian", "en"));

    [Fact]
    public void FindMisspellings_StartsAfterALeadingApostrophe()
        => Assert.Equal([new SpellSpan(1, 7)], _svc.FindMisspellings("'captian glows", "en"));

    [Fact]
    public void FindMisspellings_SkipsTags()
        => Assert.Equal([new SpellSpan(14, 7)], _svc.FindMisspellings("[Player Nmae] captian", "en"));

    [Fact]
    public void FindMisspellings_CorrectText_IsEmpty()
        => Assert.Empty(_svc.FindMisspellings("cat ships glows adra", "en"));

    [Fact]
    public void FindMisspellings_NoDictionaryForTheLanguage_IsEmpty()
        => Assert.Empty(_svc.FindMisspellings("captian", "fr"));

    [Fact]
    public void FindMisspellings_FollowsTheUserDictionary()
    {
        _store.AddWord("Xoti");
        Assert.Empty(_svc.FindMisspellings("Xoti glows", "en"));
    }
}
