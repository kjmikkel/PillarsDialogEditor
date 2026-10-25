using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;
using System.Collections.Specialized;
using System.ComponentModel;

namespace DialogEditor.Avalonia.Views;

public partial class ConversationView : UserControl
{
    // Exposed so AnnotationView can divide drag deltas by zoom.
    internal double EditorZoom => Editor.ViewportZoom;

    private ConversationViewModel? _annotationVm;

    public ConversationView()
    {
        InitializeComponent();
        InitFontScaleZoom();

        // Issue 82 (spec §6): connections are drawn beneath the cards. NodifyAvalonia 6.6.0's editor
        // template puts the items presenter and PART_ConnectionsHost in one Canvas, both at ZIndex 0
        // with the connections declared second, so links drew across card text even with
        // DisplayConnectionsOnTop=False. A style can't reach the part, so lower it once the template
        // exists. CanvasCardRenderTests.Connection_PassingUnderACard_IsHiddenByIt samples the
        // rendered pixel and fails if a Nodify upgrade changes this.
        Editor.TemplateApplied += (_, e) =>
        {
            if (e.NameScope.Find<Control>("PART_ConnectionsHost") is { } connections)
                connections.ZIndex = -1;
        };

        Editor.PropertyChanged += (_, e) =>
        {
            if (e.Property.Name is nameof(Editor.ViewportZoom) or nameof(Editor.ViewportLocation))
                SyncAllAnnotations();
        };

        DataContextChanged += (_, _) => ResubscribeAnnotations();
        Loaded += (_, _) => { ResubscribeAnnotations(); SyncAllAnnotations(); };
    }

    private void ResubscribeAnnotations()
    {
        if (_annotationVm is not null)
        {
            _annotationVm.Annotations.CollectionChanged -= OnAnnotationsChanged;
            foreach (var ann in _annotationVm.Annotations)
                ann.PropertyChanged -= OnAnnotationWorldChanged;
        }

        _annotationVm = DataContext as ConversationViewModel;

        if (_annotationVm is not null)
        {
            _annotationVm.Annotations.CollectionChanged += OnAnnotationsChanged;
            foreach (var ann in _annotationVm.Annotations)
                ann.PropertyChanged += OnAnnotationWorldChanged;
        }

        SyncAllAnnotations();
    }

