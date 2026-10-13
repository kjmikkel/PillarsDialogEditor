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
/// Issue 177: label columns that used to have a fixed width now size to their text (Auto). A
/// fixed width left a gap after short labels; an Auto column ends exactly where the label does,
/// so at 2.0× "⚙ N" ran straight into "node has N…". Every two-column label row must keep a
/// visible gap between the label and what follows it.
/// </summary>
public class LabelColumnGapTests
{
    public LabelColumnGapTests() => Loc.Configure(new StubStringProvider());

    private const double MinGap = 4;

    public static IEnumerable<object[]> Windows() =>
    [
        [typeof(LegendWindow).AssemblyQualifiedName!],
        [typeof(ConflictResolutionDialog).AssemblyQualifiedName!],
        [typeof(VoImportDialog).AssemblyQualifiedName!],
        [typeof(FindReplaceWindow).AssemblyQualifiedName!],
        [typeof(BatchReplaceWindow).AssemblyQualifiedName!],
    ];

    [AvaloniaTheory]
    [MemberData(nameof(Windows))]
    public void AutoLabelColumns_KeepAGap(string typeName)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => null;
        var w = (Window)Activator.CreateInstance(Type.GetType(typeName)!)!;
        try
        {
            new FontScaleApplier().Apply(2.0);
            w.Show();
            Dispatcher.UIThread.RunJobs();

            var tight = new List<string>();
            foreach (var g in w.GetVisualDescendants().OfType<Grid>())
            {
                if (!g.IsEffectivelyVisible || g.ColumnDefinitions.Count < 2 || !g.ColumnDefinitions[0].Width.IsAuto) continue;
                var label = g.Children.OfType<Control>().FirstOrDefault(c => Grid.GetColumn(c) == 0 && c.IsVisible);
                var next  = g.Children.OfType<Control>().FirstOrDefault(c => Grid.GetColumn(c) == 1 && c.IsVisible);
                if (label is not TextBlock tb || next is null || string.IsNullOrEmpty(tb.Text)) continue;
                // Measure the text itself, not the column: MinWidth can make the cell wider than the text.
                var textRight = tb.Bounds.X + tb.Padding.Left + tb.TextLayout.WidthIncludingTrailingWhitespace;
                var gap = next.Bounds.X - textRight;
                if (gap < MinGap) tight.Add($"\"{tb.Text}\": gap {gap:0.#} px");
            }
            Assert.True(tight.Count == 0, $"{w.GetType().Name} at 2.0x:\n" + string.Join("\n", tight));
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }
}
