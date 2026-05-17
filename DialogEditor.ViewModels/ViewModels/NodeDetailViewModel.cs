using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    private NodeViewModel? _node;

    [ObservableProperty] private bool _hasContent;

    // ── Editable proxy properties ─────────────────────────────────────────
    public string DefaultText
    {
        get => _node?.DefaultText ?? string.Empty;
        set { if (_node != null) _node.DefaultText = value; }
    }

    public string FemaleText
    {
        get => _node?.FemaleText ?? string.Empty;
        set { if (_node != null) _node.FemaleText = value; }
    }

    public bool IsPlayerChoice
    {
        get => _node?.IsPlayerChoice ?? false;
        set { if (_node != null) _node.IsPlayerChoice = value; }
    }

    public string SpeakerGuid
    {
        get => _node?.SpeakerGuid ?? string.Empty;
        set { if (_node != null) _node.SpeakerGuid = value; }
    }

    public string ListenerGuid
    {
        get => _node?.ListenerGuid ?? string.Empty;
        set { if (_node != null) _node.ListenerGuid = value; }
    }

    public string DisplayType
    {
        get => _node?.DisplayType ?? string.Empty;
        set { if (_node != null) _node.DisplayType = value; }
    }

    public string Persistence
    {
        get => _node?.Persistence ?? string.Empty;
        set { if (_node != null) _node.Persistence = value; }
    }

    public string ActorDirection
    {
        get => _node?.ActorDirection ?? string.Empty;
        set { if (_node != null) _node.ActorDirection = value; }
    }

    public string Comments
    {
        get => _node?.Comments ?? string.Empty;
        set { if (_node != null) _node.Comments = value; }
    }

    public string ExternalVO
    {
        get => _node?.ExternalVO ?? string.Empty;
        set { if (_node != null) _node.ExternalVO = value; }
    }

    public bool HasVO
    {
        get => _node?.HasVO ?? false;
        set { if (_node != null) _node.HasVO = value; }
    }

    public bool HideSpeaker
    {
        get => _node?.HideSpeaker ?? false;
        set { if (_node != null) _node.HideSpeaker = value; }
    }

    // ── NodeType proxy (bool ↔ string for ComboBox binding) ──────────────
    public string NodeTypeString
    {
        get => IsPlayerChoice ? "Player Choice" : "NPC Line";
        set { if (_node != null) _node.IsPlayerChoice = value == "Player Choice"; }
    }

    // ── Read-only display ─────────────────────────────────────────────────
    public string FemaleTextDisplay =>
        (_node?.HasFemaleText ?? false) ? _node!.FemaleText : Loc.Get("NodeDetail_SameAsDefault");

    public bool HasFemaleText => _node?.HasFemaleText ?? false;

    [ObservableProperty] private IReadOnlyList<PropertyGroup> _propertyGroups  = [];
    [ObservableProperty] private IReadOnlyList<LinkRow>       _links           = [];
    [ObservableProperty] private string                       _addLinkTargetId = string.Empty;

    // Raised when the user requests to add/delete a link — ConversationViewModel handles it
    public event Action<int, int>? AddLinkRequested;    // (fromNodeId, toNodeId)
    public event Action<int>?      DeleteLinkRequested; // (toNodeId)

    [RelayCommand]
    private void AddLink()
    {
        if (_node is null || !int.TryParse(AddLinkTargetId.Trim(), out var targetId)) return;
        AddLinkRequested?.Invoke(_node.NodeId, targetId);
        AddLinkTargetId = string.Empty;
    }

    [RelayCommand]
    private void DeleteLink(LinkRow? row)
    {
        if (_node is null || row is null) return;
        // Arrow format is "→ {toNodeId}" — parse the ID from it
        var parts = row.Arrow.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length >= 2 && int.TryParse(parts[^1], out var toId))
            DeleteLinkRequested?.Invoke(toId);
    }

    // ── Load / Clear ──────────────────────────────────────────────────────
    public void Load(NodeViewModel? node)
    {
        if (_node is not null)
            _node.PropertyChanged -= OnNodePropertyChanged;

        _node = node;

        if (node is null) { HasContent = false; return; }

        node.PropertyChanged += OnNodePropertyChanged;
        RefreshReadOnlyGroups(node);
        Links      = node.Links.Select(BuildLinkRow).ToList();
        HasContent = true;
        NotifyAllProxies();
    }

    public void Clear() => Load(null);

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyAllProxies();
        if (_node is not null)
            RefreshReadOnlyGroups(_node);
    }

    private void NotifyAllProxies()
    {
        OnPropertyChanged(nameof(DefaultText));
        OnPropertyChanged(nameof(FemaleText));
        OnPropertyChanged(nameof(FemaleTextDisplay));
        OnPropertyChanged(nameof(HasFemaleText));
        OnPropertyChanged(nameof(IsPlayerChoice));
        OnPropertyChanged(nameof(NodeTypeString));
        OnPropertyChanged(nameof(SpeakerGuid));
        OnPropertyChanged(nameof(ListenerGuid));
        OnPropertyChanged(nameof(DisplayType));
        OnPropertyChanged(nameof(Persistence));
        OnPropertyChanged(nameof(ActorDirection));
        OnPropertyChanged(nameof(Comments));
        OnPropertyChanged(nameof(ExternalVO));
        OnPropertyChanged(nameof(HasVO));
        OnPropertyChanged(nameof(HideSpeaker));
    }

    private void RefreshReadOnlyGroups(NodeViewModel node)
    {
        var none = Loc.Get("NodeDetail_None");
        PropertyGroups =
        [
            new PropertyGroup(Loc.Get("Label_GroupIdentity"),
            [
                new PropertyRow(Loc.Get("PropertyRow_NodeId"), node.NodeId.ToString()),
            ]),
            new PropertyGroup(Loc.Get("Label_GroupLogic"),
            [
                new PropertyRow(Loc.Get("PropertyRow_Conditions"),
                    string.IsNullOrEmpty(node.ConditionExpression) ? none : node.ConditionExpression,
                    PropertyValueStyle.Condition),
                new PropertyRow(Loc.Get("PropertyRow_Scripts"),
                    node.Scripts.Count == 0 ? none : string.Join(Environment.NewLine, node.Scripts),
                    PropertyValueStyle.Script),
            ]),
        ];
    }

    public void RefreshLinks(IEnumerable<NodeLink> links)
        => Links = links.Select(BuildLinkRow).ToList();

    private static LinkRow BuildLinkRow(NodeLink link)
    {
        var extras = new List<string>();
        if (link.RandomWeight != 1f)
            extras.Add($"{Loc.Get("Link_WeightPrefix")}{link.RandomWeight:0.##}");
        if (!string.IsNullOrEmpty(link.QuestionNodeTextDisplay) && link.QuestionNodeTextDisplay != "ShowOnce")
            extras.Add(link.QuestionNodeTextDisplay);
        var arrow  = $"{Loc.Get("Link_Arrow")} {link.ToNodeId}";
        var detail = extras.Count == 0 ? Loc.Get("NodeDetail_None") : $"[{string.Join(", ", extras)}]";
        return new LinkRow(arrow, detail);
    }
}
