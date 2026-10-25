using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using DialogEditor.Core.Text;

namespace DialogEditor.Avalonia.Behaviors;

/// Shows node text with the game's markup rendered rather than spelled out (GitHub issue 258):
/// bind <c>MarkupText.Source</c> instead of <c>Text</c> and the TextBlock gets one Run per
/// stretch, italic for &lt;i&gt; and &lt;ispeech&gt;, bold for &lt;b&gt;, with the other tags
/// dropped. Only for display: edit fields keep the raw text, since that is what gets saved.
public static class MarkupText
{
    public static readonly AttachedProperty<string?> SourceProperty =
        AvaloniaProperty.RegisterAttached<TextBlock, string?>("Source", typeof(MarkupText));

    public static void SetSource(TextBlock t, string? v) => t.SetValue(SourceProperty, v);
    public static string? GetSource(TextBlock t) => t.GetValue(SourceProperty);

    static MarkupText() =>
        SourceProperty.Changed.AddClassHandler<TextBlock>((t, e) => Fill(t, e.NewValue as string));

    private static void Fill(TextBlock t, string? source)
    {
        var inlines = new InlineCollection();
        if (!string.IsNullOrEmpty(source))
            foreach (var run in TextMarkup.Parse(source))
                inlines.Add(new Run(run.Text)
                {
                    FontStyle  = run.Italic ? FontStyle.Italic : FontStyle.Normal,
                    FontWeight = run.Bold ? FontWeight.Bold : FontWeight.Normal,
                });
        t.Inlines = inlines;
        // Runs leave Text empty, and the automation peer names a TextBlock by its Text.
        AutomationProperties.SetName(t, source is null ? null : TextMarkup.Strip(source));
    }
}
