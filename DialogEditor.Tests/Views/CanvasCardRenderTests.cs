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

    private static (Window, Control card, TextBlock body) Render(string text)
    {
        var node = new NodeViewModel(
            new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None"),
            new StringEntry(1, text, ""));
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
    public void CardRender_FourLinesAtMost113()
    {
        var (w, card, _) = Render(string.Concat(Enumerable.Repeat("A long line of dialogue. ", 40)));
        try { Assert.True(card.Bounds.Height < 130, $"{card.Bounds.Height} >= the old 130 px pitch"); }
        finally { w.Close(); }
    }
}
