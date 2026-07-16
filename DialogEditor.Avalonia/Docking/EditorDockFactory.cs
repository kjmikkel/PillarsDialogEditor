using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Docking;

/// Builds the editor's default docking layout: Conversations (left tool), Canvas (centre
/// document), Node Details + Condition search (right, tabbed). Also re-wires content on load.
public sealed class EditorDockFactory : Factory
{
    private readonly GameBrowserViewModel     _browser;
    private readonly ConversationViewModel    _canvas;
    private readonly NodeDetailViewModel      _details;
    private readonly ConditionSearchViewModel _search;

    public IDocumentDock? DocumentDock { get; private set; }

    public EditorDockFactory(
        GameBrowserViewModel browser, ConversationViewModel canvas,
        NodeDetailViewModel details, ConditionSearchViewModel search)
    {
        _browser = browser; _canvas = canvas; _details = details; _search = search;
    }

    public override IRootDock CreateLayout()
    {
        var canvasDoc = new CanvasDocument(_canvas);
        var documentDock = new DocumentDock
        {
            Id = "Documents",
            IsCollapsable = false,
            CanCreateDocument = false,
            ActiveDockable = canvasDoc,
            VisibleDockables = CreateList<IDockable>(canvasDoc),
        };
        DocumentDock = documentDock;

        var browserTool = new BrowserTool(_browser);
        var leftDock = new ToolDock
        {
            Id = "LeftPane",
            Proportion = 0.18,
            Alignment = Alignment.Left,
            ActiveDockable = browserTool,
            VisibleDockables = CreateList<IDockable>(browserTool),
        };

        var detailsTool = new DetailsTool(_details);
        var searchTool  = new ConditionSearchTool(_search);
        var rightDock = new ToolDock
        {
            Id = "RightPane",
            Proportion = 0.24,
            Alignment = Alignment.Right,
            ActiveDockable = detailsTool,
            VisibleDockables = CreateList<IDockable>(detailsTool, searchTool),
        };

        var main = new ProportionalDock
        {
            Orientation = Orientation.Horizontal,
            VisibleDockables = CreateList<IDockable>(
                leftDock,
                new ProportionalDockSplitter(),
                documentDock,
                new ProportionalDockSplitter(),
                rightDock),
        };

        var root = CreateRootDock();
        root.IsCollapsable = false;
        root.DefaultDockable = main;
        root.ActiveDockable  = main;
        root.VisibleDockables = CreateList<IDockable>(main);
        return root;
    }
}
