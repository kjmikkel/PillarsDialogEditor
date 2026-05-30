using System.Globalization;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DialogEditor.Avalonia.Converters;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Models;

namespace DialogEditor.Tests.Converters;

public class BrushConverterTests
{
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private static Color BrushColor(object? result)
        => ((ISolidColorBrush)result!).Color;

    // ── BoolToFemaleTextBrushConverter ────────────────────────────────────

    [AvaloniaFact]
    public void BoolToFemaleTextBrush_True_ReturnsActiveBrush()
        => Assert.Equal(Color.FromRgb(0xe8, 0xe8, 0xe8),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(true, typeof(ISolidColorBrush), null, Inv)));

    [AvaloniaFact]
    public void BoolToFemaleTextBrush_False_ReturnsDimBrush()
        => Assert.Equal(Color.FromRgb(0x55, 0x55, 0x55),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(false, typeof(ISolidColorBrush), null, Inv)));

    [AvaloniaFact]
    public void BoolToFemaleTextBrush_Null_ReturnsDimBrush()
        => Assert.Equal(Color.FromRgb(0x55, 0x55, 0x55),
            BrushColor(new BoolToFemaleTextBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv)));

    // ── BoolToNewConversationBrushConverter ───────────────────────────────

    [AvaloniaFact]
    public void BoolToNewConversationBrush_True_ReturnsGreenBrush()
        => Assert.Equal(Color.FromRgb(0x7d, 0xce, 0xa0),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(true, typeof(ISolidColorBrush), null, Inv)));

    [AvaloniaFact]
    public void BoolToNewConversationBrush_False_ReturnsNormalBrush()
        => Assert.Equal(Color.FromRgb(0xcc, 0xcc, 0xcc),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(false, typeof(ISolidColorBrush), null, Inv)));

    [AvaloniaFact]
    public void BoolToNewConversationBrush_Null_ReturnsNormalBrush()
        => Assert.Equal(Color.FromRgb(0xcc, 0xcc, 0xcc),
            BrushColor(new BoolToNewConversationBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv)));

    // ── FlowIssueKindToSeverityBrushConverter ─────────────────────────────

    [AvaloniaTheory]
    [InlineData(FlowIssueKind.Unreachable,              0xc0, 0x39, 0x2b)] // Red
    [InlineData(FlowIssueKind.PlayerDeadEnd,            0xb8, 0x76, 0x0a)] // Amber
    [InlineData(FlowIssueKind.EmptyText,                0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.NoIncomingLinks,          0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.BarkTextTooLong,          0xb8, 0x76, 0x0a)]
    [InlineData(FlowIssueKind.BarkHasPlayerChoiceChild, 0xb8, 0x76, 0x0a)]
    public void FlowIssueKindToSeverityBrush_ReturnsExpectedColor(FlowIssueKind kind, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b),
            BrushColor(new FlowIssueKindToSeverityBrushConverter().Convert(kind, typeof(ISolidColorBrush), null, Inv)));

    [AvaloniaFact]
    public void FlowIssueKindToSeverityBrush_NonKindValue_ReturnsAmber()
        => Assert.Equal(Color.FromRgb(0xb8, 0x76, 0x0a),
            BrushColor(new FlowIssueKindToSeverityBrushConverter().Convert("not-a-kind", typeof(ISolidColorBrush), null, Inv)));

    // ── PropertyValueStyleToBrushConverter ────────────────────────────────

    [AvaloniaTheory]
    [InlineData(PropertyValueStyle.Condition, 0xe8, 0xa0, 0x20)]
    [InlineData(PropertyValueStyle.Script,    0x7d, 0xce, 0xa0)]
    [InlineData(PropertyValueStyle.Code,      0x9c, 0xdc, 0xfe)]
    [InlineData(PropertyValueStyle.Default,   0xe8, 0xe8, 0xe8)]
    public void PropertyValueStyleToBrush_ReturnsExpectedColor(PropertyValueStyle style, byte r, byte g, byte b)
        => Assert.Equal(Color.FromRgb(r, g, b),
            BrushColor(new PropertyValueStyleToBrushConverter().Convert(style, typeof(ISolidColorBrush), null, Inv)));

    [AvaloniaFact]
    public void PropertyValueStyleToBrush_NullValue_ReturnsNull()
    {
        var result = new PropertyValueStyleToBrushConverter().Convert(null, typeof(ISolidColorBrush), null, Inv);
        Assert.Null(result);
    }

    // ── SpeakerCategoryToBrushConverter ───────────────────────────────────

    [AvaloniaTheory]
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

    [AvaloniaTheory]
    // Bark display type — SpeakerCategory is ignored by the converter
    [InlineData(SpeakerCategory.Npc, "Bark",         null,     0x7A, 0x5C, 0x00)]
    [InlineData(SpeakerCategory.Npc, "Bark",         "body",   0xFF, 0xF8, 0xDC)]
    [InlineData(SpeakerCategory.Npc, "Bark",         "footer", 0xE8, 0xD0, 0x80)]
    // Npc
    [InlineData(SpeakerCategory.Npc,      "Conversation", null,     0x7b, 0x24, 0x1c)]
    [InlineData(SpeakerCategory.Npc,      "Conversation", "body",   0xF5, 0xF0, 0xD0)]
    [InlineData(SpeakerCategory.Npc,      "Conversation", "footer", 0xE8, 0xE0, 0xB0)]
    // Player
    [InlineData(SpeakerCategory.Player,   "Conversation", null,     0x1a, 0x52, 0x76)]
    [InlineData(SpeakerCategory.Player,   "Conversation", "body",   0xD5, 0xE8, 0xF5)]
    [InlineData(SpeakerCategory.Player,   "Conversation", "footer", 0xB0, 0xCD, 0xE8)]
    // Narrator
    [InlineData(SpeakerCategory.Narrator, "Conversation", null,     0x0e, 0x66, 0x55)]
    [InlineData(SpeakerCategory.Narrator, "Conversation", "body",   0xD5, 0xF0, 0xE8)]
    [InlineData(SpeakerCategory.Narrator, "Conversation", "footer", 0xB0, 0xE0, 0xD5)]
    // Script
    [InlineData(SpeakerCategory.Script,   "Conversation", null,     0x2c, 0x3e, 0x50)]
    [InlineData(SpeakerCategory.Script,   "Conversation", "body",   0xE0, 0xE0, 0xE0)]
    [InlineData(SpeakerCategory.Script,   "Conversation", "footer", 0xC8, 0xC8, 0xC8)]
    public void NodeColorConverter_ReturnsExpectedColor(
        SpeakerCategory cat, string displayType, string? zone, byte r, byte g, byte b)
    {
        var conv = new NodeColorConverter();
        var result = conv.Convert(
            new object?[] { cat, displayType },
            typeof(ISolidColorBrush), zone, Inv);
        Assert.Equal(Color.FromRgb(r, g, b), BrushColor(result));
    }

    // ── DiffStatusToBrushConverter ─────────────────────────────────────────

    [AvaloniaFact]
    public void DiffStatusToBrush_Added_ReturnsGreen()
        => Assert.Equal(Color.Parse("#3a7a3a"),
            BrushColor(new DiffStatusToBrushConverter().Convert(DiffStatus.Added, typeof(IBrush), null, Inv)));

    [AvaloniaFact]
    public void DiffStatusToBrush_Changed_ReturnsAmber()
        => Assert.Equal(Color.Parse("#c08a2a"),
            BrushColor(new DiffStatusToBrushConverter().Convert(DiffStatus.Changed, typeof(IBrush), null, Inv)));

    [AvaloniaFact]
    public void DiffStatusToBrush_Removed_ReturnsRed()
        => Assert.Equal(Color.Parse("#7a2a2a"),
            BrushColor(new DiffStatusToBrushConverter().Convert(DiffStatus.Removed, typeof(IBrush), null, Inv)));

    [AvaloniaFact]
    public void DiffStatusToBrush_Unchanged_ReturnsTransparent()
    {
        var result = new DiffStatusToBrushConverter().Convert(DiffStatus.Unchanged, typeof(IBrush), null, Inv);
        Assert.IsAssignableFrom<ISolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, ((ISolidColorBrush)result!).Color);
    }

    [AvaloniaFact]
    public void DiffStatusToBrush_Null_ReturnsTransparent()
    {
        var result = new DiffStatusToBrushConverter().Convert(null, typeof(IBrush), null, Inv);
        Assert.IsAssignableFrom<ISolidColorBrush>(result);
        Assert.Equal(Colors.Transparent, ((ISolidColorBrush)result!).Color);
    }
}
