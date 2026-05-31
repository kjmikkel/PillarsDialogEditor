using System;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// Before/after text detail for one selected diff node. Pure presentation logic:
/// placeholder substitution for added/removed nodes, female-row visibility, and
/// structural-only detection. The inline word-level highlighting is rendered by
/// the view from the Before/After strings exposed here (see DiffWindow).
public class NodeDiffDetailViewModel
{
    public int        NodeId { get; }
    public DiffStatus Kind   { get; }

    public string DefaultBefore { get; }
    public string DefaultAfter  { get; }
    public string FemaleBefore  { get; }
    public string FemaleAfter   { get; }

    public bool HasFemaleRow     { get; }
    public bool IsStructuralOnly { get; }

    public bool ShowTextRows  => !IsStructuralOnly;
    public bool ShowFemaleRow => ShowTextRows && HasFemaleRow;

    public string HeaderText => Loc.Format("Diff_Detail_Header", NodeId);

    public NodeDiffDetailViewModel(
        int nodeId, DiffStatus kind,
        string defaultLeft, string defaultRight,
        string femaleLeft,  string femaleRight)
    {
        NodeId = nodeId;
        Kind   = kind;

        DefaultBefore = kind == DiffStatus.Added
            ? Loc.Get("Diff_Detail_NodeAdded")   : defaultLeft;
        DefaultAfter  = kind == DiffStatus.Removed
            ? Loc.Get("Diff_Detail_NodeRemoved") : defaultRight;

        // Female-row visibility is judged on the real text only, so a placeholder
        // side does not by itself force the row open.
        HasFemaleRow = !string.IsNullOrEmpty(femaleLeft)
                    || !string.IsNullOrEmpty(femaleRight);

        FemaleBefore = kind == DiffStatus.Added
            ? Loc.Get("Diff_Detail_NodeAdded")   : femaleLeft;
        FemaleAfter  = kind == DiffStatus.Removed
            ? Loc.Get("Diff_Detail_NodeRemoved") : femaleRight;

        IsStructuralOnly = kind == DiffStatus.Changed
            && string.Equals(defaultLeft, defaultRight, StringComparison.Ordinal)
            && string.Equals(femaleLeft,  femaleRight,  StringComparison.Ordinal);
    }
}
