using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProjectHistoryServiceTests
{
    private const char US = '\u001f';   // unit separator git emits for %x1f

    private sealed class FakeGit : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return Handler(args);
        }
    }

    private static string ProjPath() =>
        Path.Combine(Path.GetTempPath(), $"hist_{Guid.NewGuid():N}.dialogproject");

    private static FakeGit GitWithLog(string logStdout)
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        return new FakeGit
        {
            Handler = a =>
                a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
                a.Length > 0 && a[0] == "log"         ? new GitResult(0, logStdout, "") :
                                                        new GitResult(0, "", ""),
        };
    }

    private static string Line(string shortSha, string sha, string author, string isoDate, string subject)
        => string.Join(US, shortSha, sha, author, isoDate, subject);

    [Fact]
    public void ParsesCommits_IncludingDateAndSpacedSubject()
    {
        var stdout =
            Line("a1b2c3d", "a1b2c3d4e5", "Mia", "2026-05-30T14:03:11+02:00", "Fix greeting branch logic") + "\n" +
            Line("0099887", "00998877665", "Jon", "2026-05-28T09:00:00+00:00", "Add tavern small talk");
        var svc = new ProjectHistoryService(GitWithLog(stdout));

        var commits = svc.Load(ProjPath());

        Assert.Equal(2, commits.Count);
        Assert.Equal("a1b2c3d4e5", commits[0].Sha);
        Assert.Equal("a1b2c3d",    commits[0].ShortSha);
        Assert.Equal("Mia",        commits[0].Author);
        Assert.Equal("Fix greeting branch logic", commits[0].Subject);
        Assert.Equal(new DateTimeOffset(2026, 5, 30, 14, 3, 11, TimeSpan.FromHours(2)), commits[0].Date);
    }

    [Fact]
    public void UsesFollowAndProjectPath()
    {
        var git = GitWithLog("");
        new ProjectHistoryService(git).Load(ProjPath());

        var log = Assert.Single(git.Calls, c => c.Length > 0 && c[0] == "log");
        Assert.Contains("--follow", log);
        Assert.Equal("--", log[^2]);   // path passed after a -- terminator
    }

    [Fact]
    public void EmptyLog_ReturnsEmptyList()
    {
        var svc = new ProjectHistoryService(GitWithLog(""));
        Assert.Empty(svc.Load(ProjPath()));
    }

    [Fact]
    public void NotARepo_Throws()
    {
        var git = new FakeGit { Handler = _ => new GitResult(128, "", "fatal") };
        var ex = Assert.Throws<DiffException>(() => new ProjectHistoryService(git).Load(ProjPath()));
        Assert.Equal(DiffExceptionKind.NotARepo, ex.Kind);
    }

    [Fact]
    public void LogFailsAfterValidRepo_Throws()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit
        {
            Handler = a => a is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, root + "\n", "")
                : new GitResult(128, "", "fatal: bad revision"),
        };
        Assert.Throws<DiffException>(() => new ProjectHistoryService(git).Load(ProjPath()));
    }
}
