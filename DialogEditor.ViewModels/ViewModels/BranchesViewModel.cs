using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class BranchRowViewModel(BranchInfo info) : ObservableObject
{
    public string Name      => info.Name;
    public bool   IsCurrent => info.IsCurrent;
}

/// The set of files a commit-then-switch will commit, plus a default message.
public record PendingCommit(IReadOnlyList<string> Files, string DefaultMessage);

/// Lists and manages the open project's local git branches. Switching coordinates
/// with the host (unsaved-edits guard + reload) via callbacks; the VM never opens windows.
public partial class BranchesViewModel : ObservableObject
{
    private readonly GitBranchService _service;
    private readonly string _projectFilePath;

    [ObservableProperty] private BranchRowViewModel? _selected;
    [ObservableProperty] private string _statusText = "";

    public ObservableCollection<BranchRowViewModel> Branches { get; } = [];
    public bool HasBranches => Branches.Count > 0;

    // ── Host callbacks ──
    public Func<Task<bool>>?                   EnsureNoUnsavedEdits      { get; set; }
    public Action?                             ReloadProjectFromDisk     { get; set; }
    public Func<PendingCommit, Task<string?>>? RequestCommitConfirmation { get; set; }
    public Func<string, Task<bool>>?           ConfirmForceDelete        { get; set; }
    public Func<string?, Task<string?>>?       RequestBranchName         { get; set; }

    public BranchesViewModel(GitBranchService service, string projectFilePath)
    {
        _service = service;
        _projectFilePath = projectFilePath;
        Branches.CollectionChanged += (_, _) => OnPropertyChanged(nameof(HasBranches));
        LoadBranches();
    }

    private void LoadBranches()
    {
        Branches.Clear();
        try
        {
            foreach (var b in _service.List(_projectFilePath))
                Branches.Add(new BranchRowViewModel(b));
            StatusText = Branches.Count == 0 ? Loc.Get("Branches_StatusNone") : "";
        }
        catch (DiffException ex)
        {
            AppLog.Warn($"BranchesViewModel: could not list branches: {ex.Message}");
            StatusText = ex.Kind switch
            {
                DiffExceptionKind.GitMissing => Loc.Get("Branches_StatusGitMissing"),
                DiffExceptionKind.NotARepo   => Loc.Get("Branches_StatusNotARepo"),
                _                            => Loc.Get("Branches_StatusError"),
            };
        }
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        SwitchCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedChanged(BranchRowViewModel? value) => NotifyCommands();

    private bool CanActOnSelection => Selected is not null;
    private bool CanDelete         => Selected is { IsCurrent: false };

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task SwitchAsync()
    {
        var target = Selected!.Name;

        if (EnsureNoUnsavedEdits is not null && !await EnsureNoUnsavedEdits())
            return;   // cancelled at the unsaved-edits gate

        var result = _service.Checkout(_projectFilePath, target);

        if (result.Status == BranchOpStatus.BlockedByLocalChanges)
        {
            IReadOnlyList<string> files;
            try { files = _service.ListUncommittedChanges(_projectFilePath); }
            catch (DiffException) { files = []; }

            var message = RequestCommitConfirmation is null
                ? null
                : await RequestCommitConfirmation(new PendingCommit(files, Loc.Get("Branches_DefaultCommitMessage")));
            if (message is null) return;   // consent cancelled / no handler

            var commit = _service.CommitAll(_projectFilePath, message);
            if (commit.Status != BranchOpStatus.Ok)
            {
                AppLog.Warn($"BranchesViewModel: commit before switch failed: {commit.Detail}");
                StatusText = Loc.Get("Branches_StatusCommitFailed");
                return;
            }
            result = _service.Checkout(_projectFilePath, target);
        }

        FinishSwitch(result, target);
    }

    private void FinishSwitch(BranchOpResult result, string target)
    {
        switch (result.Status)
        {
            case BranchOpStatus.Ok:
                ReloadProjectFromDisk?.Invoke();
                LoadBranches();
                StatusText = Loc.Format("Branches_StatusSwitched", target);
                break;
            case BranchOpStatus.BlockedByUntrackedFiles:
                AppLog.Warn($"BranchesViewModel: switch to '{target}' blocked by untracked files: {result.Detail}");
                StatusText = Loc.Get("Branches_StatusBlockedUntracked");
                break;
            default:
                AppLog.Warn($"BranchesViewModel: switch to '{target}' failed: {result.Status} {result.Detail}");
                StatusText = Loc.Get("Branches_StatusSwitchFailed");
                break;
        }
    }

    [RelayCommand]
    private Task CreateAsync() => Task.CompletedTask;   // implemented in a later task

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task RenameAsync() => Task.CompletedTask;   // implemented in a later task

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => Task.CompletedTask;   // implemented in a later task
}
