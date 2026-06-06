using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class GitBranchServiceTests
{
    private static string ProjPath() =>
        Path.Combine(Path.GetTempPath(), $"branch_{Guid.NewGuid():N}.dialogproject");

    private static readonly string Root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

    private sealed class FakeGit : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public required Func<string[], GitResult> Handler;
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return Handler(args);
        }
    }

    // Answers repo-resolution + current-branch; delegates the rest to `rest`.
    private static FakeGit Git(Func<string[], GitResult?> rest) => new()
    {
        Handler = a =>
            a is ["rev-parse", "--show-toplevel"]    ? new GitResult(0, Root + "\n", "") :
            a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "main\n", "") :
            rest(a) ?? new GitResult(0, "", ""),
    };

    [Fact]
    public void List_ReturnsBranches_WithCurrentFlagged()
    {
        var git = Git(a => a is ["for-each-ref", ..] ? new GitResult(0, "main\nfeature/x\n", "") : null);

        var branches = new GitBranchService(git).List(ProjPath());

        Assert.Equal(2, branches.Count);
        Assert.Equal("main", branches[0].Name);
        Assert.True(branches[0].IsCurrent);
        Assert.Equal("feature/x", branches[1].Name);
        Assert.False(branches[1].IsCurrent);
    }

    [Fact]
    public void List_NotARepo_Throws()
    {
        var git = new FakeGit { Handler = _ => new GitResult(128, "", "fatal: not a git repository") };
        var ex = Assert.Throws<DiffException>(() => new GitBranchService(git).List(ProjPath()));
        Assert.Equal(DiffExceptionKind.NotARepo, ex.Kind);
    }

    [Fact]
    public void List_DetachedHead_NoCurrentBranch()
    {
        var git = new FakeGit { Handler = a =>
            a is ["rev-parse", "--show-toplevel"]      ? new GitResult(0, Root + "\n", "") :
            a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "HEAD\n", "") :
            a is ["for-each-ref", ..]                  ? new GitResult(0, "main\n", "") :
            new GitResult(0, "", "")
        };

        var branches = new GitBranchService(git).List(ProjPath());

        Assert.Single(branches);
        Assert.False(branches[0].IsCurrent);
    }

    [Fact]
    public void Checkout_Success_ReturnsOk()
    {
        var git = Git(a => a is ["checkout", "feature/x"] ? new GitResult(0, "", "") : null);
        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");
        Assert.Equal(BranchOpStatus.Ok, r.Status);
    }

    [Fact]
    public void Checkout_BlockedByTrackedChanges_IsBlockedByLocalChanges()
    {
        var git = Git(a =>
            a is ["checkout", ..]          ? new GitResult(1, "", "would be overwritten") :
            a is ["status", "--porcelain"] ? new GitResult(0, " M conv.dialogproject\n", "") : null);
        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");
        Assert.Equal(BranchOpStatus.BlockedByLocalChanges, r.Status);
    }

    [Fact]
    public void Checkout_BlockedByUntrackedOnly_IsBlockedByUntrackedFiles()
    {
        var git = Git(a =>
            a is ["checkout", ..]          ? new GitResult(1, "", "would be overwritten") :
            a is ["status", "--porcelain"] ? new GitResult(0, "?? newfile.txt\n", "") : null);
        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");
        Assert.Equal(BranchOpStatus.BlockedByUntrackedFiles, r.Status);
    }

    [Fact]
    public void Checkout_FailsWithCleanTree_IsGitFailed()
    {
        var git = Git(a =>
            a is ["checkout", ..]          ? new GitResult(1, "", "some other error") :
            a is ["status", "--porcelain"] ? new GitResult(0, "", "") : null);
        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");
        Assert.Equal(BranchOpStatus.GitFailed, r.Status);
    }

    [Fact]
    public void Checkout_GitMissing_MapsToGitMissingStatus()
    {
        // The real runner throws DiffException(GitMissing) when git isn't installed;
        // it propagates through ResolveRepoRelative into Guarded's catch.
        var git = new FakeGit { Handler = _ => throw new DiffException("git not installed", DiffExceptionKind.GitMissing) };

        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");

        Assert.Equal(BranchOpStatus.GitMissing, r.Status);
    }

    [Fact]
    public void ListUncommittedChanges_ExcludesUntracked_ReturnsTrackedPaths()
    {
        var git = Git(a => a is ["status", "--porcelain"]
            ? new GitResult(0, " M conv.dialogproject\nA  added.json\n?? scratch.tmp\n", "") : null);

        var files = new GitBranchService(git).ListUncommittedChanges(ProjPath());

        Assert.Equal(new[] { "conv.dialogproject", "added.json" }, files);
    }

    [Fact]
    public void ListUncommittedChanges_GitFails_Throws()
    {
        var git = Git(a => a is ["status", "--porcelain"]
            ? new GitResult(128, "", "fatal: not a git repository") : null);
        Assert.Throws<DiffException>(() =>
            new GitBranchService(git).ListUncommittedChanges(ProjPath()));
    }

    [Fact]
    public void CommitAll_IssuesCommitDashA_ReturnsOk()
    {
        string[]? committed = null;
        var git = Git(a => { if (a.Length > 0 && a[0] == "commit") committed = a; return a.Length > 0 && a[0] == "commit" ? new GitResult(0, "", "") : null; });

        var r = new GitBranchService(git).CommitAll(ProjPath(), "my message");

        Assert.Equal(BranchOpStatus.Ok, r.Status);
        Assert.Equal(new[] { "commit", "-a", "-m", "my message" }, committed);
    }

    [Fact]
    public void CommitAll_Failure_IsGitFailed()
    {
        var git = Git(a => a.Length > 0 && a[0] == "commit" ? new GitResult(1, "", "nothing to commit") : null);
        var r = new GitBranchService(git).CommitAll(ProjPath(), "msg");
        Assert.Equal(BranchOpStatus.GitFailed, r.Status);
    }

    // Helper: validation answers for create/rename. valid=check-ref-format ok; exists=show-ref ok.
    private static FakeGit GitForNameOps(bool valid, bool exists, Func<string[], GitResult?> rest) => new()
    {
        Handler = a =>
            a is ["rev-parse", "--show-toplevel"]      ? new GitResult(0, Root + "\n", "") :
            a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "main\n", "") :
            a is ["check-ref-format", ..]              ? new GitResult(valid  ? 0 : 1, "", "") :
            a is ["show-ref", ..]                      ? new GitResult(exists ? 0 : 1, "", "") :
            rest(a) ?? new GitResult(0, "", ""),
    };

    [Fact]
    public void Create_InvalidName_IsNameInvalid()
    {
        var git = GitForNameOps(valid: false, exists: false, _ => null);
        Assert.Equal(BranchOpStatus.NameInvalid, new GitBranchService(git).Create(ProjPath(), "bad name").Status);
    }

    [Fact]
    public void Create_ExistingName_IsNameExists()
    {
        var git = GitForNameOps(valid: true, exists: true, _ => null);
        Assert.Equal(BranchOpStatus.NameExists, new GitBranchService(git).Create(ProjPath(), "feature/x").Status);
    }

    [Fact]
    public void Create_Valid_IssuesCheckoutDashB()
    {
        string[]? created = null;
        var git = GitForNameOps(valid: true, exists: false, a =>
        {
            if (a is ["checkout", "-b", ..]) { created = a; return new GitResult(0, "", ""); }
            return null;
        });

        var r = new GitBranchService(git).Create(ProjPath(), "feature/new");

        Assert.Equal(BranchOpStatus.Ok, r.Status);
        Assert.Equal(new[] { "checkout", "-b", "feature/new" }, created);
    }
}
