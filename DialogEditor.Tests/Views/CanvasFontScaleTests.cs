using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.ViewModels;
using DialogEditor.ViewModels;
using Nodify;

namespace DialogEditor.Tests.Views;

/// Issue 82: on the canvas, font scale magnifies the zoom, so a card shows the same text at every scale.
public class CanvasFontScaleTests
{
    private static (Window, NodifyEditor, ConversationView) OpenWithView()
    {
        var view = new ConversationView { DataContext = new ConversationViewModel(new StubDispatcher()) };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view.FindControl<NodifyEditor>("Editor")!, view);
    }

    private static (Window, NodifyEditor) Open()
    {
        var (w, e, _) = OpenWithView();
        return (w, e);
    }

    [AvaloniaFact]
    public void Opens_AtZoomEqualToFontScale()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            var (w, editor) = Open();
            try { Assert.Equal(2.0, editor.ViewportZoom, 3); }
            finally { w.Close(); }
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void ZoomLimits_AreMultipliedByFontScale()
    {
        var (w1, e1) = Open();
        var (min, max) = (e1.MinViewportZoom, e1.MaxViewportZoom);
        w1.Close();
        try
        {
            new FontScaleApplier().Apply(2.0);
            var (w2, e2) = Open();
            try
            {
                Assert.Equal(min * 2, e2.MinViewportZoom, 3);
                Assert.Equal(max * 2, e2.MaxViewportZoom, 3);
            }
            finally { w2.Close(); }
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void LiveFontScaleChange_MultipliesTheCurrentZoom()
    {
        var (w, editor) = Open();
        try
        {
            editor.ViewportZoom = 0.8;
            new FontScaleApplier().Apply(2.0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.6, editor.ViewportZoom, 3);
        }
        finally { new FontScaleApplier().Apply(1.0); w.Close(); }
    }

    [AvaloniaFact]
    public void FontScaleRoundTrip_RestoresZoom()
    {
        var (w, editor) = Open();
        try
        {
            editor.ViewportZoom = 0.8;
            new FontScaleApplier().Apply(2.0);
            new FontScaleApplier().Apply(1.0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.8, editor.ViewportZoom, 3);
        }
        finally { new FontScaleApplier().Apply(1.0); w.Close(); }
    }

    // Docking can re-host the view: a detach/re-attach must not drop live font-scale changes.
    [AvaloniaFact]
    public void LiveFontScaleChange_AfterDetachAndReattach_StillFollows()
    {
        var (window, editor, view) = OpenWithView();
        try
        {
            editor.ViewportZoom = 0.8;
            window.Content = null;
            Dispatcher.UIThread.RunJobs();
            window.Content = view;
            Dispatcher.UIThread.RunJobs();

            new FontScaleApplier().Apply(2.0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.6, editor.ViewportZoom, 3);
        }
        finally { new FontScaleApplier().Apply(1.0); window.Close(); }
    }

    // Spec 3: a live change zooms around the viewport centre, not the top-left.
    [AvaloniaFact]
    public void LiveFontScaleChange_KeepsTheViewportCentreFixed()
    {
        var (w, editor) = Open();
        try
        {
            editor.ViewportLocation = new global::Avalonia.Point(300, 200);
            Dispatcher.UIThread.RunJobs();
            global::Avalonia.Point Centre() => editor.ViewportLocation + new global::Avalonia.Vector(
                editor.Bounds.Width / editor.ViewportZoom / 2, editor.Bounds.Height / editor.ViewportZoom / 2);
            var before = Centre();
            new FontScaleApplier().Apply(2.0);
            // Measured before any layout pass: WindowSizing grows the window on a later pass, which
            // legitimately changes the viewport. Bounds/zoom, not ViewportSize, which Nodify refreshes lazily.
            var after = Centre();
            Assert.InRange(after.X, before.X - 1, before.X + 1);
            Assert.InRange(after.Y, before.Y - 1, before.Y + 1);
        }
        finally { new FontScaleApplier().Apply(1.0); w.Close(); }
    }

    // Issue 82: fit-to-screen must leave the bottom-right minimap free of cards.
    [AvaloniaFact]
    public void FitToScreen_KeepsCardsOutFromUnderTheMinimap()
    {
        var vm = new ConversationViewModel(new StubDispatcher());
        for (var i = 0; i < 16; i++)
            vm.Nodes.Add(CanvasNavigationServiceTests.MakeNode(i, (i % 4) * 340, (i / 4) * 240));
        var view = new ConversationView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            view.FitToScreenClearOfMinimap();
            Dispatcher.UIThread.RunJobs();
            // Measured after layout settled: positions are recomputed from the final bounds.
            var minimap = view.GetVisualDescendants().OfType<Minimap>().Single();
            var mmRect = new Rect(minimap.TranslatePoint(default, view)!.Value, minimap.Bounds.Size);
            var zoom = view.FindControl<NodifyEditor>("Editor")!.ViewportZoom;
            var cards = view.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("card")).ToList();
            Assert.NotEmpty(cards);
            foreach (var card in cards)
            {
                var r = new Rect(card.TranslatePoint(default, view)!.Value, card.Bounds.Size * zoom);
                Assert.False(r.Intersects(mmRect), $"a card at {r} is under the minimap {mmRect}");
            }
        }
        finally { window.Close(); }
    }
}
