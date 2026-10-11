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

/// Issue 141, through the real link card: the conditions button sits inside the Links
/// ItemTemplate, whose DataContext is the link, so it reaches the pane's game-aware flag
/// through its ancestor. These pin that the binding actually lands.
public class NodeDetailViewLinkConditionsTests
{
    public NodeDetailViewLinkConditionsTests() => Loc.Configure(new StubStringProvider());

    private static Button ConditionsButton(string gameId)
    {
        var link = new ConnectionViewModel(new ConnectorViewModel(), new ConnectorViewModel());
        var vm   = new NodeDetailViewModel { ActiveGameId = gameId };
        var view = new NodeDetailView { DataContext = vm };
        new Window { Content = view }.Show();
        vm.Load(new NodeViewModel(new ConversationNode(
                NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
                Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false),
            new StringEntry(1, "", "")));   // the pane shows nothing until a node is loaded
        vm.RefreshLinks([link]);
        Dispatcher.UIThread.RunJobs();

        return view.GetLogicalDescendants().OfType<Button>().Single(b => ReferenceEquals(b.Tag, link));
    }

    [AvaloniaFact]
    public void Poe1_ConditionsButton_IsDisabled_AndSaysWhereTheConditionGoes()
    {
        var button = ConditionsButton("poe1");

        Assert.False(button.IsEnabled);
        Assert.Equal("ToolTip_LinkConditions_Poe1", ToolTip.GetTip(button));
        Assert.True(ToolTip.GetShowOnDisabled(button));
    }

    [AvaloniaFact]
    public void Poe2_ConditionsButton_IsEnabled()
    {
        var button = ConditionsButton("poe2");

        Assert.True(button.IsEnabled);
        Assert.Equal("ToolTip_LinkConditions_None", ToolTip.GetTip(button));
    }
}
