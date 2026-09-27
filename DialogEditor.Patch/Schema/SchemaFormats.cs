using DialogEditor.Core.Localisation;

namespace DialogEditor.Patch.Schema;

/// The patcher's compatibility contract (GitHub issue 79): the newest version of each file
/// format this build reads. dialog-patcher --version, the Patch Manager About window and
/// the table in PROJECT-FORMAT.md all come from here, so they can't disagree with what
/// SchemaMigrator actually accepts.
[NotLocalised("English names are for dialog-patcher and log output; the GUI names formats via Loc")]
public static class SchemaFormats
{
    /// Where a player gets a newer patcher. The whole Releases page until the patcher has its
    /// own patcher-v* tags (GitHub issue 77).
    public const string PatcherReleasesUrl = "https://github.com/kjmikkel/PillarsDialogEditor/releases";

    public static IReadOnlyList<(SchemaFileKind Kind, int Version)> Supported { get; } =
        Enum.GetValues<SchemaFileKind>().Select(k => (k, SchemaMigrator.CurrentVersions[k])).ToList();

    public static string EnglishName(SchemaFileKind kind) => kind switch
    {
        SchemaFileKind.Project           => "project format",
        SchemaFileKind.ConversationPatch => "conversation patch format",
        _                                => "load-order format",
    };

    /// "project format <= 1, conversation patch format <= 2, load-order format <= 1".
    /// ASCII "<=" because dialog-patcher's output may land in a console without Unicode.
    public static string EnglishSummary { get; } =
        string.Join(", ", Supported.Select(f => $"{EnglishName(f.Kind)} <= {f.Version}"));
}
