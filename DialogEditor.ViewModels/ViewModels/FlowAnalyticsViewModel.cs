using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class FlowIssueViewModel : ObservableObject
{
    private readonly Action<int> _navigate;

    public int           NodeId      { get; }
    public FlowIssueKind Kind        { get; }
    public string        NodeSnippet { get; }

    public string KindLabel => Kind switch
    {
        FlowIssueKind.Unreachable               => Loc.Get("FlowAnalytics_Issue_Unreachable"),
        FlowIssueKind.PlayerDeadEnd             => Loc.Get("FlowAnalytics_Issue_PlayerDeadEnd"),
        FlowIssueKind.EmptyText                 => Loc.Get("FlowAnalytics_Issue_EmptyText"),
        FlowIssueKind.NoIncomingLinks           => Loc.Get("FlowAnalytics_Issue_NoIncomingLinks"),
        FlowIssueKind.BarkTextTooLong           => Loc.Format("FlowAnalytics_Issue_BarkTextTooLong", BarkConstants.TextLengthWarningThreshold),
        FlowIssueKind.BarkHasPlayerChoiceChild  => Loc.Get("FlowAnalytics_Issue_BarkHasPlayerChoiceChild"),
        _                                       => Kind.ToString()
    };

    /// Severity tier as text — the icon's colour/glyph carry it visually; this is
    /// the screen-reader/tooltip equivalent. Same binary rule as
    /// FlowIssueKindToSeverityGlyphConverter (each pinned by its own test so they
    /// cannot drift apart silently).
    public string SeverityLabel => Kind == FlowIssueKind.Unreachable
        ? Loc.Get("FlowAnalytics_Severity_Error")
        : Loc.Get("FlowAnalytics_Severity_Warning");

    public string DisplayText => $"Node {NodeId} — {NodeSnippet}";

    public FlowIssueViewModel(FlowIssue issue, string nodeSnippet, Action<int> navigate)
    {
        NodeId      = issue.NodeId;
        Kind        = issue.Kind;
        NodeSnippet = nodeSnippet;
        _navigate   = navigate;
    }

    [RelayCommand]
    private void Navigate() => _navigate(NodeId);
}

public partial class TokenIssueRowViewModel : ObservableObject
{
    private readonly Action<int> _navigate;

    public int    NodeId   { get; }
    public string Language { get; }   // "" for Default/Female text
    public string Message  { get; }

    public string DisplayText => string.IsNullOrEmpty(Language)
        ? Loc.Format("FlowAnalytics_TagIssue_Row_Default", NodeId, Message)
        : Loc.Format("FlowAnalytics_TagIssue_Row", NodeId, Language, Message);

    public TokenIssueRowViewModel(int nodeId, string language, string message, Action<int> navigate)
    {
        NodeId    = nodeId;
        Language  = language;
        Message   = message;
        _navigate = navigate;
    }

    [RelayCommand]
    private void Navigate() => _navigate(NodeId);
}

/// One "By opening choice" row.
public sealed partial class PathBranchRowViewModel : ObservableObject
{
    private readonly Action _navigate;

    public string ChoiceText         { get; }
    public string DefaultContentText { get; }
    public string DefaultLongestText { get; }
    public string FemaleContentText  { get; }
    public string FemaleLongestText  { get; }

    public PathBranchRowViewModel(string choiceText, string defaultContent, string defaultLongest,
        string femaleContent, string femaleLongest, Action navigate)
    {
        ChoiceText         = choiceText;
        DefaultContentText = defaultContent;
        DefaultLongestText = defaultLongest;
        FemaleContentText  = femaleContent;
        FemaleLongestText  = femaleLongest;
        _navigate          = navigate;
    }

    [RelayCommand] private void Navigate() => _navigate();
}

/// One "Words per speaker" row.
public sealed class SpeakerWordRowViewModel
{
    public string Display { get; }
    public SpeakerWordRowViewModel(string display) => Display = display;
}

public partial class FlowAnalyticsViewModel : ObservableObject
{
    private readonly Func<ConversationEditSnapshot?> _getSnapshot;
    private readonly Action<int>                     _navigateToNode;
    private readonly Func<IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>> _getTranslations;
    private readonly string                          _gameId;
    private readonly TokenValidationService          _tokenValidator = new();

    [ObservableProperty] private FlowStatistics? _statistics;
    [ObservableProperty] private string          _statusText             = string.Empty;
    [ObservableProperty] private string          _lastAnalysed           = string.Empty;
    [ObservableProperty] private string          _conditionalLinksDisplay = string.Empty;
    [ObservableProperty] private bool            _hasData;

    public ObservableCollection<FlowIssueViewModel>   Issues      { get; } = [];
    public ObservableCollection<TokenIssueRowViewModel> TokenIssues { get; } = [];
    public ObservableCollection<PathBranchRowViewModel> Branches       { get; } = [];
    public ObservableCollection<SpeakerWordRowViewModel> WordsPerSpeaker { get; } = [];

    [ObservableProperty] private bool   _hasPathStats;
    [ObservableProperty] private bool   _hasSignificantFemaleVariant;
    [ObservableProperty] private string _longestPlaythroughText  = string.Empty;
    [ObservableProperty] private string _shortestPlaythroughText = string.Empty;
    [ObservableProperty] private string _totalContentText        = string.Empty;

    public FlowAnalyticsViewModel(
        Func<ConversationEditSnapshot?> getSnapshot,
        Action<int>                     navigateToNode,
        Func<IReadOnlyDictionary<string, IReadOnlyList<NodeTranslation>>>? getTranslations = null,
        string                          gameId = "")
    {
        _getSnapshot     = getSnapshot;
        _navigateToNode  = navigateToNode;
        _getTranslations = getTranslations
            ?? (() => new Dictionary<string, IReadOnlyList<NodeTranslation>>());
        _gameId          = gameId;
    }

