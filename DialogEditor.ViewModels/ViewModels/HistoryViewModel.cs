using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One commit row. Presentation-free Date (DateTimeOffset); the view formats it.
public class CommitRowViewModel(CommitInfo commit)
{
    public string         Sha      => commit.Sha;
    public string         ShortSha => commit.ShortSha;
    public string         Author   => commit.Author;
    public DateTimeOffset Date     => commit.Date;
    public string         Subject  => commit.Subject;
}

/// Lists the open project file's git history; "Compare" opens the selected commit
/// in the compare window via a host callback (the VM layer can't open windows).
public partial class HistoryViewModel : ObservableObject
{
    [ObservableProperty] private CommitRowViewModel? _selected;
    [ObservableProperty] private string _statusText = "";

    public IReadOnlyList<CommitRowViewModel> Commits { get; }
    public bool HasCommits => Commits.Count > 0;

    /// Set by the host: open the compare window with this commit as the right endpoint.
    public Action<string>? CompareWithCommit { get; set; }

    public HistoryViewModel(IGitRunner git, string projectFilePath)
    {
        IReadOnlyList<CommitInfo> commits = [];
        try
        {
            commits = new ProjectHistoryService(git).Load(projectFilePath);
        }
        catch (DiffException ex)
        {
            AppLog.Warn($"HistoryViewModel: could not load history: {ex.Message}");
            StatusText = ex.Kind == DiffExceptionKind.NotARepo
                ? Loc.Get("History_StatusNotARepo")
                : Loc.Get("History_StatusError");
        }

        Commits = commits.Select(c => new CommitRowViewModel(c)).ToList();
        if (Commits.Count == 0 && StatusText.Length == 0)
            StatusText = Loc.Get("History_StatusNoHistory");
    }

    partial void OnSelectedChanged(CommitRowViewModel? value) => CompareCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private void Compare() => CompareWithCommit?.Invoke(Selected!.Sha);

    private bool CanCompare => Selected is not null;
}
