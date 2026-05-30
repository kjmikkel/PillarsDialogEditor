using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// One conflict row in the resolution list. Exposes the mine/theirs values and
/// kind-specific labels, and an observable Choice that drives the merge.
public partial class ConflictRowViewModel : ObservableObject
{
    public MergeConflict Conflict { get; }

    public string Title       { get; }
    public string MineLabel   { get; }
    public string TheirsLabel { get; }

    public MergeConflictKind Kind        => Conflict.Kind;
    public string            MineValue   => Conflict.MineValue;
    public string            TheirsValue => Conflict.TheirsValue;

    [ObservableProperty]
    private MergeSide? _choice;

    // Two-way radio-button bindings (avoid an enum↔bool converter in XAML).
    // Setting false is ignored: the sibling radio's check drives the change.
    public bool IsMineChosen
    {
        get => Choice == MergeSide.Mine;
        set { if (value) Choice = MergeSide.Mine; }
    }

    public bool IsTheirsChosen
    {
        get => Choice == MergeSide.Theirs;
        set { if (value) Choice = MergeSide.Theirs; }
    }

    public bool IsUnresolved => Choice is null;

    public event Action? ChoiceChanged;

    partial void OnChoiceChanged(MergeSide? value)
    {
        OnPropertyChanged(nameof(IsMineChosen));
        OnPropertyChanged(nameof(IsTheirsChosen));
        OnPropertyChanged(nameof(IsUnresolved));
        ChoiceChanged?.Invoke();
    }

    public ConflictRowViewModel(MergeConflict conflict)
    {
        Conflict = conflict;

        Title = conflict.Kind switch
        {
            MergeConflictKind.FieldEdit =>
                Loc.Format("GitConflict_RowTitleField", conflict.ConversationName, conflict.NodeId, conflict.FieldName ?? ""),
            MergeConflictKind.TranslationEdit =>
                Loc.Format("GitConflict_RowTitleTranslation", conflict.ConversationName, conflict.NodeId, conflict.FieldName ?? ""),
            _ =>
                Loc.Format("GitConflict_RowTitleNode", conflict.ConversationName, conflict.NodeId, DescribeKind(conflict)),
        };

        (MineLabel, TheirsLabel) = Labels(conflict);
    }

    private static string DescribeKind(MergeConflict c) => c.Kind switch
    {
        MergeConflictKind.DeleteVsEdit     => Loc.Get("GitConflict_KindDeleteVsEdit"),
        MergeConflictKind.NodeAddAdd       => Loc.Get("GitConflict_KindAddAdd"),
        MergeConflictKind.ConversationLevel => Loc.Get("GitConflict_KindConversation"),
        _                                  => "",
    };

    private static (string Mine, string Theirs) Labels(MergeConflict c) => c.Kind switch
    {
        MergeConflictKind.DeleteVsEdit when c.MineValue == MergeConflict.DeletedMarker
            => (Loc.Get("GitConflict_AcceptDeletion"), Loc.Get("GitConflict_KeepEdit")),
        MergeConflictKind.DeleteVsEdit
            => (Loc.Get("GitConflict_KeepEdit"), Loc.Get("GitConflict_AcceptDeletion")),
        _   => (Loc.Get("GitConflict_KeepMine"), Loc.Get("GitConflict_KeepTheirs")),
    };
}

/// Presents git merge conflicts for resolution. Each conflict gets a Mine/Theirs
/// choice (field-level for same-node edits, binary for structural conflicts);
/// once all are resolved, ApplyCommand builds the merged project via MergeBuilder.
public partial class GitConflictResolutionViewModel : ObservableObject
{
    private readonly DialogProject _mine;
    private readonly DialogProject _theirs;

    public IReadOnlyList<ConflictRowViewModel> Conflicts { get; }

    [ObservableProperty]
    private ConflictRowViewModel? _selected;

    /// The merged project produced by ApplyCommand, or null until applied.
    public DialogProject? Result { get; private set; }

    /// Raised when the dialog should close (after Apply succeeds).
    public event Action? RequestClose;

    public GitConflictResolutionViewModel(
        DialogProject mine, DialogProject theirs, IReadOnlyList<MergeConflict> conflicts)
    {
        _mine   = mine;
        _theirs = theirs;

        Conflicts = conflicts.Select(c =>
        {
            var row = new ConflictRowViewModel(c);
            row.ChoiceChanged += OnRowChoiceChanged;
            return row;
        }).ToList();

        Selected = Conflicts.FirstOrDefault();
    }

    public bool AllResolved => Conflicts.All(r => r.Choice is not null);

    private void OnRowChoiceChanged()
    {
        OnPropertyChanged(nameof(AllResolved));
        ApplyCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand(CanExecute = nameof(AllResolved))]
    private void Apply()
    {
        var choices = Conflicts.Select(r => (r.Conflict, r.Choice!.Value)).ToList();
        Result = MergeBuilder.Build(_mine, _theirs, choices);
        RequestClose?.Invoke();
    }
}
