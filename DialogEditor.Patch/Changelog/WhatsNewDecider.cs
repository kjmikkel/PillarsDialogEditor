namespace DialogEditor.Patch.Changelog;

/// The releases to greet the user with on launch (empty when nothing new).
public sealed record WhatsNewResult(IReadOnlyList<ChangelogRelease> ReleasesToShow);

/// Decides which changelog releases are "new since last run". The changelog is
/// newest-first, so this walks from the top until it reaches the last-seen version —
/// no semantic-version parsing/comparison needed.
/// Design: docs/superpowers/specs/2026-07-07-whats-new-on-launch-design.md
public static class WhatsNewDecider
{
    public static WhatsNewResult Decide(
        string lastSeen, string current, IReadOnlyList<ChangelogRelease> all)
    {
        // AppVersion.Current carries a "+<git-hash>" build-metadata suffix that changelog
        // headings never have; strip it so versions compare on their released identity.
        lastSeen = StripBuildMetadata(lastSeen);
        current  = StripBuildMetadata(current);

        if (string.IsNullOrEmpty(lastSeen) || lastSeen == current)
            return new WhatsNewResult([]);

        var newer = new List<ChangelogRelease>();
        foreach (var release in all)
        {
            if (StripBuildMetadata(release.Version) == lastSeen)
                break;   // reached last-seen (exclusive)
            newer.Add(release);
        }
        return new WhatsNewResult(newer);
    }

    private static string StripBuildMetadata(string version)
    {
        var plus = version.IndexOf('+');
        return plus < 0 ? version : version[..plus];
    }
}
