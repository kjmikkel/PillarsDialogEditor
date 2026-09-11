using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public record BatchReplaceMatchViewModel(
    int        NodeId,
    BatchField Field,
    string     Before,
    string     After)
{
    /// The field's name as the preview shows it. Built here rather than in
    /// BatchReplaceService because Patch cannot reach Loc — and because the label must
    /// stay separable from Field, which Apply uses as a lookup key (see BatchField).
    public string FieldLabel => Field.Kind switch
    {
        BatchFieldKind.DefaultText    => Loc.Get("BatchReplace_Field_DefaultText"),
        BatchFieldKind.FemaleText     => Loc.Get("BatchReplace_Field_FemaleText"),
        BatchFieldKind.SpeakerGuid    => Loc.Get("BatchReplace_Field_SpeakerGuid"),
        BatchFieldKind.ListenerGuid   => Loc.Get("BatchReplace_Field_ListenerGuid"),
        // ScriptCategory is a game-data enum name (Enter/Exit/Update) and stays verbatim,
        // per the Strings.axaml note on technical identifiers.
        BatchFieldKind.ScriptParam    => Loc.Format("BatchReplace_Field_ScriptParam",
                                             Field.ScriptCategory, Field.Index, Field.ParamIndex),
        BatchFieldKind.ConditionParam => Loc.Format("BatchReplace_Field_ConditionParam",
                                             Field.Index, Field.ParamIndex),
        _                             => Loc.Get("BatchReplace_Field_Unknown"),
    };

    /// The whole preview row: node plus field. Replaces a
    /// StringFormat='Node {NodeId} — {FieldPath}' in BatchReplaceWindow.axaml that
    /// Avalonia's positional-only StringFormat could not have rendered.
    public string RowLabel => Loc.Format("BatchReplace_MatchLabel", NodeId, FieldLabel);
}

public partial class BatchReplaceConversationViewModel : ObservableObject
{
    public ConversationFile                    File     { get; }
    public string                              Name     => File.Name;
    public IReadOnlyList<BatchReplaceMatchViewModel> Matches  { get; }
    public int                                 MatchCount => Matches.Count;

    [ObservableProperty] private bool _isSelected = true;

    public BatchReplaceConversationViewModel(
        ConversationFile file,
        IReadOnlyList<BatchReplaceMatchViewModel> matches)
    {
        File    = file;
        Matches = matches;
    }
}

public partial class BatchReplaceViewModel : ObservableObject
{
    private readonly IGameDataProvider           _provider;
    private readonly IReadOnlyList<ConversationFile> _allFiles;
    private readonly Func<ConversationFile, bool>    _isOpenInEditor;

    // ── Search form ───────────────────────────────────────────────────────

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PreviewCommand))]
    private string _searchText = string.Empty;

    [ObservableProperty] private string _replaceText        = string.Empty;
    [ObservableProperty] private bool   _caseSensitive;
    [ObservableProperty] private bool   _inNodeText         = true;
    [ObservableProperty] private bool   _inSpeakerGuids;
    [ObservableProperty] private bool   _inScriptParams;
    [ObservableProperty] private bool   _inConditionParams;

    // ── Results ───────────────────────────────────────────────────────────

    public ObservableCollection<BatchReplaceConversationViewModel> Results { get; } = [];

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResults))]
    private int _resultCount;

    public bool HasResults => ResultCount > 0;

    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private bool   _isBusy;

    // ── Commands ──────────────────────────────────────────────────────────

    public BatchReplaceViewModel(
        IGameDataProvider            provider,
        IReadOnlyList<ConversationFile> allFiles,
        Func<ConversationFile, bool> isOpenInEditor)
    {
        _provider       = provider;
        _allFiles       = allFiles;
        _isOpenInEditor = isOpenInEditor;
    }

    [RelayCommand(CanExecute = nameof(CanPreview))]
    private async Task PreviewAsync()
    {
        IsBusy = true;
        Results.Clear();
        ResultCount = 0;

        var query = BuildQuery();
        var skipped = _allFiles.Count(f => _isOpenInEditor(f));
        var filesToSearch = _allFiles.Where(f => !_isOpenInEditor(f)).ToList();

        var rawResults = await Task.Run(
            () => BatchReplaceService.DryRun(query, filesToSearch, _provider));

        foreach (var r in rawResults)
        {
            var matches = r.Matches
                .Select(m => new BatchReplaceMatchViewModel(m.NodeId, m.Field, m.Before, m.After))
                .ToList();
            var conv = new BatchReplaceConversationViewModel(r.File, matches);
            conv.PropertyChanged += (_, _) => ApplyCommand.NotifyCanExecuteChanged();
            Results.Add(conv);
        }

        ResultCount = Results.Count;
        ApplyCommand.NotifyCanExecuteChanged();

        StatusText = BuildStatusText(rawResults.Sum(r => r.Matches.Count),
                                     rawResults.Count, skipped);
        IsBusy = false;
    }

    private bool CanPreview() => !string.IsNullOrEmpty(SearchText);

    [RelayCommand(CanExecute = nameof(CanApply))]
    private async Task ApplyAsync()
    {
        IsBusy = true;

        var selected = Results
            .Where(r => r.IsSelected)
            .Select(r => new BatchConversationResult(
                r.File,
                r.Matches.Select(m => new BatchFieldMatch(m.NodeId, m.Field, m.Before, m.After))
                         .ToList()))
            .ToList();

        await Task.Run(() => BatchReplaceService.Apply(selected, _provider));

        Results.Clear();
        ResultCount = 0;
        ApplyCommand.NotifyCanExecuteChanged();

        var totalMatches = selected.Sum(r => r.Matches.Count);
        // Wrapper + pre-pluralised fragments: each count pluralises independently.
        StatusText = Loc.Format("BatchReplace_StatusApplied",
            Loc.FormatCount("BatchReplace_ReplacementCount", totalMatches),
            Loc.FormatCount("BatchReplace_ConversationCount", selected.Count));
        IsBusy = false;
    }

    private bool CanApply()
        => HasResults && Results.Any(r => r.IsSelected);

    // ── Helpers ───────────────────────────────────────────────────────────

    private BatchReplaceQuery BuildQuery() => new(
        SearchText, ReplaceText, CaseSensitive,
        InNodeText, InSpeakerGuids, InScriptParams,
        InConditionParams);

    private string BuildStatusText(int matchCount, int convCount, int skipped)
    {
        var msg = matchCount > 0
            ? Loc.Format("BatchReplace_StatusMatches",
                Loc.FormatCount("BatchReplace_MatchCount", matchCount),
                Loc.FormatCount("BatchReplace_ConversationCount", convCount))
            : Loc.Get("BatchReplace_StatusNoMatches");
        if (skipped > 0)
            msg += " " + Loc.FormatCount("BatchReplace_StatusSkipped", skipped);
        return msg;
    }
}
