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
}
