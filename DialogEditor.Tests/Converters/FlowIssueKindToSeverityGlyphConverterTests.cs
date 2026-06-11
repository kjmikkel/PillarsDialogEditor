using System.Globalization;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Core.Analytics;
using Xunit;

namespace DialogEditor.Tests.Converters;

public class FlowIssueKindToSeverityGlyphConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    [Theory]
    [InlineData(FlowIssueKind.Unreachable,             "⛔")] // ⛔ error
    [InlineData(FlowIssueKind.PlayerDeadEnd,           "⚠")] // ⚠ warning
    [InlineData(FlowIssueKind.EmptyText,               "⚠")]
    [InlineData(FlowIssueKind.NoIncomingLinks,         "⚠")]
    [InlineData(FlowIssueKind.BarkTextTooLong,         "⚠")]
    [InlineData(FlowIssueKind.BarkHasPlayerChoiceChild,"⚠")]
    public void Convert_FlowIssueKind_ReturnsExpectedGlyph(FlowIssueKind kind, string expected)
        => Assert.Equal(expected, new FlowIssueKindToSeverityGlyphConverter().Convert(kind, typeof(string), null, Inv));
}
