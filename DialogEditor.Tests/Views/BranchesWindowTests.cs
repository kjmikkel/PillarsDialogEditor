using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class BranchesWindowTests
{
    private static readonly string Root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

    public BranchesWindowTests() => Loc.Configure(new StubStringProvider());

    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static BranchesViewModel TwoBranchesVm()
    {
        Loc.Configure(new StubStringProvider());
        var git = new FakeGit
        {
            Handler = a =>
                a is ["rev-parse", "--show-toplevel"]      ? new GitResult(0, Root + "\n", "") :
                a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "main\n", "") :
                a is ["for-each-ref", ..]                  ? new GitResult(0, "main\nfeature/x\n", "") :
                                                              new GitResult(0, "", ""),
        };
        return new BranchesViewModel(new GitBranchService(git),
            Path.Combine(Path.GetTempPath(), $"bw_{Guid.NewGuid():N}.dialogproject"));
    }

    [AvaloniaFact]
    public void List_PopulatesFromBranches()
    {
        var vm = TwoBranchesVm();
        var win = new BranchesWindow(vm);
        win.Show();

        Assert.Equal(2, win.FindControl<ListBox>("BranchList")!.ItemCount);
    }

    [AvaloniaFact]
    public void SwitchButton_DisabledUntilSelected()
    {
        var vm = TwoBranchesVm();
        var win = new BranchesWindow(vm);
        win.Show();

        var btn = win.FindControl<Button>("SwitchButton")!;
        Assert.False(btn.Command!.CanExecute(null));   // nothing selected

        vm.Selected = vm.Branches[0];                  // select current branch (main)
        Assert.True(btn.Command!.CanExecute(null));
    }

    [AvaloniaFact]
    public void DeleteButton_DisabledForCurrentBranch()
    {
        var vm = TwoBranchesVm();
        var win = new BranchesWindow(vm);
        win.Show();

        vm.Selected = vm.Branches[0];                  // main — IsCurrent == true
        var btn = win.FindControl<Button>("DeleteButton")!;
        Assert.False(btn.Command!.CanExecute(null));   // can't delete current branch
    }
}
