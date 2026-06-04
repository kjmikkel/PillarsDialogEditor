using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// Lists per-node git attribution (who last touched each node) for the open project
/// file. Read-only; the view just displays rows. Mirrors HistoryViewModel.
public partial class BlameViewModel : ObservableObject
{
    [ObservableProperty] private string _statusText = "";

    public IReadOnlyList<NodeBlameRowViewModel> Rows { get; }
    public bool HasData => Rows.Count > 0;

    public BlameViewModel(IGitRunner git, string projectFilePath)
    {
        IReadOnlyList<NodeBlame> blames = [];
        try
        {
            blames = new ProjectBlameService(git).Load(projectFilePath);
        }
        catch (DiffException ex)
        {
            AppLog.Warn($"BlameViewModel: could not load attribution: {ex.Message}");
            StatusText = ex.Kind == DiffExceptionKind.NotARepo
                ? Loc.Get("Blame_StatusNotARepo")
                : Loc.Get("Blame_StatusError");
        }

        Rows = blames.Select(b => new NodeBlameRowViewModel(b)).ToList();
        if (Rows.Count == 0 && StatusText.Length == 0)
            StatusText = Loc.Get("Blame_StatusNoData");
    }
}
