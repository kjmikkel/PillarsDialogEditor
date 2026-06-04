using System.Linq;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class DiffViewModelApplyTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public DiffViewModelApplyTests() => Loc.Configure(new StubStringProvider());

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best-effort */ }
    }

    // ── existing pure-VM tests (keep) ─────────────────────────────────────
    [Fact]
    public void ConversationGroup_TogglingAll_SelectsEveryNode()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: [3]);
        var group  = new ConversationChangeViewModel(change);
        group.IsAllSelected = true;
        Assert.All(group.Nodes, n => Assert.True(n.IsSelected));
    }

    [Fact]
    public void ConversationGroup_IsAllSelected_IsNull_WhenPartiallySelected()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: []);
        var group  = new ConversationChangeViewModel(change);
        group.Nodes[0].IsSelected = true;
        Assert.Null(group.IsAllSelected);
    }

    [Fact]
    public void ConversationGroup_SelectedNodeIds_ReflectsChecked()
    {
        var change = new ConversationChange("c", Added: [1, 2], Removed: [], Modified: []);
        var group  = new ConversationChangeViewModel(change);
        group.Nodes[0].IsSelected = true;
        Assert.Equal([1], group.SelectedNodeIds);
    }

    // ── CanApply / selection-tree tests ───────────────────────────────────
    [Fact]
    public void CanApply_False_WhenNeitherEndpointIsWorkingCopy()
    {
        var vm = MakeWorkingVsRef();
        // Move BOTH endpoints to the git ref → ref vs ref (no working copy involved).
        var gitRefOption = vm.EndpointOptions.First(o => o.Endpoint is DiffEndpoint.GitRef);
        vm.LeftEndpoint  = gitRefOption;
        vm.RightEndpoint = gitRefOption;
        Assert.False(vm.CanApply);
    }

    [Fact]
    public void CanApply_False_WhenNothingSelected()
    {
        var vm = MakeWorkingVsRef();
        Assert.False(vm.CanApply);
    }

    [Fact]
    public void CanApply_True_WhenWorkingCopyIsAnEndpoint_AndNodesSelected()
    {
        var vm = MakeWorkingVsRef();
        vm.Groups[0].IsAllSelected = true;
        Assert.True(vm.CanApply);
    }

    // ── fixture helpers ───────────────────────────────────────────────────
    // Working copy (disk) = greeting nodes [1,9]; ref "main" = greeting node [1].
    // Default endpoints: Left = working copy = TARGET, Right = main (git ref) = SOURCE.
    private DiffViewModel MakeWorkingVsRef()
    {
        var disk = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1), Node(9)], [], []));
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1)], [], []));
        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, DialogProjectSerializer.Serialize(refProject), "main\n");
        return new DiffViewModel(git, new StubDispatcher(), path);
    }

    private string WriteTempProject(DialogProject project)
    {
        var path = Path.Combine(Path.GetTempPath(), $"diffapply_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        File.WriteAllText(path, DialogProjectSerializer.Serialize(project));
        return path;
    }

    private static NodeEditSnapshot Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    private static NodeEditSnapshot NodeWithLink(int id, int toId) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false,
            [new LinkEditSnapshot(id, toId, 1f, "", false)], [], []);

    private static FakeGit MakeFakeGit(string projectDir, string? refContent, string branchOutput = "")
        => new(args =>
        {
            if (args is ["rev-parse", "--show-toplevel"]) return new GitResult(0, projectDir + "\n", "");
            if (args.Length == 2 && args[0] == "show")
                return refContent is null ? new GitResult(128, "", "fatal: bad ref") : new GitResult(0, refContent, "");
            if (args.Length >= 1 && args[0] == "branch") return new GitResult(0, branchOutput, "");
            if (args.Length >= 1 && args[0] == "log")    return new GitResult(0, "", "");
            return new GitResult(0, "", "");
        });

    // Source (ref/right) HAS node 9; target (working copy/left) lacks it.
    // Bringing in node 9 adds it to the working copy.
    private DiffViewModel MakeRefHasExtraNode()
    {
        var disk = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1)], [], []));
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1), Node(9)], [], []));
        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, DialogProjectSerializer.Serialize(refProject), "main\n");
        return new DiffViewModel(git, new StubDispatcher(), path);
    }

    // Source (ref/right) adds node 5 (which links to node 8) AND deletes node 8.
    // Target (working copy/left) has neither. Bringing both in leaves node 5's
    // link pointing at deleted node 8 → a dangling link.
    private DiffViewModel MakeDanglingScenario()
    {
        var disk = DialogProject.Empty("p");
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [NodeWithLink(5, 8)], [8], []));
        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, DialogProjectSerializer.Serialize(refProject), "main\n");
        return new DiffViewModel(git, new StubDispatcher(), path);
    }

    [Fact]
    public void SelectedGroup_SettingIt_SetsMatchingSelectedChange()
    {
        var vm = MakeWorkingVsRef();           // existing fixture; has a "greeting" change
        vm.SelectedGroup = vm.Groups.First(g => g.Name == "greeting");
        Assert.Equal("greeting", vm.Selected?.Name);
    }

    [Fact]
    public void Apply_RaisesCommitApply_BringingInSelectedNode()
    {
        var vm = MakeRefHasExtraNode();
        // node 9 appears as a change; tick it.
        vm.Groups[0].Nodes.First(n => n.NodeId == 9).IsSelected = true;
        DialogProject? committed = null;
        vm.CommitApply = p => committed = p;

        vm.ApplyCommand.Execute(null);

        Assert.NotNull(committed);
        Assert.Contains(committed!.Patches["greeting"].AddedNodes, n => n.NodeId == 9);
    }

    [Fact]
    public void Apply_PopulatesDanglingWarning_WhenSelectionLeavesADanglingLink()
    {
        var vm = MakeDanglingScenario();
        foreach (var g in vm.Groups) g.IsAllSelected = true;
        vm.CommitApply = _ => { };

        vm.ApplyCommand.Execute(null);

        Assert.NotEmpty(vm.DanglingLinks);
    }

    [Fact]
    public void Apply_PopulatesDanglingLinkDescriptions_MatchingDanglingLinks()
    {
        var vm = MakeDanglingScenario();
        foreach (var g in vm.Groups) g.IsAllSelected = true;
        vm.CommitApply = _ => { };

        vm.ApplyCommand.Execute(null);

        Assert.NotEmpty(vm.DanglingLinkDescriptions);
        Assert.Equal(vm.DanglingLinks.Count, vm.DanglingLinkDescriptions.Count);
        // StubStringProvider returns the key verbatim, so every row is the format key.
        Assert.All(vm.DanglingLinkDescriptions, d => Assert.Equal("Diff_DanglingRow", d));
    }

    [Fact]
    public void AppliedPreview_TintsBroughtInNode_AsAdded()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var baseSnap = new ConversationEditSnapshot([Node(1), Node(2)]);
        var provider = new StubProvider(file, baseSnap);

        // working copy (target/left) = [1]; ref (source/right) = [1,2]
        var disk = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [Node(1)], [], []));
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, [Node(1), Node(2)], [], []));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, DialogProjectSerializer.Serialize(refProject), "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");

        // node 2 shows as a change (Added: ref/right has it, working copy/left doesn't). Tick it.
        vm.Groups[0].Nodes.First(n => n.NodeId == 2).IsSelected = true;
        vm.CanvasMode = CanvasMode.AppliedPreview;
        vm.Selected   = vm.Changes.First(c => c.Name == convName);

        Assert.NotNull(vm.DiffCanvas);
        var node2 = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 2);
        Assert.Equal(DiffStatus.Added, node2.DiffStatus);
    }

    private sealed class FakeGit(Func<string[], GitResult> handler) : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => handler(args);
    }
}
