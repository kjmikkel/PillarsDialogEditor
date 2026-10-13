using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 178: the app's tables are a header grid plus a row grid per item. Their columns must line
/// up, headers must be at least the size of the rows beneath them, a cell that trims its text must
/// offer the full value as a tooltip, and a font-scale change must reach a table that is already open.
/// </summary>
public class TableColumnsTests
{
    // The real strings: cell text such as "Node 1234 · Female" is what has to fit, and the stub
    // provider returns resource keys, which are far longer.
    public TableColumnsTests() => Loc.Configure(new AvaloniaStringProvider());

    public static IEnumerable<object[]> Cases() =>
        from name in TableWindowFixtures.All.Keys
        from scale in new[] { 1.0, 2.0 }
        select new object[] { name, scale };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void HeaderCells_LineUpWithRowCells(string name, double scale) =>
        WithWindow(name, scale, w =>
        {
            foreach (var (header, row) in HeaderRowPairs(w))
                foreach (var h in header.Children.Where(c => c.IsEffectivelyVisible))
                {
                    var r = row.Children.First(c => Grid.GetColumn(c) == Grid.GetColumn(h));
                    var hx = h.TranslatePoint(default, w)!.Value.X;
                    var rx = r.TranslatePoint(default, w)!.Value.X;
                    Assert.True(Math.Abs(hx - rx) <= 0.5,
                        $"{name} at {scale}x: column {Grid.GetColumn(h)} header starts at x = {hx:0.#}, its row cell at x = {rx:0.#}");
                }
        });

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void Headers_AreAtLeastRowSize(string name, double scale) =>
        WithWindow(name, scale, w =>
        {
            foreach (var (header, row) in HeaderRowPairs(w))
            {
                var rowSize = row.GetVisualDescendants().OfType<TextBlock>().Max(t => t.FontSize);
                foreach (var h in header.Children.OfType<TextBlock>())
                    Assert.True(h.FontSize >= rowSize - 0.01,
                        $"{name} at {scale}x: header \"{h.Text}\" is {h.FontSize:0.#} px, its rows {rowSize:0.#} px");
            }
        });

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void TrimmedCells_HaveTooltips(string name, double scale) =>
        WithWindow(name, scale, w =>
        {
            var bare = Items(w)
                .SelectMany(items => items.GetVisualDescendants().OfType<TextBlock>())
                .Where(t => t.TextTrimming != TextTrimming.None && !string.IsNullOrEmpty(t.Text) && IsInItem(t))   // an empty cell has nothing to trim
                .Where(t => ToolTip.GetTip(t) is not string { Length: > 0 })
                .Select(t => $"\"{t.Text}\"")
                .ToList();
            Assert.True(bare.Count == 0, $"{name}: trimmed cells without a tooltip: {string.Join(", ", bare)}");
        });

    [AvaloniaFact]
    public void LiveRescale_WidensAnOpenTable() =>
        WithWindow("FindInProject", 1.0, w =>
        {
            var header = HeaderRowPairs(w).First().Header.Children[0];
            var before = header.Bounds.Width;
            new FontScaleApplier().Apply(2.0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before * 2.0, header.Bounds.Width, 0.5);
        });

    private static IEnumerable<ItemsControl> Items(Window w) =>
        w.GetVisualDescendants().OfType<ItemsControl>()
         .Where(i => i is not ComboBox && i.IsEffectivelyVisible && i.ItemCount > 0);

    // A cell inside a realised item, not the ItemsControl's own chrome.
    private static bool IsInItem(Visual v) =>
        v.GetVisualAncestors().Any(a => a is ListBoxItem || a is ContentPresenter { Parent: ItemsControl or ItemsPresenter });

    // A table's header is a Grid beside its ItemsControl in the same panel, with as many columns as
    // the item's row grid and at least two text cells.
    private static IEnumerable<(Grid Header, Grid Row)> HeaderRowPairs(Window w)
    {
        foreach (var items in Items(w))
        {
            var row = items.GetVisualDescendants().OfType<Grid>()
                           .FirstOrDefault(g => g.ColumnDefinitions.Count >= 2 && IsInItem(g));
            if (row is null || items.GetVisualParent() is not Panel panel) continue;
            var header = panel.Children.OfType<Grid>().FirstOrDefault(g =>
                g.ColumnDefinitions.Count == row.ColumnDefinitions.Count &&
                g.Children.OfType<TextBlock>().Count() >= 2);
            if (header is not null) yield return (header, row);
        }
    }

    private static void WithWindow(string name, double scale, Action<Window> check)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => null;
        var w = TableWindowFixtures.All[name]();
        try
        {
            new FontScaleApplier().Apply(scale);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            check(w);
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }
}
