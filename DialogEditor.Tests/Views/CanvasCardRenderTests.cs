using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// Issue 82: the rendered card matches CardMetrics, the numbers auto-layout uses. If the template
/// and the estimate drift apart, cards overlap (estimate too short) or waste space (too tall).
public class CanvasCardRenderTests
{
    public CanvasCardRenderTests() => Loc.Configure(new AvaloniaStringProvider());

    public static IEnumerable<object[]> Lines() => new[]
    {
        new object[] { "Tell me about the storm." },
        new object[] { "You're the one asking about the lighthouse keeper? Sit down. Nobody has seen him since the storm put the lamp out." },
        new object[] { string.Concat(Enumerable.Repeat("A long line of dialogue that keeps going. ", 12)) },
        new object[] { "SetGlobalValue(lighthouse_rumour_seen_by_the_harbour_master_on_the_second_night,1)" },
        new object[] { "First paragraph.\nSecond paragraph after a line break." },
    };

    private static (Window, Control card, TextBlock body) Render(
        string text, string speakerGuid = "", Action<NodeViewModel>? setup = null)
    {
        var node = new NodeViewModel(
            new ConversationNode(1, false, SpeakerCategory.Npc, speakerGuid, "", [], [], [], "Conversation", "None"),
            new StringEntry(1, text, ""));
        setup?.Invoke(node);
        node.Input.Owner = node; node.Output.Owner = node;
        var vm = new ConversationViewModel(new StubDispatcher());
        vm.Nodes.Add(node);
        var window = new Window { Width = 1200, Height = 800, Content = new ConversationView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var card = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("card"));
        var body = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("cardBody"));
        return (window, card, body);
    }

    // Spec §6: a link passing under a card is hidden by the card. The GUI screenshots showed a
    // link drawn across a card's text, so this samples the rendered pixel where a straight link
    // between A and B passes under card C's body: it must be the body colour, not the link's.
    [AvaloniaFact]
    public void Connection_PassingUnderACard_IsHiddenByIt()
    {
        NodeViewModel Node(int id, double x, double y)
        {
            var n = new NodeViewModel(
                new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None"),
                new StringEntry(id, "Line " + id, "")) { Location = new LayoutPoint(x, y) };
            n.Input.Owner = n; n.Output.Owner = n;
            return n;
        }
        var a = Node(1, 0, 100);
        var b = Node(2, 900, 100);
        var c = Node(3, 400, 75);   // header above the link's y, body across it
        var vm = new ConversationViewModel(new StubDispatcher());
        vm.Nodes.Add(a); vm.Nodes.Add(b); vm.Nodes.Add(c);
        vm.Connections.Add(new ConnectionViewModel(a.Output, b.Input));
        var view = new ConversationView { DataContext = vm };
        var window = new Window { Width = 1400, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            var cardC = window.GetVisualDescendants().OfType<Grid>()
                .Single(g => g.Classes.Contains("card") && ReferenceEquals(g.DataContext, c));
            var bodyC = cardC.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("cardBody"));
            var editor = view.FindControl<Nodify.NodifyEditor>("Editor")!;
            // The link's y in window pixels: A's output anchor, through the editor's viewport.
            var editorOrigin = editor.TranslatePoint(default, window)!.Value;
            var linkY = editorOrigin.Y + (a.Output.Anchor.Y - editor.ViewportLocation.Y) * editor.ViewportZoom;
            var bodyTop = bodyC.TranslatePoint(default, window)!.Value;
            Assert.True(linkY > bodyTop.Y + 1 && linkY < bodyTop.Y + bodyC.Bounds.Height - 1,
                $"test setup: link y {linkY:0.#} is not across C's body ({bodyTop.Y:0.#}..{bodyTop.Y + bodyC.Bounds.Height:0.#})");
            var sampleX = bodyTop.X + bodyC.Bounds.Width - 20;   // right of the short text, inside the body

            using var frame = window.CaptureRenderedFrame()!;
            using var fb = frame.Lock();
            var scale = frame.PixelSize.Width / window.Bounds.Width;
            // The headless Skia frame may be RGBA or BGRA; read the channels in the frame's own order.
            var bgra = fb.Format == global::Avalonia.Platform.PixelFormat.Bgra8888;
            Color At(double x, double y)
            {
                var o = (int)Math.Round(y * scale) * fb.RowBytes + (int)(x * scale) * 4;
                var p = new byte[4];
                System.Runtime.InteropServices.Marshal.Copy(fb.Address + o, p, 0, 4);
                return bgra ? Color.FromRgb(p[2], p[1], p[0]) : Color.FromRgb(p[0], p[1], p[2]);
            }
            static int Distance(Color x, Color y) => Math.Abs(x.R - y.R) + Math.Abs(x.G - y.G) + Math.Abs(x.B - y.B);

            var bodyColour = ((ISolidColorBrush)((Border)((Visual)bodyC.Parent!).GetVisualParent()!).Background!).Color;
            // Control: on the link left of C, outside every card, the sample must NOT be the body colour,
            // or the test isn't looking at the link at all.
            var control = At(bodyTop.X - 60, linkY);
            Assert.True(Distance(control, bodyColour) > 30, $"test setup: the link isn't where expected (control {control})");
            for (var dy = -1; dy <= 1; dy++)
            {
                var sampled = At(sampleX, linkY + dy / scale);
                Assert.True(Distance(sampled, bodyColour) <= 30,
                    $"the link shows through card C's body at ({sampleX:0},{linkY + dy:0}): sampled {sampled}, body {bodyColour}, link {control}");
            }
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Lines))]
    public void CardRender_HeightMatchesEstimate(string text)
    {
        var (w, card, _) = Render(text);
        try
        {
            var estimate = CardMetrics.EstimateHeight(text);
            Assert.True(card.Bounds.Height <= estimate + 4,
                $"rendered {card.Bounds.Height:0.#} > estimate {estimate:0.#}: layout would overlap");
            Assert.True(card.Bounds.Height >= estimate - CardMetrics.BodyLineHeight,
                $"rendered {card.Bounds.Height:0.#} is more than a line under estimate {estimate:0.#}");
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Lines))]
    public void CardRender_WidthIsCardMetricsWidth(string text)
    {
        var (w, card, _) = Render(text);
        try { Assert.Equal(CardMetrics.Width, card.Bounds.Width, 1); }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_NoSpaceLine_StaysInsideCard()
    {
        var (w, card, body) = Render(new string('x', 300));
        try
        {
            Assert.Equal(CardMetrics.Width, card.Bounds.Width, 1);
            Assert.True(body.Bounds.Right <= card.Bounds.Width + 0.5);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_LongText_TrimsAfterFourLines()
    {
        var (w, _, body) = Render(string.Concat(Enumerable.Repeat("A long line of dialogue. ", 40)));
        try
        {
            Assert.Equal(CardMetrics.MaxBodyLines, body.MaxLines);
            Assert.Equal(TextTrimming.CharacterEllipsis, body.TextTrimming);
            Assert.True(body.TextLayout.TextLines.Count <= CardMetrics.MaxBodyLines);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_EmptyText_MatchesOneLineEstimate()
    {
        var (w, card, _) = Render("");
        try { Assert.True(card.Bounds.Height <= CardMetrics.EstimateHeight("") + 4); }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Ports_DoNotAddSideColumns()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var node = card.GetVisualAncestors().OfType<Nodify.Node>().First();
            Assert.True(node.Bounds.Width <= CardMetrics.Width + 16,
                $"node is {node.Bounds.Width:0} wide for a {CardMetrics.Width} card: connector columns are back");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Ports_HaveAtLeast16pxHitArea()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var node = card.GetVisualAncestors().OfType<Nodify.Node>().First();
            var ports = node.GetVisualDescendants().OfType<Nodify.Connector>().ToList();
            Assert.Equal(2, ports.Count);
            Assert.All(ports, p => Assert.True(p.Bounds.Width >= 16 && p.Bounds.Height >= 16,
                $"port is {p.Bounds.Width:0}x{p.Bounds.Height:0}"));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Ports_AreEightPxDotsCentredOnCardEdgesAtHeaderHeight()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var node = card.GetVisualAncestors().OfType<Nodify.Node>().First();
            var input = node.GetVisualDescendants().OfType<Nodify.NodeInput>().Single();
            var output = node.GetVisualDescendants().OfType<Nodify.NodeOutput>().Single();
            var header = card.GetVisualDescendants().OfType<Border>()
                .First(b => Grid.GetRow(b) == 0 && Grid.GetRowSpan(b) == 1 && b.Child is Grid);

            Point Centre(Control c) => c.TranslatePoint(new Point(c.Bounds.Width / 2, c.Bounds.Height / 2), w)!.Value;
            var cardLeft = card.TranslatePoint(new Point(0, 0), w)!.Value.X;
            var headerY = Centre(header).Y;

            foreach (var (port, edge) in new (Control, double)[] { (input, cardLeft), (output, cardLeft + card.Bounds.Width) })
            {
                var dot = port.GetVisualDescendants().OfType<global::Avalonia.Controls.Shapes.Ellipse>().Single();
                Assert.Equal(8, dot.Bounds.Width, 0.5);
                Assert.Equal(8, dot.Bounds.Height, 0.5);
                var c = Centre(dot);
                Assert.True(Math.Abs(c.X - edge) <= 2, $"{port.GetType().Name} dot centre x {c.X:0.#} vs card edge {edge:0.#}");
                Assert.True(Math.Abs(c.Y - headerY) <= 2, $"{port.GetType().Name} dot centre y {c.Y:0.#} vs header centre {headerY:0.#}");
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Connections_AreNotDrawnOverCards()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var editor = card.GetVisualAncestors().OfType<Nodify.NodifyEditor>().First();
            Assert.False(editor.DisplayConnectionsOnTop);
            // No part of the card may be see-through-but-visible: solid fills are fully opaque or fully
            // transparent (the overlay borders are deliberately Transparent, alpha 0).
            foreach (var b in card.GetVisualDescendants().OfType<Border>().Where(b => b.Background is ISolidColorBrush))
            {
                var a = ((ISolidColorBrush)b.Background!).Color.A;
                Assert.True(a == 0 || a == 255, $"border background alpha {a}: lines would show through");
            }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void DimmedCard_StaysOpaque()
    {
        // Search dims non-matching cards. Dimming must not make them see-through, or every line
        // behind them shows through their text.
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var vm = (ConversationViewModel)card.GetVisualAncestors().OfType<ConversationView>().First().DataContext!;
            vm.Nodes[0].SearchMatchState = SearchMatchState.Dimmed;
            Dispatcher.UIThread.RunJobs();
            var opacity = card.GetSelfAndVisualAncestors().TakeWhile(v => v is not Nodify.ItemContainer)
                              .Aggregate(1.0, (o, v) => o * v.Opacity);
            Assert.Equal(1.0, opacity, 3);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void DimmedCard_ShowsDimmingOverlay_OnlyWhenDimmed()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var vm = (ConversationViewModel)card.GetVisualAncestors().OfType<ConversationView>().First().DataContext!;
            var overlay = card.GetVisualDescendants().OfType<Border>().Single(b => b.Classes.Contains("dimOverlay"));
            Assert.Equal(0.0, overlay.Opacity, 3);
            vm.Nodes[0].SearchMatchState = SearchMatchState.Dimmed;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.6, overlay.Opacity, 3);
            vm.Nodes[0].SearchMatchState = SearchMatchState.Match;
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.0, overlay.Opacity, 3);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_FourLinesAtMost113()
    {
        var (w, card, _) = Render(string.Concat(Enumerable.Repeat("A long line of dialogue. ", 40)));
        try { Assert.True(card.Bounds.Height < 130, $"{card.Bounds.Height} >= the old 130 px pitch"); }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_LongTitle_IsTrimmedInsideTheCard()
    {
        // A speaker name unknown to SpeakerNameService resolves to the raw GUID, so this is a long title.
        var (w, card, _) = Render("Tell me about the storm.", new string('W', 55));
        try
        {
            var title = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Text!.StartsWith("Node 1"));
            var right = title.TranslatePoint(new Point(title.Bounds.Width, 0), card)!.Value.X;
            Assert.True(right <= card.Bounds.Width + 0.5, $"title right edge {right:0.#} is past the {card.Bounds.Width:0.#} card");
            Assert.Contains(title.TextLayout.TextLines, l => l.HasCollapsed);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CornerBadges_AreNotClippedByAnyAncestor()
    {
        // The badges sit half outside the card (Margin -9); Nodify's Node clips to bounds by default, which cut them.
        var (w, card, _) = Render("Tell me about the storm.", setup: n => n.IsConnectionSource = true);
        try
        {
            var badges = card.GetVisualDescendants().OfType<Border>()
                .Where(b => b.Width == 18 && b.Height == 18 && b.Margin.Top < 0).ToList();
            Assert.Equal(2, badges.Count);
            foreach (var badge in badges)
                foreach (var anc in badge.GetVisualAncestors().OfType<Visual>().TakeWhile(a => a is not Nodify.ItemContainer))
                {
                    if (anc is not Control c || !c.ClipToBounds) continue;
                    var tl = badge.TranslatePoint(new Point(0, 0), c)!.Value;
                    var br = badge.TranslatePoint(new Point(badge.Bounds.Width, badge.Bounds.Height), c)!.Value;
                    Assert.True(tl.X >= -0.5 && tl.Y >= -0.5 && br.X <= c.Bounds.Width + 0.5 && br.Y <= c.Bounds.Height + 0.5,
                        $"{c.GetType().Name} clips a badge: badge {tl}..{br} vs {c.Bounds.Width}x{c.Bounds.Height}");
                }
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void MinimapItems_AreCardWidth()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var item = card.GetVisualAncestors().OfType<ConversationView>().First()
                .GetVisualDescendants().OfType<Nodify.MinimapItem>().Single();
            Assert.Equal(CardMetrics.Width, item.Width, 1);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_SameLinesAtFontScale1And2()
    {
        // Spec §7: font scale magnifies the zoom, not the text, so a card wraps identically at every scale.
        const string text = "You're the one asking about the lighthouse keeper? Sit down. Nobody has seen him since the storm put the lamp out.";
        static int[] LineLengths(string t)
        {
            var (w, _, body) = Render(t);
            try { return body.TextLayout.TextLines.Select(l => l.Length).ToArray(); }
            finally { w.Close(); }
        }
        try
        {
            new FontScaleApplier().Apply(1.0);
            var at1 = LineLengths(text);
            new FontScaleApplier().Apply(2.0);
            var at2 = LineLengths(text);
            Assert.True(at1.Length > 1);
            Assert.Equal(at1, at2);
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }
}
