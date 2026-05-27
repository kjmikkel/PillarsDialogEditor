using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

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
        SpeakerCategory speakerCategory = SpeakerCategory.Npc,
        string speakerGuid = "",
        string listenerGuid = "",
        string displayType = "Conversation",
        string persistence = "None",
        string actorDirection = "",
        string comments = "",
        string externalVO = "",
        bool hasVO = false,
        bool hideSpeaker = false,
        IReadOnlyList<ScriptCall>? scripts = null,
        IReadOnlyList<NodeLink>? links = null,
        string defaultText = "Hello",
        string femaleText = "")
    {
        var node = new ConversationNode(
            NodeId: id,
            IsPlayerChoice: isPlayerChoice,
            SpeakerCategory: speakerCategory,
            SpeakerGuid: speakerGuid,
            ListenerGuid: listenerGuid,
            Links: links ?? [],
            Conditions: [],
            Scripts: scripts ?? [],
            DisplayType: displayType,
            Persistence: persistence,
            ActorDirection: actorDirection,
            Comments: comments,
            ExternalVO: externalVO,
            HasVO: hasVO,
            HideSpeaker: hideSpeaker);

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
    public void Load_PropertyGroups_HasIdentityGroup()
    {
        _vm.Load(MakeNode());
        Assert.Single(_vm.PropertyGroups);   // Scripts/Conditions now have dedicated panels
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
    public void Load_ScriptSummary_EmptyWhenNoScripts()
    {
        // Scripts now have their own panel — verified via ScriptSummary property
        _vm.Load(MakeNode());
        Assert.Equal("NodeDetail_None", _vm.ScriptSummary);
    }

    // ── Links ─────────────────────────────────────────────────────────────

    [Fact]
    public void RefreshLinks_AfterLoad_ShowsCorrectCount()
    {
        _vm.Load(MakeNode());
        _vm.RefreshLinks([MakeConn(), MakeConn()]);
        Assert.Equal(2, _vm.Links.Count);
    }

    [Fact]
    public void Load_ClearsLinksFromPreviousNode()
    {
        _vm.Load(MakeNode());
        _vm.RefreshLinks([MakeConn()]);
        _vm.Load(MakeNode(id: 2));   // loading a new node doesn't keep old links
        _vm.RefreshLinks([]);
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

    // ── New-node empty text (Fix B) ───────────────────────────────────────

    [Fact]
    public void NewNode_WithEmptyStringEntry_ShowsEmptyDefaultText()
    {
        var node = new ConversationNode(
            NodeId: 99, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [],
            Scripts: [], DisplayType: "Conversation", Persistence: "None");
        var vm = new NodeViewModel(node, new StringEntry(99, string.Empty, string.Empty));
        _vm.Load(vm);
        Assert.Equal(string.Empty, _vm.DefaultText);
    }

    // ── SpeakerCategory proxy ─────────────────────────────────────────────

    [Fact]
    public void Load_ExposesSpeakerCategoryString_ForNpcNode()
    {
        _vm.Load(MakeNode());
        Assert.Equal(Loc.Get("Speaker_Npc"), _vm.SpeakerCategoryString);
    }

    [Fact]
    public void Load_ExposesSpeakerCategoryString_ForPlayerNode()
    {
        _vm.Load(MakeNode(speakerCategory: SpeakerCategory.Player));
        Assert.Equal(Loc.Get("Speaker_Player"), _vm.SpeakerCategoryString);
    }

    [Fact]
    public void SetSpeakerCategoryString_Player_UpdatesNodeViewModel()
    {
        var node = MakeNode();
        _vm.Load(node);
        _vm.SpeakerCategoryString = Loc.Get("Speaker_Player");
        Assert.Equal(SpeakerCategory.Player, node.SpeakerCategory);
    }

    [Fact]
    public void SetSpeakerCategoryString_UnknownValue_DefaultsToNpc()
    {
        var node = MakeNode(speakerCategory: SpeakerCategory.Player);
        _vm.Load(node);
        _vm.SpeakerCategoryString = "SomethingUnrecognised";
        Assert.Equal(SpeakerCategory.Npc, node.SpeakerCategory);
    }

    // ── RefreshLinks (Fix C) ──────────────────────────────────────────────

    private static ConnectionViewModel MakeConn()
    {
        var src = new ConnectorViewModel();
        var tgt = new ConnectorViewModel();
        return new ConnectionViewModel(src, tgt);
    }

    [Fact]
    public void RefreshLinks_UpdatesLinksList()
    {
        _vm.Load(MakeNode(links: []));
        _vm.RefreshLinks([MakeConn()]);
        Assert.Single(_vm.Links);
    }

    [Fact]
    public void RefreshLinks_WhenCalledTwice_ReplacesNotAppends()
    {
        _vm.Load(MakeNode());
        _vm.RefreshLinks([MakeConn(), MakeConn()]);
        _vm.RefreshLinks([MakeConn()]);
        Assert.Single(_vm.Links);
    }

    // ── Translator note ────────────────────────────────────────────────────

    private static ConversationViewModel MakeCanvas() =>
        new(new StubDispatcher());

    private static NodeViewModel MakeNodeOnCanvas(ConversationViewModel canvas, int id = 1)
    {
        var node = new NodeViewModel(
            new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [],
                                 "Conversation", "None"),
            new StringEntry(id, "Hello", ""));
        canvas.AddNode(node, new LayoutPoint(0, 0));
        return node;
    }

    [Fact]
    public void TranslatorNote_LoadsFromCanvas_WhenNodeSelected()
    {
        var canvas = MakeCanvas();
        canvas.LoadNodeComments(new Dictionary<int, string> { [1] = "Test note" });
        var node = MakeNodeOnCanvas(canvas, 1);

        _vm.Canvas = canvas;
        _vm.Load(node);

        Assert.Equal("Test note", _vm.TranslatorNote);
    }

    [Fact]
    public void TranslatorNote_WritesToCanvas_WhenChanged()
    {
        var canvas = MakeCanvas();
        var node = MakeNodeOnCanvas(canvas, 1);

        _vm.Canvas = canvas;
        _vm.Load(node);
        _vm.TranslatorNote = "Hello translators";

        Assert.Equal("Hello translators", canvas.GetNodeComment(1));
    }

    [Fact]
    public void TranslatorNote_EmptyString_RemovesFromCanvas()
    {
        var canvas = MakeCanvas();
        canvas.LoadNodeComments(new Dictionary<int, string> { [1] = "some note" });
        var node = MakeNodeOnCanvas(canvas, 1);

        _vm.Canvas = canvas;
        _vm.Load(node);
        _vm.TranslatorNote = "";

        Assert.Equal(string.Empty, canvas.GetNodeComment(1));
        Assert.False(canvas.NodeComments.ContainsKey(1));
    }

    [Fact]
    public void TranslatorNote_MarksCanvasDirty_WhenChanged()
    {
        var canvas = MakeCanvas();
        var node = MakeNodeOnCanvas(canvas, 1);
        canvas.IsModified = false;   // reset after AddNode

        _vm.Canvas = canvas;
        _vm.Load(node);
        _vm.TranslatorNote = "note";

        Assert.True(canvas.IsModified);
    }

    [Fact]
    public void TranslatorNote_ClearsOnNodeChange()
    {
        var canvas = MakeCanvas();
        canvas.LoadNodeComments(new Dictionary<int, string> { [1] = "note for node 1" });
        var node1 = MakeNodeOnCanvas(canvas, 1);
        var node2 = MakeNodeOnCanvas(canvas, 2);

        _vm.Canvas = canvas;
        _vm.Load(node1);
        Assert.Equal("note for node 1", _vm.TranslatorNote);

        _vm.Load(node2);
        Assert.Equal(string.Empty, _vm.TranslatorNote);
    }
}
