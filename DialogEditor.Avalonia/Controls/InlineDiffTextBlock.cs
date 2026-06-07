using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;
using DialogEditor.Patch.GitConflict;

namespace DialogEditor.Avalonia.Controls;

/// A TextBlock that renders one side of a before/after text diff, highlighting
/// the changed run. Each instance shows the before side (ShowAfter=false) or the
/// after side (ShowAfter=true). Reuses TextDiff so the highlighting matches the
/// git-conflict resolution window.
public class InlineDiffTextBlock : TextBlock
{
    // Colours resolve from the token registry (Tokens.axaml) at render time, so the
    // diff highlight shares one source of truth with the rest of the app and a future
    // Layer 2 palette swap re-resolves live. See
    // docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md §10.
    private static IBrush CommonBrush => TokenBrushes.Resolve("Brush.Text.Primary");
    private static IBrush BeforeBrush => TokenBrushes.Resolve("Brush.Diff.Inline.Mine");
    private static IBrush AfterBrush  => TokenBrushes.Resolve("Brush.Diff.Inline.Theirs");

    public static readonly StyledProperty<string?> BeforeProperty =
        AvaloniaProperty.Register<InlineDiffTextBlock, string?>(nameof(Before));
    public static readonly StyledProperty<string?> AfterProperty =
        AvaloniaProperty.Register<InlineDiffTextBlock, string?>(nameof(After));
    public static readonly StyledProperty<bool> ShowAfterProperty =
        AvaloniaProperty.Register<InlineDiffTextBlock, bool>(nameof(ShowAfter));

    public string? Before { get => GetValue(BeforeProperty); set => SetValue(BeforeProperty, value); }
    public string? After  { get => GetValue(AfterProperty);  set => SetValue(AfterProperty, value); }

    /// false → render the before side; true → render the after side.
    public bool ShowAfter { get => GetValue(ShowAfterProperty); set => SetValue(ShowAfterProperty, value); }

    static InlineDiffTextBlock()
    {
        BeforeProperty.Changed.AddClassHandler<InlineDiffTextBlock>((c, _) => c.RenderDiff());
        AfterProperty.Changed.AddClassHandler<InlineDiffTextBlock>((c, _) => c.RenderDiff());
        ShowAfterProperty.Changed.AddClassHandler<InlineDiffTextBlock>((c, _) => c.RenderDiff());
    }

    private void RenderDiff()
    {
        var before = Before ?? "";
        var after  = After ?? "";
        var inlines = new InlineCollection();

        foreach (var span in TextDiff.Diff(before, after))
        {
            switch (span.Kind)
            {
                case DiffKind.Common:
                    inlines.Add(MakeRun(span.Text, CommonBrush));
                    break;
                case DiffKind.MineOnly:
                    if (!ShowAfter) inlines.Add(MakeRun(span.Text, BeforeBrush));
                    break;
                case DiffKind.TheirsOnly:
                    if (ShowAfter) inlines.Add(MakeRun(span.Text, AfterBrush));
                    break;
            }
        }

        Inlines = inlines;
    }

    private static Run MakeRun(string text, IBrush brush) => new(text) { Foreground = brush };
}
