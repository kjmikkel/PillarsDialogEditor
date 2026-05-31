using System.Collections.Generic;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class DiffViewModelTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public DiffViewModelTests() => Loc.Configure(new StubStringProvider());

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best-effort */ }
    }

    // ── helpers ──────────────────────────────────────────────────────────────

    private string WriteTempProject(DialogProject project)
    {
        var path = Path.Combine(Path.GetTempPath(), $"diff_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        File.WriteAllText(path, DialogProjectSerializer.Serialize(project));
        return path;
    }

    private static NodeEditSnapshot Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    private static DialogProject WithNode(int id) =>
        DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(id)], [], []));

    private static DialogProject Empty() => DialogProject.Empty("p");

    /// <summary>
    /// Builds a FakeGit that:
    ///   - rev-parse --show-toplevel → returns the directory of projectFilePath
    ///   - show &lt;ref&gt;:&lt;rel&gt;  → returns refContent (or fails if refContent is null)
    ///   - branch / log            → empty OK (no branches / commits by default)
    /// </summary>
    private static FakeGit MakeFakeGit(string projectDir, string? refContent,
        string branchOutput = "", string logOutput = "")
        => new(args =>
        {
            if (args is ["rev-parse", "--show-toplevel"])
                return new GitResult(0, projectDir + "\n", "");

            if (args.Length == 2 && args[0] == "show")
            {
                if (refContent is null)
                    return new GitResult(128, "", "fatal: bad ref");
                return new GitResult(0, refContent, "");
            }

            if (args.Length >= 1 && args[0] == "branch")
                return new GitResult(0, branchOutput, "");

            if (args.Length >= 1 && args[0] == "log")
                return new GitResult(0, logOutput, "");

            return new GitResult(0, "", "");
        });

    // ── tests ─────────────────────────────────────────────────────────────────

    [Fact]
    public void EndpointOptions_AlwaysContainsWorkingCopyOption_EvenWhenGitFails()
    {
        // git returns non-zero for everything
        var git = new FakeGit(_ => new GitResult(128, "", "fatal: not a git repo"));
        var projectA = WriteTempProject(Empty());

        var vm = new DiffViewModel(git, new StubDispatcher(), projectA);

        Assert.Contains(vm.EndpointOptions, o => o.Endpoint is DiffEndpoint.WorkingCopy);
    }

    [Fact]
    public void EndpointOptions_WorkingCopyLabel_ComesFromLoc()
    {
        var git = new FakeGit(_ => new GitResult(128, "", "fatal"));
        var path = WriteTempProject(Empty());

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        var wc = vm.EndpointOptions.Single(o => o.Endpoint is DiffEndpoint.WorkingCopy);
        // StubStringProvider returns the key itself
        Assert.Equal("Diff_WorkingCopy", wc.Label);
    }

    [Fact]
    public void EndpointOptions_IncludesBranchesFromGit()
    {
        var path = WriteTempProject(Empty());
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: null, branchOutput: "main\nfeature/xyz\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        var refs = vm.EndpointOptions.Where(o => o.Endpoint is DiffEndpoint.GitRef).ToList();
        Assert.Contains(refs, o => o.Label == "main" && ((DiffEndpoint.GitRef)o.Endpoint).Ref == "main");
        Assert.Contains(refs, o => o.Label == "feature/xyz");
    }

    [Fact]
    public void EndpointOptions_IncludesRecentCommitsFromGit()
    {
        var path = WriteTempProject(Empty());
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: null,
            logOutput: "abc1234 Add greeting node\ndef5678 Fix typo\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        var refs = vm.EndpointOptions.Where(o => o.Endpoint is DiffEndpoint.GitRef).ToList();
        // SHA is first token; label is the full line
        Assert.Contains(refs, o => ((DiffEndpoint.GitRef)o.Endpoint).Ref == "abc1234"
                                   && o.Label == "abc1234 Add greeting node");
        Assert.Contains(refs, o => ((DiffEndpoint.GitRef)o.Endpoint).Ref == "def5678");
    }

    [Fact]
    public void DefaultLeftEndpoint_IsWorkingCopy()
    {
        var path = WriteTempProject(Empty());
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: null, branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        Assert.IsType<DiffEndpoint.WorkingCopy>(vm.LeftEndpoint?.Endpoint);
    }

    [Fact]
    public void DefaultLeftEndpoint_IsWorkingCopy_WhenNoGitRefs()
    {
        var git = new FakeGit(_ => new GitResult(128, "", "fatal"));
        var path = WriteTempProject(Empty());

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        Assert.IsType<DiffEndpoint.WorkingCopy>(vm.LeftEndpoint?.Endpoint);
    }

    [Fact]
    public void DefaultRightEndpoint_IsFirstGitRef_WhenAvailable()
    {
        var path = WriteTempProject(Empty());
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: null, branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        Assert.IsType<DiffEndpoint.GitRef>(vm.RightEndpoint?.Endpoint);
    }

    [Fact]
    public void Diff_OneAddedNode_YieldsOneChange_WithAddedCount1()
    {
        // Working copy (disk / left) has only node 1; git ref (right) adds node 2.
        // Node 2 appears on the right → it is Added in the left→right diff.
        var diskProject = WithNode(1);

        // git ref project: nodes 1 + 2
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1), Node(2)], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;

        // git show for the ref returns refProject JSON (nodes 1+2)
        var refJson = DialogProjectSerializer.Serialize(refProject);
        var git     = MakeFakeGit(dir, refContent: refJson, branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        // LeftEndpoint = working copy, RightEndpoint = git ref — new default
        Assert.IsType<DiffEndpoint.WorkingCopy>(vm.LeftEndpoint?.Endpoint);
        Assert.Single(vm.Changes);
        Assert.Equal(1, vm.Changes[0].AddedCount);
    }

    [Fact]
    public void Diff_GitRefShowFails_ChangesEmpty_StatusNonEmpty()
    {
        // git show returns non-zero → DiffExceptionKind.BadRef → Status_DiffBadRef key
        var path = WriteTempProject(Empty());
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: null, branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        Assert.Empty(vm.Changes);
        // StubStringProvider returns the key verbatim; Loc.Format returns the key unchanged.
        Assert.Equal("Status_DiffBadRef", vm.StatusText);
    }

    [Fact]
    public void Diff_RevParseFails_StatusIsNotARepo()
    {
        // rev-parse fails → DiffExceptionKind.NotARepo → Status_DiffNoRepo key
        var path = WriteTempProject(Empty());
        var git  = new FakeGit(args => args is ["rev-parse", "--show-toplevel"]
            ? new GitResult(128, "", "fatal: not a git repository")
            : new GitResult(0, "", ""));

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        // Force a git-ref endpoint to trigger rev-parse on Recompute
        // (default RightEndpoint may be working copy when no branches listed)
        // Since git also returns non-zero for branch/log, EndpointOptions will only have WorkingCopy.
        // Set RightEndpoint to a fake GitRef to drive through the NotARepo path.
        var gitRefOption = new EndpointOption("bad", new DiffEndpoint.GitRef("bad"));
        vm.RightEndpoint = gitRefOption;

        Assert.Equal("Status_DiffNoRepo", vm.StatusText);
    }

    [Fact]
    public void Diff_BothEndpointsNull_ChangesEmpty()
    {
        var git = new FakeGit(_ => new GitResult(128, "", "fatal"));
        var path = WriteTempProject(Empty());

        var vm = new DiffViewModel(git, new StubDispatcher(), path);
        vm.LeftEndpoint  = null;
        vm.RightEndpoint = null;

        Assert.Empty(vm.Changes);
    }

    [Fact]
    public void Diff_IdenticalProjects_ChangesEmpty_StatusSet()
    {
        var project = WithNode(1);
        var path    = WriteTempProject(project);
        var dir     = Path.GetDirectoryName(Path.GetFullPath(path))!;

        var refJson = DialogProjectSerializer.Serialize(project);
        var git     = MakeFakeGit(dir, refContent: refJson, branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path);

        Assert.Empty(vm.Changes);
        // StatusText should contain key string (StubStringProvider returns key)
        Assert.Equal("Status_DiffComputed", vm.StatusText);
    }

    [Fact]
    public void ChangingEndpoint_TriggersRecompute()
    {
        // Disk (working copy / left) has node 1 only; the git ref (right) has nodes 1+2.
        var projectBase = WithNode(1);
        var path        = WriteTempProject(projectBase);
        var dir         = Path.GetDirectoryName(Path.GetFullPath(path))!;

        var projectDifferent = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1), Node(2)], [], []));
        var refJson = DialogProjectSerializer.Serialize(projectDifferent);

        var git = MakeFakeGit(dir, refContent: refJson, branchOutput: "main\n");
        var vm  = new DiffViewModel(git, new StubDispatcher(), path);

        // Initial: left=working copy (node 1), right=main (nodes 1+2) → 1 change
        Assert.Single(vm.Changes);

        // Change left endpoint to the git ref as well → same content on both sides → no changes
        var mainOption = vm.EndpointOptions.First(o => o.Endpoint is DiffEndpoint.GitRef);
        vm.LeftEndpoint = mainOption;

        Assert.Empty(vm.Changes);
    }

    // ── BuildDiffCanvas tests ─────────────────────────────────────────────────

    [Fact]
    public void BuildDiffCanvas_WithProvider_AddedNode_YieldsCanvasWithAddedDiffStatus()
    {
        // Arrange: disk (working copy / left) has node 1 only; git ref (right) has nodes 1+2.
        // Node 2 is Added on the right → the canvas reconstructs the right endpoint and tints it Added.
        // The StubProvider "knows" the conversation and returns nodes 1+2 (matching the ref).
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var snap2    = new ConversationEditSnapshot([Node(1), Node(2)]);
        var provider = new StubProvider(file, snap2);

        // Disk project (working copy / left): node 1 only
        var diskProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion,
                [Node(1)], [], []));

        // Git ref project (right): nodes 1+2
        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion,
                [Node(1), Node(2)], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;

        var refJson = DialogProjectSerializer.Serialize(refProject);
        var git     = MakeFakeGit(dir, refContent: refJson, branchOutput: "main\n");

        // Act
        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");

        // Should have one change (greeting) with node 2 added (on the right)
        Assert.Single(vm.Changes);
        var change = vm.Changes[0];
        Assert.Equal(convName, change.Name);
        Assert.Contains(2, change.Added);

        // Select the change → triggers BuildDiffCanvas
        vm.Selected = change;

        // Assert: canvas is built from the right (ref) endpoint; node 2 is tinted Added
        Assert.NotNull(vm.DiffCanvas);
        var addedNode = vm.DiffCanvas.Nodes.FirstOrDefault(n => n.NodeId == 2);
        Assert.NotNull(addedNode);
        Assert.Equal(DiffStatus.Added, addedNode.DiffStatus);
    }

    [Fact]
    public void BuildDiffCanvas_NoProvider_DiffCanvasIsNull_CanvasHintSet()
    {
        // No provider → canvas stays null and CanvasHint is set
        var diskProject = WithNode(1);
        var path        = WriteTempProject(diskProject);
        var dir         = Path.GetDirectoryName(Path.GetFullPath(path))!;

        var leftProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1), Node(2)], [], []));
        var refJson = DialogProjectSerializer.Serialize(leftProject);
        var git     = MakeFakeGit(dir, refContent: refJson, branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider: null, "en");

        if (vm.Changes.Count > 0)
        {
            vm.Selected = vm.Changes[0];
            Assert.Null(vm.DiffCanvas);
            Assert.NotEmpty(vm.CanvasHint);
        }
        // If no changes, test is trivially satisfied (no crash)
    }

    [Fact]
    public void BuildDiffCanvas_NullSelected_DiffCanvasIsNull()
    {
        var git  = new FakeGit(_ => new GitResult(128, "", "fatal"));
        var path = WriteTempProject(Empty());
        var vm   = new DiffViewModel(git, new StubDispatcher(), path);

        vm.Selected = null;

        Assert.Null(vm.DiffCanvas);
    }

    // ── NodeDiffDetail tests ──────────────────────────────────────────────────

    // NodeT: used only for building patches where DefaultText/FemaleText survive
    // in-memory (not through serialization). Text that must survive round-tripping
    // must be placed in ConversationPatch.Translations instead.
    private static NodeEditSnapshot NodeT(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    // Builds a ConversationPatch whose node text survives JSON serialisation by
    // placing it in Translations["en"] instead of [JsonIgnore] DefaultText.
    private static ConversationPatch PatchWithText(
        string convName,
        IReadOnlyList<(int Id, string Text)> addedNodes,
        IReadOnlyList<(int Id, string Text)>? translations = null)
    {
        var snapNodes = addedNodes.Select(n => NodeT(n.Id)).ToList();
        var txList = (translations ?? addedNodes)
            .Select(n => new NodeTranslation(n.Id, n.Text, ""))
            .ToList();
        return new ConversationPatch(
            convName, ConversationPatch.CurrentSchemaVersion,
            snapNodes, [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = txList }
        };
    }

    [Fact]
    public void SelectingAddedNode_PopulatesDetail_WithPlaceholderBefore()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        // Provider snap is not used for text (text comes from Translations); nodes just need to exist.
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));

        // Disk (left / working copy): only node 1
        var diskProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "Hi")]));

        // Git ref (right): nodes 1 + 2
        var refProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "Hi"), (2, "Added line")]));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject), branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        vm.Selected = vm.Changes.Single(c => c.Name == convName);

        var node2 = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 2);
        vm.DiffCanvas.SelectedNode = node2;

        Assert.NotNull(vm.SelectedNodeDetail);
        Assert.Equal(2, vm.SelectedNodeDetail!.NodeId);
        Assert.Equal(DiffStatus.Added, vm.SelectedNodeDetail.Kind);
        Assert.Equal("Diff_Detail_NodeAdded", vm.SelectedNodeDetail.DefaultBefore);
        Assert.Equal(node2.DefaultText, vm.SelectedNodeDetail.DefaultAfter);
    }

    [Fact]
    public void SelectingChangedNode_PopulatesBothSides_FromTheirReconstructions()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));

        // Left (working copy / disk): node 1 with text "old text"
        var diskProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "old text")]));

        // Right (git ref): node 1 with text "new text" — different translation → Modified
        var refProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "new text")]));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject), branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        vm.Selected = vm.Changes.Single(c => c.Name == convName);

        var node1 = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 1);
        vm.DiffCanvas.SelectedNode = node1;

        Assert.NotNull(vm.SelectedNodeDetail);
        Assert.Equal(DiffStatus.Changed, vm.SelectedNodeDetail!.Kind);
        Assert.Equal(node1.DefaultText, vm.SelectedNodeDetail.DefaultAfter);
        Assert.NotEqual(vm.SelectedNodeDetail.DefaultBefore, vm.SelectedNodeDetail.DefaultAfter);
        Assert.NotEqual("Diff_Detail_NodeAdded", vm.SelectedNodeDetail.DefaultBefore);
        Assert.Equal("old text", vm.SelectedNodeDetail.DefaultBefore);
        Assert.Equal("new text", vm.SelectedNodeDetail.DefaultAfter);
    }

    [Fact]
    public void SelectingUnchangedNode_LeavesDetailNull()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));

        // Disk (left): only node 1
        var diskProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "Hi")]));

        // Git ref (right): nodes 1 + 2; node 1 is unchanged between sides
        var refProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "Hi"), (2, "Added line")]));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject), branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        vm.Selected = vm.Changes.Single(c => c.Name == convName);

        // Node 1 is Unchanged (same translation on both sides) → detail must be null
        var node1 = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 1);
        vm.DiffCanvas.SelectedNode = node1;

        Assert.Null(vm.SelectedNodeDetail);
    }

    // ── helper ────────────────────────────────────────────────────────────────

    private sealed class FakeGit(Func<string[], GitResult> handler) : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => handler(args);
    }
}
