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
    }

    // StubStringProvider echoes keys, so the localised separator appears as its key.
    private const string Sep = "NodeDetail_HeaderSeparator";

    private void LoadNode(int id = 1, bool playerChoice = false)
    {
        var node = new ConversationNode(
            NodeId: id, IsPlayerChoice: playerChoice,
            SpeakerCategory: playerChoice ? SpeakerCategory.Player : SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "",
            ExternalVO: "", HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(id, "Test line", "")));
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
}
