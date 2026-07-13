using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One row of the Validate Text window's "Stale data" section.
public sealed partial class StaleDataRowViewModel
{
    private readonly StaleDataRow _row;
    private readonly Action<StaleDataRow>? _removeOne;

    public StaleDataRow Row => _row;
    public string ConversationName { get; }
    public string NodeLabel        { get; }
    public string CategoryLabel    { get; }
    public string ConfidenceLabel  { get; }
    public bool   IsLikely         => _row.Confidence == StaleConfidence.Likely;

    /// Per-row Remove is offered for likely rows only (confirmed rows go through
    /// the armed bulk clean-up).
    public bool CanRemove => IsLikely && _removeOne is not null;

    public StaleDataRowViewModel(StaleDataRow row, string primaryLanguage, Action<StaleDataRow>? removeOne)
    {
        _row       = row;
        _removeOne = removeOne;

        ConversationName = row.ConversationName;
        NodeLabel        = Loc.Format("VoValidation_NodeRow", row.NodeId);
        CategoryLabel    = row.Kind switch
        {
            StaleDataKind.Comment  => Loc.Get("StaleData_Category_Comment"),
            StaleDataKind.Layout   => Loc.Get("StaleData_Category_Layout"),
            _ => IsPrimary(row.Language, primaryLanguage)
                    ? Loc.Get("StaleData_Category_Translation")
                    : Loc.Format("StaleData_Category_TranslationLang", row.Language!),
        };
        ConfidenceLabel  = IsLikely
            ? Loc.Get("StaleData_Confidence_Likely")
            : Loc.Get("StaleData_Confidence_Confirmed");
    }

    private static bool IsPrimary(string? lang, string primary) =>
        string.IsNullOrEmpty(lang) ||
        string.Equals(lang, primary, StringComparison.OrdinalIgnoreCase);

    [RelayCommand]
    private void Remove() => _removeOne?.Invoke(_row);
}
