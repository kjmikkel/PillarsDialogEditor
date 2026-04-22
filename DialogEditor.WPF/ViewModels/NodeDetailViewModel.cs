using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.WPF.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasContent;
    [ObservableProperty] private int _nodeId;
    [ObservableProperty] private string _nodeType = string.Empty;
    [ObservableProperty] private string _speakerName = string.Empty;
    [ObservableProperty] private string _speakerGuid = string.Empty;
    [ObservableProperty] private string _listenerName = string.Empty;
    [ObservableProperty] private string _defaultText = string.Empty;
    [ObservableProperty] private string _femaleText = string.Empty;
    [ObservableProperty] private string _femaleTextDisplay = string.Empty;
    [ObservableProperty] private bool _hasFemaleText;
    [ObservableProperty] private string _conditionsText = string.Empty;
    [ObservableProperty] private string _displayType = string.Empty;
    [ObservableProperty] private string _persistence = string.Empty;
    [ObservableProperty] private string _actorDirection = string.Empty;
    [ObservableProperty] private bool _hasActorDirection;
    [ObservableProperty] private string _linksTo = string.Empty;
    [ObservableProperty] private string _scriptsText = string.Empty;

    public void Load(NodeViewModel? node)
    {
        if (node is null) { HasContent = false; return; }

        NodeId = node.NodeId;
        NodeType = node.IsPlayerChoice ? "Player Choice" : "NPC Line";
        SpeakerName = node.SpeakerName;
        SpeakerGuid = node.SpeakerGuid;
        ListenerName = node.ListenerName;
        DefaultText = node.DefaultText;
        FemaleText = node.FemaleText;
        HasFemaleText = !string.IsNullOrEmpty(node.FemaleText);
        FemaleTextDisplay = HasFemaleText ? node.FemaleText : "(same as default)";
        ConditionsText = node.ConditionStrings.Count > 0
            ? string.Join(Environment.NewLine, node.ConditionStrings)
            : "(none)";
        DisplayType = node.DisplayType;
        Persistence = node.Persistence;
        ActorDirection = node.ActorDirection;
        HasActorDirection = !string.IsNullOrEmpty(node.ActorDirection);
        LinksTo = node.Links.Count > 0
            ? string.Join(", ", node.Links.Select(l => $"\u2192 {l.ToNodeId}"))
            : "(none)";
        ScriptsText = node.Scripts.Count > 0
            ? string.Join(Environment.NewLine, node.Scripts)
            : "(none)";
        HasContent = true;
    }

    public void Clear() => HasContent = false;
}
