namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 180: colour tokens used for things they weren't meant for. These markup scans keep each
/// token on its purpose.
/// </summary>
public class TokenPurposeTests
{
    [Fact]
    public void DisabledText_OnlyStylesDisabledControls()
    {
        // Brush.Text.Disabled reads as greyed out, so on a hint, an empty state or a label the
        // text looks unavailable when it isn't (and fails contrast in Light). Hints use
        // Brush.Text.Hint. The only legitimate use is a style setter for the :disabled state.
        var offenders = MarkupScan.Elements()
            .Where(m => m.Element.Attributes().Any(a => a.Value.Contains("Resource Brush.Text.Disabled}")))
            .Where(m => !(m.Element.Name.LocalName == "Setter" &&
                          ((string?)m.Element.Parent?.Attribute("Selector"))?.Contains(":disabled") == true))
            .Select(m => $"{m.File}: {m.Element.Name.LocalName} {(string?)m.Element.Attribute("Text") ?? (string?)m.Element.Attribute("Content")}")
            .ToList();
        Assert.True(offenders.Count == 0,
            "Brush.Text.Disabled outside a :disabled style (use Brush.Text.Hint):\n" + string.Join("\n", offenders));
    }
}
