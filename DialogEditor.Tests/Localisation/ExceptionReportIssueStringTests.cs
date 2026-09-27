using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// Issue #72: every label in the pre-filled GitHub issue, and the report button itself,
/// comes from the real Strings.axaml. AvaloniaStringProvider renders a missing key as
/// "[Key]", so a typo'd or forgotten resource shows up here as a bracketed key.
/// </summary>
public class ExceptionReportIssueStringTests
{
    public ExceptionReportIssueStringTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void IssueText_EveryTemplateResolvesToARealResource()
    {
        var text = ExceptionReportViewModel.LocalisedIssueText();

        foreach (var value in new[]
                 {
                     text.TitleFormat, text.VersionLine, text.OperatingSystemLine, text.ExceptionLine,
                     text.MessageHeading, text.StackTraceHeading, text.TruncatedMarker,
                     text.LogFileLine, text.StepsHeading,
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(value));
            Assert.DoesNotContain("[ExceptionReport_", value);
        }

        // Positional placeholders the builder fills in.
        Assert.Contains("{0}", text.TitleFormat);
        Assert.Contains("{1}", text.TitleFormat);
        Assert.Contains("{0}", text.VersionLine);
        Assert.Contains("{0}", text.OperatingSystemLine);
        Assert.Contains("{0}", text.ExceptionLine);
        Assert.Contains("{0}", text.LogFileLine);
    }

    [AvaloniaFact]
    public void ReportButtonLabelAndTooltip_AreRealResources()
    {
        Assert.True(new AvaloniaStringProvider().TryGet("ExceptionReport_ReportButton", out var label));
        Assert.False(string.IsNullOrWhiteSpace(label));
        Assert.True(new AvaloniaStringProvider().TryGet("ToolTip_ExceptionReport_IssuesLink", out var tip));
        Assert.Contains("review", tip, StringComparison.OrdinalIgnoreCase);
    }

    [AvaloniaFact]
    public void DefaultReportUrl_UsesTheRealTitleTemplate()
    {
        var vm = new ExceptionReportViewModel(
            new InvalidOperationException("oops"), @"C:\logs\app.log", "https://example.com/issues");

        var decoded = Uri.UnescapeDataString(vm.ReportUrl);

        Assert.StartsWith("https://example.com/issues/new?", decoded);
        Assert.Contains("InvalidOperationException: oops", decoded);
        Assert.DoesNotContain("[ExceptionReport_", decoded);
    }
}
