using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelValidationTests : IDisposable
{
    private readonly string _spellDir;

    public NodeDetailViewModelValidationTests()
    {
        Loc.Configure(new StubStringProvider());
        _spellDir = Path.Combine(Path.GetTempPath(), $"ndspell_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_spellDir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_spellDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    /// A SpellCheckService over the fixture .aff/.dic (words: cat/ship/captain/glows + S rule).
    private SpellCheckService FixtureChecker()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "DialogEditor.slnx")))
            root = root.Parent;
        var fixtures = Path.Combine(root!.FullName, "DialogEditor.Tests", "Fixtures", "spell");
        File.Copy(Path.Combine(fixtures, "test_en.aff"), Path.Combine(_spellDir, "en_US.aff"));
        File.Copy(Path.Combine(fixtures, "test_en.dic"), Path.Combine(_spellDir, "en_US.dic"));
        return new SpellCheckService(
            new SpellDictionaryStore(_spellDir, Path.Combine(_spellDir, "user.txt")));
    }

    // Build a NodeViewModel with the given Default/Female text (mirrors the
    // construction in NodeDetailViewModelTests.MakeNode).
    private static NodeViewModel MakeNode(string defaultText, string femaleText = "")
    {
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
            Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false);
        return new NodeViewModel(node, new StringEntry(1, defaultText, femaleText));
    }

    private static NodeDetailViewModel Loaded(string defaultText, string femaleText = "")
    {
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2" };
        vm.Load(MakeNode(defaultText, femaleText));
        return vm;
    }

    [Fact]
    public void TokenWarnings_CleanText_IsEmpty()
        => Assert.Empty(Loaded("Hello [Player Name].").TokenWarnings);

    [Fact]
    public void TokenWarnings_MisspelledToken_UsesSuggestionBranch()
    {
        var msg = Assert.Single(Loaded("Hi [Player Nmae]!").TokenWarnings);
        // The stub echoes resource keys, so the message IS the key of the branch taken.
        Assert.Equal("Validation_UnknownToken_Suggest", msg);
    }

    [Fact]
    public void TokenWarnings_RecomputeOnDefaultTextEdit()
    {
        var vm = Loaded("clean text");
        Assert.Empty(vm.TokenWarnings);
        vm.DefaultText = "now with [Player Nmae]";
        Assert.NotEmpty(vm.TokenWarnings);
    }

    [Fact]
    public void TokenWarnings_ValidatesFemaleText()
        => Assert.NotEmpty(Loaded("clean", femaleText: "she says [Player Nmae]").TokenWarnings);

    // ── Spelling joins the same warning box (spell checker feature) ─────────

    [Fact]
    public void TokenWarnings_Misspelling_AppearsWithChecker()
    {
        var vm = Loaded("the captian glows");
        vm.SpellChecker = FixtureChecker();
        vm.ActiveLanguage = "en";
        vm.Load(MakeNode("the captian glows")); // reload to recompute with checker
        var msg = Assert.Single(vm.TokenWarnings, w => w == "Spelling_Misspelled_Suggest");
        Assert.NotNull(msg);
    }

    [Fact]
    public void TokenWarnings_NullChecker_TagOnly()
    {
        // No checker wired (unit-test default): spelling silently off.
        var vm = Loaded("the captian glows [Player Nmae]");
        Assert.Single(vm.TokenWarnings); // only the tag warning
    }

    [Fact]
    public void TokenWarnings_SpellingRecomputesOnEdit()
    {
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2", ActiveLanguage = "en" };
        vm.SpellChecker = FixtureChecker();
        vm.Load(MakeNode("captain"));
        Assert.Empty(vm.TokenWarnings);
        vm.DefaultText = "captian";
        Assert.Contains("Spelling_Misspelled_Suggest", vm.TokenWarnings);
    }
}