    [RelayCommand]
    private void Refresh()
    {
        var snapshot = _getSnapshot();
        if (snapshot is null)
        {
            StatusText = Loc.Get("FlowAnalytics_NoData");
            return;
        }

        var report = FlowAnalysisService.Analyze(snapshot);

        Statistics = report.Statistics;
        HasData    = true;

        Issues.Clear();
        foreach (var issue in report.Issues)
        {
            var node    = snapshot.Nodes.FirstOrDefault(n => n.NodeId == issue.NodeId);
            var snippet = node is not null && !string.IsNullOrWhiteSpace(node.DefaultText)
                ? Truncate(node.DefaultText, 60)
                : $"({node?.SpeakerCategory.ToString().ToLower() ?? "unknown"}, no text)";
            Issues.Add(new FlowIssueViewModel(issue, snippet, _navigateToNode));
        }

        var total = report.Statistics.TotalLinkCount;
        var cond  = report.Statistics.ConditionalLinkCount;
        ConditionalLinksDisplay = total > 0
            ? $"{cond} ({(int)(cond * 100.0 / total)}%)"
            : cond.ToString();

        var issueCount = report.Issues.Count;
        StatusText  = issueCount > 0
            ? Loc.Format("FlowAnalytics_Issues", issueCount)
            : Loc.Get("FlowAnalytics_NoIssues");
        LastAnalysed = Loc.Format("FlowAnalytics_LastAnalysed",
            DateTime.Now.ToString("HH:mm:ss"));

        // ── Token/markup validation (Default/Female + translations) ──────────
        TokenIssues.Clear();
        foreach (var node in snapshot.Nodes)
        {
            AddTokenIssues(node.NodeId, "", node.DefaultText);
            AddTokenIssues(node.NodeId, "", node.FemaleText);
        }
        foreach (var (lang, entries) in _getTranslations())
            foreach (var t in entries)
            {
                AddTokenIssues(t.NodeId, lang, t.DefaultText);
                AddTokenIssues(t.NodeId, lang, t.FemaleText);
            }

        RefreshPathStats(snapshot);
    }

    private void RefreshPathStats(ConversationEditSnapshot snapshot)
    {
        var report = PathStatsService.Analyze(snapshot);
        HasSignificantFemaleVariant = report.HasSignificantFemaleVariant;

        LongestPlaythroughText  = WordsTimePair(report.DefaultLongestWords,  report.FemaleLongestWords);
        ShortestPlaythroughText = WordsTimePair(report.DefaultShortestWords, report.FemaleShortestWords);
        TotalContentText        = WordsTimePair(report.DefaultTotalWords,    report.FemaleTotalWords);

        WordsPerSpeaker.Clear();
        foreach (var s in report.WordsPerSpeaker)
        {
            var name = ResolveSpeakerName(s);
            var display = HasSignificantFemaleVariant
                ? Loc.Format("PathStats_SpeakerRowFemale", name, s.DefaultWords, s.FemaleWords)
                : Loc.Format("PathStats_SpeakerRow", name, s.DefaultWords);
            WordsPerSpeaker.Add(new SpeakerWordRowViewModel(display));
        }

        Branches.Clear();
        foreach (var b in report.Branches)
        {
            var choice = Truncate(b.ChoiceText, 50);
            Branches.Add(new PathBranchRowViewModel(
                choice,
                Loc.Format("PathStats_BranchContent", WordsTime(b.DefaultContentWords)),
                Loc.Format("PathStats_BranchLongest", WordsTime(b.DefaultLongestWords)),
                Loc.Format("PathStats_BranchContent", WordsTime(b.FemaleContentWords)),
                Loc.Format("PathStats_BranchLongest", WordsTime(b.FemaleLongestWords)),
                () => _navigateToNode(b.ChoiceNodeId)));
        }

        HasPathStats = report.DefaultTotalWords > 0 || report.WordsPerSpeaker.Count > 0;
    }

    private string WordsTime(int words) =>
        Loc.Format("PathStats_WordsTime", words, PathStatsFormat.ReadingTime(words));

    private string WordsTimePair(int defaultWords, int femaleWords) =>
        HasSignificantFemaleVariant
            ? Loc.Format("PathStats_DefaultFemale", WordsTime(defaultWords), WordsTime(femaleWords))
            : WordsTime(defaultWords);

    private static string ResolveSpeakerName(SpeakerWordCount s)
    {
        var resolved = SpeakerNameService.Resolve(s.SpeakerGuid);
        if (!string.IsNullOrEmpty(resolved)) return resolved;
        return s.Category switch
        {
            SpeakerCategory.Player   => Loc.Get("PathStats_Cat_Player"),
            SpeakerCategory.Narrator => Loc.Get("PathStats_Cat_Narrator"),
            SpeakerCategory.Script   => Loc.Get("PathStats_Cat_Script"),
            _                        => Loc.Get("PathStats_Cat_Npc"),
        };
    }

    private void AddTokenIssues(int nodeId, string language, string? text)
    {
        if (string.IsNullOrEmpty(text)) return;
        foreach (var issue in _tokenValidator.Validate(text, _gameId))
        {
            var msg = issue.Kind switch
            {
                TokenIssueKind.UnbalancedMarkup =>
                    Loc.Format("Validation_UnbalancedMarkup", issue.Fragment),
                _ when issue.Suggestion is not null =>
                    Loc.Format("Validation_UnknownToken_Suggest", issue.Fragment, issue.Suggestion),
                _ => Loc.Format("Validation_UnknownToken", issue.Fragment),
            };
            TokenIssues.Add(new TokenIssueRowViewModel(nodeId, language, msg, _navigateToNode));
        }
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";
}
