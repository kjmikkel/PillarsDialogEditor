using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Issue 172: making a node the start node renumbers it to 0 as one undoable edit.
public class MakeStartNodeTests : IDisposable
{
    private const string NarratorGuid = "6a99a109-0000-0000-0000-000000000000";
    private readonly string _dir = Path.Combine(Path.GetTempPath(), $"startnode_{Guid.NewGuid():N}");

    public MakeStartNodeTests()
    {
        Loc.Configure(new StubStringProvider());
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static ConversationNode Node(int id, params int[] to) =>
        new(id, false, SpeakerCategory.Npc, NarratorGuid, "", [.. to.Select(t => new NodeLink(id, t, []))],
            [], [], "Conversation", "None");

    /// "fresh": node 1 → node 2, as a pre-issue-162 new conversation.
    private ConversationViewModel Canvas(bool canChoose = true)
    {
        var vm = new ConversationViewModel(new StubDispatcher())
        {
            IsEditable  = true,
            ProjectPath = Path.Combine(_dir, "proj.dialogproject"),
        };
        vm.Load(new Conversation("fresh", [Node(1, 2), Node(2)],
            new StringTable([new StringEntry(1, "first", ""), new StringEntry(2, "second", "")])));
        vm.CanChooseStartNode = canChoose;
        return vm;
    }

    private static NodeViewModel ById(ConversationViewModel vm, int id) => vm.Nodes.Single(n => n.NodeId == id);

    [Fact]
    public void Load_WithoutNode0_HasNoStartNode() => Assert.False(Canvas().HasStartNode);

    [Fact]
    public void Make_RenumbersTheNode_AndTheLinksFollow()
    {
        var vm   = Canvas();
        var node = ById(vm, 1);

        Assert.Equal(StartNodeOutcome.Made, vm.MakeStartNode(node));

        Assert.Equal(0, node.NodeId);
        Assert.True(vm.HasStartNode);
        var snap = vm.BuildSnapshot();
        var n0   = snap.Nodes.Single(n => n.NodeId == 0);
        Assert.Equal("first", n0.DefaultText);
        Assert.Equal(2, Assert.Single(n0.Links).ToNodeId);
        Assert.Equal(0, Assert.Single(n0.Links).FromNodeId);
        Assert.DoesNotContain(snap.Nodes, n => n.NodeId == 1);
    }

    [Fact]
    public void Make_MovesTheNodeComment()
    {
        var vm = Canvas();
        vm.LoadNodeComments(new Dictionary<int, string> { [1] = "translator note" });

        vm.MakeStartNode(ById(vm, 1));

        Assert.Equal("translator note", vm.GetNodeComment(0));
        Assert.Equal("", vm.GetNodeComment(1));
    }

    [Fact]
    public void Make_IsOneUndoStep_AndRedoRepeatsIt()
    {
        var vm   = Canvas();
        var node = ById(vm, 1);
        vm.LoadNodeComments(new Dictionary<int, string> { [1] = "note" });
        vm.MakeStartNode(node);

        vm.Undo();
        Assert.Equal(1, node.NodeId);
        Assert.Equal("note", vm.GetNodeComment(1));
        Assert.False(vm.HasStartNode);
        Assert.False(vm.CanUndo);

        vm.Redo();
        Assert.Equal(0, node.NodeId);
        Assert.True(vm.HasStartNode);
    }

    [Fact]
    public void Make_RecordsRemaps_ForTheSave()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        vm.Undo();

        Assert.Equal(new[] { (1, 0), (0, 1) }, vm.TakePendingRemaps());
        Assert.Empty(vm.TakePendingRemaps());   // taking clears them
    }

