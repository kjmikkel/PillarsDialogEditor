using DialogEditor.Core.Audio;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// PoE1 voice preview (issue 4): play-only, from the resources.assets index.
public class NodeDetailViewModelPoe1VoiceTests : IDisposable
{
    private const string Conv = "00_cv_test";

    private readonly NodeDetailViewModel _vm = new();
    private readonly StubPlayer _stub = new();
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poe1vm-" + Guid.NewGuid().ToString("N"));
    private string DataDir => Path.Combine(_root, "data");
    private readonly List<string> _extracted = [];

    public NodeDetailViewModelPoe1VoiceTests()
    {
        Loc.Configure(new StubStringProvider());
        Poe1VoiceService.Clear();
        VoAliasIndexService.Clear();
        Directory.CreateDirectory(DataDir);

        var canvas = new ConversationViewModel(new StubDispatcher());
        canvas.Load(new Conversation(Conv, [], new StringTable([])));
        _vm.Canvas = canvas;
        _vm.Player = _stub;
        _vm.GameRoot = _root;
        _vm.ActiveGameId = "poe1";
    }

    public void Dispose()
    {
        Poe1VoiceService.Clear();
        foreach (var p in _extracted) try { File.Delete(p); } catch { /* best-effort */ }
        try { Directory.Delete(_root, true); } catch { /* best-effort */ }
    }

    // Slices at 0 (primary) and 8 (fem) of a fake resources.resource; both start with FSB5.
    private Poe1VoiceIndex Index(bool primary = true, bool fem = true)
    {
        File.WriteAllBytes(Path.Combine(DataDir, "resources.resource"), "FSB5aaaaFSB5bbbb"u8.ToArray());
        var recs = new List<Poe1AudioClipRecord>();
        if (primary) recs.Add(new($"{Conv}_0001", 48000, 2f, "resources.resource", 0, 8));
        if (fem) recs.Add(new($"{Conv}_0001_fem", 48000, 2f, "resources.resource", 8, 8));
        return new Poe1VoiceIndex(recs, DataDir);
    }

