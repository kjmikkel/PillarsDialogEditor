using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;

namespace DialogEditor.ViewModels;

/// Presents git merge conflicts for resolution. Each conflict gets a Mine/Theirs
/// choice (field-level for same-node edits, binary for structural conflicts);
/// once all are resolved, ApplyCommand builds the merged project via MergeBuilder.
public partial class GitConflictResolutionViewModel : ObservableObject
{
    private readonly DialogProject _mine;
    private readonly DialogProject _theirs;
    private readonly IReadOnlyList<MergeConflict> _conflicts;

    public GitConflictResolutionViewModel(
        DialogProject mine, DialogProject theirs, IReadOnlyList<MergeConflict> conflicts)
    {
        _mine      = mine;
        _theirs    = theirs;
        _conflicts = conflicts;
    }
}