    private void OnAnnotationsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (AnnotationViewModel ann in e.OldItems)
                ann.PropertyChanged -= OnAnnotationWorldChanged;
        if (e.NewItems is not null)
            foreach (AnnotationViewModel ann in e.NewItems)
                ann.PropertyChanged += OnAnnotationWorldChanged;
        SyncAllAnnotations();
    }

    private void OnAnnotationWorldChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is AnnotationViewModel ann &&
            e.PropertyName is nameof(AnnotationViewModel.X) or nameof(AnnotationViewModel.Y)
                          or nameof(AnnotationViewModel.Width) or nameof(AnnotationViewModel.Height))
        {
            ann.SyncScreen(Editor.ViewportZoom, Editor.ViewportLocation.X, Editor.ViewportLocation.Y);
        }
    }

    private void SyncAllAnnotations()
    {
        if (DataContext is not ConversationViewModel vm) return;
        var zoom = Editor.ViewportZoom;
        var loc  = Editor.ViewportLocation;
        foreach (var ann in vm.Annotations)
            ann.SyncScreen(zoom, loc.X, loc.Y);
    }

    /// Raised when the user presses Enter on a selected node — MainWindow owns
    /// the detail panel and moves focus there (keyboard path into text editing).
    public event EventHandler? FocusDetailRequested;

    // Keyboard nudge steps (canvas units). Ctrl+arrow = fine, Ctrl+Shift+arrow = coarse.
    private const double NudgeStep      = 10;
    private const double NudgeStepLarge = 50;

    private void Editor_KeyDown(object? sender, KeyEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        var ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        var none  = e.KeyModifiers == KeyModifiers.None;
        var step  = e.KeyModifiers.HasFlag(KeyModifiers.Shift) ? NudgeStepLarge : NudgeStep;

        var handled = e.Key switch
        {
            Key.Right when ctrl => vm.NudgeSelected(step, 0),
            Key.Left  when ctrl => vm.NudgeSelected(-step, 0),
            Key.Up    when ctrl => vm.NudgeSelected(0, -step),
            Key.Down  when ctrl => vm.NudgeSelected(0, step),
            Key.L     when ctrl => vm.TryBeginConnect(),

            Key.Right when none => vm.TryNavigate(CanvasNavDirection.Child),
            Key.Left  when none => vm.TryNavigate(CanvasNavDirection.Parent),
            Key.Up    when none => vm.TryNavigate(CanvasNavDirection.PreviousSibling),
            Key.Down  when none => vm.TryNavigate(CanvasNavDirection.NextSibling),

            Key.PageDown when none => vm.TryCycle(forward: true),
            Key.PageUp   when none => vm.TryCycle(forward: false),
            Key.Home     when none => vm.TrySelectRoot(),

            // Order matters: connect-mode arms must win over the unconditional
            // SelectedNode-based arms below (same Key, both `when none`).
            Key.Enter when none && vm.IsChoosingStartNode => vm.TryConfirmStartNode(vm.SelectedNode),
            Key.Enter when none && vm.IsConnecting => vm.TryConfirmConnection(),
            Key.Enter when none && vm.SelectedNode is not null => RaiseFocusDetail(),

            Key.Apps                                          => OpenSelectedNodeContextMenu(vm),
            Key.F10 when e.KeyModifiers == KeyModifiers.Shift => OpenSelectedNodeContextMenu(vm),

            Key.Escape when none && vm.IsChoosingStartNode => CancelChoosingStartNode(vm),
            Key.Escape when none && vm.IsConnecting => vm.CancelConnect(),
            Key.Escape when none                    => vm.Deselect(),

            _ => false,
        };

        if (!handled) return;
        FollowSelection(vm);
        e.Handled = true;
    }

    // Issue 172: in pick mode a click on a node confirms it as the start node. Selection
    // alone doesn't, so clicking the node that is already selected must still count.
    private void Node_Tapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is ConversationViewModel { IsChoosingStartNode: true } vm
            && (sender as Control)?.DataContext is NodeViewModel node)
            e.Handled = vm.TryConfirmStartNode(node);
    }

    // Issue 172: Esc leaves start-node pick mode without changing anything.
    private static bool CancelChoosingStartNode(ConversationViewModel vm)
    {
        vm.CancelChooseStartNodeCommand.Execute(null);
        return true;
    }

    private bool RaiseFocusDetail()
    {
        FocusDetailRequested?.Invoke(this, EventArgs.Empty);
        return true;
    }

    private bool OpenSelectedNodeContextMenu(ConversationViewModel vm)
    {
        if (vm.SelectedNode is null) return false;

        // The ContextMenu lives on the nodify:Node inside the item template, not
        // on the ItemContainer itself — walk down from the realized container.
        var container = Editor.ContainerFromItem(vm.SelectedNode);
        var owner = container?.GetVisualDescendants()
            .OfType<Control>()
            .FirstOrDefault(c => c.ContextMenu is not null);
        if (owner?.ContextMenu is not { } menu) return false;

        menu.Open(owner);
        return true;
    }

    // Keep the selected node on screen after every keyboard move.
    private void FollowSelection(ConversationViewModel vm)
    {
        if (vm.SelectedNode is { } node)
            Editor.BringIntoView(new global::Avalonia.Point(node.Location.X, node.Location.Y));
    }

    private void Editor_GotFocus(object? sender, GotFocusEventArgs e)
    {
        // Only keyboard-driven focus (Tab) auto-restores a selection. Pointer
        // focus must not: clicking empty canvas is how mouse users deselect.
        if (e.NavigationMethod != NavigationMethod.Tab) return;
        if (DataContext is not ConversationViewModel vm) return;

        if (vm.EnsureKeyboardSelection())
            FollowSelection(vm);
    }

    public void FocusSearch()
    {
        SearchBox.Focus();
        SearchBox.SelectAll();
    }

    public void FocusEditor() => Editor.Focus();

    private void SearchBox_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && DataContext is ConversationViewModel vm)
        {
            vm.SearchQuery = string.Empty;
            Editor.Focus();
            e.Handled = true;
        }
    }

    // ── Font scale magnifies the canvas (issue 82) ───────────────────────────────
    // The canvas's text, cards and gaps are fixed canvas units (Canvas.* tokens / CardMetrics)
    // that FontScaleApplier never touches. Font scale multiplies the zoom instead, so a card
    // shows exactly the same text at 2.0x as at 1.0x, and saved layouts stay valid at every
    // scale. The zoom range is scaled too, so the user's zoom range doesn't change.
    // The subscription lives between attach and detach (not constructor to detach): the docking
    // shell can re-host this view, and a constructor-only subscription would silently drop live
    // font-scale changes after a re-attach. Re-attaching re-applies the current scale to catch
    // any change that happened while detached.
    private double _baseMinZoom, _baseMaxZoom;
    private bool _fontScaleAttachedBefore;
    internal double FontScaleFactor { get; private set; } = 1.0;

    private void InitFontScaleZoom()
    {
        _baseMinZoom = Editor.MinViewportZoom;
        _baseMaxZoom = Editor.MaxViewportZoom;
        AttachedToVisualTree += (_, _) =>
        {
            ApplyFontScale(FontScaleService.Current.Scale, keepUserZoom: _fontScaleAttachedBefore);
            _fontScaleAttachedBefore = true;
            FontScaleService.Current.PropertyChanged -= OnFontScaleChanged; // idempotent
            FontScaleService.Current.PropertyChanged += OnFontScaleChanged;
        };
        DetachedFromVisualTree += (_, _) => FontScaleService.Current.PropertyChanged -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FontScaleService.Scale))
            ApplyFontScale(FontScaleService.Current.Scale, keepUserZoom: true);
    }

    private void ApplyFontScale(double scale, bool keepUserZoom)
    {
        if (scale <= 0) return;
        var userZoom = keepUserZoom ? Editor.ViewportZoom / FontScaleFactor : 1.0;
        // Keep the viewport centre in place while the zoom changes.
        var centre = Editor.ViewportLocation + new global::Avalonia.Vector(
            Editor.ViewportSize.Width / 2, Editor.ViewportSize.Height / 2);
        FontScaleFactor = scale;
        // Widen the range before zooming, so the new zoom is never clamped by the old limits.
        Editor.MinViewportZoom = Math.Min(_baseMinZoom * scale, Editor.MinViewportZoom);
        Editor.MaxViewportZoom = Math.Max(_baseMaxZoom * scale, Editor.MaxViewportZoom);
        Editor.ViewportZoom    = userZoom * scale;
        Editor.MinViewportZoom = _baseMinZoom * scale;
        Editor.MaxViewportZoom = _baseMaxZoom * scale;
        Editor.ViewportLocation = centre - new global::Avalonia.Vector(
            Editor.ViewportSize.Width / 2, Editor.ViewportSize.Height / 2);
    }

    // Issue 82: plain FitToScreen fits content to the whole viewport, so the bottom-right minimap
    // covered cards. Widening the fitted area on the right by the minimap's share of the viewport
    // width leaves a free strip there for it.
    internal void FitToScreenClearOfMinimap()
    {
        var extent = Editor.ItemsExtent;
        if (extent.Width <= 0 || Editor.Bounds.Width <= 0) { Editor.FitToScreen(); return; }
        var reserved = Minimap.Bounds.Width + Minimap.Margin.Left + Minimap.Margin.Right;
        var usable = Math.Max(1, Editor.Bounds.Width - reserved);
        var widened = extent.Width * Editor.Bounds.Width / usable;
        Editor.FitToScreen(new global::Avalonia.Rect(extent.X, extent.Y, widened, extent.Height));
    }

    private void FitToScreen_Click(object? sender, RoutedEventArgs e) => FitToScreenClearOfMinimap();
    private void ZoomIn_Click(object? sender, RoutedEventArgs e)      => Editor.ZoomIn();
    private void ZoomOut_Click(object? sender, RoutedEventArgs e)     => Editor.ZoomOut();

    public void ScrollToNode(NodeViewModel node)
    {
        node.IsSelected = true;
        Editor.BringIntoView(new global::Avalonia.Point(node.Location.X, node.Location.Y));
    }

    private void CenterOnRoot_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;
        var root = vm.Nodes.FirstOrDefault(n => n.NodeId == 0);
        if (root is not null)
            Editor.BringIntoView(new global::Avalonia.Point(root.Location.X, root.Location.Y));
    }

    // ── Annotation handlers ───────────────────────────────────────────────

    // Canvas coords captured on right-click (PointerPressed fires before the context menu
    // opens, so the position is still accurate when a MenuItem.Click eventually fires).
    private global::Avalonia.Point _contextMenuCanvasPos;

    private void Editor_PointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(Editor).Properties.IsRightButtonPressed) return;
        var screenPos = e.GetPosition(Editor);
        var zoom      = Editor.ViewportZoom;
        var origin    = Editor.ViewportLocation;
        var (cx, cy)  = CanvasMath.ScreenToCanvas(screenPos.X, screenPos.Y, zoom, origin.X, origin.Y);
        _contextMenuCanvasPos = new global::Avalonia.Point(cx, cy);
    }

    private void AddAnnotation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        // Place at the centre of the current viewport.
        var zoom  = Editor.ViewportZoom;
        var loc   = Editor.ViewportLocation;
        var cx    = loc.X + (Editor.Bounds.Width  / 2) / zoom;
        var cy    = loc.Y + (Editor.Bounds.Height / 2) / zoom;

        var annotation = new AnnotationViewModel { X = cx - 120, Y = cy - 70 };
        vm.AddAnnotation(annotation);
    }

    private void CanvasMenu_AddNode_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        var newId   = vm.NextNodeId();
        var newNode = new NodeViewModel(
            new ConversationNode(newId, false, SpeakerCategory.Npc,
                string.Empty, string.Empty, [], [], [], "Conversation", "None"),
            new StringEntry(newId, string.Empty, string.Empty));

        vm.AddNode(newNode, new LayoutPoint((int)_contextMenuCanvasPos.X, (int)_contextMenuCanvasPos.Y));
    }

    private void CanvasMenu_AddAnnotation_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;

        var annotation = new AnnotationViewModel
        {
            X = _contextMenuCanvasPos.X - 120,
            Y = _contextMenuCanvasPos.Y - 70,
        };
        vm.AddAnnotation(annotation);
    }

    private void Annotation_DeleteRequested(object? sender, AnnotationViewModel annotation)
    {
        if (DataContext is ConversationViewModel vm)
            vm.DeleteAnnotation(annotation);
    }

    private void Editor_DoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is not ConversationViewModel vm) return;
        if (!vm.IsEditable) return;

        // Only create a node when tapping directly on the editor background
        if (e.Source is not Nodify.NodifyEditor) return;

        var screenPos  = e.GetPosition(Editor);
        var zoom       = Editor.ViewportZoom;
        var origin     = Editor.ViewportLocation;
        var (cx, cy)   = CanvasMath.ScreenToCanvas(screenPos.X, screenPos.Y, zoom, origin.X, origin.Y);
        var canvasPos  = new global::Avalonia.Point(cx, cy);

        var newId  = vm.NextNodeId();
        var newNode = new NodeViewModel(
            new ConversationNode(newId, false, SpeakerCategory.Npc,
                string.Empty, string.Empty, [], [], [], "Conversation", "None"),
            new StringEntry(newId, string.Empty, string.Empty));

        vm.AddNode(newNode, new LayoutPoint((int)canvasPos.X, (int)canvasPos.Y));
        e.Handled = true;
    }
}
