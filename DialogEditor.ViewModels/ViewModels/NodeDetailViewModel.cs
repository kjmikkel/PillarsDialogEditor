using System.Collections.ObjectModel;
using System.ComponentModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Models;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    private NodeViewModel? _node;
    public  NodeViewModel? Node => _node;

    /// Set by MainWindowViewModel when a game folder is opened.
    public string ActiveGameId { get; set; } = string.Empty;

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

    // ── SpeakerCategory proxy (enum ↔ string for ComboBox binding) ───────
    public string SpeakerCategoryString
    {
        get => _node?.SpeakerCategory switch
        {
            SpeakerCategory.Player   => Loc.Get("Speaker_Player"),
            SpeakerCategory.Narrator => Loc.Get("Speaker_Narrator"),
            SpeakerCategory.Script   => Loc.Get("Speaker_Script"),
            _                        => Loc.Get("Speaker_Npc"),
        };
        set
        {
            if (_node is null) return;
            _node.SpeakerCategory =
                value == Loc.Get("Speaker_Player")   ? SpeakerCategory.Player   :
                value == Loc.Get("Speaker_Narrator") ? SpeakerCategory.Narrator :
                value == Loc.Get("Speaker_Script")   ? SpeakerCategory.Script   :
                                                        SpeakerCategory.Npc;
        }
    }

    public static IReadOnlyList<string> SpeakerCategoryOptions =>
    [
        Loc.Get("Speaker_Npc"),
        Loc.Get("Speaker_Player"),
        Loc.Get("Speaker_Narrator"),
        Loc.Get("Speaker_Script"),
    ];

    // ── NodeType proxy (bool ↔ string for ComboBox binding) ──────────────
    public string NodeTypeString
    {
        get => IsPlayerChoice ? Loc.Get("Option_PlayerChoice") : Loc.Get("Option_NpcLine");
        set { if (_node != null) _node.IsPlayerChoice = value == Loc.Get("Option_PlayerChoice"); }
    }

    public static IReadOnlyList<string> NodeTypeOptions
        => [Loc.Get("Option_NpcLine"), Loc.Get("Option_PlayerChoice")];

    public static IReadOnlyList<string> DisplayTypeOptions
        => [Loc.Get("Option_DisplayConversation"), Loc.Get("Option_DisplayBark")];

    public static IReadOnlyList<string> PersistenceOptions
        => [Loc.Get("Option_PersistenceNone"), Loc.Get("Option_PersistenceOnceEver")];

    // ── Read-only display ─────────────────────────────────────────────────
    public string FemaleTextDisplay =>
        (_node?.HasFemaleText ?? false) ? _node!.FemaleText : Loc.Get("NodeDetail_SameAsDefault");

    public bool HasFemaleText => _node?.HasFemaleText ?? false;

    [ObservableProperty] private IReadOnlyList<PropertyGroup>      _propertyGroups  = [];
    [ObservableProperty] private IReadOnlyList<ConnectionViewModel> _links           = [];
    [ObservableProperty] private string                             _addLinkTargetId = string.Empty;

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
    private void DeleteLink(ConnectionViewModel? conn)
    {
        if (conn?.Target.Owner is { } target)
            DeleteLinkRequested?.Invoke(target.NodeId);
    }

    // ── Load / Clear ──────────────────────────────────────────────────────
    public void Load(NodeViewModel? node)
    {
        if (_node is not null)
            _node.PropertyChanged -= OnNodePropertyChanged;

        _node = node;

        if (node is null) { HasContent = false; ConditionRows.Clear(); return; }

        node.PropertyChanged += OnNodePropertyChanged;
        RefreshReadOnlyGroups(node);
        RebuildConditionRows(node);
        HasContent = true;
        NotifyAllProxies();
    }

    public void Clear() => Load(null);

    private void OnNodePropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        NotifyAllProxies();
        if (_node is not null)
            RefreshReadOnlyGroups(_node);
        if (e.PropertyName == nameof(NodeViewModel.Conditions))
            OnPropertyChanged(nameof(ConditionSummary));
        if (e.PropertyName == nameof(NodeViewModel.Scripts))
        {
            if (_node is not null) RefreshReadOnlyGroups(_node);
            OnPropertyChanged(nameof(ScriptSummary));
        }
    }

    private void NotifyAllProxies()
    {
        OnPropertyChanged(nameof(DefaultText));
        OnPropertyChanged(nameof(FemaleText));
        OnPropertyChanged(nameof(SpeakerCategoryString));
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

        // Keep AutoCompleteBox selections in sync whenever any proxy changes
        _selectedSpeakerEntry  = SpeakerNameService.FindByGuid(_node?.SpeakerGuid);
        _selectedListenerEntry = SpeakerNameService.FindByGuid(_node?.ListenerGuid);
        OnPropertyChanged(nameof(SelectedSpeakerEntry));
        OnPropertyChanged(nameof(SelectedListenerEntry));
    }

    // ── Speaker / Listener name picker ───────────────────────────────────

    /// True when the loaded game has speaker name data (PoE2); hides picker for PoE1.
    public bool HasSpeakerData => SpeakerNameService.HasNames;

    /// All known speakers from the loaded game, sorted by name.
    public IReadOnlyList<SpeakerEntry> AvailableSpeakers => SpeakerNameService.All;

    private SpeakerEntry? _selectedSpeakerEntry;
    private SpeakerEntry? _selectedListenerEntry;

    public SpeakerEntry? SelectedSpeakerEntry
    {
        get => _selectedSpeakerEntry;
        set
        {
            if (SetProperty(ref _selectedSpeakerEntry, value) && value is not null)
                SpeakerGuid = value.Guid;
        }
    }

    public SpeakerEntry? SelectedListenerEntry
    {
        get => _selectedListenerEntry;
        set
        {
            if (SetProperty(ref _selectedListenerEntry, value) && value is not null)
                ListenerGuid = value.Guid;
        }
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
            // Scripts and Conditions each have their own editable panel sections.
        ];
    }

    public void RefreshLinks(IEnumerable<ConnectionViewModel> connections)
        => Links = connections.ToList();

    public void NotifyConditionSummary()
        => OnPropertyChanged(nameof(ConditionSummary));

    public void NotifyScriptSummary()
        => OnPropertyChanged(nameof(ScriptSummary));

    public string ScriptSummary
    {
        get
        {
            if (_node is null || _node.Scripts.Count == 0)
                return Loc.Get("NodeDetail_None");
            return string.Join(", ", _node.Scripts.Select(s => s.DisplayName));
        }
    }

    // ── Condition editing ─────────────────────────────────────────────────

    public ObservableCollection<ConditionRowViewModel> ConditionRows { get; } = [];

    /// Brief summary shown in the detail panel (replaces the old inline editor).
    public string ConditionSummary
    {
        get
        {
            if (_node is null || _node.Conditions.Count == 0)
                return Loc.Get("NodeDetail_None");
            var names = _node.Conditions
                .OfType<ConditionLeaf>()
                .Select(l =>
                {
                    var mn = l.FullName.Contains(' ')
                        ? l.FullName[(l.FullName.IndexOf(' ') + 1)..].Split('(')[0]
                        : l.FullName;
                    return ConditionCatalogue.Instance.Find(mn)?.DisplayName ?? mn;
                });
            return string.Join(", ", names);
        }
    }


    private void RebuildConditionRows(NodeViewModel node)
    {
        ConditionRows.Clear();
        foreach (var c in node.Conditions)
        {
            if (c is ConditionLeaf leaf)
            {
                var entry = ConditionCatalogue.Instance.Find(
                    leaf.FullName.Contains(' ')
                        ? leaf.FullName[(leaf.FullName.IndexOf(' ') + 1)..]
                            .Split('(')[0]
                        : leaf.FullName);
                var row = new ConditionRowViewModel(leaf, entry);
                SubscribeRow(row);
                ConditionRows.Add(row);
            }
            else if (c is ConditionBranch branch)
            {
                // Branch rows are read-only pass-throughs — no subscription needed
                ConditionRows.Add(new ConditionRowViewModel(branch));
            }
        }
    }

    private void SubscribeRow(ConditionRowViewModel row)
    {
        row.PropertyChanged += OnRowPropertyChanged;
        foreach (var p in row.Parameters)
            p.PropertyChanged += OnRowPropertyChanged;
    }

    private void OnRowPropertyChanged(object? sender, PropertyChangedEventArgs e)
        => CommitConditions();

    public void AddCondition(ConditionEntry entry)
    {
        if (_node is null) return;
        var defaults = entry.Parameters.Select(p => p.Default).ToList();
        var leaf     = new ConditionLeaf(entry.ReflectionFullName, defaults, false, "And");
        var row      = new ConditionRowViewModel(leaf, entry);
        SubscribeRow(row);
        ConditionRows.Add(row);
        CommitConditions();
    }

    public void DeleteConditionRow(ConditionRowViewModel row)
    {
        if (ConditionRows.Remove(row))
            CommitConditions();
    }

    public void MoveConditionUp(ConditionRowViewModel row)
    {
        var i = ConditionRows.IndexOf(row);
        if (i > 0) { ConditionRows.Move(i, i - 1); CommitConditions(); }
    }

    public void MoveConditionDown(ConditionRowViewModel row)
    {
        var i = ConditionRows.IndexOf(row);
        if (i >= 0 && i < ConditionRows.Count - 1) { ConditionRows.Move(i, i + 1); CommitConditions(); }
    }

    /// Builds the structured condition list from current rows and pushes it to NodeViewModel.
    public void CommitConditions()
    {
        if (_node is null) return;
        _node.Conditions = ConditionRows.Select(r => r.ToNode()).ToList();
    }
}
