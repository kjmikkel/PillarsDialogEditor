using DialogEditor.Patch;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Tests.Patch.Schema;

/// The patcher's declared compatibility contract (GitHub issue 79): what --version, the
/// Patch Manager About window and PROJECT-FORMAT.md all report.
public class SchemaFormatsTests
{
    [Fact]
    public void Supported_ListsEveryKindOnce_InEnumOrder()
        => Assert.Equal(Enum.GetValues<SchemaFileKind>(), SchemaFormats.Supported.Select(f => f.Kind));

    [Fact]
    public void Supported_ReportsTheVersionsTheMigratorAccepts()
    {
        foreach (var (kind, version) in SchemaFormats.Supported)
            Assert.Equal(SchemaMigrator.CurrentVersions[kind], version);
    }

    [Fact]
    public void EnglishSummary_NamesEveryFormatWithItsVersion()
        => Assert.Equal(
            $"project format <= {DialogProject.CurrentSchemaVersion}, " +
            $"conversation patch format <= {ConversationPatch.CurrentSchemaVersion}, " +
            $"load-order format <= {PatchList.CurrentSchemaVersion}",
            SchemaFormats.EnglishSummary);

    [Fact]
    public void EnglishName_MatchesTheRefusalMessage()
    {
        foreach (var kind in Enum.GetValues<SchemaFileKind>())
            Assert.Contains(SchemaFormats.EnglishName(kind), new UnsupportedSchemaVersionException(kind, 9, 1).Message);
    }

    [Fact]
    public void PatcherReleasesUrl_PointsAtTheRepositoryReleases()
        => Assert.Equal("https://github.com/kjmikkel/PillarsDialogEditor/releases", SchemaFormats.PatcherReleasesUrl);
}
