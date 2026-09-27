using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// <summary>
/// Issue #84: nodes with no string-table entry (normal for script/trigger nodes) used
/// to have the "[text unavailable — stringtable not found]" placeholder stored in their
/// real DefaultText. That both mislabelled healthy nodes and let the placeholder leak
/// into saved/exported/searched data. The placeholder is now display-only
/// (<see cref="NodeViewModel.TextPlaceholder"/>) and only claims "not found" when the
/// conversation's string table genuinely failed to load.
///
/// StubStringProvider echoes keys, so the old placeholder text is literally
/// "Node_TextUnavailable" here.
/// </summary>
public class TextlessNodePlaceholderTests
{
    private const string OldPlaceholder = "Node_TextUnavailable";

    public TextlessNodePlaceholderTests() => Loc.Configure(new StubStringProvider());

    private static ConversationNode Node(int id, SpeakerCategory category) =>
        new(id, false, category, "", "", [], [], [], "Conversation", "None");

    /// Table loaded fine: node 1 (NPC) has text, node 2 (Script) has no entry.
    private static Conversation LoadedTableWithScriptNode() =>
        new("conv",
            [Node(1, SpeakerCategory.Npc), Node(2, SpeakerCategory.Script)],
            new StringTable([new StringEntry(1, "Hello there", "")]));

    private static ConversationViewModel LoadCanvas(Conversation conversation)
    {
        var canvas = new ConversationViewModel(new StubDispatcher());
        canvas.Load(conversation);
        return canvas;
    }

    // ── Data leak (the priority part of #84) ──────────────────────────────

    [Fact]
    public void NodeWithoutEntry_RealTextIsEmpty()
    {
        var vm = new NodeViewModel(Node(2, SpeakerCategory.Script), null);

        Assert.Equal(string.Empty, vm.DefaultText);
    }

    [Fact]
    public void NodeWithoutEntry_EditOtherField_SnapshotHasNoPlaceholder()
    {
        // Export ("Export conversations") and Find-in-Project read Canvas.BuildSnapshot().
        var canvas = LoadCanvas(LoadedTableWithScriptNode());
        var script = canvas.Nodes.Single(n => n.NodeId == 2);

        script.Comments = "edited";
        var snap = canvas.BuildSnapshot().Nodes.Single(n => n.NodeId == 2);

        Assert.Equal(string.Empty, snap.DefaultText);
    }

    [Fact]
    public void NodeWithoutEntry_EditOtherField_DiffAgainstGameData_HasNoTranslation()
    {
        // Diffing the canvas against the game-data baseline (what the diff view and a
        // reopened project use) must not see a text change on the text-less node.
        var conversation = LoadedTableWithScriptNode();
        var canvas       = LoadCanvas(conversation);
        canvas.Nodes.Single(n => n.NodeId == 2).Comments = "edited";

        var patch = DiffEngine.Diff("conv",
            ConversationSnapshotBuilder.Build(conversation), canvas.BuildSnapshot(), "en");

        Assert.Empty(patch.Translations);
    }

    [Fact]
    public void FindReplaceAll_DoesNotRewritePlaceholderIntoSavedTranslation()
    {
        // Worst case before the fix: a project-wide Replace All whose search term hit
        // the placeholder turned it into "real" node text, and the save diff then
        // recorded it as a translation that F5 would write into the game's string table.
        var canvas = LoadCanvas(LoadedTableWithScriptNode());
        var find   = new FindReplaceViewModel(canvas);
        find.SearchText  = "Unavailable";
        find.ReplaceText = "X";
        find.FindCommand.Execute(null);
        find.ReplaceAllCommand.Execute(null);

        var patch = DiffEngine.Diff("conv", canvas.BaseSnapshot!, canvas.BuildSnapshot(), "en");

        Assert.Empty(patch.Translations);
    }

    [Fact]
    public void Find_DoesNotMatchPlaceholder()
    {
        var canvas = LoadCanvas(LoadedTableWithScriptNode());
        var find   = new FindReplaceViewModel(canvas);
        find.SearchText = "Unavailable";
        find.FindCommand.Execute(null);

        Assert.Empty(find.Results);
    }

    // ── Display placeholder: loaded table, no entry ───────────────────────

    [Fact]
    public void ScriptNodeWithoutEntry_ShowsMutedScriptLabel_NotNotFoundMessage()
    {
        var canvas = LoadCanvas(LoadedTableWithScriptNode());
        var script = canvas.Nodes.Single(n => n.NodeId == 2);

        Assert.Equal("Node_NoTextScript", script.TextPlaceholder);
        Assert.True(script.HasTextPlaceholder);
        Assert.Equal(string.Empty, script.TextPreview);
    }

    [Fact]
    public void NonScriptNodeWithoutEntry_ShowsNoPlaceholder()
    {
        var vm = new NodeViewModel(Node(3, SpeakerCategory.Npc), null);

        Assert.Equal(string.Empty, vm.TextPlaceholder);
        Assert.False(vm.HasTextPlaceholder);
    }

    [Fact]
    public void NodeWithText_ShowsNoPlaceholder()
    {
        var canvas = LoadCanvas(LoadedTableWithScriptNode());
        var npc    = canvas.Nodes.Single(n => n.NodeId == 1);

        Assert.False(npc.HasTextPlaceholder);
        Assert.Equal("Hello there", npc.TextPreview);
    }

    // ── Display placeholder: string table genuinely missing ───────────────

    [Fact]
    public void MissingStringTable_ShowsNotFoundMessage_ButRealTextStaysEmpty()
    {
        var conversation = new Conversation("conv",
            [Node(1, SpeakerCategory.Npc), Node(2, SpeakerCategory.Script)],
            StringTable.Missing);
        var canvas = LoadCanvas(conversation);

        foreach (var node in canvas.Nodes)
        {
            Assert.Equal(OldPlaceholder, node.TextPlaceholder);
            Assert.Equal(string.Empty, node.DefaultText);
        }
        Assert.All(canvas.BuildSnapshot().Nodes, n => Assert.Equal(string.Empty, n.DefaultText));
    }

    [Fact]
    public void MissingStringTable_ExplicitFlagOnLoad_ShowsNotFoundMessage()
    {
        // The patched-load path rebuilds the conversation's StringTable from the
        // snapshot, so MainWindowViewModel passes the vanilla table's state explicitly.
        var patched = new Conversation("conv", [Node(1, SpeakerCategory.Npc)],
            new StringTable([new StringEntry(1, "", "")]));
        var canvas = new ConversationViewModel(new StubDispatcher());

        canvas.Load(patched, baseSnapshot: null, stringTableMissing: true);

        Assert.Equal(OldPlaceholder, canvas.Nodes.Single().TextPlaceholder);
    }

    [Fact]
    public void MissingStringTable_TypingTextClearsPlaceholder()
    {
        var vm = new NodeViewModel(Node(1, SpeakerCategory.Npc), null, stringTableMissing: true);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.DefaultText = "New line";

        Assert.False(vm.HasTextPlaceholder);
        Assert.Contains(nameof(NodeViewModel.TextPlaceholder), raised);
        Assert.Contains(nameof(NodeViewModel.HasTextPlaceholder), raised);
    }

    [Fact]
    public void ChangingSpeakerCategoryToScript_RaisesPlaceholderChange()
    {
        var vm = new NodeViewModel(Node(1, SpeakerCategory.Npc), null);
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.SpeakerCategory = SpeakerCategory.Script;

        Assert.Equal("Node_NoTextScript", vm.TextPlaceholder);
        Assert.Contains(nameof(NodeViewModel.TextPlaceholder), raised);
    }
}
