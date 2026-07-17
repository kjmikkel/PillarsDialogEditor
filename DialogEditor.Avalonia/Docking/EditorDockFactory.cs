using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Docking;

/// Builds the editor's default docking layout: Conversations (left tool), Canvas (centre
/// document), Node Details + Condition search (right, tabbed). Also re-wires content on load.
public sealed class EditorDockFactory : Factory
{
    // Dock Ids — shared by CreateLayout, InitLayout's locators, RestoreLayout's wrapper
    // substitution, and MainWindow.axaml.cs's View-menu ShowToolById calls (previously these
    // strings were duplicated across both files — Task 6 review Minor). The VALUES themselves
    // must never change: they are baked into any layout.json already saved to disk.
    public const string BrowserId         = "Browser";
    public const string CanvasId          = "Canvas";
    public const string DetailsId         = "Details";
    public const string ConditionSearchId = "ConditionSearch";
    public const string DocumentsId       = "Documents";

    // The four leaf tool/document wrapper ids whose Inner VM is [JsonIgnore] — a deserialized
    // instance of one of these has a null Inner (its constructor's live-VM parameter isn't in
    // the JSON payload) and must be swapped for a freshly constructed, live-wired instance by
    // RestoreLayout before the layout is shown. Structural containers (LeftPane/RightPane/
    // Documents ToolDock/DocumentDock) deserialize fine as-is — they have parameterless
    // constructors and carry no live VM of their own.
    private static readonly string[] WrapperIds = { BrowserId, CanvasId, DetailsId, ConditionSearchId };

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

