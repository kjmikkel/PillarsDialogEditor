using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.VisualTree;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Characterization test pinning the keyboard-focus-visibility guarantee (Gaps.md
/// accessibility item 3). The 2026-06-12 audit assumed that replacing a button's
/// template (ToolbarPlainButton/ToolbarPlainToggleButton define only :pointerover/
/// :pressed styles) discards the focus visual — a headless probe disproved that:
/// Avalonia's focus adorner lives in the ADORNER LAYER, independent of the control
/// template, and renders a contrast-proof double ring (2px white outer + 1px
/// semi-transparent black inner) whenever :focus-visible is active. So keyboard focus
/// IS visible on the custom-templated toolbar buttons, in every palette.
///
/// This test pins that behaviour so a future regression — setting FocusAdorner to
/// null app-wide, a theme override, or an Avalonia upgrade changing the default —
/// fails the build instead of silently blinding keyboard users.
/// </summary>
public class FocusVisibilityTests
{
    [AvaloniaFact]
    public void KeyboardFocusOnCustomTemplatedToolbarButton_ShowsVisibleAdornerRing()
    {
        var theme = (ControlTheme)Application.Current!.FindResource("ToolbarPlainButton")!;
        var button = new Button { Content = "?", Theme = theme };
        var window = new Window { Content = new StackPanel { Children = { button } } };
        window.Show();

        button.Focus(NavigationMethod.Tab);

        // Keyboard-driven focus must report :focus-visible (mouse clicks intentionally don't).
        Assert.Contains(":focus-visible", button.Classes);

        // The default focus adorner must be present in the adorner layer...
        var layer = AdornerLayer.GetAdornerLayer(button);
        Assert.NotNull(layer);
        Assert.NotEmpty(layer!.Children);

        // ...and actually paint something: at least one border with a non-transparent
        // brush and a non-zero thickness.
        var paintedBorders = layer.Children
            .SelectMany(c => ((Visual)c).GetSelfAndVisualDescendants().OfType<Border>())
            .Where(b => b.BorderThickness is { Left: > 0 } or { Top: > 0 }
                        && b.BorderBrush is ISolidColorBrush { Color.A: > 0 })
            .ToList();
        Assert.NotEmpty(paintedBorders);
    }
}
