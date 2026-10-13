using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 188: Find/Replace stacks its Find and Replace rows as separate label/box grids, each
/// with an Auto label column, so each box started right after its own label and "Find:" (shorter
/// than "Replace:") left the Find box further left. Stacked label rows in the same panel must
/// start their inputs at the same x, at any font scale.
/// </summary>
public class StackedInputAlignmentTests
{
    public StackedInputAlignmentTests() => Loc.Configure(new StubStringProvider());

    public static IEnumerable<object[]> Cases() =>
        from type in new[] { typeof(FindReplaceWindow), typeof(BatchReplaceWindow) }
        from scale in new[] { 1.0, 2.0 }
        select new object[] { type.AssemblyQualifiedName!, scale };

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void StackedLabelRows_StartTheirInputsAtTheSameX(string typeName, double scale)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => null;
        var w = (Window)Activator.CreateInstance(Type.GetType(typeName)!)!;
        try
        {
            new FontScaleApplier().Apply(scale);
            w.Show();
            Dispatcher.UIThread.RunJobs();

            // A label row: a Grid whose column 0 holds a TextBlock label and column 1 a TextBox.
            var rows = w.GetVisualDescendants().OfType<Grid>()
                .Where(g => g.ColumnDefinitions.Count >= 2
                            && g.Children.Any(c => Grid.GetColumn(c) == 0 && c is TextBlock)
                            && g.Children.Any(c => Grid.GetColumn(c) == 1 && c is TextBox))
                .ToList();

            // Rows that are siblings in one panel are stacked; their boxes must line up.
            foreach (var group in rows.GroupBy(g => g.GetVisualParent()).Where(g => g.Count() > 1))
            {
                var xs = group.Select(g =>
                {
                    var box = g.Children.OfType<TextBox>().First(c => Grid.GetColumn(c) == 1);
                    return box.TranslatePoint(new Point(0, 0), w)!.Value.X;
                }).ToList();
                Assert.True(xs.Max() - xs.Min() <= 0.5,
                    $"{w.GetType().Name} at {scale}x: stacked input boxes start at x = {string.Join(", ", xs.Select(x => x.ToString("0.#")))}");
            }
            Assert.NotEmpty(rows);
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }
}
