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

    // ── HasContent ────────────────────────────────────────────────────────

    [Fact]
    public void Load_SetsHasContentTrue()
    {
        _vm.Load(MakeNode());
        Assert.True(_vm.HasContent);
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

    // ── Read-only property groups (Identity = NodeId; Logic = conditions/scripts) ──

    [Fact]
    public void Load_PropertyGroups_HasTwoGroups()
    {
        _vm.Load(MakeNode());
        Assert.Equal(2, _vm.PropertyGroups.Count);
    }

    [Fact]
    public void Load_IdentityGroup_ContainsNodeIdRow()
    {
        _vm.Load(MakeNode(id: 42));
        var identity = _vm.PropertyGroups[0];
        Assert.Single(identity.Rows);
        Assert.Equal("42", identity.Rows[0].Value);
    }

    [Fact]
    public void Load_LogicGroup_ContainsConditionsAndScriptsRows()
    {
        _vm.Load(MakeNode());
        var logic = _vm.PropertyGroups[1];
        Assert.Equal(2, logic.Rows.Count);
        Assert.Contains(logic.Rows, r => r.Label == "PropertyRow_Conditions");
        Assert.Contains(logic.Rows, r => r.Label == "PropertyRow_Scripts");
    }

    // ── Links ─────────────────────────────────────────────────────────────

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

    // ── FemaleText display ────────────────────────────────────────────────

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

    // ── Editable proxy properties ─────────────────────────────────────────

    [Fact]
    public void Load_ExposesDefaultText()
    {
        _vm.Load(MakeNode(defaultText: "Hello world"));
        Assert.Equal("Hello world", _vm.DefaultText);
    }

    [Fact]
    public void Load_ExposesSpeakerGuid()
    {
        _vm.Load(MakeNode(speakerGuid: "test-guid-123"));
        Assert.Equal("test-guid-123", _vm.SpeakerGuid);
    }

    [Fact]
    public void SetSpeakerGuid_UpdatesNodeViewModel()
    {
        var node = MakeNode(speakerGuid: "original");
        _vm.Load(node);
        _vm.SpeakerGuid = "updated";
        Assert.Equal("updated", node.SpeakerGuid);
    }

    [Fact]
    public void SetDefaultText_UpdatesNodeViewModel()
    {
        var node = MakeNode(defaultText: "old text");
        _vm.Load(node);
        _vm.DefaultText = "new text";
        Assert.Equal("new text", node.DefaultText);
    }

    [Fact]
    public void Clear_ResetsProxyProperties()
    {
        _vm.Load(MakeNode(speakerGuid: "abc"));
        _vm.Clear();
        Assert.Equal(string.Empty, _vm.SpeakerGuid);
        Assert.False(_vm.HasContent);
    }
}
