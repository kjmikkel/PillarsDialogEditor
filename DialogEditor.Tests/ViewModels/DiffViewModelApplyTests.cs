using DialogEditor.Core.Editing;
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
        // Move the right endpoint off the working copy onto the git ref → ref vs ref.
        vm.RightEndpoint = vm.EndpointOptions.First(o => o.Endpoint is DiffEndpoint.GitRef);
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
    // Default endpoints: Left = main (git ref) = SOURCE, Right = working copy = TARGET.
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

    private sealed class FakeGit(Func<string[], GitResult> handler) : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => handler(args);
    }
}
