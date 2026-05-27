using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeViewModelTests
{
    public NodeViewModelTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(
        int    id             = 1,
        string defaultText    = "Hello",
        string femaleText     = "",
        bool   isPlayerChoice = false,
        SpeakerCategory speakerCategory = SpeakerCategory.Npc,
        string speakerGuid   = "",
        string listenerGuid  = "",
        string displayType   = "Conversation")
    {
        var node = new ConversationNode(
            NodeId: id,
            IsPlayerChoice: isPlayerChoice,
            SpeakerCategory: speakerCategory,
            SpeakerGuid: speakerGuid,
            ListenerGuid: listenerGuid,
            Links: [],
            Conditions: [],
            Scripts: [],
            DisplayType: displayType,
            Persistence: "None");
        return new NodeViewModel(node, new StringEntry(id, defaultText, femaleText));
    }

    // ── Push<T> — no undo stack: applies directly ─────────────────────────

    [Fact]
    public void SetProperty_WithoutUndoStack_AppliesValueDirectly()
    {
        var vm = MakeNode(defaultText: "original");
        vm.DefaultText = "updated";
        Assert.Equal("updated", vm.DefaultText);
    }

    // ── Push<T> — same value: no command pushed ───────────────────────────

    [Fact]
    public void SetProperty_SameValue_DoesNotPushToUndoStack()
    {
        var vm    = MakeNode(defaultText: "same");
        var stack = new UndoRedoStack();
        vm.UndoStack   = stack;
        vm.DefaultText = "same";
        Assert.False(stack.CanUndo);
    }

    // ── Push<T> — with undo stack: command is undoable ───────────────────

    [Fact]
    public void SetProperty_WithUndoStack_PushesUndoableCommand()
    {
        var vm    = MakeNode(defaultText: "before");
        var stack = new UndoRedoStack();
        vm.UndoStack   = stack;
        vm.DefaultText = "after";
        Assert.True(stack.CanUndo);
        stack.Undo();
        Assert.Equal("before", vm.DefaultText);
    }

    [Fact]
    public void SetProperty_WithUndoStack_CommandDescriptionIsSet()
    {
        var vm    = MakeNode();
        var stack = new UndoRedoStack();
        vm.UndoStack   = stack;
        vm.DefaultText = "new text";
        Assert.NotNull(stack.UndoDescription);
        Assert.NotEmpty(stack.UndoDescription);
    }

    // ── Push<T> — multiple properties each push their own command ─────────

    [Fact]
    public void MultiplePropertyEdits_EachPushSeparateUndoCommands()
    {
        var vm    = MakeNode(speakerGuid: "A", listenerGuid: "B");
        var stack = new UndoRedoStack();
        vm.UndoStack    = stack;
        vm.SpeakerGuid  = "X";
        vm.ListenerGuid = "Y";

        // Two commands on the stack; undo the last one
        stack.Undo();
        Assert.Equal("X",  vm.SpeakerGuid);
        Assert.Equal("B",  vm.ListenerGuid);
    }

    // ── Push<T> — bool and enum properties ───────────────────────────────

    [Fact]
    public void SetIsPlayerChoice_WithUndoStack_IsUndoable()
    {
        var vm    = MakeNode(isPlayerChoice: false);
        var stack = new UndoRedoStack();
        vm.UndoStack     = stack;
        vm.IsPlayerChoice = true;
        stack.Undo();
        Assert.False(vm.IsPlayerChoice);
    }

    [Fact]
    public void SetSpeakerCategory_WithUndoStack_IsUndoable()
    {
        var vm    = MakeNode(speakerCategory: SpeakerCategory.Npc);
        var stack = new UndoRedoStack();
        vm.UndoStack      = stack;
        vm.SpeakerCategory = SpeakerCategory.Player;
        stack.Undo();
        Assert.Equal(SpeakerCategory.Npc, vm.SpeakerCategory);
    }

    // ── Computed: TextPreview ─────────────────────────────────────────────

    [Fact]
    public void TextPreview_ShortText_ReturnsUnchanged()
    {
        var vm = MakeNode(defaultText: "Short text");
        Assert.Equal("Short text", vm.TextPreview);
    }

    [Fact]
    public void TextPreview_TextExactly80Chars_ReturnsUnchanged()
    {
        var text = new string('x', 80);
        var vm   = MakeNode(defaultText: text);
        Assert.Equal(text, vm.TextPreview);
    }

    [Fact]
    public void TextPreview_TextOver80Chars_TruncatesWithEllipsis()
    {
        var text = new string('x', 81);
        var vm   = MakeNode(defaultText: text);
        Assert.EndsWith("…", vm.TextPreview);
        Assert.Equal(81, vm.TextPreview.Length); // 80 chars + ellipsis
    }

    // ── Computed: HasFemaleText ───────────────────────────────────────────

    [Fact]
    public void HasFemaleText_WhenFemaleTextIsEmpty_IsFalse()
    {
        var vm = MakeNode(femaleText: "");
        Assert.False(vm.HasFemaleText);
    }

    [Fact]
    public void HasFemaleText_WhenFemaleTextIsPresent_IsTrue()
    {
        var vm = MakeNode(femaleText: "Her words");
        Assert.True(vm.HasFemaleText);
    }

    [Fact]
    public void SetFemaleText_UpdatesHasFemaleText()
    {
        var vm = MakeNode(femaleText: "");
        vm.FemaleText = "New female text";
        Assert.True(vm.HasFemaleText);
    }

    // ── Computed: Title (PropertyChanged notifications) ───────────────────

    [Fact]
    public void SetIsPlayerChoice_RaisesPropertyChangedForTitle()
    {
        var vm      = MakeNode(isPlayerChoice: false);
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.IsPlayerChoice = true;
        Assert.Contains(nameof(vm.Title), changed);
    }

    [Fact]
    public void SetSpeakerGuid_RaisesPropertyChangedForTitle()
    {
        var vm      = MakeNode();
        var changed = new List<string?>();
        vm.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        vm.SpeakerGuid = "new-guid";
        Assert.Contains(nameof(vm.Title), changed);
    }

    // ── Computed: HasConditions / HasScripts ──────────────────────────────

    [Fact]
    public void HasConditions_FalseWhenNoConditions()
    {
        var vm = MakeNode();
        Assert.False(vm.HasConditions);
    }

    [Fact]
    public void HasConditions_TrueAfterConditionsSet()
    {
        var vm   = MakeNode();
        vm.Conditions = [new ConditionLeaf("Boolean A()", [], false, "And")];
        Assert.True(vm.HasConditions);
    }

    [Fact]
    public void HasScripts_FalseWhenNoScripts()
    {
        var vm = MakeNode();
        Assert.False(vm.HasScripts);
    }

    [Fact]
    public void HasScripts_TrueAfterScriptsSet()
    {
        var vm = MakeNode();
        vm.Scripts = [new ScriptCall("Void DoSomething()", [], ScriptCategory.Enter)];
        Assert.True(vm.HasScripts);
    }

    // ── Conditions — undo ─────────────────────────────────────────────────

    [Fact]
    public void SetConditions_WithUndoStack_IsUndoable()
    {
        var vm    = MakeNode();
        var stack = new UndoRedoStack();
        vm.UndoStack  = stack;
        vm.Conditions = [new ConditionLeaf("Boolean A()", [], false, "And")];
        stack.Undo();
        Assert.Empty(vm.Conditions);
    }

    // ── Scripts — undo ────────────────────────────────────────────────────

    [Fact]
    public void SetScripts_WithUndoStack_IsUndoable()
    {
        var vm    = MakeNode();
        var stack = new UndoRedoStack();
        vm.UndoStack = stack;
        vm.Scripts   = [new ScriptCall("Void DoSomething()", [], ScriptCategory.Enter)];
        stack.Undo();
        Assert.Empty(vm.Scripts);
    }

    // ── ToSnapshot ────────────────────────────────────────────────────────

    [Fact]
    public void ToSnapshot_CapturesNodeId()
    {
        var vm       = MakeNode(id: 7);
        var snapshot = vm.ToSnapshot([]);
        Assert.Equal(7, snapshot.NodeId);
    }

    [Fact]
    public void ToSnapshot_CapturesDefaultText()
    {
        var vm       = MakeNode(defaultText: "Dialog line");
        var snapshot = vm.ToSnapshot([]);
        Assert.Equal("Dialog line", snapshot.DefaultText);
    }

    [Fact]
    public void ToSnapshot_CapturesIsPlayerChoice()
    {
        var vm       = MakeNode(isPlayerChoice: true);
        var snapshot = vm.ToSnapshot([]);
        Assert.True(snapshot.IsPlayerChoice);
    }

    [Fact]
    public void ToSnapshot_ReflectsMutatedState()
    {
        var vm = MakeNode(defaultText: "before");
        vm.DefaultText = "after";
        var snapshot = vm.ToSnapshot([]);
        Assert.Equal("after", snapshot.DefaultText);
    }

    [Fact]
    public void ToSnapshot_IncludesLinksProvided()
    {
        var vm   = MakeNode(id: 1);
        var link = new LinkEditSnapshot(1, 2, 1f, "", false);
        var snap = vm.ToSnapshot([link]);
        Assert.Single(snap.Links);
        Assert.Equal(2, snap.Links[0].ToNodeId);
    }

    // ── IsBark ───────────────────────────────────────────────────────

    [Fact]
    public void IsBark_TrueWhenDisplayTypeBark()
    {
        var vm = MakeNode(displayType: "Bark");
        Assert.True(vm.IsBark);
    }

    [Fact]
    public void IsBark_FalseWhenDisplayTypeConversation()
    {
        var vm = MakeNode(displayType: "Conversation");
        Assert.False(vm.IsBark);
    }

    // ── BarkWarnings ─────────────────────────────────────────────────

    [Fact]
    public void BarkWarnings_EmptyForConversationNode_EvenWithLongText()
    {
        var longText = new string('x', BarkConstants.TextLengthWarningThreshold + 1);
        var vm = MakeNode(defaultText: longText, displayType: "Conversation");
        Assert.Empty(vm.BarkWarnings);
    }

    [Fact]
    public void BarkWarnings_EmptyForShortBark()
    {
        var shortText = new string('x', BarkConstants.TextLengthWarningThreshold);
        var vm = MakeNode(defaultText: shortText, displayType: "Bark");
        Assert.Empty(vm.BarkWarnings);
    }

    [Fact]
    public void BarkWarnings_TextLengthWarning_WhenBarkTextExceedsThreshold()
    {
        var longText = new string('x', BarkConstants.TextLengthWarningThreshold + 1);
        var vm = MakeNode(defaultText: longText, displayType: "Bark");
        Assert.Single(vm.BarkWarnings);
        Assert.Equal("Bark_Warning_TextTooLong", vm.BarkWarnings[0]);
    }
}
