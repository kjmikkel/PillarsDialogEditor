using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Nodify;

namespace DialogEditor.Avalonia.Controls;

/// The conversation canvas backdrop: the theme's canvas colour plus a faint dot grid that pans
/// and zooms with the NodifyEditor in front of it (GitHub issue 225; spec
/// docs/superpowers/specs/2026-10-15-canvas-readable-cards-design.md §6).
///
/// Drawn here rather than as the editor's Background brush bound to Nodify's ViewportTransform:
/// that transform is a group Nodify updates in place, and whether a brush redraws on an in-place
/// change is not something the headless tests can show. This control recomputes the tile matrix
/// from ViewportLocation/ViewportZoom on every change, so the mapping is plain arithmetic that
/// TileMatrix's tests pin down. One tiled brush fill per frame keeps the cost flat however many
/// nodes the conversation has.
public class CanvasDotGrid : Control
{
    /// Distance between dots in graph units, before zoom.
    public const double Spacing = 20;

    /// Below this on-screen spacing the dots merge into a grey haze, so none are drawn.
    public const double MinScreenSpacing = 8;

    private const double DotSize = 1.5;

    public static readonly StyledProperty<NodifyEditor?> EditorProperty =
        AvaloniaProperty.Register<CanvasDotGrid, NodifyEditor?>(nameof(Editor));
    public static readonly StyledProperty<IBrush?> BackgroundProperty =
        AvaloniaProperty.Register<CanvasDotGrid, IBrush?>(nameof(Background));
    public static readonly StyledProperty<IBrush?> DotBrushProperty =
        AvaloniaProperty.Register<CanvasDotGrid, IBrush?>(nameof(DotBrush));

    public NodifyEditor? Editor { get => GetValue(EditorProperty); set => SetValue(EditorProperty, value); }
    public IBrush? Background   { get => GetValue(BackgroundProperty); set => SetValue(BackgroundProperty, value); }
    public IBrush? DotBrush     { get => GetValue(DotBrushProperty); set => SetValue(DotBrushProperty, value); }

    static CanvasDotGrid()
    {
        AffectsRender<CanvasDotGrid>(BackgroundProperty, DotBrushProperty);
        EditorProperty.Changed.AddClassHandler<CanvasDotGrid>((g, e) =>
        {
            if (e.OldValue is NodifyEditor old) old.PropertyChanged -= g.OnEditorPropertyChanged;
            if (e.NewValue is NodifyEditor now) now.PropertyChanged += g.OnEditorPropertyChanged;
            g.InvalidateVisual();
        });
    }

    private void OnEditorPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == NodifyEditor.ViewportLocationProperty || e.Property == NodifyEditor.ViewportZoomProperty)
            InvalidateVisual();
    }

    /// Maps tile space (graph units) to screen space the way Nodify maps graph to screen:
    /// a graph point p is drawn at (p - location) * zoom.
    public static Matrix TileMatrix(Point location, double zoom) =>
        Matrix.CreateScale(zoom, zoom) * Matrix.CreateTranslation(-location.X * zoom, -location.Y * zoom);

    public static bool IsDrawnAt(double zoom) => Spacing * zoom >= MinScreenSpacing;

    public override void Render(DrawingContext context)
    {
        var bounds = new Rect(Bounds.Size);
        if (Background is { } background) context.FillRectangle(background, bounds);

        if (Editor is not { } editor || DotBrush is not { } dot || !IsDrawnAt(editor.ViewportZoom)) return;

        // A transparent square fixes the tile's extent at one Spacing; the dot sits in its middle.
        var tile = new DrawingGroup
        {
            Children =
            {
                new GeometryDrawing { Brush = Brushes.Transparent, Geometry = new RectangleGeometry(new Rect(0, 0, Spacing, Spacing)) },
                new GeometryDrawing
                {
                    Brush    = dot,
                    Geometry = new EllipseGeometry(new Rect((Spacing - DotSize) / 2, (Spacing - DotSize) / 2, DotSize, DotSize)),
                },
            },
        };
        var brush = new DrawingBrush(tile)
        {
            TileMode        = TileMode.Tile,
            Stretch         = Stretch.None,
            SourceRect      = new RelativeRect(0, 0, Spacing, Spacing, RelativeUnit.Absolute),
            DestinationRect = new RelativeRect(0, 0, Spacing, Spacing, RelativeUnit.Absolute),
            Transform       = new MatrixTransform(TileMatrix(editor.ViewportLocation, editor.ViewportZoom)),
            TransformOrigin = RelativePoint.TopLeft,
        };
        context.FillRectangle(brush, bounds);
    }
}
