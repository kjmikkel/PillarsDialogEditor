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
        ["ColumnWidth.BatchVo.File"]         = 220,
        ["ColumnWidth.BatchVo.Result"]       = 50,
    };
}
