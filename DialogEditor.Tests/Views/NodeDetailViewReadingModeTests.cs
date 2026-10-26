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

namespace DialogEditor.Tests.Views;

/// Issue 234 (Task 10): Node Details in reading mode. Spec section 3 "Node Details": text, speaker and
/// summaries stay (read-only, still selectable); every editing control is hidden, not just disabled.
public class NodeDetailViewReadingModeTests
{
    public NodeDetailViewReadingModeTests() => Loc.Configure(new StubStringProvider());

    private static object? R(string key) => Application.Current!.FindResource(key);

    // Reading mode with a project open: the canvas is not editable, the pane is told the mode.
    private static NodeDetailView Show(bool reading)
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
                Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false),
            new StringEntry(1, "<b>Hello {zzz}", "")));
        vm.RefreshLinks([new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel())]);
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
}
