namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Unscaled table column widths in DIPs (issue 178). A table is a header grid plus a row grid per
/// item; both read the same ColumnWidth.* token, and FontScaleApplier multiplies it by the font
/// scale, so columns grow with their text instead of overlapping at large scales. Single source of
/// truth for FontScaleApplier and ColumnWidthTokenTests; Tokens.axaml declares the same values.
/// Where two windows used different widths for the same kind of column, the token takes the larger.
/// See docs/superpowers/specs/2026-10-13-table-columns-design.md.
/// </summary>
public static class ColumnWidthTokens
{
    public static readonly IReadOnlyDictionary<string, double> BaseValues = new Dictionary<string, double>
    {
        ["ColumnWidth.Conversation"]         = 160,
        ["ColumnWidth.NodeId"]               = 70,
        ["ColumnWidth.Field"]                = 110,
        ["ColumnWidth.Language"]             = 90,
        ["ColumnWidth.Author"]               = 140,
        ["ColumnWidth.Date"]                 = 110,
        ["ColumnWidth.CommitSha"]            = 90,
        ["ColumnWidth.Origin"]               = 70,
        ["ColumnWidth.SpeakerConversation"]  = 180,
        ["ColumnWidth.NodeLabel"]            = 90,
        ["ColumnWidth.Count"]                = 50,
        ["ColumnWidth.Share"]                = 70,
        ["ColumnWidth.Flag"]                 = 110,
        ["ColumnWidth.BatchVo.Conversation"] = 140,
        ["ColumnWidth.BatchVo.Node"]         = 60,
        ["ColumnWidth.BatchVo.Status"]       = 50,
        ["ColumnWidth.BatchVo.Result"]       = 50,
        ["ColumnWidth.Speaker"]               = 140,
        ["ColumnWidth.WemGlyph"]              = 60,
        ["ColumnWidth.FlowIssueKind"]         = 110,
        // Issue 190: Text Tag Validation lists and the Script/Condition parameter rows.
        ["ColumnWidth.IssueType"]            = 60,
        ["ColumnWidth.Category"]             = 110,
        ["ColumnWidth.Confidence"]           = 70,
        ["ColumnWidth.Tier"]                 = 70,
        ["ColumnWidth.ParameterName"]        = 110,
        // Minimum width of a Batch VO Import file column: the file name plus its Play, Browse and Clear buttons.
        ["ColumnWidth.BatchVo.FileMin"]      = 150,
        // The Flow Analytics severity badge is square: this is its width and its height.
        ["ColumnWidth.SeverityBadge"]        = 16,
        // GitHub issue 261: Dock.Avalonia's pane buttons (Pane options, Pin, Close). Not column
        // widths, but sizes that must grow with the text in the same way.
        ["DockChromeButtonWidth"]            = 18,
        ["DockChromeButtonHeight"]           = 16,
    };
}
