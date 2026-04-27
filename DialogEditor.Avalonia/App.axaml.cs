using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DialogEditor.Avalonia.Services;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            Loc.Configure(new AvaloniaStringProvider());
            var window = new MainWindow();
            desktop.MainWindow = window;

            // DIAGNOSTIC: add two hardcoded nodes on startup to verify Nodify renders at all.
            // If these appear → canvas works, issue is in data loading pipeline.
            // If these don't appear → fundamental Nodify rendering problem.
            AddTestNodes(window);
        }
        base.OnFrameworkInitializationCompleted();
    }

    private static void AddTestNodes(MainWindow window)
    {
        if (window.DataContext is not MainWindowViewModel vm) return;

        var canvas = vm.Canvas;
        canvas.Nodes.Clear();
        canvas.Connections.Clear();

        NodeViewModel MakeNode(int id, bool isPlayer, string text, double x, double y)
        {
            var cat = isPlayer ? SpeakerCategory.Player : SpeakerCategory.Npc;
            var coreNode = new ConversationNode(
                NodeId: id, IsPlayerChoice: isPlayer,
                SpeakerCategory: cat,
                SpeakerGuid: "00000000-0000-0000-0000-000000000000",
                ListenerGuid: "00000000-0000-0000-0000-000000000000",
                Links: Array.Empty<NodeLink>(),
                ConditionStrings: Array.Empty<string>(),
                Scripts: Array.Empty<string>(),
                DisplayType: "Conversation",
                Persistence: "None");
            var nodeVm = new NodeViewModel(coreNode, new StringEntry(id, text, ""));
            nodeVm.Location = new LayoutPoint(x, y);
            return nodeVm;
        }

        var n0 = MakeNode(0, false, "Hello — this is a test NPC node.", 120, 100);
        var n1 = MakeNode(1, true,  "And this is a player choice.",      120, 280);

        canvas.Nodes.Add(n0);
        canvas.Nodes.Add(n1);
        canvas.Connections.Add(new ConnectionViewModel(n0.Output, n1.Input));
    }
}
