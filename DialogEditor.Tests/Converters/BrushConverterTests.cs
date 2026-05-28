using System.Globalization;
using Avalonia.Media;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Models;

namespace DialogEditor.Tests.Converters;

public class BrushConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static Color BrushColor(object? result)
        => ((ISolidColorBrush)result!).Color;

    // ── BoolToFemaleTextBrushConverter ────────────────────────────────────

    [Fact]
    public void BoolToFemaleTextBrush_True_ReturnsActiveBrush()
        => Assert.Equal(Color.FromRgb(0xe8, 0xe8, 0xe8),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(true, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void BoolToFemaleTextBrush_False_ReturnsDimBrush()
        => Assert.Equal(Color.FromRgb(0x55, 0x55, 0x55),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(false, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void BoolToFemaleTextBrush_Null_ReturnsDimBrush()
        => Assert.Equal(Color.FromRgb(0x55, 0x55, 0x55),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv)));

    // ── BoolToNewConversationBrushConverter ───────────────────────────────

    [Fact]
    public void BoolToNewConversationBrush_True_ReturnsGreenBrush()
        => Assert.Equal(Color.FromRgb(0x7d, 0xce, 0xa0),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(true, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void BoolToNewConversationBrush_False_ReturnsNormalBrush()
        => Assert.Equal(Color.FromRgb(0xcc, 0xcc, 0xcc),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(false, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void BoolToNewConversationBrush_Null_ReturnsNormalBrush()
        => Assert.Equal(Color.FromRgb(0xcc, 0xcc, 0xcc),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv)));

    // ── FlowIssueKindToSeverityBrushConverter ─────────────────────────────

    [Theory]
    [InlineData(FlowIssueKind.Unreachable,              0xc0, 0x39, 0x2b)] // Red
    [InlineData(FlowIssueKind.PlayerDeadEnd,            0xb8, 0x76, 0x0a)] // Amber
    [InlineData(FlowIssueKind.EmptyText,                0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.NoIncomingLinks,          0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.BarkTextTooLong,          0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.BarkHasPlayerChoiceChild, 0xb8, 0x76, 0x0a)]
    public void FlowIssueKindToSeverityBrush_ReturnsExpectedColor(FlowIssueKind kind, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b),
            BrushColor(new FlowIssueKindToSeverityBrushConverter().Convert(kind, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void FlowIssueKindToSeverityBrush_NonKindValue_ReturnsAmber()
        => Assert.Equal(Color.FromRgb(0xb8, 0x76, 0x0a),
            BrushColor(new FlowIssueKindToSeverityBrushConverter().Convert("not-a-kind", typeof(ISolidColorBrush), null, Inv)));

    // ── PropertyValueStyleToBrushConverter ────────────────────────────────

    [Theory]
    [InlineData(PropertyValueStyle.Condition, 0xe8, 0xa0, 0x20)]
    [InlineData(PropertyValueStyle.Script,    0x7d, 0xce, 0xa0)]
    [InlineData(PropertyValueStyle.Code,      0x9c, 0xdc, 0xfe)]
    [InlineData(PropertyValueStyle.Default,   0xe8, 0xe8, 0xe8)]
    public void PropertyValueStyleToBrush_ReturnsExpectedColor(PropertyValueStyle style, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b),
            BrushColor(new PropertyValueStyleToBrushConverter().Convert(style, typeof(ISolidColorBrush), null, Inv)));

    [Fact]
    public void PropertyValueStyleToBrush_NullValue_ReturnsNull()
    {
        var result = new PropertyValueStyleToBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv);
        Assert.Null(result);
    }

    // ── SpeakerCategoryToBrushConverter ───────────────────────────────────

    [Theory]
    // Header (default zone — null or any unrecognised parameter)
    [InlineData(SpeakerCategory.Npc,      null,     0x7b, 0x24, 0x1c)]
    [InlineData(SpeakerCategory.Player,   null,     0x1a, 0x52, 0x76)]
    [InlineData(SpeakerCategory.Narrator, null,     0x0e, 0x66, 0x55)]
    [InlineData(SpeakerCategory.Script,   null,     0x2c, 0x3e, 0x50)]
    // Body
    [InlineData(SpeakerCategory.Npc,      "body",   0xF5, 0xF0, 0xD0)]
    [InlineData(SpeakerCategory.Player,   "body",   0xD5, 0xE8, 0xF5)]
    [InlineData(SpeakerCategory.Narrator, "body",   0xD5, 0xF0, 0xE8)]
    [InlineData(SpeakerCategory.Script,   "body",   0xE0, 0xE0, 0xE0)]
    // Footer
    [InlineData(SpeakerCategory.Npc,      "footer", 0xE8, 0xE0, 0xB0)]
    [InlineData(SpeakerCategory.Player,   "footer", 0xB0, 0xCD, 0xE8)]
    [InlineData(SpeakerCategory.Narrator, "footer", 0xB0, 0xE0, 0xD5)]
    [InlineData(SpeakerCategory.Script,   "footer", 0xC8, 0xC8, 0xC8)]
    public void SpeakerCategoryToBrush_ReturnsExpectedColor(SpeakerCategory cat, string? param, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b),
            BrushColor(new SpeakerCategoryToBrushConverter().Convert(cat, typeof(ISolidColorBrush), param, Inv)));

    // ── NodeColorConverter ────────────────────────────────────────────────

    public static IEnumerable<object?[]> NodeColorData => new object?[][]
    {
        // Bark display type — SpeakerCategory is ignored by the converter
        [SpeakerCategory.Npc, "Bark",         null,     (byte)0x7A, (byte)0x5C, (byte)0x00], // BarkHeader
        [SpeakerCategory.Npc, "Bark",         "body",   (byte)0xFF, (byte)0xF8, (byte)0xDC], // BarkBody
        [SpeakerCategory.Npc, "Bark",         "footer", (byte)0xE8, (byte)0xD0, (byte)0x80], // BarkFooter
        // Npc
        [SpeakerCategory.Npc,      "Conversation", null,     (byte)0x7b, (byte)0x24, (byte)0x1c],
        [SpeakerCategory.Npc,      "Conversation", "body",   (byte)0xF5, (byte)0xF0, (byte)0xD0],
        [SpeakerCategory.Npc,      "Conversation", "footer", (byte)0xE8, (byte)0xE0, (byte)0xB0],
        // Player
        [SpeakerCategory.Player,   "Conversation", null,     (byte)0x1a, (byte)0x52, (byte)0x76],
        [SpeakerCategory.Player,   "Conversation", "body",   (byte)0xD5, (byte)0xE8, (byte)0xF5],
        [SpeakerCategory.Player,   "Conversation", "footer", (byte)0xB0, (byte)0xCD, (byte)0xE8],
        // Narrator
        [SpeakerCategory.Narrator, "Conversation", null,     (byte)0x0e, (byte)0x66, (byte)0x55],
        [SpeakerCategory.Narrator, "Conversation", "body",   (byte)0xD5, (byte)0xF0, (byte)0xE8],
        [SpeakerCategory.Narrator, "Conversation", "footer", (byte)0xB0, (byte)0xE0, (byte)0xD5],
        // Script
        [SpeakerCategory.Script,   "Conversation", null,     (byte)0x2c, (byte)0x3e, (byte)0x50],
        [SpeakerCategory.Script,   "Conversation", "body",   (byte)0xE0, (byte)0xE0, (byte)0xE0],
        [SpeakerCategory.Script,   "Conversation", "footer", (byte)0xC8, (byte)0xC8, (byte)0xC8],
    };

    [Theory, MemberData(nameof(NodeColorData))]
    public void NodeColorConverter_ReturnsExpectedColor(
        SpeakerCategory cat, string displayType, string? zone, byte r, byte g, byte b)
    {
        var conv = new NodeColorConverter();
        var result = conv.Convert(
            new object?[] { cat, displayType },
            typeof(ISolidColorBrush), zone, Inv);
        Assert.Equal(Color.FromRgb(r, g, b), BrushColor(result));
    }
}