        // Closing a tool tab (the little X) defaults to Dock.Model's RemoveDockable,
        // which drops the dockable from the tree entirely with no way back short of
        // rebuilding the whole layout. Hiding instead moves it to IRootDock.HiddenDockables
        // and remembers its original owner dock, so the View menu's "show tool" commands
        // (MainWindow.ShowToolById) can bring a closed tool back via RestoreDockable(id)
        // instead of only being able to focus an already-open one.
        HideToolsOnClose = true;
    }

    public override IRootDock CreateLayout()
    {
        var canvasDoc = new CanvasDocument(_canvas);
        var documentDock = new DocumentDock
        {
            Id = DocumentsId,
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

    // Registers EditorHostWindow (a themed HostWindow carrying app.ico — Docking Shell Phase 1
    // Task 7) as the window Dock.Avalonia creates when a dockable is floated/torn off, so
    // floating windows match the app icon + theme instead of stock Dock chrome. Task 9 extends
    // this SAME override with ContextLocator/DockableLocator (via SetLocators) so a loaded
    // layout re-attaches live content by id; do not duplicate the override elsewhere.
    public override void InitLayout(IDockable layout)
    {
        SetLocators();
        base.InitLayout(layout);
    }

    private void SetLocators()
    {
        // ContextLocator sets each dockable's .Context by id (the standard Dock.Model
        // mechanism — see FactoryBase.InitDockable/GetContext). Our XAML DataTemplates bind
        // through Inner, not Context (see App.axaml's two-hop-selection comment), so Context
        // isn't load-bearing for rendering today, but it's cheap, matches the documented Dock
        // pattern, and costs nothing to keep correct for any future template that does bind it.
        ContextLocator = new Dictionary<string, Func<object?>>
        {
            [BrowserId]         = () => _browser,
            [CanvasId]          = () => _canvas,
            [DetailsId]         = () => _details,
            [ConditionSearchId] = () => _search,
        };
        // DockableLocator hands back a FRESH, live-wired wrapper instance per id — this is
        // what RestoreLayout uses (see below) to replace deserialized wrapper instances whose
        // Inner is null (their constructor's live-VM argument isn't part of the JSON payload).
        DockableLocator = new Dictionary<string, Func<IDockable?>>
        {
            [BrowserId]         = () => new BrowserTool(_browser),
            [CanvasId]          = () => new CanvasDocument(_canvas),
            [DetailsId]         = () => new DetailsTool(_details),
            [ConditionSearchId] = () => new ConditionSearchTool(_search),
            [DocumentsId]       = () => DocumentDock,
        };
        HostWindowLocator = new Dictionary<string, Func<IHostWindow?>>
        {
            [nameof(IDockWindow)] = () => new EditorHostWindow(),
        };
    }

    /// Re-hydrates a layout previously deserialized by DockLayoutStore.Load, then initialises
    /// it (locators + FactoryBase bookkeeping) exactly like a freshly created layout.
    ///
    /// Two problems, discovered empirically against the installed Dock.Serializer.SystemTextJson
    /// 11.3.12.1 (see .superpowers/sdd/task-9-report.md for the JSON evidence), must be fixed
    /// before a restored layout can show live content:
    ///
    ///  1. BrowserTool/CanvasDocument/DetailsTool/ConditionSearchTool have no parameterless
    ///     constructor (their live VM is a required ctor argument, and [JsonIgnore]d so it's
    ///     never in the JSON). System.Text.Json still constructs them — passing null for the
    ///     unresolvable constructor parameter — so a deserialized wrapper's Inner is null
    ///     (rendering blank, since App.axaml's DataTemplates bind Content="{Binding Inner}").
    ///     ReplaceWrappers swaps each by id for a fresh instance from DockableLocator.
    ///
    ///  2. The custom ObservableCollection converter Dock.Serializer.SystemTextJson uses for
    ///     VisibleDockables/etc. does not participate in System.Text.Json's
    ///     ReferenceHandler.Preserve — so every ActiveDockable/DefaultDockable round-trips as a
    ///     DETACHED DUPLICATE subtree (same Id, different object) instead of the same reference
    ///     as its VisibleDockables entry. Left alone, Dock's tab/active-item rendering (which
    ///     compares by reference) would show no selection. ReconcileActiveDockables re-points
    ///     each ActiveDockable/DefaultDockable back at the matching (now-fresh) VisibleDockables
    ///     entry, recursively, including hidden/pinned dockables and floated windows' layouts.
    public void RestoreLayout(IRootDock restored)
    {
        SetLocators();
        ReplaceWrappers(restored);
        ReconcileActiveDockables(restored);
        DocumentDock = FindDockable(restored, d => d.Id == DocumentsId) as IDocumentDock;
        InitLayout(restored);
    }

    private void ReplaceWrappers(IDockable node)
    {
        if (node is IRootDock root)
        {
            ReplaceInList(root.HiddenDockables);
            ReplaceInList(root.LeftPinnedDockables);
            ReplaceInList(root.RightPinnedDockables);
            ReplaceInList(root.TopPinnedDockables);
            ReplaceInList(root.BottomPinnedDockables);
            if (root.PinnedDock is { } pinned) ReplaceWrappers(pinned);
            if (root.Windows is not null)
                foreach (var window in root.Windows)
                    if (window.Layout is not null) ReplaceWrappers(window.Layout);
        }
        if (node is IDock dock) ReplaceInList(dock.VisibleDockables);
    }

    private void ReplaceInList(IList<IDockable>? list)
    {
        if (list is null) return;
        for (var i = 0; i < list.Count; i++)
        {
            var child = list[i];
            if (child.Id is { Length: > 0 } id && WrapperIds.Contains(id))
            {
                if (DockableLocator is not null && DockableLocator.TryGetValue(id, out var makeFresh)
                    && makeFresh() is { } fresh)
                {
                    list[i] = fresh;   // leaf wrapper — no need to recurse into the fresh instance
                    continue;
                }

                // A known wrapper id that DockableLocator couldn't re-hydrate (e.g. an id that
                // existed in a previously-saved layout.json but has since been renamed/removed
                // from WrapperIds/DockableLocator). The deserialized wrapper's Inner stays null
                // ([JsonIgnore]), so this panel would otherwise render blank with no diagnostic —
                // log it so a future id-rename regression is visible instead of silent.
                AppLog.Warn($"Dock layout restore: no live wrapper found for wrapper id '{id}'; panel will render blank.");
                continue;
            }
            ReplaceWrappers(child);
        }
    }

    private static void ReconcileActiveDockables(IDockable node)
    {
        if (node is IDock dock && dock.VisibleDockables is { } list)
        {
            if (dock.ActiveDockable is { } active && !list.Contains(active))
            {
                var match = list.FirstOrDefault(d => d.Id == active.Id);
                if (match is not null) dock.ActiveDockable = match;
            }
            if (dock.DefaultDockable is { } def && !list.Contains(def))
            {
                var match = list.FirstOrDefault(d => d.Id == def.Id);
                if (match is not null) dock.DefaultDockable = match;
            }
            foreach (var child in list) ReconcileActiveDockables(child);
        }
        if (node is IRootDock root)
        {
            foreach (var l in new[]
                     {
                         root.HiddenDockables, root.LeftPinnedDockables, root.RightPinnedDockables,
                         root.TopPinnedDockables, root.BottomPinnedDockables,
                     })
                if (l is not null)
                    foreach (var d in l) ReconcileActiveDockables(d);
            if (root.PinnedDock is { } pinned) ReconcileActiveDockables(pinned);
            if (root.Windows is not null)
                foreach (var window in root.Windows)
                    if (window.Layout is not null) ReconcileActiveDockables(window.Layout);
        }
    }
}
