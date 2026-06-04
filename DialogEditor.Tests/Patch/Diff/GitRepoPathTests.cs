using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class GitRepoPathTests
{
    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    [Fact]
    public void ResolvesRelativePathAgainstRepoRoot()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.Combine(root, "sub", "proj.dialogproject");
        var git  = new FakeGit { Handler = a => a is ["rev-parse", "--show-toplevel"]
            ? new GitResult(0, root + "\n", "") : new GitResult(0, "", "") };

        var (workingDir, relative) = GitRepoPath.ResolveRepoRelative(git, path);

        Assert.Equal("sub/proj.dialogproject", relative);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), workingDir);
    }

    [Fact]
    public void NotARepo_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), "proj.dialogproject");
        var git  = new FakeGit { Handler = _ => new GitResult(128, "", "fatal: not a git repo") };

        var ex = Assert.Throws<DiffException>(() => GitRepoPath.ResolveRepoRelative(git, path));
        Assert.Equal(DiffExceptionKind.NotARepo, ex.Kind);
    }
}
