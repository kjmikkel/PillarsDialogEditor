using DialogEditor.Patch.Changelog;
using Xunit;

namespace DialogEditor.Tests.Patch.Changelog;

public class ChangelogParserTests
{
    [Fact]
    public void Parse_GroupsSubsections_NewestFirst()
    {
        const string md = """
            # Changelog

            ## [1.2.0] — 2026-05-31
            ### Added
            - Diff viewer
            - Selective apply
            ### Fixed
            - Crash on open

            ## [1.1.0] - 2026-05-01
            ### Added
            - Branches
            """;

        var releases = ChangelogParser.Parse(md);

        Assert.Collection(releases,
            r =>
            {
                Assert.Equal("1.2.0", r.Version);
                Assert.Equal("2026-05-31", r.Date);
                Assert.Collection(r.Sections,
                    s => { Assert.Equal("Added", s.Heading); Assert.Equal(new[] { "Diff viewer", "Selective apply" }, s.Entries); },
                    s => { Assert.Equal("Fixed", s.Heading); Assert.Equal(new[] { "Crash on open" }, s.Entries); });
            },
            r =>
            {
                Assert.Equal("1.1.0", r.Version);
                Assert.Equal("2026-05-01", r.Date);
                var s = Assert.Single(r.Sections);
                Assert.Equal("Added", s.Heading);
                Assert.Equal(new[] { "Branches" }, s.Entries);
            });
    }

    [Fact]
    public void Parse_BulletsBeforeAnySubheading_FormFlatNullHeadingSection()
    {
        const string md = """
            ## [1.0.0] — 2026-04-01
            - First note
            * Second note
            """;

        var release = Assert.Single(ChangelogParser.Parse(md));
        var section = Assert.Single(release.Sections);
        Assert.Null(section.Heading);
        Assert.False(section.HasHeading);
        Assert.Equal(new[] { "First note", "Second note" }, section.Entries);
    }

    [Fact]
    public void Parse_EmptyOrProseOnly_ReturnsEmpty()
    {
        Assert.Empty(ChangelogParser.Parse(""));
        Assert.Empty(ChangelogParser.Parse("# Changelog\n\nNo releases yet.\n## [Unreleased]\n"));
    }
}
