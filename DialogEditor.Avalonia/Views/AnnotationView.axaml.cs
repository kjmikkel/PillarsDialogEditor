using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class AnnotationView : UserControl
{
    // Raised so ConversationView can forward the delete command to ConversationViewModel.
    public event EventHandler<AnnotationViewModel>? DeleteRequested;

    public AnnotationView()
    {
        InitializeComponent();
        DoubleTapped += OnAnnotationDoubleTapped;
        // Intercept Tab in the tunnel phase (before the focused TextBox moves focus away).
        this.AddHandler(KeyDownEvent, OnKeyDown_Tunnel, RoutingStrategies.Tunnel);
    }

    // Cycles focus between TitleEdit and BodyEdit when editing; prevents Tab escaping the annotation.
    private void OnKeyDown_Tunnel(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Tab) return;
        if (DataContext is not AnnotationViewModel { IsEditing: true }) return;

        // IsKeyboardFocusWithin is correct here: IsFocused is false when focus is on an
        // inner template part of the TextBox rather than the TextBox control itself.
        if (TitleEdit.IsKeyboardFocusWithin)
            BodyEdit.Focus();
        else
            TitleEdit.Focus();

        e.Handled = true;
    }

    // ── Edit mode ─────────────────────────────────────────────────────────

    private void OnAnnotationDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (DataContext is AnnotationViewModel vm && !vm.IsEditing)
            EnterEditMode(e);
    }

    private void Edit_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnnotationViewModel vm && !vm.IsEditing)
            EnterEditMode(e);
    }

    private void EnterEditMode(RoutedEventArgs e)
    {
        if (DataContext is not AnnotationViewModel vm) return;
        vm.IsEditing = true;
        TitleEdit.Focus();
        TitleEdit.SelectAll();
        e.Handled = true;
    }

    private void TitleEdit_LostFocus(object? sender, RoutedEventArgs e) => CommitIfFocusLeft();
    private void BodyEdit_LostFocus(object? sender, RoutedEventArgs e)   => CommitIfFocusLeft();

    // Only commit when focus leaves the annotation entirely — not when Tab moves between fields.
    private void CommitIfFocusLeft()
    {
        if (!IsKeyboardFocusWithin)
            CommitEdit();
    }

    private void TitleEdit_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CommitEdit(); e.Handled = true; }
    }

    private void BodyEdit_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape) { CommitEdit(); e.Handled = true; }
    }

    private void CommitEdit()
    {
        if (DataContext is AnnotationViewModel vm)
            vm.IsEditing = false;
    }

    // ── Delete ────────────────────────────────────────────────────────────

    private void Delete_Click(object? sender, RoutedEventArgs e)
    {
        if (DataContext is AnnotationViewModel vm)
            DeleteRequested?.Invoke(this, vm);
        e.Handled = true;
    }

    // ── Drag / Resize ─────────────────────────────────────────────────────
    // Positions are in canvas (layout) coordinates. GetPosition relative to the
    // Canvas panel gives layout coords regardless of the viewport RenderTransform.

    private enum DragMode { None, Move, Resize }

    private DragMode _dragMode;
    private Point    _dragAnchor;
    private double   _startX, _startY, _startW, _startH;

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);
        if (DataContext is not AnnotationViewModel vm) return;
        if (vm.IsEditing) return;
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed) return;
        if (e.Source is Button) return;

        var canvas = GetLayoutCanvas();
        _dragAnchor = e.GetPosition(canvas);

        if (IsDescendantOf(e.Source, ResizeHandle))
        {
            _dragMode  = DragMode.Resize;
            _startW    = vm.Width;
            _startH    = vm.Height;
        }
        else if (IsDescendantOf(e.Source, TitleBar))
        {
            _dragMode  = DragMode.Move;
            _startX    = vm.X;
            _startY    = vm.Y;
        }
        else
        {
            return; // body area — let clicks fall through to nodes beneath
        }

        e.Pointer.Capture(this);
        e.Handled = true;
    }

    protected override void OnPointerMoved(PointerEventArgs e)
    {
        base.OnPointerMoved(e);
        if (_dragMode == DragMode.None || DataContext is not AnnotationViewModel vm) return;

        var pos  = e.GetPosition(GetLayoutCanvas());
        // Positions are now in screen pixels; divide by zoom to get world-unit deltas.
        var zoom = GetConversationView()?.EditorZoom ?? 1.0;
        var dx   = (pos.X - _dragAnchor.X) / zoom;
        var dy   = (pos.Y - _dragAnchor.Y) / zoom;

        if (_dragMode == DragMode.Move)
        {
            vm.X = _startX + dx;
            vm.Y = _startY + dy;
        }
        else
        {
            vm.Width  = _startW + dx;
            vm.Height = _startH + dy;
        }

        e.Handled = true;
    }

    private ConversationView? GetConversationView() =>
        this.GetVisualAncestors().OfType<ConversationView>().FirstOrDefault();

    protected override void OnPointerReleased(PointerReleasedEventArgs e)
    {
        base.OnPointerReleased(e);
        if (_dragMode != DragMode.None)
        {
            _dragMode = DragMode.None;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    // GetPosition relative to the Canvas panel (grandparent in the visual tree)
    // returns layout coordinates = canvas world coordinates.
    private Visual GetLayoutCanvas()
    {
        Visual? parent = this.GetVisualParent()?.GetVisualParent();
        return parent ?? this;
    }

    private static bool IsDescendantOf(object? source, Visual ancestor)
    {
        Visual? v = source as Visual;
        while (v is not null)
        {
            if (ReferenceEquals(v, ancestor)) return true;
            v = v.GetVisualParent();
        }
        return false;
    }
}
