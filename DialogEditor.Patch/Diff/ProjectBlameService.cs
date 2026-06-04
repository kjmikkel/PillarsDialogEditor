using System.Globalization;
using System.Text;

namespace DialogEditor.Patch.Diff;

/// Reads per-node git attribution for a project file (read-only). Runs a single
/// `git blame --line-porcelain HEAD`, maps blamed lines back to nodes via
/// DialogProjectLineMap, and reports each node's most-recent touching commit.
/// Testable via IGitRunner.
public class ProjectBlameService(IGitRunner git)
{
    public IReadOnlyList<NodeBlame> Load(string projectFilePath)
    {
        var (dir, relative) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);

        var blame = git.Run(dir, "blame", "--line-porcelain", "HEAD", "--", relative);
        if (!blame.Ok)
        {
            // A path absent from HEAD (never committed) or a repo with no commits yet
            // means "no attribution to show", not an error.
            if (LooksLikeNoHistory(blame.StdErr))
                return [];
            throw new DiffException(
                $"Could not blame '{relative}': {blame.StdErr.Trim()}", DiffExceptionKind.Unknown);
        }

        // lineCommits[i] = commit for 1-based file line (i+1).
        var (content, lineCommits) = ParsePorcelain(blame.StdOut);
        if (lineCommits.Count == 0) return [];

        var lineMap = DialogProjectLineMap.Build(content);

        var result = new List<NodeBlame>();
        foreach (var ((conv, nodeId), ranges) in lineMap)
        {
            CommitInfo? newest = null;
            foreach (var (start, end) in ranges)
                for (var line = start; line <= end; line++)
                {
                    if (line < 1 || line > lineCommits.Count) continue;
                    var c = lineCommits[line - 1];
                    if (newest is null || c.Date > newest.Date) newest = c;
                }

            if (newest is not null)
                result.Add(new NodeBlame(conv, nodeId, newest));
        }

        return result
            .OrderBy(b => b.ConversationName, StringComparer.Ordinal)
            .ThenBy(b => b.NodeId)
            .ToList();
    }

    private static bool LooksLikeNoHistory(string stderr) =>
        stderr.Contains("no such path", StringComparison.OrdinalIgnoreCase)
        || stderr.Contains("no such ref", StringComparison.OrdinalIgnoreCase)
        || stderr.Contains("bad revision", StringComparison.OrdinalIgnoreCase)
        || stderr.Contains("does not have any commits", StringComparison.OrdinalIgnoreCase);

    // Returns the reassembled file content and, per file line, its commit.
    private static (string Content, List<CommitInfo> LineCommits) ParsePorcelain(string stdout)
    {
        var contentLines = new List<string>();
        var lineCommits  = new List<CommitInfo>();

        string? sha = null, author = null, summary = null, tz = "+0000";
        long authorTime = 0;

        foreach (var raw in stdout.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            if (line[0] == '\t')
            {
                // Content line — closes the current entry.
                contentLines.Add(line[1..]);
                lineCommits.Add(new CommitInfo(
                    Sha:      sha ?? "",
                    ShortSha: (sha ?? "").Length >= 8 ? sha![..8] : (sha ?? ""),
                    Author:   author ?? "",
                    Date:     DateTimeOffset.FromUnixTimeSeconds(authorTime).ToOffset(ParseTz(tz)),
                    Subject:  summary ?? ""));
                continue;
            }

            var sp = line.IndexOf(' ');
            var key = sp < 0 ? line : line[..sp];
            var val = sp < 0 ? "" : line[(sp + 1)..];

            switch (key)
            {
                case "author":      author     = val; break;
                case "author-time": _ = long.TryParse(val, out authorTime); break;
                case "author-tz":   tz         = val; break;
                case "summary":     summary    = val; break;
                default:
                    // A header line starts with a 40-char hex sha; reset per-entry fields.
                    if (IsSha(key)) { sha = key; author = summary = null; tz = "+0000"; authorTime = 0; }
                    break;
            }
        }

        return (string.Join('\n', contentLines), lineCommits);
    }

    private static bool IsSha(string s) =>
        s.Length == 40 && s.All(Uri.IsHexDigit);

    private static TimeSpan ParseTz(string tz)
    {
        // Git tz form: "+0200" / "-0530".
        if (tz.Length == 5
            && (tz[0] == '+' || tz[0] == '-')
            && int.TryParse(tz.AsSpan(1, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var h)
            && int.TryParse(tz.AsSpan(3, 2), NumberStyles.None, CultureInfo.InvariantCulture, out var m))
        {
            var span = new TimeSpan(h, m, 0);
            return tz[0] == '-' ? -span : span;
        }
        return TimeSpan.Zero;
    }
}
