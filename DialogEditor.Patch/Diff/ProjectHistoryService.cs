using System.Globalization;

namespace DialogEditor.Patch.Diff;

/// Reads the git history of a project file (read-only). Testable via IGitRunner.
public class ProjectHistoryService(IGitRunner git)
{
    // %h short sha, %H full sha, %an author, %aI strict-ISO author date, %s subject.
    // 0x1f (unit separator) between fields so subjects with spaces parse cleanly.
    private const string Format = "--format=%h%x1f%H%x1f%an%x1f%aI%x1f%s";

    public IReadOnlyList<CommitInfo> Load(string projectFilePath)
    {
        var (dir, relative) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);

        var log = git.Run(dir, "log", "--follow", "--date=iso-strict", Format, "--", relative);
        if (!log.Ok)
            throw new DiffException(
                $"Could not read git history for '{relative}': {log.StdErr.Trim()}", DiffExceptionKind.Unknown);

        var commits = new List<CommitInfo>();
        foreach (var raw in log.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            var f = line.Split('\u001f');
            if (f.Length < 5) continue;   // defensive: skip malformed lines
            if (!DateTimeOffset.TryParse(f[3], CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var date)) continue;

            commits.Add(new CommitInfo(Sha: f[1], ShortSha: f[0], Author: f[2], Date: date, Subject: f[4]));
        }
        return commits;
    }
}
