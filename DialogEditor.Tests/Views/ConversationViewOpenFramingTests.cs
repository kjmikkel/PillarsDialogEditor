using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Nodify;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 246: the first view of a conversation was clipped at the top, or showed no
/// nodes at all at 800×600. Opening a conversation now frames it: the whole graph when it fits
/// at a readable zoom, otherwise the root node at that zoom.
/// </summary>
public class ConversationViewOpenFramingTests
{
    public ConversationViewOpenFramingTests() => Loc.Configure(new StubStringProvider());

    // Root 0 fans out to `fan` children; the last child then starts a chain `depth` long, which
    // runs far to the right. `orphans` unlinked nodes join the root in layout layer 0, which
    // auto-layout stacks centred on y = 0, so some cards sit above the origin (negative y).
    private static Conversation Graph(int fan, int depth = 0, int orphans = 2)
    {
        var links = new Dictionary<int, List<NodeLink>>();
        void Link(int from, int to) =>
            (links.TryGetValue(from, out var l) ? l : links[from] = []).Add(new NodeLink(from, to, []));
        for (var i = 1; i <= fan; i++) Link(0, i);
        for (var i = 0; i < depth; i++) Link(fan + i, fan + i + 1);

        var count = fan + depth + 1 + orphans;
        var nodes = Enumerable.Range(0, count).Select(i => new ConversationNode(
            i, false, SpeakerCategory.Npc, "", "", links.TryGetValue(i, out var l) ? l : [],
            [], [], "Conversation", "None")).ToList();
        var strings = new StringTable(Enumerable.Range(0, count).Select(i => new StringEntry(i, $"Line {i}", "")));
        return new Conversation("graph", nodes, strings);
    }

    // The whole card, not just its top-left corner: a header cut off at the top was the bug.
    private static Rect Card(NodeViewModel n) =>
        new(n.Location.X, n.Location.Y, CardMetrics.Width, CardMetrics.EstimateHeight(n.DefaultText));

    private static (ConversationView view, NodifyEditor editor, ConversationViewModel vm) Open(
        Conversation conversation, double width, double height)
    {
        var vm = new ConversationViewModel(new StubDispatcher());
        var view = new ConversationView { DataContext = vm };
        var window = new Window { Width = width, Height = height, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        vm.Load(conversation);
        Dispatcher.UIThread.RunJobs();
        return (view, view.FindControl<NodifyEditor>("Editor")!, vm);
    }

    private static Rect Viewport(NodifyEditor editor) => new(editor.ViewportLocation, editor.ViewportSize);

    [AvaloniaFact]
    public void SmallConversation_OpensWithEveryNodeInView()
    {
        var (_, editor, vm) = Open(Graph(fan: 4), 1200, 800);

        var viewport = Viewport(editor);
        foreach (var node in vm.Nodes)
            Assert.True(viewport.Contains(Card(node)),
                        $"node {node.NodeId} card {Card(node)} is not wholly in the first view {viewport}");
    }

    [AvaloniaFact]
    public void LargeConversation_OpensReadable_OnTheRootNode()
    {
        var (view, editor, vm) = Open(Graph(fan: 30, depth: 40), 800, 600);

        Assert.True(editor.ViewportZoom >= ConversationView.MinOpeningZoom * view.FontScaleFactor - 1e-9,
                    $"opening zoom {editor.ViewportZoom:0.###} is below the readable minimum");
        var root = vm.Nodes.Single(n => n.NodeId == 0);
        Assert.True(Viewport(editor).Contains(Card(root)),
                    $"root card {Card(root)} is not wholly in the first view {Viewport(editor)}");
    }
}
