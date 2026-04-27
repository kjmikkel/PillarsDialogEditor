using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

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

    // Comments (PoE1)
    [ObservableProperty] private string _comments = string.Empty;
    [ObservableProperty] private bool _hasComments;

    // Voice (PoE1 + PoE2)
    [ObservableProperty] private bool _hasVoiceSection;
    [ObservableProperty] private string _externalVO = string.Empty;
    [ObservableProperty] private bool _hasExternalVO;
    [ObservableProperty] private bool _hasVO;
    [ObservableProperty] private bool _hideSpeaker;

    public void Load(NodeViewModel? node)
    {
        if (node is null) { HasContent = false; return; }

        NodeId = node.NodeId;
        NodeType = node.IsPlayerChoice ? Loc.Get("NodeDetail_PlayerChoice") : Loc.Get("NodeDetail_NpcLine");
        SpeakerName = node.SpeakerName;
        SpeakerGuid = node.SpeakerGuid;
        ListenerName = node.ListenerName;
        DefaultText = node.DefaultText;
        FemaleText = node.FemaleText;
        HasFemaleText = !string.IsNullOrEmpty(node.FemaleText);
        FemaleTextDisplay = HasFemaleText ? node.FemaleText : Loc.Get("NodeDetail_SameAsDefault");
        ConditionsText = !string.IsNullOrEmpty(node.ConditionExpression)
            ? node.ConditionExpression
            : Loc.Get("NodeDetail_None");
        DisplayType = node.DisplayType;
        Persistence = node.Persistence;
        ActorDirection = node.ActorDirection;
        HasActorDirection = !string.IsNullOrEmpty(node.ActorDirection);
        LinksTo = node.Links.Count > 0
            ? string.Join(", ", node.Links.Select(FormatLink))
            : Loc.Get("NodeDetail_None");
        ScriptsText = node.Scripts.Count > 0
            ? string.Join(Environment.NewLine, node.Scripts)
            : Loc.Get("NodeDetail_None");
        Comments = node.Comments;
        HasComments = !string.IsNullOrEmpty(node.Comments);
        ExternalVO = node.ExternalVO;
        HasExternalVO = !string.IsNullOrEmpty(node.ExternalVO);
        HasVO = node.HasVO;
        HideSpeaker = node.HideSpeaker;
        HasVoiceSection = node.HasVO || !string.IsNullOrEmpty(node.ExternalVO) || node.HideSpeaker;
        HasContent = true;
    }

    private static string FormatLink(NodeLink link)
    {
        var extras = new List<string>();
        if (link.RandomWeight != 1f)
            extras.Add($"{Loc.Get("Link_WeightPrefix")}{link.RandomWeight:0.##}");
        if (!string.IsNullOrEmpty(link.QuestionNodeTextDisplay) && link.QuestionNodeTextDisplay != "ShowOnce")
            extras.Add(link.QuestionNodeTextDisplay);
        var arrow = $"{Loc.Get("Link_Arrow")} {link.ToNodeId}";
        return extras.Count == 0 ? arrow : $"{arrow} [{string.Join(", ", extras)}]";
    }

    public void Clear() => HasContent = false;
}
