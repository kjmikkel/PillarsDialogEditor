using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Guard for issue 176: a control with no FontSize of its own must still follow the font
/// scale. Fluent's control themes read their size from theme keys (ControlContentThemeFontSize
/// and friends), not from the window, so a control type whose key we don't override stays at
/// Fluent's fixed default at every scale. Each control type the app uses is rendered here in a
/// real headless window; add new control types to <see cref="Controls"/> when a view starts
/// using one. Restores scale 1.0 because the Application is an assembly-wide singleton.
/// </summary>
public class UnstyledControlsFollowFontScaleTests
{
    private static double Body =>
        Application.Current!.TryGetResource("FontSize.Body", Application.Current.ActualThemeVariant, out var v)
            ? (double)v! : throw new InvalidOperationException("FontSize.Body is not defined");

    private static IEnumerable<Control> Controls() =>
    [
        new Button { Content = "Button" },
        new global::Avalonia.Controls.Primitives.ToggleButton { Content = "Toggle" },
        new RadioButton { Content = "Radio" },
        new CheckBox { Content = "Check" },
        new ComboBox { ItemsSource = new[] { "One" }, SelectedIndex = 0 },
        new ComboBoxItem { Content = "Item" },
        new TextBox { Text = "Text" },
        new AutoCompleteBox { Text = "Auto" },
        new NumericUpDown { Value = 1 },
        new ListBoxItem { Content = "Row" },
        new ToolTip { Content = "Tip" },
        new TextBlock { Text = "Bare" },
        new Menu { ItemsSource = new[] { new MenuItem { Header = "Menu" } } },
    ];

    private static List<(string Name, double Size)> RenderedSizes(double scale)
    {
        new FontScaleApplier().Apply(scale);
        var panel = new StackPanel();
        foreach (var c in Controls()) panel.Children.Add(c);
        var window = new Window { Content = panel };
        window.Show();
        try
        {
            return panel.Children
                .SelectMany(c => c is Menu m ? m.Items.OfType<Control>() : [c])
                .Select(c => (c.GetType().Name, c.GetValue(TextElement.FontSizeProperty)))
                .ToList();
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void AtBaseScale_UnstyledControlsRenderAtBodySize()
    {
        try
        {
            var wrong = RenderedSizes(1.0).Where(s => s.Size != Body).ToList();
            Assert.True(wrong.Count == 0,
                $"Expected every unstyled control at FontSize.Body ({Body}). Off: " +
                string.Join(", ", wrong.Select(w => $"{w.Name}={w.Size}")));
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void AtDoubleScale_UnstyledControlsRenderAtTwiceBodySize()
    {
        try
        {
            var expected = FontSizeTokens.BaseValues["FontSize.Body"] * 2;
            var wrong = RenderedSizes(2.0).Where(s => s.Size != expected).ToList();
            Assert.True(wrong.Count == 0,
                $"Expected every unstyled control at {expected} at 2.0x. Off: " +
                string.Join(", ", wrong.Select(w => $"{w.Name}={w.Size}")));
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }
}
