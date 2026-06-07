using System.Text.RegularExpressions;

namespace DialogEditor.Patch.Changelog;

/// Parses the project's "Keep a Changelog"-style CHANGELOG.md into grouped releases.
/// Intentionally understands only our shape (## release / ### section / - bullet); any
/// other markdown is ignored. Never throws on malformed input.
public static class ChangelogParser
{
    // "## [1.2.0] — 2026-05-31" or "## 1.2.0 - 2026-05-31"
    private static readonly Regex ReleaseRx =
        new(@"^\s*##\s+\[?(?<ver>[^\]\s]+)\]?\s*[—-]\s*(?<date>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex SectionRx =
        new(@"^\s*###\s+(?<head>.+?)\s*$", RegexOptions.Compiled);
    private static readonly Regex BulletRx =
        new(@"^\s*[-*]\s+(?<text>.+?)\s*$", RegexOptions.Compiled);

    public static IReadOnlyList<ChangelogRelease> Parse(string markdown)
    {
        var releases = new List<ChangelogRelease>();

        List<ChangelogSection>? sections = null;
        string? version = null, date = null;
        string? heading = null;
        List<string>? entries = null;

        void FlushSection()
        {
            if (sections is null) return;
            if (heading is not null || entries is { Count: > 0 })
                sections.Add(new ChangelogSection(heading, entries ?? new List<string>()));
            heading = null;
            entries = null;
        }

        void FlushRelease()
        {
            if (version is not null)
            {
                FlushSection();
                releases.Add(new ChangelogRelease(version, date ?? "", sections!));
            }
            sections = null; version = null; date = null;
        }

        foreach (var raw in markdown.Split('\n'))
        {
            var line = raw.TrimEnd('\r');

            var rel = ReleaseRx.Match(line);
            if (rel.Success)
            {
                FlushRelease();
                version = rel.Groups["ver"].Value;
                date = rel.Groups["date"].Value;
                sections = new List<ChangelogSection>();
                continue;
            }

            if (sections is null) continue; // ignore anything before the first release

            var sec = SectionRx.Match(line);
            if (sec.Success)
            {
                FlushSection();
                heading = sec.Groups["head"].Value;
                entries = new List<string>();
                continue;
            }

            var bul = BulletRx.Match(line);
            if (bul.Success)
            {
                (entries ??= new List<string>()).Add(bul.Groups["text"].Value);
            }
            // any other line is ignored
        }

        FlushRelease();
        return releases;
    }
}
