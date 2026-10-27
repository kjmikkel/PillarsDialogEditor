using System.ComponentModel;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

/// Issue 234 (Task 10): Node Details in reading mode. Spec section 3 "Node Details": text, speaker and
/// summaries stay (read-only, still selectable); every editing control is hidden, not just disabled.
public class NodeDetailViewReadingModeTests
{
    public NodeDetailViewReadingModeTests() => Loc.Configure(new StubStringProvider());

    private static object? R(string key) => Application.Current!.FindResource(key);

    // Reading mode with a project open: the canvas is not editable, the pane is told the mode.
    private static NodeDetailView Show(bool reading, string externalVo = "", bool voStatus = false,
                                       params ConnectionViewModel[] links)
    {
        var canvas = new ConversationViewModel(new StubDispatcher()) { IsEditable = !reading };
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2", Canvas = canvas, IsReadingMode = reading };
        var view = new NodeDetailView { DataContext = vm };
        new Window { Content = view, Width = 500, Height = 900 }.Show();
        // "<b>" is never closed and "{zzz}" is no known token, so the text warnings have something to show.
        vm.Load(new NodeViewModel(new ConversationNode(
                NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
                Comments: "", ExternalVO: externalVo, HasVO: false, HideSpeaker: false),
            new StringEntry(1, "<b>Hello {zzz}", "")));
        vm.RefreshLinks(links.Length > 0 ? links : [new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel())]);
        if (voStatus)
        {
            // A PoE2 node whose VO state is known: the alias, reuse and import controls depend on it.
            typeof(NodeDetailViewModel).GetField("_voCheck", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, new VoCheckResult(VoPresence.Missing, false, null, null));
            typeof(NodeDetailViewModel).GetMethod("OnPropertyChanged", BindingFlags.NonPublic | BindingFlags.Instance,
                [typeof(PropertyChangedEventArgs)])!.Invoke(vm, [new PropertyChangedEventArgs(string.Empty)]);
        }
        // Sections start collapsed and a collapsed Expander has no content to inspect.
        vm.IsIdentityExpanded = vm.IsDisplayExpanded = vm.IsVoiceExpanded = vm.IsLogicExpanded = vm.IsNotesExpanded = true;
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    private static List<Button> EditButtons(NodeDetailView view) =>
        view.GetLogicalDescendants().OfType<Button>()
            .Where(b => new[] { "Button_EditScripts", "Button_EditConditions", "Button_AddLink",
                                "Button_DeleteLink" }.Select(R).Contains(b.Content)
                        || b.Tag is ConnectionViewModel)   // the link's conditions button
            .ToList();

    private static List<Control> TextWarningBoxes(NodeDetailView view) =>
        view.GetLogicalDescendants().OfType<Border>()
            .Where(b => Equals(global::Avalonia.Automation.AutomationProperties.GetHelpText(b), R("ToolTip_TokenWarnings"))
                     || Equals(global::Avalonia.Automation.AutomationProperties.GetHelpText(b), R("ToolTip_SpellingIssues")))
            .Cast<Control>().ToList();

