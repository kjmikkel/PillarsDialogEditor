using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelPaneTests
{
    private readonly NodeDetailViewModel _vm = new();

    public NodeDetailViewModelPaneTests()
    {
        Loc.Configure(new StubStringProvider());
        // SpeakerNameService is a process-wide static; earlier tests in the serial
        // suite may have registered names. Reset so HasSpeakerData is false here.
        SpeakerNameService.Register(new Dictionary<string, string>());
        // VoAliasIndexService is a process-wide static too; reset so
        // VoAliasSharedCount tests start from "index not ready".
        VoAliasIndexService.Clear();
    }

    // StubStringProvider echoes keys, so the localised separator appears as its key.
    private const string Sep = "NodeDetail_HeaderSeparator";

    private void LoadNode(int id = 1, bool playerChoice = false, string externalVO = "")
    {
        var node = new ConversationNode(
            NodeId: id, IsPlayerChoice: playerChoice,
            SpeakerCategory: playerChoice ? SpeakerCategory.Player : SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "",
            ExternalVO: externalVO, HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(id, "Test line", "")));
    }

    // Sets up PoE2 game context (mirrors NodeDetailViewModelPlaybackTests) so
    // _voCheck is non-null and the alias surface becomes reachable, then loads
    // a node carrying the given ExternalVO alias.
    private void LoadPoe2Node(string externalVO)
    {
        var gameRoot = Path.Combine(Path.GetTempPath(), $"VoAliasTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(gameRoot);
        _vm.GameRoot     = gameRoot;
        _vm.ActiveGameId = "poe2";
        LoadNode(externalVO: externalVO);
    }

    // ── NodeHeaderSummary ────────────────────────────────────────────────

    [Fact]
    public void NodeHeaderSummary_EmptyWhenNoNode()
        => Assert.Equal(string.Empty, _vm.NodeHeaderSummary);

    [Fact]
    public void NodeHeaderSummary_NpcNode_ComposesIdCategoryAndType()
    {
        LoadNode(id: 42);
        // No speaker data loaded in tests → no speaker-name segment.
        Assert.Equal($"#42{Sep}Speaker_Npc{Sep}Option_NpcLine", _vm.NodeHeaderSummary);
    }

    [Fact]
    public void NodeHeaderSummary_PlayerNode_ShowsPlayerCategoryAndChoiceType()
    {
        LoadNode(id: 7, playerChoice: true);
        Assert.Equal($"#7{Sep}Speaker_Player{Sep}Option_PlayerChoice", _vm.NodeHeaderSummary);
    }

    // ── GUID box visibility ──────────────────────────────────────────────

    [Fact]
    public void GuidBoxes_VisibleByDefault_WhenNoSpeakerData()
    {
        // SpeakerNameService has no names in the test environment → the raw GUID
        // boxes are the only way to edit, so they must be visible without the toggle.
        LoadNode();
        Assert.False(_vm.ShowSpeakerGuidBox);
        Assert.True(_vm.IsSpeakerGuidBoxVisible);
        Assert.True(_vm.IsListenerGuidBoxVisible);
    }

    [Fact]
    public void GuidToggle_RaisesVisibilityChange()
    {
        LoadNode();
        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        _vm.ShowSpeakerGuidBox = true;
        Assert.Contains(nameof(NodeDetailViewModel.IsSpeakerGuidBoxVisible), raised);
        _vm.ShowListenerGuidBox = true;
        Assert.Contains(nameof(NodeDetailViewModel.IsListenerGuidBoxVisible), raised);
    }

    // ── Expander summaries ───────────────────────────────────────────────

    [Fact]
    public void IdentitySummary_NoSpeakerData_ShowsCategoryOnly()
    {
        LoadNode();
        Assert.Equal("Speaker_Npc", _vm.IdentitySummary);
    }

    [Fact]
    public void DisplaySummary_ComposesDisplayTypeAndPersistence()
    {
        LoadNode();
        Assert.Equal($"Conversation{Sep}NodeDetail_PersistsPrefix None", _vm.DisplaySummary);
    }

    [Fact]
    public void VoiceSummary_NoVoStatus_ShowsNoneShort()
    {
        LoadNode(); // no GameRoot/ActiveGameId → VO not applicable
        Assert.Equal("NodeDetail_NoneShort", _vm.VoiceSummary);
    }

    [Fact]
    public void LogicSummary_CountsConditionsAndScripts()
    {
        LoadNode();
        Assert.Equal($"0 NodeDetail_ConditionsWord{Sep}0 NodeDetail_ScriptsWord", _vm.LogicSummary);
    }

    [Fact]
    public void NotesSummary_EmptyNotes_ShowsNoneShort()
    {
        LoadNode();
        Assert.Equal("NodeDetail_NoneShort", _vm.NotesSummary);
    }

    [Fact]
    public void NotesSummary_WithComment_ShowsCount()
    {
        LoadNode();
        _vm.Comments = "watch the pacing here";
        Assert.Equal("1 NodeDetail_NotesWord", _vm.NotesSummary);
    }

    // ── Session-static expander state ────────────────────────────────────

    [Fact]
    public void ExpanderState_SharedAcrossInstances_AndSurvivesLoad()
    {
        NodeDetailViewModel.ResetExpanderStateForTests();
        try
        {
            _vm.IsVoiceExpanded = true;
            LoadNode(id: 2); // selecting another node must not collapse it

            var second = new NodeDetailViewModel();
            Assert.True(second.IsVoiceExpanded);   // session-wide
            Assert.False(second.IsLogicExpanded);  // others untouched
        }
        finally
        {
            NodeDetailViewModel.ResetExpanderStateForTests();
        }
    }

    // ── ExternalVO alias surface (2026-07-03 alias UX) ───────────────────

    [Fact]
    public void HasVoAlias_FalseWithoutAlias_TrueWithAlias()
    {
        LoadPoe2Node(externalVO: "");
        Assert.False(_vm.HasVoAlias);
        LoadPoe2Node(externalVO: "narrator/other_conv_0005");
        Assert.True(_vm.HasVoAlias);
    }

    [Fact]
    public void VoAliasDescription_ParseableAlias_UsesFriendlyKey()
    {
        LoadPoe2Node(externalVO: "narrator/other_conv_0005");
        // StubStringProvider echoes the Loc.Format key.
        Assert.StartsWith("NodeDetail_AliasDescription", _vm.VoAliasDescription);
    }

    [Fact]
    public void VoAliasDescription_UnparseableAlias_FallsBackToRawKey()
    {
        LoadPoe2Node(externalVO: "narrator/no_digits_here");
        Assert.StartsWith("NodeDetail_AliasRaw", _vm.VoAliasDescription);
    }

    [Fact]
    public void VoAliasSharedCount_NullBeforeIndexReady()
    {
        LoadPoe2Node(externalVO: "narrator/other_conv_0005");
        Assert.Null(_vm.VoAliasSharedCount);
        Assert.Equal(string.Empty, _vm.VoAliasSharedText);
    }

    [Fact]
    public void VoAliasSharedCount_CountsOthers_ExcludingSelf()
    {
        try
        {
            VoAliasIndexService.RegisterForTests(new Dictionary<string, IReadOnlyList<VoAliasRef>>
            {
                ["narrator/other_conv_0005"] =
                [
                    new VoAliasRef("conv_a", 3),
                    new VoAliasRef("conv_b", 9),
                ]
            });
            LoadPoe2Node(externalVO: "narrator/other_conv_0005");
            Assert.Equal(2, _vm.VoAliasSharedCount);
            Assert.StartsWith("NodeDetail_AliasSharedCount", _vm.VoAliasSharedText);
        }
        finally { VoAliasIndexService.Clear(); }
    }

    [Fact]
    public void VoAliasSharedCount_OverlayShadowsDiskEntries()
    {
        try
        {
            VoAliasIndexService.RegisterForTests(new Dictionary<string, IReadOnlyList<VoAliasRef>>
            {
                ["narrator/other_conv_0005"] = [new VoAliasRef("conv_a", 3)]
            });
            // In-memory state says conv_a node 3 no longer aliases this path,
            // but conv_c node 4 now does.
            _vm.ProjectAliasOverlay = () =>
            [
                new VoAliasUse("conv_a", 3, ""),
                new VoAliasUse("conv_c", 4, "narrator/other_conv_0005"),
            ];
            LoadPoe2Node(externalVO: "narrator/other_conv_0005");
            Assert.Equal(1, _vm.VoAliasSharedCount);
        }
        finally { VoAliasIndexService.Clear(); }
    }

    [Fact]
    public void ClearVoAlias_EmptiesExternalVO()
    {
        LoadPoe2Node(externalVO: "narrator/other_conv_0005");
        _vm.ClearVoAliasCommand.Execute(null);
        Assert.False(_vm.HasVoAlias);
        Assert.Equal(string.Empty, _vm.ExternalVO);
    }

    [Fact]
    public async Task PickVoAlias_WritesPickerResult()
    {
        LoadPoe2Node(externalVO: "");
        _vm.ShowAliasPicker = _ => Task.FromResult<string?>("eder/some_conv_0042");
        await _vm.PickVoAliasCommand.ExecuteAsync(null);
        Assert.Equal("eder/some_conv_0042", _vm.ExternalVO);
        Assert.True(_vm.HasVoAlias);
    }

    [Fact]
    public async Task PickVoAlias_NullResult_LeavesAliasUnchanged()
    {
        LoadPoe2Node(externalVO: "narrator/keep_me_0001");
        _vm.ShowAliasPicker = _ => Task.FromResult<string?>(null);
        await _vm.PickVoAliasCommand.ExecuteAsync(null);
        Assert.Equal("narrator/keep_me_0001", _vm.ExternalVO);
    }

    [Fact]
    public void HasVoAlias_Poe1_AlwaysFalse()
    {
        // No PoE2 context (default test state): _voCheck is null → alias UI hidden
        // even if the data somehow carried a value.
        LoadNode();
        Assert.False(_vm.HasVoAlias);
        Assert.False(_vm.CanStartVoAliasPick);
    }
}
