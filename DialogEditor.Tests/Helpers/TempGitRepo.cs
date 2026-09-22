using DialogEditor.Patch.Diff;

namespace DialogEditor.Tests.Helpers;

/// <summary>
/// A throwaway git repository in the temp directory, driven by the real
/// <see cref="ProcessGitRunner"/> — for tests that must cover what FakeGit cannot
/// (real porcelain, real blame, real checkout refusals). Extracted from
/// ProjectBlameServiceRealGitTests so real-git tests stop being copy-paste (#21).
///
/// Use <see cref="TryCreate"/> and return early on null: machines without git skip
/// quietly rather than fail, matching the original test's behaviour.
/// </summary>
public sealed class TempGitRepo : IDisposable
{
    public IGitRunner Git { get; } = new ProcessGitRunner();
    public string Dir { get; }

    private TempGitRepo(string dir) => Dir = dir;

    /// Initialises a repo on branch <c>main</c> with a pinned identity, or returns null
    /// (and cleans up) when git is unavailable.
    public static TempGitRepo? TryCreate()
    {
        var dir = Path.Combine(Path.GetTempPath(), "realgit_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        var repo = new TempGitRepo(dir);
        try
        {
            if (!repo.Git.Run(dir, "--version").Ok) { repo.Dispose(); return null; }
        }
        catch (DiffException)
        {
            repo.Dispose();   // git not installed / not on PATH
            return null;
        }

        repo.Run("init");
        // Pin the branch name: init.defaultBranch varies by machine (master/main).
        repo.Run("symbolic-ref", "HEAD", "refs/heads/main");
        // A user's global commit.gpgsign=true would make every commit here prompt or fail.
        repo.Run("config", "commit.gpgsign", "false");
        repo.SetAuthor("Setup", "setup@example.com");
        return repo;
    }

    public string PathOf(string relative) => Path.Combine(Dir, relative);

    /// Runs git in the repo and asserts success — a failed setup step should fail the
    /// test at that step, not surface later as a confusing attribution mismatch.
    public GitResult Run(params string[] args)
    {
        var res = Git.Run(Dir, args);
        Assert.True(res.Ok, $"git {string.Join(' ', args)} failed ({res.ExitCode}): {res.StdErr}");
        return res;
    }

    /// Sets the repo-local identity. Commits the app itself makes (e.g. the Branches
    /// window's commit-all) pick this up too, which is how a test attributes them.
    public void SetAuthor(string name, string email)
    {
        Run("config", "user.name", name);
        Run("config", "user.email", email);
    }

    /// Stages everything and commits with an explicit author date. Git author-time is
    /// second-granular, so two commits in the same wall-clock second would tie and the
    /// "latest" author would be arbitrary — never commit here without a date.
    public void CommitAll(string message, DateTimeOffset authorDate)
    {
        Run("add", "-A");
        Run("commit", "-q", "-m", message, $"--date={authorDate:yyyy-MM-ddTHH:mm:sszzz}");
    }

    public void Dispose()
    {
        // git marks object files read-only, which Directory.Delete refuses on Windows.
        try
        {
            foreach (var f in Directory.EnumerateFiles(Dir, "*", SearchOption.AllDirectories))
                File.SetAttributes(f, FileAttributes.Normal);
            Directory.Delete(Dir, recursive: true);
        }
        catch (Exception) { /* best-effort */ }
    }
}
