using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Analytics;
using DialogEditor.Core.Editing;
using DialogEditor.ViewModels.Resources;

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
        FlowIssueKind.BarkTextTooLong           => Loc.Get("FlowAnalytics_Issue_BarkTextTooLong"),
        FlowIssueKind.BarkHasPlayerChoiceChild  => Loc.Get("FlowAnalytics_Issue_BarkHasPlayerChoiceChild"),
        _                                       => Kind.ToString()
    };

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

public partial class FlowAnalyticsViewModel : ObservableObject
{
    private readonly Func<ConversationEditSnapshot?> _getSnapshot;
    private readonly Action<int>                     _navigateToNode;

    [ObservableProperty] private FlowStatistics? _statistics;
    [ObservableProperty] private string          _statusText             = string.Empty;
    [ObservableProperty] private string          _lastAnalysed           = string.Empty;
    [ObservableProperty] private string          _conditionalLinksDisplay = string.Empty;
    [ObservableProperty] private bool            _hasData;

    public ObservableCollection<FlowIssueViewModel> Issues { get; } = [];

    public FlowAnalyticsViewModel(
        Func<ConversationEditSnapshot?> getSnapshot,
        Action<int>                     navigateToNode)
    {
        _getSnapshot    = getSnapshot;
        _navigateToNode = navigateToNode;
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
    }

    private static string Truncate(string text, int maxLength)
        => text.Length <= maxLength ? text : text[..maxLength] + "…";
}
