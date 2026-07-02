using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelPlaybackTests : IDisposable
{
    private readonly NodeDetailViewModel _vm = new();
    private readonly StubVoAudioPlayer   _stub;
    private readonly string              _gameRoot;
    private readonly string              _voRoot;

    public NodeDetailViewModelPlaybackTests()
    {
        Loc.Configure(new StubStringProvider());
        _stub     = new StubVoAudioPlayer();
        _gameRoot = Path.Combine(Path.GetTempPath(), $"VoPlayTest_{Guid.NewGuid():N}");
        _voRoot   = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Directory.CreateDirectory(_voRoot);

        _vm.Player       = _stub;
        _vm.GameRoot     = _gameRoot;
        _vm.ActiveGameId = "poe2";
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    // Plants real .wem stubs under _voRoot and loads the node so VoPathResolver
    // resolves it as Found. Uses ExternalVO to bypass ChatterPrefixService.
    private void PlantAndLoad(bool withFem = false)
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "testline_0001.wem"), "");
        if (withFem)
            File.WriteAllText(Path.Combine(dir, "testline_0001_fem.wem"), "");

        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "",
            ExternalVO: "eder/testline_0001", HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(1, "Test line", withFem ? "Fem line" : "")));
    }

    // ── CanPlayAudio / CanPlayFem ─────────────────────────────────────────

    [Fact]
    public void CanPlayAudio_TrueWhenPlayerAvailableAndFileFound()
    {
        PlantAndLoad();
        Assert.True(_vm.CanPlayAudio);
    }

    [Fact]
    public void CanPlayAudio_FalseWhenPlayerUnavailable()
    {
        _stub.IsAvailable = false;
        PlantAndLoad();
        Assert.False(_vm.CanPlayAudio);
    }

    [Fact]
    public void CanPlayFem_FalseWhenNoFemVariant()
    {
        PlantAndLoad(withFem: false);
        Assert.False(_vm.CanPlayFem);
    }

    [Fact]
    public void CanPlayFem_TrueWhenFemVariantExists()
    {
        PlantAndLoad(withFem: true);
        Assert.True(_vm.CanPlayFem);
    }

    // ── PlayPrimaryCommand ────────────────────────────────────────────────

    [Fact]
    public void PlayPrimaryCommand_CallsPlayerPlay()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.Equal(1, _stub.PlayCallCount);
        Assert.True(_stub.LastPlayPath?.EndsWith(".wem", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlayPrimaryCommand_SetsIsPlayingPrimaryTrue()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.True(_vm.IsPlayingPrimary);
        Assert.False(_vm.IsPlayingFem);
    }

    [Fact]
    public void PlayPrimaryCommand_GlyphChangesToStop()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.Equal("■", _vm.PlayPrimaryGlyph);
    }

    [Fact]
    public void PlayPrimaryCommand_WhenAlreadyPlayingPrimary_StopsAndResetsState()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null); // start
        _vm.PlayPrimaryCommand.Execute(null); // toggle off
        Assert.False(_vm.IsPlayingPrimary);
        Assert.Equal("▶", _vm.PlayPrimaryGlyph);
        Assert.Equal(1, _stub.PlayCallCount); // only played once
    }

    [Fact]
    public void PlayPrimaryCommand_FileOnlyInProjectVoFolder_PlaysLocalCopy()
    {
        // After F6 the synced game copy is gone; the VO lives in the project's
        // _vo/ folder. The status row shows ✓ via the fallback — the play button
        // must target the file that actually exists, not the removed game copy.
        var projectDir = Path.Combine(_gameRoot, "proj");
        var localDir   = Path.Combine(projectDir, "_vo", "eder");
        Directory.CreateDirectory(localDir);
        var localWem = Path.Combine(localDir, "testline_0001.wem");
        File.WriteAllText(localWem, "");
        _vm.ProjectPath = Path.Combine(projectDir, "test.dialogproject");

        // Load the node without planting the game copy.
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "",
            ExternalVO: "eder/testline_0001", HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(1, "Test line", "")));

        Assert.True(_vm.CanPlayAudio);
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.Equal(localWem, _stub.LastPlayPath);
    }

    // ── PlayFemCommand ────────────────────────────────────────────────────

    [Fact]
    public void PlayFemCommand_SetsIsPlayingFemTrue()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayFemCommand.Execute(null);
        Assert.True(_vm.IsPlayingFem);
        Assert.False(_vm.IsPlayingPrimary);
    }

    [Fact]
    public void PlayFemCommand_WhilePrimaryPlaying_SwitchesToFem()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.True(_vm.IsPlayingPrimary);

        _vm.PlayFemCommand.Execute(null); // switch mid-play
        Assert.False(_vm.IsPlayingPrimary);
        Assert.True(_vm.IsPlayingFem);
        Assert.Equal(2, _stub.PlayCallCount); // two plays: primary then fem
    }

    [Fact]
    public void PlayFemCommand_WhenAlreadyPlayingFem_Stops()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayFemCommand.Execute(null); // start fem
        _vm.PlayFemCommand.Execute(null); // toggle off
        Assert.False(_vm.IsPlayingFem);
        Assert.Equal(1, _stub.PlayCallCount);
    }

    // ── PlaybackStopped (natural end) ────────────────────────────────────

    [Fact]
    public void NaturalPlaybackStopped_ResetsIsPlayingPrimary()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        _stub.FirePlaybackStopped(); // simulate track ending naturally
        Assert.False(_vm.IsPlayingPrimary);
        Assert.Equal("▶", _vm.PlayPrimaryGlyph);
    }

    [Fact]
    public void NaturalPlaybackStopped_ResetsIsPlayingFem()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayFemCommand.Execute(null);
        _stub.FirePlaybackStopped();
        Assert.False(_vm.IsPlayingFem);
        Assert.Equal("▶", _vm.PlayFemGlyph);
    }

    // ── Node navigation stops playback ───────────────────────────────────

    [Fact]
    public void LoadNewNode_StopsCurrentPlayback()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.True(_vm.IsPlayingPrimary);

        PlantAndLoad(); // loading any node calls NotifyAllProxies which stops player
        Assert.False(_vm.IsPlayingPrimary);
    }

    // ── Stub ─────────────────────────────────────────────────────────────

    private sealed class StubVoAudioPlayer : IVoAudioPlayer
    {
        public bool IsAvailable { get; set; } = true;
        public event Action? PlaybackStopped;
        public string? LastPlayPath { get; private set; }
        public int PlayCallCount { get; private set; }

        public void Play(string wemPath) { LastPlayPath = wemPath; PlayCallCount++; }

        // Explicit stop — does NOT fire PlaybackStopped (by design, matching VoAudioPlayer).
        public void Stop() { }

        // Simulates natural track end for tests.
        public void FirePlaybackStopped() => PlaybackStopped?.Invoke();
    }
}
