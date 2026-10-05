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

    private static readonly DialogProject NoPatches = DialogProject.Empty("p");

    /// The project's own view of a conversation: vanilla with its patch replayed and
    /// the provider language's translations laid over the node text — what the canvas
    /// would show if the conversation were opened.
    private static ConversationEditSnapshot Effective(
        DialogProject project, ConversationFile file, StubProvider provider)
    {
        var vanilla = ConversationSnapshotBuilder.Build(provider.LoadConversation(file));
        var patch   = project.Patches[file.Name];
        var snap    = PatchApplier.Apply(vanilla, patch);
        var text    = patch.Translations.GetValueOrDefault(provider.Language) ?? [];
        return ConversationSnapshotBuilder.Build(
            ConversationSnapshotBuilder.ToConversation(file.Name, snap, text));
    }

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

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

        Assert.Single(results);
        Assert.Single(results[0].Matches);
        Assert.Equal(new BatchField(BatchFieldKind.DefaultText), results[0].Matches[0].Field);
        Assert.Equal("Hello world", results[0].Matches[0].Before);
        Assert.Equal("Hello earth", results[0].Matches[0].After);
    }

    [Fact]
    public void DryRun_MatchInFemaleText_ReturnsMatch()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, femaleText: "She said hello"));
        var query    = TextQuery("hello", "hi");

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

        Assert.Single(results[0].Matches);
        Assert.Equal(new BatchField(BatchFieldKind.FemaleText), results[0].Matches[0].Field);
    }

    [Fact]
    public void DryRun_NoMatch_FileExcludedFromResults()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Nothing relevant"));
        var query    = TextQuery("xyz", "abc");

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

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

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

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

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

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

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

        Assert.Single(results[0].Matches);
        Assert.Equal(new BatchField(BatchFieldKind.SpeakerGuid), results[0].Matches[0].Field);
    }

    [Fact]
    public void DryRun_MatchInListenerGuid_ReturnsMatch()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, listenerGuid: "old-guid"));
        var query    = new BatchReplaceQuery(
            "old-guid", "new-guid", false, InSpeakerGuids: true);

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

        Assert.Single(results[0].Matches);
        Assert.Equal(new BatchField(BatchFieldKind.ListenerGuid), results[0].Matches[0].Field);
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

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

        Assert.Single(results[0].Matches);
        Assert.Equal(BatchFieldKind.ScriptParam, results[0].Matches[0].Field.Kind);
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

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

        Assert.Single(results[0].Matches);
        Assert.Equal(BatchFieldKind.ConditionParam, results[0].Matches[0].Field.Kind);
    }

    // ── QuestionNodeTextDisplay is an enum, not prose (#24) ──────────────

    [Fact]
    public void Apply_ReplaceMatchingEnumName_DoesNotCorruptQuestionNodeTextDisplay()
    {
        // QuestionNodeTextDisplay holds one of exactly three values — ShowOnce,
        // Always, Never — controlling whether a question node's text is shown.
        // A writer replacing "on" with "in" across node prose must not silently
        // rewrite "ShowOnce" to "ShowInce": PoE2's serializer maps any unknown
        // name to 0 (ShowOnce) with no error, and PoE1 writes the garbage
        // straight into the XML.
        var link     = new LinkEditSnapshot(1, 2, 1f, "ShowOnce", false);
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Come on in", links: [link]));
        // Every field toggle the query supports is enabled, so this fails the moment
        // anyone reintroduces link fields to the batch replace surface.
        var query    = new BatchReplaceQuery(
            "on", "in", false, InNodeText: true, InSpeakerGuids: true,
            InScriptParams: true, InConditionParams: true);

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);
        var project = BatchReplaceService.Apply(results, provider, NoPatches);

        var node = Effective(project, file, provider).Nodes[0];
        Assert.Equal("Come in in", node.DefaultText);
        Assert.Equal("ShowOnce",   node.Links[0].QuestionNodeTextDisplay);
        // No BatchFieldKind covers a link field at all — see the note in BatchReplaceModels.
        Assert.DoesNotContain(results[0].Matches, m =>
            m.Field.Kind is not (BatchFieldKind.DefaultText or BatchFieldKind.FemaleText
                              or BatchFieldKind.SpeakerGuid or BatchFieldKind.ListenerGuid
                              or BatchFieldKind.ScriptParam or BatchFieldKind.ConditionParam));
    }

    // ── DryRun — field toggle respected ──────────────────────────────────

    [Fact]
    public void DryRun_InNodeTextFalse_SkipsNodeText()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "hello"));
        var query    = new BatchReplaceQuery("hello", "hi", false, InNodeText: false);

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);

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

        var results = BatchReplaceService.DryRun(query, [f1, f2, f3], p, NoPatches);

        Assert.Single(results);
        Assert.Equal("conv1", results[0].File.Name);
    }

    // ── Apply — edits the project, never the game folder (#124) ───────────

    [Fact]
    public void Apply_NeverWritesTheGameFolder()
    {
        // Batch Replace used to save straight into the game's conversation files: the
        // one editor write that neither Restore (F6) nor the patcher could undo, and that
        // never became part of the mod. It now only edits the project.
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Hello world"));

        var results = BatchReplaceService.DryRun(TextQuery("world", "earth"), [file], provider, NoPatches);
        BatchReplaceService.Apply(results, provider, NoPatches);

        Assert.Null(provider.SavedSnapshot);
    }

    [Fact]
    public void Apply_TextReplacement_BecomesATranslationInTheProviderLanguage()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Hello world"));

        var results = BatchReplaceService.DryRun(TextQuery("world", "earth"), [file], provider, NoPatches);
        var project = BatchReplaceService.Apply(results, provider, NoPatches);

        var patch = project.Patches["conv"];
        Assert.Equal([new NodeTranslation(1, "Hello earth", "")], patch.Translations["en"]);
        Assert.Equal("Hello earth", Effective(project, file, provider).Nodes[0].DefaultText);
    }

    [Fact]
    public void Apply_StructuralReplacement_BecomesANodeModification()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, speakerGuid: "old-guid"));
        var query    = new BatchReplaceQuery("old-guid", "new-guid", false, InSpeakerGuids: true);

        var results = BatchReplaceService.DryRun(query, [file], provider, NoPatches);
        var project = BatchReplaceService.Apply(results, provider, NoPatches);

        Assert.Equal("new-guid", Effective(project, file, provider).Nodes[0].SpeakerGuid);
    }

    [Fact]
    public void DryRun_SearchesTheProjectsEditsNotJustVanilla()
    {
        // The project already rewrote node 1; the vanilla text no longer matters.
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Hello world"));
        var project  = NoPatches.WithPatch(TextPatch("conv", "en", new NodeTranslation(1, "Goodbye moon", "")));

        Assert.Empty(BatchReplaceService.DryRun(TextQuery("world", "earth"), [file], provider, project));

        var results = BatchReplaceService.DryRun(TextQuery("moon", "sun"), [file], provider, project);
        Assert.Equal("Goodbye moon", results[0].Matches[0].Before);
        Assert.Equal("Goodbye sun",  results[0].Matches[0].After);
    }

    [Fact]
    public void Apply_KeepsTheProjectsEarlierEditsToTheConversation()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file,
            MakeNode(1, defaultText: "Hello world"),
            MakeNode(2, defaultText: "Untouched", speakerGuid: "guid-a"));
        // Earlier edits: node 2's text and speaker.
        var earlier  = DiffEngine.Diff("conv",
            ConversationSnapshotBuilder.Build(provider.LoadConversation(file)),
            new ConversationEditSnapshot([
                MakeNode(1, defaultText: "Hello world"),
                MakeNode(2, defaultText: "Edited earlier", speakerGuid: "guid-b")]),
            "en");
        var project = NoPatches.WithPatch(earlier);

        var results = BatchReplaceService.DryRun(TextQuery("world", "earth"), [file], provider, project);
        var updated = BatchReplaceService.Apply(results, provider, project);

        var nodes = Effective(updated, file, provider).Nodes.OrderBy(n => n.NodeId).ToList();
        Assert.Equal("Hello earth",    nodes[0].DefaultText);
        Assert.Equal("Edited earlier", nodes[1].DefaultText);
        Assert.Equal("guid-b",         nodes[1].SpeakerGuid);
    }

    [Fact]
    public void Apply_KeepsOtherLanguagesTranslationsAndNodeComments()
    {
        // The diff only knows the provider's language; an imported French translation
        // and translator comments must survive the batch edit untouched.
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Hello world"));
        var french   = new NodeTranslation(1, "Bonjour le monde", "");
        var project  = NoPatches.WithPatch(TextPatch("conv", "fr", french) with
        {
            NodeComments = new Dictionary<int, string> { [1] = "greeting" },
        });

        var results = BatchReplaceService.DryRun(TextQuery("world", "earth"), [file], provider, project);
        var patch   = BatchReplaceService.Apply(results, provider, project).Patches["conv"];

        Assert.Equal([french], patch.Translations["fr"]);
        Assert.Equal([new NodeTranslation(1, "Hello earth", "")], patch.Translations["en"]);
        Assert.Equal("greeting", patch.NodeComments[1]);
    }

    [Fact]
    public void Apply_LeavesConversationsItDidNotTouchAlone()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "Hello world"));
        var other    = TextPatch("other", "en", new NodeTranslation(1, "Other", ""));
        var project  = NoPatches.WithPatch(other);

        var results = BatchReplaceService.DryRun(TextQuery("world", "earth"), [file], provider, project);
        var updated = BatchReplaceService.Apply(results, provider, project);

        Assert.Same(other, updated.Patches["other"]);
    }

    private static ConversationPatch TextPatch(string conv, string language, NodeTranslation text) =>
        new(conv, ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>> { [language] = [text] },
        };

    /// ApplyToNode indexes matches with ToDictionary(p => p.Field), which throws on a
    /// duplicate key. Links used to emit a constant path and crash a node with two matching
    /// links (#24); they are gone now, and scripts and conditions key positionally. Nothing
    /// asserted that invariant, so pin it: every field path a single node emits is unique.
    [Fact]
    public void DryRun_FieldIdentitiesAreUniquePerNode()
    {
        var leafA = new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)",
                                      ["quest", "EqualTo", "1"], false, "And");
        var leafB = new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)",
                                      ["quest", "EqualTo", "2"], false, "And");
        var callA = new ScriptCall("Void SetGlobal(String, Int32)", ["quest", "1"], ScriptCategory.Enter);
        var callB = new ScriptCall("Void SetGlobal(String, Int32)", ["quest", "2"], ScriptCategory.Exit);
        var file  = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(
            1, defaultText: "the quest", femaleText: "her quest",
            speakerGuid: "quest", conditions: [leafA, leafB], scripts: [callA, callB]));
        var query = new BatchReplaceQuery(
            "quest", "mission", false, InNodeText: true, InSpeakerGuids: true,
            InScriptParams: true, InConditionParams: true);

        var matches = BatchReplaceService.DryRun(query, [file], provider, NoPatches)[0].Matches;

        Assert.True(matches.Count > 1, "expected several matching fields on the one node");
        Assert.Equal(matches.Select(m => m.Field).Distinct().Count(), matches.Count);

        // The real consequence: apply must not throw on that node.
        BatchReplaceService.Apply(
            BatchReplaceService.DryRun(query, [file], provider, NoPatches), provider, NoPatches);
    }

    [Fact]
    public void Apply_EmptyResults_ReturnsTheProjectUnchanged()
    {
        var file     = MakeFile("conv");
        var provider = MakeProvider(file, MakeNode(1, defaultText: "nothing"));

        Assert.Same(NoPatches, BatchReplaceService.Apply([], provider, NoPatches));
    }
}
