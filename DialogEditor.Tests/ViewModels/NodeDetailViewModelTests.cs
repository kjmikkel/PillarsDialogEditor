using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelTests
{
    private readonly NodeDetailViewModel _vm = new();

    public NodeDetailViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    private static NodeViewModel MakeNode(
        int id = 1,
        bool isPlayerChoice = false,
        string speakerGuid = "",
        string listenerGuid = "",
        string displayType = "Conversation",
        string persistence = "None",
        string actorDirection = "",
        string conditionExpression = "",
        string comments = "",
        string externalVO = "",
        bool hasVO = false,
        bool hideSpeaker = false,
        IReadOnlyList<string>? scripts = null,
        IReadOnlyList<string>? conditionStrings = null,
        IReadOnlyList<NodeLink>? links = null,
        string defaultText = "Hello",
        string femaleText = "")
    {
        var node = new ConversationNode(
            NodeId: id,
            IsPlayerChoice: isPlayerChoice,
            SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: speakerGuid,
            ListenerGuid: listenerGuid,
            Links: links ?? [],
            ConditionStrings: conditionStrings ?? [],
            Scripts: scripts ?? [],
            DisplayType: displayType,
            Persistence: persistence,
            ActorDirection: actorDirection,
            Comments: comments,
            ExternalVO: externalVO,
            HasVO: hasVO,
            HideSpeaker: hideSpeaker,
            ConditionExpression: conditionExpression);

        var entry = new StringEntry(id, defaultText, femaleText);
        return new NodeViewModel(node, entry);
    }

    [Fact]
    public void Load_SetsHasContentTrue()
    {
        _vm.Load(MakeNode());
        Assert.True(_vm.HasContent);
    }

    [Fact]
    public void Load_WithAllProperties_PopulatesAllFourGroups()
    {
        _vm.Load(MakeNode());
        Assert.Equal(4, _vm.PropertyGroups.Count);
    }

    [Fact]
    public void Load_IdentityGroup_ContainsFiveRows()
    {
        _vm.Load(MakeNode());
        Assert.Equal(5, _vm.PropertyGroups[0].Rows.Count);
    }

    [Fact]
    public void Load_DisplayGroup_AlwaysContainsActorDirectionRow_EvenWhenEmpty()
    {
        _vm.Load(MakeNode(actorDirection: ""));
        var displayGroup = _vm.PropertyGroups[1];
        Assert.Equal(3, displayGroup.Rows.Count);
        Assert.Contains(displayGroup.Rows, r => r.Label == "PropertyRow_ActorDirection");
    }

    [Fact]
    public void Load_LogicGroup_AlwaysContainsCommentsRow_EvenWhenEmpty()
    {
        _vm.Load(MakeNode(comments: ""));
        var logicGroup = _vm.PropertyGroups[2];
        Assert.Equal(3, logicGroup.Rows.Count);
        Assert.Contains(logicGroup.Rows, r => r.Label == "PropertyRow_Comments");
    }

    [Fact]
    public void Load_VoiceGroup_AlwaysContainsAllThreeRows()
    {
        _vm.Load(MakeNode(externalVO: "", hasVO: false, hideSpeaker: false));
        var voiceGroup = _vm.PropertyGroups[3];
        Assert.Equal(3, voiceGroup.Rows.Count);
    }

    [Fact]
    public void Load_WithMultipleLinks_CreatesOneRowPerLink()
    {
        var links = new[]
        {
            new NodeLink(1, 10, false, 1f, "ShowOnce"),
            new NodeLink(1, 20, false, 2f, "Always"),
        };
        _vm.Load(MakeNode(links: links));
        Assert.Equal(2, _vm.Links.Count);
    }

    [Fact]
    public void Load_WithNoLinks_ProducesEmptyLinksList()
    {
        _vm.Load(MakeNode(links: []));
        Assert.Empty(_vm.Links);
    }

    [Fact]
    public void Load_FemaleTextDisplay_WhenEmpty_ShowsSameAsDefaultString()
    {
        _vm.Load(MakeNode(femaleText: ""));
        Assert.Equal("NodeDetail_SameAsDefault", _vm.FemaleTextDisplay);
        Assert.False(_vm.HasFemaleText);
    }

    [Fact]
    public void Load_FemaleTextDisplay_WhenPresent_ShowsActualText()
    {
        _vm.Load(MakeNode(femaleText: "Her voice"));
        Assert.Equal("Her voice", _vm.FemaleTextDisplay);
        Assert.True(_vm.HasFemaleText);
    }

    [Fact]
    public void Clear_SetsHasContentFalse()
    {
        _vm.Load(MakeNode());
        _vm.Clear();
        Assert.False(_vm.HasContent);
    }

    [Fact]
    public void Clear_DoesNotThrowWhenCalledBeforeLoad()
    {
        var exception = Record.Exception(() => _vm.Clear());
        Assert.Null(exception);
    }
}