    [Fact]
    public void Clear_DropsPendingRemaps()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        vm.Clear();
        Assert.Empty(vm.TakePendingRemaps());
    }

    [Fact]
    public void Make_RaisesStartNodeAttempted()
    {
        var vm = Canvas();
        StartNodeChangedEventArgs? seen = null;
        vm.StartNodeAttempted += (_, e) => seen = e;

        vm.MakeStartNode(ById(vm, 1));

        Assert.Equal(StartNodeOutcome.Made, seen!.Outcome);
        Assert.Equal(1, seen.OldNodeId);
        Assert.Equal(Path.Combine("narrator", "fresh_0001"), seen.OldVoPath);
    }

    [Fact]
    public void Make_RefusedForAShippedConversation()
    {
        var vm = Canvas(canChoose: false);
        Assert.Equal(StartNodeOutcome.NotAllowed, vm.MakeStartNode(ById(vm, 1)));
        Assert.Equal(1, ById(vm, 1).NodeId);
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Make_RefusedWhenReadOnly()
    {
        var vm = Canvas();
        vm.IsEditable = false;
        Assert.Equal(StartNodeOutcome.NotAllowed, vm.MakeStartNode(ById(vm, 1)));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Make_RefusedWhenNode0Exists()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        Assert.Equal(StartNodeOutcome.StartNodeExists, vm.MakeStartNode(ById(vm, 2)));
        Assert.Equal(2, ById(vm, 2).NodeId);
    }

    [Fact]
    public void HasStartNode_FollowsDeleteAndUndo()
    {
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));
        vm.DeleteNode(ById(vm, 0));
        Assert.False(vm.HasStartNode);
        vm.Undo();
        Assert.True(vm.HasStartNode);
    }

    // ── VO staged in _vo/ follows the node ────────────────────────────────

    private string Staged(string name) => Path.Combine(_dir, "_vo", "narrator", name);

    private void Stage(params string[] names)
    {
        Directory.CreateDirectory(Path.Combine(_dir, "_vo", "narrator"));
        foreach (var n in names) File.WriteAllText(Staged(n), n);
    }

    [Fact]
    public void Vo_StagedFilesAreRenamed()
    {
        Stage("fresh_0001.wem", "fresh_0001_fem.wem");
        var vm = Canvas();

        vm.MakeStartNode(ById(vm, 1));

        Assert.True(File.Exists(Staged("fresh_0000.wem")));
        Assert.True(File.Exists(Staged("fresh_0000_fem.wem")));
        Assert.False(File.Exists(Staged("fresh_0001.wem")));
    }

    [Fact]
    public void Vo_UndoRedo_MovesFilesBackAndForth()
    {
        Stage("fresh_0001.wem");
        var vm = Canvas();
        vm.MakeStartNode(ById(vm, 1));

        vm.Undo();
        Assert.True(File.Exists(Staged("fresh_0001.wem")));
        Assert.False(File.Exists(Staged("fresh_0000.wem")));

        vm.Redo();
        Assert.True(File.Exists(Staged("fresh_0000.wem")));
        Assert.True(vm.CanUndo);
    }

    [Fact]
    public void Vo_TargetExists_RefusesAndChangesNothing()
    {
        Stage("fresh_0001.wem", "fresh_0000.wem");
        var vm = Canvas();

        Assert.Equal(StartNodeOutcome.VoTargetExists, vm.MakeStartNode(ById(vm, 1)));

        Assert.Equal(1, ById(vm, 1).NodeId);
        Assert.Equal("fresh_0001.wem", File.ReadAllText(Staged("fresh_0001.wem")));
        Assert.False(vm.CanUndo);
    }

    [Fact]
    public void Vo_ExternalVoSet_RenamesNothing()
    {
        Stage("fresh_0001.wem");
        var vm = Canvas();
        ById(vm, 1).ExternalVO = "narrator/shared_line";

        vm.MakeStartNode(ById(vm, 1));

        Assert.True(File.Exists(Staged("fresh_0001.wem")));
    }

    [Fact]
    public void Vo_RenameFails_ChangesNothing()
    {
        Stage("fresh_0001.wem");
        var vm = Canvas();
        using (File.Open(Staged("fresh_0001.wem"), FileMode.Open, FileAccess.Read, FileShare.None))   // locked: the move fails
            Assert.Equal(StartNodeOutcome.VoRenameFailed, vm.MakeStartNode(ById(vm, 1)));

        Assert.Equal(1, ById(vm, 1).NodeId);
        Assert.True(File.Exists(Staged("fresh_0001.wem")));
        Assert.False(vm.CanUndo);
    }
}
