using System.Collections.ObjectModel;
using System.Linq;
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
        var previousName = Selected?.Name;   // rows are rebuilt below, so reconcile by name
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
        // Re-point the selection into the freshly built rows (or clear it if the branch is
        // gone), so Selected is never a stale row detached from Branches.
        Selected = previousName is null
            ? null
            : Branches.FirstOrDefault(b => b.Name == previousName);
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
            try
            {
                files = _service.ListUncommittedChanges(_projectFilePath);
            }
            catch (DiffException ex)
            {
                // Fall back to an empty list so the user can still consent; commit -a
                // commits all tracked changes regardless of what the dialog lists.
                AppLog.Warn($"BranchesViewModel: could not list uncommitted files before commit prompt: {ex.Message}");
                files = [];
            }

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
    private async Task CreateAsync()
    {
        if (RequestBranchName is null) return;
        var name = await RequestBranchName(null);
        if (string.IsNullOrWhiteSpace(name)) return;
        ApplyMutationResult(_service.Create(_projectFilePath, name), "Branches_StatusCreated", name);
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task RenameAsync()
    {
        if (RequestBranchName is null) return;
        var from = Selected!.Name;
        var name = await RequestBranchName(from);
        if (string.IsNullOrWhiteSpace(name) || name == from) return;
        ApplyMutationResult(_service.Rename(_projectFilePath, from, name), "Branches_StatusRenamed", name);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var name   = Selected!.Name;
        var result = _service.Delete(_projectFilePath, name, force: false);

        if (result.Status == BranchOpStatus.NotMerged)
        {
            var ok = ConfirmForceDelete is not null && await ConfirmForceDelete(name);
            if (!ok) return;
            result = _service.Delete(_projectFilePath, name, force: true);
        }
        ApplyMutationResult(result, "Branches_StatusDeleted", name);
    }

    private void ApplyMutationResult(BranchOpResult result, string successKey, string name)
    {
        if (result.Status == BranchOpStatus.Ok)
        {
            LoadBranches();
            StatusText = Loc.Format(successKey, name);
            return;
        }
        AppLog.Warn($"BranchesViewModel: operation failed: {result.Status} {result.Detail}");
        StatusText = result.Status switch
        {
            BranchOpStatus.NameInvalid => Loc.Get("Branches_StatusNameInvalid"),
            BranchOpStatus.NameExists  => Loc.Get("Branches_StatusNameExists"),
            BranchOpStatus.GitMissing  => Loc.Get("Branches_StatusGitMissing"),
            BranchOpStatus.NotARepo    => Loc.Get("Branches_StatusNotARepo"),
            _                          => Loc.Get("Branches_StatusError"),
        };
    }
}
