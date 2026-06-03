using System;
using System.Collections.Generic;
using System.Linq;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One language's before/after text for a diff node, with placeholder
/// substitution for added/removed nodes and per-language female-row visibility.
public sealed class LanguageDiffSection
{
    public string LanguageCode  { get; }
    public string LanguageName  { get; }
    public bool   IsPrimary     { get; }
    public string DefaultBefore { get; }
    public string DefaultAfter  { get; }
    public string FemaleBefore  { get; }
    public string FemaleAfter   { get; }
    public bool   HasFemaleRow  { get; }

    public LanguageDiffSection(
        string code, bool isPrimary, DiffStatus kind,
        (string Default, string Female) left, (string Default, string Female) right)
    {
        LanguageCode = code;
        LanguageName = LanguageNameResolver.Resolve(code);
        IsPrimary    = isPrimary;

        DefaultBefore = kind == DiffStatus.Added   ? Loc.Get("Diff_Detail_NodeAdded")   : left.Default;
        DefaultAfter  = kind == DiffStatus.Removed ? Loc.Get("Diff_Detail_NodeRemoved") : right.Default;

        HasFemaleRow  = !string.IsNullOrEmpty(left.Female) || !string.IsNullOrEmpty(right.Female);
        FemaleBefore  = kind == DiffStatus.Added   ? Loc.Get("Diff_Detail_NodeAdded")   : left.Female;
        FemaleAfter   = kind == DiffStatus.Removed ? Loc.Get("Diff_Detail_NodeRemoved") : right.Female;
    }
}

/// Before/after text detail for one selected diff node, across the primary
/// language plus every language whose text changed. Pure presentation logic;
/// the view renders each section's before/after via InlineDiffTextBlock.
public sealed class NodeDiffDetailViewModel
{
    public int        NodeId { get; }
    public DiffStatus Kind   { get; }

    public IReadOnlyList<LanguageDiffSection> Sections { get; }
    public bool   IsStructuralOnly { get; }
    public bool   ShowSections => !IsStructuralOnly;
    public string HeaderText   => Loc.Format("Diff_Detail_Header", NodeId);

    public NodeDiffDetailViewModel(
        int nodeId, DiffStatus kind, string primaryLanguage,
        IReadOnlyDictionary<string, (string Default, string Female)> leftByLang,
        IReadOnlyDictionary<string, (string Default, string Female)> rightByLang)
    {
        NodeId = nodeId;
        Kind   = kind;

        var codes = new HashSet<string>(StringComparer.Ordinal) { primaryLanguage };
        foreach (var k in leftByLang.Keys)  codes.Add(k);
        foreach (var k in rightByLang.Keys) codes.Add(k);

        (string Default, string Female) Left(string c)  => leftByLang.GetValueOrDefault(c, ("", ""));
        (string Default, string Female) Right(string c) => rightByLang.GetValueOrDefault(c, ("", ""));

        bool Differs(string c)
        {
            var l = Left(c);
            var r = Right(c);
            return !string.Equals(l.Default, r.Default, StringComparison.Ordinal)
                || !string.Equals(l.Female,  r.Female,  StringComparison.Ordinal);
        }

        IsStructuralOnly = kind == DiffStatus.Changed && !codes.Any(Differs);

        Sections = IsStructuralOnly
            ? []
            : codes
                .Where(c => c == primaryLanguage || Differs(c))
                .OrderBy(c => c == primaryLanguage ? 0 : 1)
                .ThenBy(c => c, StringComparer.Ordinal)
                .Select(c => new LanguageDiffSection(c, c == primaryLanguage, kind, Left(c), Right(c)))
                .ToList();
    }
}
