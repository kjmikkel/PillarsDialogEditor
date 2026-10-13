using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 177: the Condition Editor's And/Or combo sat in a fixed 70 px grid column, so at 2.0×
/// it showed "An". The headless window doesn't resolve the combo's selection, so the clipping
/// guard has no text to measure; this checks the room the combo gives its selection instead.
/// </summary>
public class ConditionOperatorComboTests
{
    // The real strings: the option text ("And", "Or") is what has to fit.
    public ConditionOperatorComboTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void OperatorCombo_FitsEveryOperatorAtDoubleScale()
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => new Size(1366, 768);
        var w = new ConditionEditorWindow(new ConditionEditorViewModel(
            "canonical",
            [new ConditionLeaf("Boolean IsGlobalValue(String, Operator, Int32)", ["flag", "EqualTo", "1"], false, "And")],
            _ => { }));
        try
        {
            new FontScaleApplier().Apply(2.0);
            w.Show();
            Dispatcher.UIThread.RunJobs();

            // The row header's first control is the And/Or combo; its selection box is the one
            // laid-out ContentControl in its template (DataValidationErrors wraps the whole combo).
            var combo = w.GetVisualDescendants().OfType<ComboBox>().First();
            var room = combo.GetVisualDescendants().OfType<ContentControl>()
                            .First(c => c is not DataValidationErrors && c.Bounds.Width > 0).Bounds.Width;
            foreach (var option in ConditionRowViewModel.OperatorOptions)
            {
                var text = new TextBlock { Text = option, FontSize = combo.FontSize, FontFamily = combo.FontFamily };
                text.Measure(Size.Infinity);
                Assert.True(text.DesiredSize.Width <= room,
                    $"\"{option}\" needs {text.DesiredSize.Width:0} px, the combo shows {room:0}");
            }
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }
}
