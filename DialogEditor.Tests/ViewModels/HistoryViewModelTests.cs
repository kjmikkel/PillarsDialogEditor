using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class HistoryViewModelTests
{
    private const char US = '\u001f';

    public HistoryViewModelTests() => Loc.Configure(new StubStringProvider());

    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static string ProjPath() =>
        Path.Combine(Path.GetTempPath(), $"histvm_{Guid.NewGuid():N}.dialogproject");

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

    private static string Line(string shortSha, string sha, string author, string iso, string subject)
        => string.Join(US, shortSha, sha, author, iso, subject);

    [Fact]
    public void LoadsCommitRows()
    {
        var git = GitWithLog(Line("a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "Greeting tweak"));
        var vm  = new HistoryViewModel(git, ProjPath());

        Assert.True(vm.HasCommits);
        Assert.Equal("a1b2c3d4", vm.Commits[0].Sha);
        Assert.Equal("Greeting tweak", vm.Commits[0].Subject);
    }

    [Fact]
    public void Compare_DisabledUntilSelected()
    {
        var git = GitWithLog(Line("a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "x"));
        var vm  = new HistoryViewModel(git, ProjPath());

        Assert.False(vm.CompareCommand.CanExecute(null));
        vm.Selected = vm.Commits[0];
        Assert.True(vm.CompareCommand.CanExecute(null));
    }

    [Fact]
    public void Compare_InvokesCallbackWithSelectedSha()
    {
        var git = GitWithLog(Line("a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "x"));
        var vm  = new HistoryViewModel(git, ProjPath());
        string? captured = null;
        vm.CompareWithCommit = sha => captured = sha;

        vm.Selected = vm.Commits[0];
        vm.CompareCommand.Execute(null);

        Assert.Equal("a1b2c3d4", captured);
    }

    [Fact]
    public void RepoError_SetsStatus_NoCommits()
    {
        var git = new FakeGit { Handler = _ => new GitResult(128, "", "fatal") };
        var vm  = new HistoryViewModel(git, ProjPath());

        Assert.False(vm.HasCommits);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public void EmptyHistory_SetsStatus()
    {
        var vm = new HistoryViewModel(GitWithLog(""), ProjPath());

        Assert.False(vm.HasCommits);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }
}
