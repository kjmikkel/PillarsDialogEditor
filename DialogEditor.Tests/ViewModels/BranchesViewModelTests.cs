using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class BranchesViewModelTests
{
    public BranchesViewModelTests() => Loc.Configure(new StubStringProvider());

    private static readonly string Root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
    private static string ProjPath() => Path.Combine(Path.GetTempPath(), $"bvm_{Guid.NewGuid():N}.dialogproject");

    private sealed class FakeGit : IGitRunner
    {
        public required Func<string[], GitResult> Handler;
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static FakeGit Git(Func<string[], GitResult?> rest) => new()
    {
        Handler = a =>
            a is ["rev-parse", "--show-toplevel"]      ? new GitResult(0, Root + "\n", "") :
            a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "main\n", "") :
            rest(a) ?? new GitResult(0, "", ""),
    };

    private static GitBranchService TwoBranches() =>
        new(Git(a => a is ["for-each-ref", ..] ? new GitResult(0, "main\nfeature/x\n", "") : null));

    [Fact]
    public void Ctor_LoadsBranches()
    {
        var vm = new BranchesViewModel(TwoBranches(), ProjPath());
        Assert.True(vm.HasBranches);
        Assert.Equal(2, vm.Branches.Count);
        Assert.True(vm.Branches[0].IsCurrent);
    }

    [Fact]
    public void Ctor_NotARepo_SetsStatus_NoBranches()
    {
        var svc = new GitBranchService(new FakeGit { Handler = _ => new GitResult(128, "", "fatal") });
        var vm  = new BranchesViewModel(svc, ProjPath());
        Assert.False(vm.HasBranches);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public void Switch_DisabledUntilSelected()
    {
        var vm = new BranchesViewModel(TwoBranches(), ProjPath());
        Assert.False(vm.SwitchCommand.CanExecute(null));
        vm.Selected = vm.Branches[1];
        Assert.True(vm.SwitchCommand.CanExecute(null));
    }

    [Fact]
    public void Delete_DisabledForCurrentBranch()
    {
        var vm = new BranchesViewModel(TwoBranches(), ProjPath());
        vm.Selected = vm.Branches[0];          // current
        Assert.False(vm.DeleteCommand.CanExecute(null));
        vm.Selected = vm.Branches[1];          // not current
        Assert.True(vm.DeleteCommand.CanExecute(null));
    }
}
