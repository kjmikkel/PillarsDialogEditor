using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// Covers ConversationJumpResolver: discovering StartConversation handoffs, resolving them
/// per game (PoE2 by GUID, PoE1 by name), the project-patched traversal boundary, and the
/// added-node text fix-up.
/// Spec: docs/superpowers/specs/2026-09-10-cross-conversation-path-stats-design.md
public class ConversationJumpResolverTests
{
    private static NodeEditSnapshot Node(
        int id, string text = "", IReadOnlyList<LinkEditSnapshot>? links = null,
        IReadOnlyList<ScriptCall>? scripts = null) =>
        new(id, false, SpeakerCategory.Npc, "", "", text, "",
            "Conversation", "None", "", "", "", false, false,
            links ?? [], [], scripts ?? []);

    private static ScriptCall Poe2Start(string convGuid, int nodeId) =>
        new("Void StartConversation(Guid, Guid, Int32)",
            ["speaker", convGuid, nodeId.ToString()], ScriptCategory.Exit);

    private static ScriptCall Poe1Start(string convName, int nodeId) =>
        new("Void StartConversation(Guid, String, Int32)",
            ["speaker", convName, nodeId.ToString()], ScriptCategory.Exit);

    private static ConversationPatch Patch(string name) =>
        new(name, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private static DialogProject ProjectWith(params string[] patchedConversations) =>
        new("test",
            DialogProject.CurrentSchemaVersion,
            patchedConversations.ToDictionary(n => n, Patch));

    /// Purpose-built multi-conversation fake. The existing StubProvider in
    /// DialogEditor.Tests/Helpers/ is single-conversation with a hard-coded
    /// `GameId => "stub"`, and other tests depend on its current shape — bending it would
    /// risk them, so these tests carry their own.
    private sealed class FakeJumpProvider : IGameDataProvider
    {
        private readonly Dictionary<string, ConversationEditSnapshot> _convs = new();
        private readonly HashSet<string> _throwOn = [];

        public string GameName => "Fake";
        public string GameId   { get; init; } = "poe2";
        public IReadOnlyList<string> AvailableLanguages => ["en"];
        public string Language { get; set; } = "en";

        public void AddConversation(string name, ConversationEditSnapshot snap)
            => _convs[name] = snap;

        public void ThrowOnLoad(string name)
        {
            _convs[name] = new ConversationEditSnapshot([]);
            _throwOn.Add(name);
        }

        public IReadOnlyList<ConversationFile> EnumerateConversations()
            => _convs.Keys.Select(n => new ConversationFile(n, "", n + ".bundle", "")).ToList();

        public Conversation LoadConversation(ConversationFile f)
        {
            if (_throwOn.Contains(f.Name))
                throw new IOException($"simulated load failure for {f.Name}");
            var snap = _convs[f.Name];
            var nodes = snap.Nodes.Select(n => new ConversationNode(
                n.NodeId, n.IsPlayerChoice, n.SpeakerCategory, n.SpeakerGuid, n.ListenerGuid,
                n.Links.Select(l => new NodeLink(l.FromNodeId, l.ToNodeId, l.Conditions ?? [],
                                                 l.RandomWeight, l.QuestionNodeTextDisplay)).ToList(),
                n.Conditions, n.Scripts, n.DisplayType, n.Persistence,
                n.ActorDirection, n.Comments, n.ExternalVO, n.HasVO, n.HideSpeaker)).ToList();
            var strings = new StringTable(
                snap.Nodes.Select(n => new StringEntry(n.NodeId, n.DefaultText, n.FemaleText)));
            return new Conversation(f.Name, nodes, strings);
        }

        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
        public void SaveConversation(ConversationFile f, ConversationEditSnapshot s) { }
        public string GetStringTablePath(ConversationFile f) => "";
        public string GetStringTablePath(ConversationFile f, string language) => "";
        public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots() => ("", "");
        public ConversationFile BuildNewConversationFile(string name) => new(name, "", "", "");
        public void InitializeConversationFile(ConversationFile file) { }
    }

    [Fact]
    public void Poe2_ResolvesGuid_AndFollowsPatchedTarget()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-B", 7)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(7, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Contains(graph.Jumps,
            j => j.From == new NodeRef("A", 0) && j.To == new NodeRef("B", 7));
        Assert.True(graph.Conversations.ContainsKey("B"));
    }

    [Fact]
    public void Poe1_ResolvesStringName()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe1Start("B", 2)])]);
        var provider = new FakeJumpProvider { GameId = "poe1" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(2, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string>());   // PoE1 needs no GUID map

        Assert.Contains(graph.Jumps, j => j.To == new NodeRef("B", 2));
    }

    // Same parameter shape as StartConversation — a Conversation lookup kind plus a
    // "Conversation Node ID" — but starts nothing. If this fails, someone replaced the verb
    // whitelist with parameter-shape matching and the report now contains phantom handoffs,
    // silently and with no exception.
    [Fact]
    public void MarkConversationNodeAsRead_ProducesNoJump()
    {
        var mark = new ScriptCall("Void MarkConversationNodeAsRead(Guid, Int32)",
                                  ["GUID-B", "7"], ScriptCategory.Enter);
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [mark])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(7, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Empty(graph.Jumps);
        Assert.Empty(graph.Unfollowed);
    }

    [Fact]
    public void UnpatchedTarget_IsReportedNotFollowed()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-B", 0)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(0, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A"), provider, "en", "A", open,     // B not patched
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Empty(graph.Jumps);
        Assert.Contains(graph.Unfollowed,
            u => u.Reason == UnfollowedReason.NotPatched && u.TargetLabel == "B");
    }

    [Fact]
    public void UnresolvableGuid_IsReportedUnresolved()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-?", 0)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A"), provider, "en", "A", open,
            new Dictionary<string, string>());

        Assert.Contains(graph.Unfollowed, u => u.Reason == UnfollowedReason.Unresolved);
    }

    [Fact]
    public void LoadFailure_IsReported_NotThrown()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-B", 0)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.ThrowOnLoad("B");

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Contains(graph.Unfollowed, u => u.Reason == UnfollowedReason.LoadFailed);
    }

    // NodeEditSnapshot.DefaultText is [JsonIgnore], so an ADDED node comes back from a patch
    // with empty text. Without the fix-up every patch-loaded conversation counts zero words —
    // wrong numbers, no exception, and nothing else in the suite would catch it.
    [Fact]
    public void AddedNodeText_IsRestoredFromTranslations()
    {
        var open = new ConversationEditSnapshot([Node(0, "a", scripts: [Poe2Start("GUID-B", 5)])]);
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(5, "")]));  // empty

        var patchB = Patch("B") with
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(5, "one two three", "")]
            }
        };
        var project = new DialogProject("test", DialogProject.CurrentSchemaVersion,
            new Dictionary<string, ConversationPatch> { ["A"] = Patch("A"), ["B"] = patchB });

        var graph = ConversationJumpResolver.Resolve(
            project, provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Equal("one two three",
            graph.Conversations["B"].Nodes.Single(n => n.NodeId == 5).DefaultText);
    }

    [Fact]
    public void OnlyJumpReachableConversations_AreLoaded()
    {
        var open = new ConversationEditSnapshot([Node(0, "a")]);   // no jumps
        var provider = new FakeJumpProvider { GameId = "poe2" };
        provider.AddConversation("B", new ConversationEditSnapshot([Node(0, "b")]));

        var graph = ConversationJumpResolver.Resolve(
            ProjectWith("A", "B"), provider, "en", "A", open,
            new Dictionary<string, string> { ["GUID-B"] = "B" });

        Assert.Single(graph.Conversations);           // only the open one
        Assert.False(graph.Conversations.ContainsKey("B"));
    }
}