    private void Load(bool femText = false, string externalVo = "")
    {
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None", ActorDirection: "", Comments: "",
            ExternalVO: externalVo, HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(1, "Line", femText ? "Fem line" : "")));
    }

    [Fact]
    public void NodeWithClip_ShowsFoundAndCanPlay()
    {
        Poe1VoiceService.RegisterForTests(Index(fem: false));
        Load();

        Assert.True(_vm.HasVoStatus);
        Assert.True(_vm.VoStatusIsFound);
        Assert.True(_vm.CanPlayAudio);
        Assert.False(_vm.CanPlayFem);
    }

    [Fact]
    public void PlayPrimary_ExtractsAndPlaysAnExistingFsbFile()
    {
        Poe1VoiceService.RegisterForTests(Index());
        Load();

        _vm.PlayPrimaryCommand.Execute(null);

        Assert.Equal(1, _stub.PlayCount);
        Assert.NotNull(_stub.LastPath);
        _extracted.Add(_stub.LastPath!);
        Assert.EndsWith(".fsb", _stub.LastPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(_stub.LastPath));
        Assert.True(_vm.IsPlayingPrimary);
    }

    [Fact]
    public void FemaleClip_OnlyOfferedWhenNodeHasFemaleText()
    {
        Poe1VoiceService.RegisterForTests(Index());

        Load(femText: false);
        Assert.False(_vm.CanPlayFem);

        Load(femText: true);
        Assert.True(_vm.CanPlayFem);
        Assert.Equal("NodeDetail_VoFoundWithFem", _vm.VoiceSummary);
    }

    [Fact]
    public void PlayFem_PlaysTheFemaleClip()
    {
        Poe1VoiceService.RegisterForTests(Index());
        Load(femText: true);

        _vm.PlayFemCommand.Execute(null);

        Assert.NotNull(_stub.LastPath);
        _extracted.Add(_stub.LastPath!);
        Assert.StartsWith(Conv + "_0001_fem.", Path.GetFileName(_stub.LastPath), StringComparison.OrdinalIgnoreCase);
        Assert.True(_vm.IsPlayingFem);
    }

    [Fact]
    public void FemaleTextButNoFemaleClip_NoFemButton()
    {
        Poe1VoiceService.RegisterForTests(Index(fem: false));
        Load(femText: true);

        Assert.True(_vm.CanPlayAudio);
        Assert.False(_vm.CanPlayFem);
    }

    [Fact]
    public void NodeWithoutClip_HasNoVoStatusAndShowsNone()
    {
        Poe1VoiceService.RegisterForTests(Index(primary: false, fem: false));
        Load();

        Assert.False(_vm.HasVoStatus);
        Assert.False(_vm.CanPlayAudio);
        Assert.Equal("NodeDetail_NoneShort", _vm.VoiceSummary);
    }

    [Fact]
    public void ServiceNotReady_HasNoVoStatus()
    {
        Load();

        Assert.False(_vm.HasVoStatus);
        Assert.Equal("NodeDetail_NoneShort", _vm.VoiceSummary);
    }

    [Fact]
    public void ImportAndAliasUi_AreHiddenForPoe1_EvenWithAClip()
    {
        Poe1VoiceService.RegisterForTests(Index());
        Load(externalVo: "someone/line_0001");

        Assert.True(_vm.HasVoStatus);
        Assert.False(_vm.IsVoImportVisible);
        Assert.False(_vm.CanImportVo);
        Assert.False(_vm.ShowImportVoMenuItem);
        Assert.False(_vm.HasVoAlias);
        Assert.False(_vm.CanStartVoAliasPick);
        Assert.False(_vm.ImportVoCommand.CanExecute(null));
    }

    [Fact]
    public void ServiceChangedAfterNodeSelected_UpdatesTheNode()
    {
        Load();
        Assert.False(_vm.HasVoStatus);

        Poe1VoiceService.RegisterForTests(Index());

        Assert.True(_vm.VoStatusIsFound);
        Assert.True(_vm.CanPlayAudio);
    }

    [Fact]
    public void ServiceCleared_RemovesTheStatus()
    {
        Poe1VoiceService.RegisterForTests(Index());
        Load();
        Assert.True(_vm.VoStatusIsFound);

        Poe1VoiceService.Clear();

        Assert.False(_vm.HasVoStatus);
    }

    [Fact]
    public void PlayPrimary_ExtractionFails_DoesNotPlay()
    {
        // Slice runs past the end of the file, so Extract returns null.
        File.WriteAllBytes(Path.Combine(DataDir, "resources.resource"), "FSB5"u8.ToArray());
        Poe1VoiceService.RegisterForTests(new Poe1VoiceIndex(
            [new Poe1AudioClipRecord($"{Conv}_0001", 48000, 2f, "resources.resource", 0, 800)], DataDir));
        Load();

        _vm.PlayPrimaryCommand.Execute(null);

        Assert.Equal(0, _stub.PlayCount);
        Assert.False(_vm.IsPlayingPrimary);
    }

    [Fact]
    public void Poe2_IsUnaffectedByPoe1Service()
    {
        Poe1VoiceService.RegisterForTests(Index());
        _vm.ActiveGameId = "poe2";
        Load();

        // PoE2 with no planted .wem: resolver result, not the PoE1 clip.
        Assert.True(_vm.IsVoImportVisible);
        Assert.False(_vm.CanPlayAudio);
    }

    private sealed class StubPlayer : IVoAudioPlayer
    {
        public bool IsAvailable => true;
        public event Action? PlaybackStopped { add { } remove { } }
        public string? LastPath { get; private set; }
        public int PlayCount { get; private set; }
        public void Play(string path) { LastPath = path; PlayCount++; }
        public void Stop() { }
    }
}
