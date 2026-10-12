namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138, spec §4: the in-game checklist has one row per scenario step of each mod, and
/// each row names the open issue that step exposes, so the checklist can't drift from the mods.
[Trait("Category", "Canonical")]
public class ChecklistTests
{
    private static string Checklist()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "canonical-mod-checklist.md")).Replace("\r\n", "\n");
    }

    /// The checkbox rows of one game's section: from its "## " heading to the next one.
    private static List<string> Rows(string gameId)
    {
        var text  = Checklist();
        var title = gameId == "poe1" ? "## Pillars of Eternity\n" : "## Deadfire\n";
        var start = text.IndexOf(title, StringComparison.Ordinal);
        Assert.True(start >= 0, $"no '{title.Trim()}' section");
        var end     = text.IndexOf("\n## ", start + title.Length, StringComparison.Ordinal);
        var section = end < 0 ? text[start..] : text[start..end];
        return [.. section.Split('\n').Where(l => l.StartsWith("- [ ]"))];
    }

    // A row starts with its own tag; it may mention other features' tags further on.
    private static string? RowFor(string gameId, string tag) =>
        Rows(gameId).SingleOrDefault(r => r.StartsWith($"- [ ] `{tag}`", StringComparison.Ordinal));

    public static TheoryData<string> Games => ["poe1", "poe2"];

    [Theory]
    [MemberData(nameof(Games))]
    public void EveryStep_HasARow_NamingItsKnownIssue(string gameId)
    {
        var problems = new List<string>();
        foreach (var step in CanonicalGame.For(gameId).Steps)
        {
            var row = RowFor(gameId, step.Tag);
            if (row is null) problems.Add($"{step.Tag}: no checkbox row");
            else if (step.KnownIssue is not null && !row.Contains(step.KnownIssue)) problems.Add($"{step.Tag}: row doesn't name {step.KnownIssue}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    // Issues the raw-file and game-model checks are skipped against, outside a step's own
    // KnownIssue. The tester should expect them in play too.
    public static TheoryData<string, string, string> SkippedElsewhere => new()
    {
        { "poe1", "links.display-values", "#164" },
        { "poe1", "conversation.new",     "#162" },
        { "poe2", "conversation.new",     "#162" },
    };

    [Theory]
    [MemberData(nameof(SkippedElsewhere))]
    public void RowsNameTheIssuesTheRawAndGameChecksSkip(string gameId, string tag, string issue)
    {
        var row = RowFor(gameId, tag);
        Assert.NotNull(row);
        Assert.Contains(issue, row);
    }
}
