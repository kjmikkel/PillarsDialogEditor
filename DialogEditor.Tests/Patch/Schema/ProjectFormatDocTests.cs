using DialogEditor.Patch;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Tests.Patch.Schema;

/// PROJECT-FORMAT.md's patcher compatibility table (GitHub issue 79) is what mod authors and
/// installer scripts rely on, so a schema bump without a new row fails the build.
public class ProjectFormatDocTests
{
    private static string Doc()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "PROJECT-FORMAT.md"));
    }

    /// Cells of the table rows under a "## heading", header and separator skipped.
    private static List<string[]> TableRows(string heading)
    {
        var lines = Doc().Split('\n').Select(l => l.Trim()).ToList();
        var start = lines.IndexOf(heading);
        Assert.True(start >= 0, $"PROJECT-FORMAT.md has no '{heading}' section");
        return lines.Skip(start + 1)
            .SkipWhile(l => !l.StartsWith('|'))
            .TakeWhile(l => l.StartsWith('|'))
            .Skip(2)
            .Select(l => l.Trim('|').Split('|').Select(c => c.Trim()).ToArray())
            .ToList();
    }

    [Fact]
    public void CompatibilityTable_LatestRow_MatchesWhatThisBuildReads()
    {
        var latest = TableRows("## Patcher compatibility").Last();
        // | Patcher | Project | Conversation patch | Load order |, each version as "≤ N"
        Assert.Equal(
            [$"≤ {DialogProject.CurrentSchemaVersion}", $"≤ {ConversationPatch.CurrentSchemaVersion}", $"≤ {PatchList.CurrentSchemaVersion}"],
            latest[1..]);
    }

    [Fact]
    public void CompatibilityTable_HasOneColumnPerFormat()
        => Assert.All(TableRows("## Patcher compatibility"),
            row => Assert.Equal(1 + SchemaFormats.Supported.Count, row.Length));

    [Fact]
    public void Doc_DocumentsExitCode4()
        => Assert.Contains("exits with code **4**", Doc());
}
