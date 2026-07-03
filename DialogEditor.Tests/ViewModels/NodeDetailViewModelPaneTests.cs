using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelPaneTests
{
    private readonly NodeDetailViewModel _vm = new();

    public NodeDetailViewModelPaneTests()
        => Loc.Configure(new StubStringProvider());

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
}
