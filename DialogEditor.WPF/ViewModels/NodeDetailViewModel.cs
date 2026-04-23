using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;

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

    // Comments (PoE1)
    [ObservableProperty] private string _comments = string.Empty;
    [ObservableProperty] private bool _hasComments;

    // Voice (PoE2)
    [ObservableProperty] private bool _hasVoiceSection;
    [ObservableProperty] private string _externalVO = string.Empty;
    [ObservableProperty] private bool _hasVO;
    [ObservableProperty] private bool _hideSpeaker;

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
            ? string.Join(", ", node.Links.Select(FormatLink))
            : "(none)";
        ScriptsText = node.Scripts.Count > 0
            ? string.Join(Environment.NewLine, node.Scripts)
            : "(none)";
        Comments = node.Comments;
        HasComments = !string.IsNullOrEmpty(node.Comments);
        ExternalVO = node.ExternalVO;
        HasVO = node.HasVO;
        HideSpeaker = node.HideSpeaker;
        HasVoiceSection = node.HasVO || !string.IsNullOrEmpty(node.ExternalVO) || node.HideSpeaker;
        HasContent = true;
    }

    private static string FormatLink(NodeLink link)
    {
        var extras = new List<string>();
        if (link.RandomWeight != 1f)
            extras.Add($"w:{link.RandomWeight:0.##}");
        if (!string.IsNullOrEmpty(link.QuestionNodeTextDisplay) && link.QuestionNodeTextDisplay != "ShowOnce")
            extras.Add(link.QuestionNodeTextDisplay);
        var arrow = $"→ {link.ToNodeId}";
        return extras.Count == 0 ? arrow : $"{arrow} [{string.Join(", ", extras)}]";
    }

    public void Clear() => HasContent = false;
}
