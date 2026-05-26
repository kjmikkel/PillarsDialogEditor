using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch;

public class BatchReplaceServiceTests
{
    // ── Helpers ───────────────────────────────────────────────────────────

    private static ConversationFile MakeFile(string name) =>
        new(name, $@"quests\{name}.conversation", "quests", $@"quests\{name}.stringtable");

    private static NodeEditSnapshot MakeNode(
        int    id,
        string defaultText  = "",
        string femaleText   = "",
        string speakerGuid  = "",
        string listenerGuid = "",
        IReadOnlyList<ScriptCall>?    scripts    = null,
        IReadOnlyList<ConditionNode>? conditions = null,
        IReadOnlyList<LinkEditSnapshot>? links   = null) =>
        new(id, false, SpeakerCategory.Npc, speakerGuid, listenerGuid,
            defaultText, femaleText, "Conversation", "None", "", "", "", false, false,
            links ?? [], conditions ?? [], scripts ?? []);

    private static BatchReplaceQuery TextQuery(
        string search, string replace, bool caseSensitive = false) =>
        new(search, replace, caseSensitive, InNodeText: true);

    private static StubProvider MakeProvider(
        ConversationFile file, params NodeEditSnapshot[] nodes) =>
        new(file, new ConversationEditSnapshot(nodes));

    // ── DryRun — node text ────────────────────────────────────────────────

    [Fact]
    public void DryRun_MatchInDefaultText_ReturnsMatch()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Hello world"));
        var query    = TextQuery("world", "earth");

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Single(results);
        Assert.Single(results[0].Matches);
        Assert.Equal("Default Text", results[0].Matches[0].FieldPath);
        Assert.Equal("Hello world", results[0].Matches[0].Before);
        Assert.Equal("Hello earth", results[0].Matches[0].After);
    }

    [Fact]
    public void DryRun_MatchInFemaleText_ReturnsMatch()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, femaleText: "She said hello"));
        var query    = TextQuery("hello", "hi");

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Single(results[0].Matches);
        Assert.Equal("Female Text", results[0].Matches[0].FieldPath);
    }

    [Fact]
    public void DryRun_NoMatch_FileExcludedFromResults()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Nothing relevant"));
        var query    = TextQuery("xyz", "abc");

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Empty(results);
    }

    [Fact]
    public void DryRun_CaseSensitive_OnlyMatchesExactCase()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file,
            MakeNode(1, defaultText: "Hello"),
            MakeNode(2, defaultText: "hello"));
        var query = new BatchReplaceQuery("hello", "hi", CaseSensitive: true, InNodeText: true);

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Single(results[0].Matches);
        Assert.Equal(2, results[0].Matches[0].NodeId);
    }

    [Fact]
    public void DryRun_CaseInsensitive_MatchesBothCases()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file,
            MakeNode(1, defaultText: "Hello"),
            MakeNode(2, defaultText: "hello"));
        var query = TextQuery("hello", "hi");

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Equal(2, results[0].Matches.Count);
    }

    // ── DryRun — speaker GUIDs ────────────────────────────────────────────

    [Fact]
    public void DryRun_MatchInSpeakerGuid_ReturnsMatch()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, speakerGuid: "old-guid"));
        var query    = new BatchReplaceQuery(
            "old-guid", "new-guid", false, InSpeakerGuids: true);

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Single(results[0].Matches);
        Assert.Equal("Speaker GUID", results[0].Matches[0].FieldPath);
    }

    [Fact]
    public void DryRun_MatchInListenerGuid_ReturnsMatch()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, listenerGuid: "old-guid"));
        var query    = new BatchReplaceQuery(
            "old-guid", "new-guid", false, InSpeakerGuids: true);

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Single(results[0].Matches);
        Assert.Equal("Listener GUID", results[0].Matches[0].FieldPath);
    }

    // ── DryRun — script params ────────────────────────────────────────────

    [Fact]
    public void DryRun_MatchInScriptParam_ReturnsMatch()
    {
        var script   = new ScriptCall("Void SetGlobalValue(String, Int32)",
                                      ["myFlag", "1"], ScriptCategory.Enter);
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, scripts: [script]));
        var query    = new BatchReplaceQuery(
            "myFlag", "renamedFlag", false, InScriptParams: true);

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Single(results[0].Matches);
        Assert.Contains("Script", results[0].Matches[0].FieldPath);
        Assert.Equal("myFlag", results[0].Matches[0].Before);
        Assert.Equal("renamedFlag", results[0].Matches[0].After);
    }

    // ── DryRun — condition params ─────────────────────────────────────────

    [Fact]
    public void DryRun_MatchInConditionParam_ReturnsMatch()
    {
        var leaf     = new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)",
                                         ["myFlag", "EqualTo", "1"], false, "And");
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, conditions: [leaf]));
        var query    = new BatchReplaceQuery(
            "myFlag", "renamedFlag", false, InConditionParams: true);

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Single(results[0].Matches);
        Assert.Contains("Condition", results[0].Matches[0].FieldPath);
    }

    // ── DryRun — link choice text ─────────────────────────────────────────

    [Fact]
    public void DryRun_MatchInLinkChoiceText_ReturnsMatch()
    {
        var link     = new LinkEditSnapshot(1, 2, 1f, "Ask about the quest", false);
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, links: [link]));
        var query    = new BatchReplaceQuery(
            "quest", "mission", false, InLinkChoiceText: true);

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Single(results[0].Matches);
        Assert.Equal("Link Choice Text", results[0].Matches[0].FieldPath);
    }

    // ── DryRun — field toggle respected ──────────────────────────────────

    [Fact]
    public void DryRun_InNodeTextFalse_SkipsNodeText()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "hello"));
        var query    = new BatchReplaceQuery("hello", "hi", false, InNodeText: false);

        var results = BatchReplaceService.DryRun(query, [file], provider);

        Assert.Empty(results);
    }

    // ── DryRun — multiple conversations ──────────────────────────────────

    [Fact]
    public void DryRun_MultipleFiles_ReturnsResultPerMatchingFile()
    {
        var f1 = MakeFile("conv1");
        var f2 = MakeFile("conv2");
        var f3 = MakeFile("conv3");
        var p  = new MultiFileProvider([
            (f1, new ConversationEditSnapshot([MakeNode(1, defaultText: "hello")])),
            (f2, new ConversationEditSnapshot([MakeNode(1, defaultText: "world")])),
            (f3, new ConversationEditSnapshot([MakeNode(1, defaultText: "nothing")])),
        ]);
        var query = TextQuery("hello", "hi");

        var results = BatchReplaceService.DryRun(query, [f1, f2, f3], p);

        Assert.Single(results);
        Assert.Equal("conv1", results[0].File.Name);
    }

    // ── Apply — modifies conversations via provider ───────────────────────

    [Fact]
    public void Apply_CallsSaveConversationWithReplacedText()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Hello world"));
        var query    = TextQuery("world", "earth");

        var dryRunResults = BatchReplaceService.DryRun(query, [file], provider);
        BatchReplaceService.Apply(dryRunResults, provider);

        Assert.True(provider.SavedSnapshot is not null);
        Assert.Equal("Hello earth", provider.SavedSnapshot!.Nodes[0].DefaultText);
    }

    [Fact]
    public void Apply_EmptyResults_DoesNotCallSave()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "nothing"));

        BatchReplaceService.Apply([], provider);

        Assert.Null(provider.SavedSnapshot);
    }
}

