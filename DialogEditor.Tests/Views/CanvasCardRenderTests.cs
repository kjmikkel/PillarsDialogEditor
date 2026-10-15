using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Services;
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
}
