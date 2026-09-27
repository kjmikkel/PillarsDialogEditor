using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// Issue #72: the report button's label and tooltip come from the real Strings.axaml
/// (AvaloniaStringProvider renders a missing key as "[Key]"), while the pre-filled issue
/// body is deliberately English whatever the UI language.
/// </summary>
public class ExceptionReportIssueStringTests
{
    public ExceptionReportIssueStringTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void IssueText_EveryTemplateIsFilledIn()
    {
        var text = ExceptionReportViewModel.EnglishIssueText;

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

    /// Issue-body labels are English whatever the UI language: the issue lands on a public,
    /// English-language tracker, beside an exception type and stack trace that are English
    /// anyway (same reasoning as the clipboard CopyText).
    [AvaloniaFact]
    public void DefaultReportUrl_BodyStaysEnglish_UnderATranslatedUi()
    {
        Loc.Configure(new TranslatedStringProvider());

        var vm = new ExceptionReportViewModel(
            new InvalidOperationException("oops"), @"C:\logs\app.log", "https://example.com/issues");

        var decoded = Uri.UnescapeDataString(vm.ReportUrl);

        Assert.StartsWith("https://example.com/issues/new?", decoded);
        Assert.Contains("Crash: InvalidOperationException: oops", decoded);
        Assert.Contains("Editor version: ", decoded);
        Assert.Contains("Operating system: ", decoded);
        Assert.Contains("### Error message", decoded);
        Assert.Contains("What were you doing when the error occurred?", decoded);
        Assert.DoesNotContain("XX:", decoded);
    }

    /// Stands in for a UI translation overlay: every key resolves to non-English text.
    private sealed class TranslatedStringProvider : IStringProvider
    {
        public string Get(string key) => "XX:" + key;
        public bool TryGet(string key, out string value) { value = Get(key); return true; }
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