    [AvaloniaFact]
    public void Details_ShowTextSpeakerAndSummaries_ReadOnly()
    {
        var view = Show(reading: true);

        var text = view.FindControl<TextBox>("DefaultTextBox")!;
        Assert.True(text.IsEffectivelyVisible);
        Assert.True(text.IsReadOnly);   // read-only, not a TextBlock: the line stays selectable and copyable
        Assert.Equal("<b>Hello {zzz}", text.Text);

        var visibleText = view.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => t.IsEffectivelyVisible).Select(t => t.Text).ToList();
        var vm = (NodeDetailViewModel)view.DataContext!;
        Assert.Contains(vm.ConditionSummary, visibleText);
        Assert.Contains(vm.ScriptSummary, visibleText);
        Assert.Contains(vm.NodeHeaderSummary, visibleText);
    }

    [AvaloniaFact]
    public void Details_HideEveryEditingControl()
    {
        var view = Show(reading: true);

        var buttons = EditButtons(view);
        Assert.Equal(5, buttons.Count);   // edit scripts, edit conditions, add link, delete link, link conditions
        Assert.All(buttons, b => Assert.False(b.IsEffectivelyVisible, $"{b.Content}"));

        // The add-link target box goes with its button, so no orphan field is left behind.
        var addBox = view.GetLogicalDescendants().OfType<TextBox>()
            .Single(t => Equals(t.Watermark, R("Placeholder_AddLinkTargetId")));
        Assert.False(addBox.IsEffectivelyVisible);

        var warnings = TextWarningBoxes(view);
        Assert.Equal(2, warnings.Count);
        Assert.All(warnings, w => Assert.False(w.IsEffectivelyVisible));

        var dictionary = view.GetLogicalDescendants().OfType<Button>()
            .Where(b => Equals(b.Content, R("Button_AddToDictionary"))
                     || Equals(b.Content, R("Button_AddToProjectDictionary")));
        Assert.All(dictionary, b => Assert.False(b.IsEffectivelyVisible));
    }

    [AvaloniaFact]
    public void Details_ShowEverythingAgain_InEditingMode()
    {
        var view = Show(reading: false);

        Assert.All(EditButtons(view), b => Assert.True(b.IsEffectivelyVisible, $"{b.Content}"));
        var addBox = view.GetLogicalDescendants().OfType<TextBox>()
            .Single(t => Equals(t.Watermark, R("Placeholder_AddLinkTargetId")));
        Assert.True(addBox.IsEffectivelyVisible);
        // At least the token warning box is populated by the sample text.
        Assert.Contains(TextWarningBoxes(view), w => w.IsEffectivelyVisible);
    }

    private static ConnectionViewModel LinkWithConditions(int count) =>
        new(new ConnectorViewModel(), new ConnectorViewModel(), conditions:
            Enumerable.Range(0, count).Select(i => (ConditionNode)new ConditionLeaf(
                "Boolean IsGlobalValue(String, Operator, Int32)", [$"g{i}", "Equal", "1"], false, "And")).ToList());

    // The read-only stand-in for the link-conditions button: its face (glyph + count) is the only
    // place a reader sees that a link is conditional.
    private static List<TextBlock> ConditionIndicators(NodeDetailView view) =>
        view.GetLogicalDescendants().OfType<TextBlock>()
            .Where(t => Equals(global::Avalonia.Automation.AutomationProperties.GetName(t), R("AutomationName_LinkConditionsReading")))
            .ToList();

    [AvaloniaFact]
    public void ConditionalLinks_ShowAReadOnlyIndicator_InReadingMode()
    {
        var withConditions = LinkWithConditions(2);
        var view = Show(reading: true, links: [withConditions, LinkWithConditions(0)]);

        var visible = ConditionIndicators(view).Where(t => t.IsEffectivelyVisible).ToList();
        var single = Assert.Single(visible);
        Assert.Equal(withConditions.ConditionCountLabel, single.Text);
        Assert.NotNull(ToolTip.GetTip(single));
    }

    [AvaloniaFact]
    public void LinkConditionsButton_IsBack_AndTheIndicatorGone_InEditingMode()
    {
        var link = LinkWithConditions(2);
        var view = Show(reading: false, links: [link]);

        Assert.All(ConditionIndicators(view), t => Assert.False(t.IsEffectivelyVisible));
        Assert.True(view.GetLogicalDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Tag, link)).IsEffectivelyVisible);
    }

    private static List<Button> VoButtons(NodeDetailView view) =>
        view.GetLogicalDescendants().OfType<Button>()
            .Where(b => new[] { "NodeDetail_AliasChange", "NodeDetail_AliasClear", "NodeDetail_ReuseVoButton" }
                            .Select(R).Contains(b.Content) || Equals(b.Content, "🎤"))
            .ToList();

    // The alias controls exist only for an aliased node and Reuse only for an unaliased one, so each
    // state is checked against the controls it should offer when editing.
    [AvaloniaTheory]
    [InlineData("alias", new[] { "NodeDetail_AliasChange", "NodeDetail_AliasClear", "🎤" })]
    [InlineData("", new[] { "NodeDetail_ReuseVoButton", "🎤" })]
    public void VoAliasReuseAndImport_AreHidden_InReadingMode_AndBackInEditing(string alias, string[] offered)
    {
        var shown = offered.Select(k => k == "🎤" ? (object)k : R(k)).ToList();

        var reading = VoButtons(Show(reading: true, externalVo: alias, voStatus: true));
        Assert.Equal(4, reading.Count);   // the controls stay in the tree, hidden
        Assert.All(reading, b => Assert.False(b.IsEffectivelyVisible, $"{b.Content}"));

        var editing = VoButtons(Show(reading: false, externalVo: alias, voStatus: true));
        Assert.Equal(shown.Count, editing.Count(b => b.IsEffectivelyVisible));
        Assert.All(editing.Where(b => shown.Contains(b.Content)), b => Assert.True(b.IsEffectivelyVisible, $"{b.Content}"));
    }
}
