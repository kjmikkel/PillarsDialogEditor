using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelImportTests : IDisposable
{
    private readonly NodeDetailViewModel _vm = new();
    private readonly string              _gameRoot;
    private readonly string              _voRoot;
    private readonly string              _projectDir;
    private readonly string              _projectPath;
    private readonly string              _voFolder;

    public NodeDetailViewModelImportTests()
    {
        Loc.Configure(new StubStringProvider());
        _gameRoot   = Path.Combine(Path.GetTempPath(), $"VoImportTest_{Guid.NewGuid():N}");
        _voRoot     = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        _projectDir = Path.Combine(Path.GetTempPath(), $"VoImportProj_{Guid.NewGuid():N}");
        _projectPath = Path.Combine(_projectDir, "mymod.dialogproject");
        _voFolder   = Path.Combine(_projectDir, "_vo");

        Directory.CreateDirectory(_voRoot);
        Directory.CreateDirectory(_projectDir);

        _vm.GameRoot     = _gameRoot;
        _vm.ActiveGameId = "poe2";
        _vm.ProjectPath  = _projectPath;
    }

    public void Dispose()
    {
        foreach (var dir in new[] { _gameRoot, _projectDir })
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
    }

    // Plants a game-path stub and loads the node via ExternalVO (bypasses ChatterPrefixService).
    private void LoadNode()
    {
        Directory.CreateDirectory(Path.Combine(_voRoot, "eder"));
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "",
            ExternalVO: "eder/testline_0001", HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(1, "Test", "")));
    }

    // ── helpers ──────────────────────────────────────────────────────────

    // Loads a plain node with HasVO=false and no ExternalVO (the "fresh node" case).
    private void LoadFreshNode()
    {
        var node = new ConversationNode(
            NodeId: 99, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "6a99a109-0000-0000-0000-000000000000" /* Narrator */,
            ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "",
            ExternalVO: "", HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(99, "Hello", "")));
    }

    // ── CanImportVo ──────────────────────────────────────────────────────

    [Fact]
    public void CanImportVo_FalseWhenNoNode()
    {
        Assert.False(_vm.CanImportVo);
    }

    [Fact]
    public void CanImportVo_TrueEvenWhenProjectPathIsNull()
    {
        // The button stays enabled; clicking it with no project reports status and exits early.
        _vm.ProjectPath = null;
        LoadNode();
        Assert.True(_vm.CanImportVo);
    }

    [Fact]
    public void CanImportVo_TrueWhenNodeHasVoStatusAndProjectPathSet()
    {
        LoadNode();
        Assert.True(_vm.CanImportVo);
    }

    [Fact]
    public void CanImportVo_TrueWhenFreshNodeWithoutHasVO()
    {
        // A node with HasVO=false should still be importable — the command auto-sets HasVO.
        LoadFreshNode();
        Assert.True(_vm.CanImportVo);
    }

    // ── IsVoImportVisible ────────────────────────────────────────────────

    [Fact]
    public void IsVoImportVisible_FalseWhenNoNode()
    {
        Assert.False(_vm.IsVoImportVisible);
    }

    [Fact]
    public void IsVoImportVisible_TrueForPoE2NodeWithoutHasVO()
    {
        // Button must be visible even on a fresh node so users can discover it.
        LoadFreshNode();
        Assert.True(_vm.IsVoImportVisible);
    }

    [Fact]
    public void IsVoImportVisible_TrueForPoE2NodeWithHasVO()
    {
        LoadNode();
        Assert.True(_vm.IsVoImportVisible);
    }

    // ── ImportVoCommand — auto-set HasVO ──────────────────────────────────

    [Fact]
    public async Task ImportVoCommand_SetsHasVO_WhenNodeHasNoVoConfigured()
    {
        LoadFreshNode();
        _vm.Importer        = new StubVoImporter();
        _vm.ShowImportDialog = _ => Task.FromResult<VoImportDialogResult?>(null); // cancel dialog

        await _vm.ImportVoCommand.ExecuteAsync(null);

        // HasVO should be set to true before the dialog is shown.
        Assert.True(_vm.HasVO);
    }

    // ── ImportVoCommand — disabled guard ─────────────────────────────────

    [Fact]
    public async Task ImportVoCommand_ReportsStatus_WhenProjectNotSaved()
    {
        _vm.ProjectPath = null;
        LoadNode();
        string? reported = null;
        _vm.ReportStatus = msg => reported = msg;
        _vm.ShowImportDialog = _ => throw new Exception("dialog must not open");

        await _vm.ImportVoCommand.ExecuteAsync(null);

        Assert.NotNull(reported);
    }

    [Fact]
    public async Task ImportVoCommand_DoesNothing_WhenShowImportDialogIsNull()
    {
        LoadNode();
        _vm.ShowImportDialog = null;
        // Should not throw
        await _vm.ImportVoCommand.ExecuteAsync(null);
    }

    [Fact]
    public async Task ImportVoCommand_DoesNothing_WhenDialogCancelled()
    {
        LoadNode();
        var stub = new StubVoImporter();
        _vm.Importer        = stub;
        _vm.ShowImportDialog = _ => Task.FromResult<VoImportDialogResult?>(null);

        await _vm.ImportVoCommand.ExecuteAsync(null);

        Assert.Equal(0, stub.ImportCallCount);
    }

    // ── ImportVoCommand — success path ───────────────────────────────────

    [Fact]
    public async Task ImportVo_OnSuccess_RefreshesVoStatusToFound()
    {
        LoadNode();

        // Pre-plant the file in _vo/ to simulate what a successful import writes.
        var expectedDest = Path.Combine(_voFolder, "eder", "testline_0001.wem");
        Directory.CreateDirectory(Path.GetDirectoryName(expectedDest)!);
        File.WriteAllText(expectedDest, "");

        var stub = new StubVoImporter(success: true);
        _vm.Importer        = stub;
        _vm.ShowImportDialog = _ => Task.FromResult<VoImportDialogResult?>(
            new VoImportDialogResult("source.wem", null));

        await _vm.ImportVoCommand.ExecuteAsync(null);

        Assert.True(_vm.VoStatusIsFound);
    }

    [Fact]
    public async Task ImportVo_OnFailure_DoesNotFlipStatusToFound()
    {
        LoadNode();
        // No file planted → _vo/ folder is empty

        var stub = new StubVoImporter(success: false);
        _vm.Importer        = stub;
        _vm.ShowImportDialog = _ => Task.FromResult<VoImportDialogResult?>(
            new VoImportDialogResult("source.wem", null));

        await _vm.ImportVoCommand.ExecuteAsync(null);

        Assert.False(_vm.VoStatusIsFound);
    }

    // ── Local _vo/ status supplement ────────────────────────────────────

    [Fact]
    public void VoStatusIsFound_WhenFileInLocalVoFolder_AndAbsentFromGame()
    {
        LoadNode();
        // No game file planted. Plant in _vo/ instead.
        var dest = Path.Combine(_voFolder, "eder", "testline_0001.wem");
        Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
        File.WriteAllText(dest, "");

        // Trigger NotifyAllProxies by reloading.
        LoadNode();

        Assert.True(_vm.VoStatusIsFound);
    }

    // ── Stub ─────────────────────────────────────────────────────────────

    private sealed class StubVoImporter : IVoImporter
    {
        private readonly bool _success;
        public StubVoImporter(bool success = true) => _success = success;

        public bool IsWwiseAvailable => false;
        public int  ImportCallCount  { get; private set; }
        public VoImportRequest? LastRequest { get; private set; }

        public Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct)
        {
            ImportCallCount++;
            LastRequest = request;
            return Task.FromResult(new VoImportResult(_success,
                _success ? null : "Stub failure"));
        }
    }
}
