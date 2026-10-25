using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// Issue 240, through the real pane: in "Viewing only" mode every editor is read-only or
/// disabled and a notice says why; once a project is open they all take edits again.
public class NodeDetailViewReadOnlyTests
{
    public NodeDetailViewReadOnlyTests() => Loc.Configure(new StubStringProvider());

    // XAML text comes from the app's string resources, not the stubbed Loc.
    private static object? R(string key) => Application.Current!.FindResource(key);

    private static NodeDetailView Show(bool editable)
    {
        var canvas = new ConversationViewModel(new StubDispatcher()) { IsEditable = editable };
        var vm   = new NodeDetailViewModel { ActiveGameId = "poe2", Canvas = canvas };
        var view = new NodeDetailView { DataContext = vm };
        new Window { Content = view }.Show();
        vm.Load(new NodeViewModel(new ConversationNode(
                NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
                Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false),
            new StringEntry(1, "Hello", "")));
        vm.RefreshLinks([new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel())]);
        Dispatcher.UIThread.RunJobs();
        return view;
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void TextBoxes_AreReadOnly_OnlyWhileViewing(bool editable)
    {
        var boxes = Show(editable).GetLogicalDescendants().OfType<TextBox>().ToList();

        Assert.NotEmpty(boxes);
        Assert.All(boxes, b => Assert.Equal(!editable, b.IsReadOnly));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void Pickers_AreDisabled_OnlyWhileViewing(bool editable)
    {
        var pickers = Show(editable).GetLogicalDescendants()
            .Where(c => c is ComboBox or CheckBox or NumericUpDown or AutoCompleteBox)
            .Cast<Control>().ToList();

        Assert.NotEmpty(pickers);
        Assert.All(pickers, p => Assert.Equal(editable, p.IsEffectivelyEnabled));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void EditButtons_AreDisabled_OnlyWhileViewing(bool editable)
    {
        var view = Show(editable);
        var buttons = view.GetLogicalDescendants().OfType<Button>()
            .Where(b => new[] { "Button_EditScripts", "Button_EditConditions", "Button_AddLink",
                                "Button_DeleteLink", "NodeDetail_ReuseVoButton" }.Select(R).Contains(b.Content)
                        || b.Tag is ConnectionViewModel)   // the link's conditions button
            .ToList();

        Assert.Equal(6, buttons.Count);
        Assert.All(buttons, b => Assert.Equal(editable, b.IsEffectivelyEnabled));
    }

    [AvaloniaTheory]
    [InlineData(true)]
    [InlineData(false)]
    public void ViewingOnlyNotice_ShowsOnlyWhileViewing(bool editable)
    {
        var notice = Show(editable).GetLogicalDescendants().OfType<TextBlock>()
            .Single(t => Equals(t.Text, R("Status_NoProjectReadOnly")));

        Assert.Equal(!editable, notice.IsVisible);
    }
}
