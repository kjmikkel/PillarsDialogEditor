using DialogEditor.Patch.Changelog;

namespace DialogEditor.Tests.Patch.Changelog;

public class WhatsNewDeciderTests
{
    private static ChangelogRelease Rel(string version) =>
        new(version, "2026-01-01", []);

    // Newest-first, as the parser produces.
    private static readonly IReadOnlyList<ChangelogRelease> Log =
        [Rel("1.3.0"), Rel("1.2.0"), Rel("1.1.0"), Rel("1.0.0")];

    [Fact]
    public void EmptyLastSeen_ShowsNothing()
        => Assert.Empty(WhatsNewDecider.Decide("", "1.3.0", Log).ReleasesToShow);

    [Fact]
    public void SameVersion_ShowsNothing()
        => Assert.Empty(WhatsNewDecider.Decide("1.3.0", "1.3.0", Log).ReleasesToShow);

    [Fact]
    public void Upgrade_ShowsOnlyNewerReleases_Exclusive()
    {
        var shown = WhatsNewDecider.Decide("1.1.0", "1.3.0", Log).ReleasesToShow;
        Assert.Equal(["1.3.0", "1.2.0"], shown.Select(r => r.Version));
    }

    [Fact]
    public void LastSeenIsNewest_ShowsNothing()
        => Assert.Empty(WhatsNewDecider.Decide("1.3.0", "1.3.0", Log).ReleasesToShow);

    [Fact]
    public void LastSeenNotInLog_ShowsAll()
    {
        var shown = WhatsNewDecider.Decide("0.9.0", "1.3.0", Log).ReleasesToShow;
        Assert.Equal(4, shown.Count);
    }

    [Fact]
    public void EmptyChangelog_ShowsNothing()
        => Assert.Empty(WhatsNewDecider.Decide("1.1.0", "1.3.0", []).ReleasesToShow);

    // AppVersion.Current carries a "+<git-hash>" build-metadata suffix; changelog
    // headings do not. Build metadata must be ignored so versions match.
    [Fact]
    public void BuildMetadataSuffix_IsIgnored_WhenMatchingReleases()
    {
        var shown = WhatsNewDecider
            .Decide("1.1.0+abc123", "1.3.0+def456", Log).ReleasesToShow;
        Assert.Equal(["1.3.0", "1.2.0"], shown.Select(r => r.Version));
    }

    [Fact]
    public void BuildMetadataOnly_SameVersion_ShowsNothing()
        => Assert.Empty(WhatsNewDecider
            .Decide("1.3.0+abc123", "1.3.0+def456", Log).ReleasesToShow);
}
