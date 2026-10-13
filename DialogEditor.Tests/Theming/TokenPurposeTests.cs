using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using DialogEditor.Avalonia.Views;
using DialogEditor.PatchManager;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 180: colour tokens used for things they weren't meant for. These markup scans keep each
/// token on its purpose.
/// </summary>
public class TokenPurposeTests
{
    // Some windows look up strings while they are built (Tag Reference does).
    public TokenPurposeTests() => Loc.Configure(new StubStringProvider());

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

    // ── Window surfaces ──────────────────────────────────────────────────────────

    [AvaloniaTheory]
    // No Background of their own: they fell back to Fluent's (pure black in Dark, pure white in Light).
    [InlineData(typeof(TagReferenceWindow))]
    [InlineData(typeof(VoAliasPickerWindow))]
    [InlineData(typeof(BranchesWindow))]
    [InlineData(typeof(AliasImportConfirmDialog))]
    [InlineData(typeof(BatchVoImportDialog))]
    [InlineData(typeof(ExceptionReportWindow))]
    [InlineData(typeof(VoImportDialog))]
    // "Info" windows had a navy/lavender surface no other window used.
    [InlineData(typeof(AboutWindow))]
    [InlineData(typeof(LegendWindow))]
    [InlineData(typeof(ChangelogWindow))]
    [InlineData(typeof(DiffHelpWindow))]
    [InlineData(typeof(PatchManagerAboutWindow))]
    public void Window_UsesTheWindowSurface(Type windowType)
    {
        var w = (Window)Activator.CreateInstance(windowType)!;
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            Assert.True(Application.Current!.TryGetResource("Brush.Surface.Window", null, out var token));
            Assert.Equal(((ISolidColorBrush)token!).Color, Assert.IsAssignableFrom<ISolidColorBrush>(w.Background).Color);
        }
        finally { w.Close(); }
    }

    [Fact]
    public void NoWindow_UsesTheInfoSurface()
    {
        // Brush.Surface.Info stays for the canvas minimap, which has to stand apart from the
        // canvas behind it. A window that looks different for being "info" has no meaning.
        var offenders = MarkupScan.Elements()
            .Where(m => m.Element.Parent is null && m.Element.Name.LocalName == "Window" &&
                        ((string?)m.Element.Attribute("Background"))?.Contains("Brush.Surface.Info") == true)
            .Select(m => m.File)
            .ToList();
        Assert.True(offenders.Count == 0, "Window on Brush.Surface.Info:\n" + string.Join("\n", offenders));
    }
}
