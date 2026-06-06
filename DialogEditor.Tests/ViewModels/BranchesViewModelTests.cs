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

    [Fact]
    public async Task Switch_HappyPath_ChecksOut_GuardsThenReloads()
    {
        var order = new List<string>();
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["checkout", "feature/x"]) { order.Add("checkout"); return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath())
        {
            EnsureNoUnsavedEdits  = () => { order.Add("guard"); return Task.FromResult(true); },
            ReloadProjectFromDisk = () => order.Add("reload"),
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "guard", "checkout", "reload" }, order);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task Switch_CancelledAtGuard_DoesNotCheckout()
    {
        var checkedOut = false;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["checkout", ..]) { checkedOut = true; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath())
        {
            EnsureNoUnsavedEdits = () => Task.FromResult(false),   // user cancelled
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.False(checkedOut);
    }

    // Checkout blocks once (tracked), succeeds after a commit; status reports tracked changes.
    private static GitBranchService BlockingThenCommitting(List<string> log) => new(Git(a =>
    {
        if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
        if (a is ["status", "--porcelain"]) return new GitResult(0, " M conv.dialogproject\n", "");
        if (a is ["checkout", "feature/x"])
        {
            log.Add("checkout");
            return log.Contains("commit") ? new GitResult(0, "", "") : new GitResult(1, "", "would be overwritten");
        }
        if (a.Length > 0 && a[0] == "commit") { log.Add("commit"); return new GitResult(0, "", ""); }
        return null;
    }));

    [Fact]
    public async Task Switch_Blocked_OffersCommit_ThenRetriesAndReloads()
    {
        var log = new List<string>();
        PendingCommit? shown = null;
        var vm = new BranchesViewModel(BlockingThenCommitting(log), ProjPath())
        {
            EnsureNoUnsavedEdits      = () => Task.FromResult(true),
            ReloadProjectFromDisk     = () => log.Add("reload"),
            RequestCommitConfirmation = p => { shown = p; return Task.FromResult<string?>("commit msg"); },
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Contains("conv.dialogproject", shown!.Files);
        Assert.Equal(new[] { "checkout", "commit", "checkout", "reload" }, log);
    }

    [Fact]
    public async Task Switch_Blocked_ConsentCancelled_DoesNotCommit()
    {
        var log = new List<string>();
        var vm = new BranchesViewModel(BlockingThenCommitting(log), ProjPath())
        {
            EnsureNoUnsavedEdits      = () => Task.FromResult(true),
            RequestCommitConfirmation = _ => Task.FromResult<string?>(null),   // cancelled
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.DoesNotContain("commit", log);
    }

    [Fact]
    public async Task Switch_BlockedByUntracked_ShowsCaseAStatus_NoCommit()
    {
        var committed = false;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            if (a is ["checkout", ..]) return new GitResult(1, "", "would be overwritten");
            if (a is ["status", "--porcelain"]) return new GitResult(0, "?? new.tmp\n", "");
            if (a.Length > 0 && a[0] == "commit") { committed = true; return new GitResult(0, "", ""); }
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath())
        {
            EnsureNoUnsavedEdits      = () => Task.FromResult(true),
            RequestCommitConfirmation = _ => Task.FromResult<string?>("x"),
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.False(committed);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task Create_PromptsName_AndCreates()
    {
        string[]? created = null;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["check-ref-format", ..]) return new GitResult(0, "", "");
            if (a is ["show-ref", ..]) return new GitResult(1, "", "");
            if (a is ["checkout", "-b", ..]) { created = a; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { RequestBranchName = _ => Task.FromResult<string?>("feature/new") };

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "checkout", "-b", "feature/new" }, created);
    }

    [Fact]
    public async Task Create_NameExists_SetsStatus()
    {
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["check-ref-format", ..]) return new GitResult(0, "", "");
            if (a is ["show-ref", ..]) return new GitResult(0, "", "");   // already exists
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { RequestBranchName = _ => Task.FromResult<string?>("main") };

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task Delete_NotMerged_AsksForceConfirm_ThenForceDeletes()
    {
        string[]? forced = null;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["branch", "-d", ..]) return new GitResult(1, "", "not fully merged");
            if (a is ["branch", "-D", ..]) { forced = a; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { ConfirmForceDelete = _ => Task.FromResult(true) };
        vm.Selected = vm.Branches[1];

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "branch", "-D", "feature/x" }, forced);
    }

    [Fact]
    public async Task Delete_NotMerged_ConfirmDeclined_DoesNotForce()
    {
        var forced = false;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["branch", "-d", ..]) return new GitResult(1, "", "not fully merged");
            if (a is ["branch", "-D", ..]) { forced = true; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { ConfirmForceDelete = _ => Task.FromResult(false) };
        vm.Selected = vm.Branches[1];

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.False(forced);
    }

    [Fact]
    public async Task Switch_Succeeds_ReselectsSameBranchInRebuiltList()
    {
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["checkout", "feature/x"]) return new GitResult(0, "", "");
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { EnsureNoUnsavedEdits = () => Task.FromResult(true) };
        vm.Selected = vm.Branches[1];          // feature/x — the row that LoadBranches will discard

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.NotNull(vm.Selected);
        Assert.Equal("feature/x", vm.Selected!.Name);
        Assert.Contains(vm.Selected, vm.Branches);   // re-pointed into the rebuilt list, not stale
    }

    [Fact]
    public async Task Delete_Succeeds_ClearsSelectionForGoneBranch()
    {
        var deleted = false;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["branch", "-d", "feature/x"]) { deleted = true; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, deleted ? "main\n" : "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath());
        vm.Selected = vm.Branches[1];          // feature/x — about to be deleted

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Null(vm.Selected);              // the deleted branch is no longer selectable
        Assert.DoesNotContain(vm.Branches, b => b.Name == "feature/x");
    }
}
