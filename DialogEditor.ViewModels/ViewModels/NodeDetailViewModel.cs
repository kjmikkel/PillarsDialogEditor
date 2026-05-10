using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasContent;
    [ObservableProperty] private string _defaultText = string.Empty;
    [ObservableProperty] private string _femaleTextDisplay = string.Empty;
    [ObservableProperty] private bool _hasFemaleText;
    [ObservableProperty] private IReadOnlyList<PropertyGroup> _propertyGroups = [];
    [ObservableProperty] private IReadOnlyList<LinkRow> _links = [];

    public void Load(NodeViewModel? node)
    {
        if (node is null) { HasContent = false; return; }

        DefaultText = node.DefaultText;
        HasFemaleText = !string.IsNullOrEmpty(node.FemaleText);
        FemaleTextDisplay = HasFemaleText ? node.FemaleText : Loc.Get("NodeDetail_SameAsDefault");

        var none = Loc.Get("NodeDetail_None");

        PropertyGroups =
        [
            new PropertyGroup(Loc.Get("Label_GroupIdentity"),
            [
                new PropertyRow(Loc.Get("PropertyRow_NodeId"),      node.NodeId.ToString()),
                new PropertyRow(Loc.Get("PropertyRow_Type"),        node.IsPlayerChoice ? Loc.Get("NodeDetail_PlayerChoice") : Loc.Get("NodeDetail_NpcLine")),
                new PropertyRow(Loc.Get("PropertyRow_Speaker"),     node.SpeakerName),
                new PropertyRow(Loc.Get("PropertyRow_SpeakerGuid"), node.SpeakerGuid, PropertyValueStyle.Code),
                new PropertyRow(Loc.Get("PropertyRow_Listener"),    node.ListenerName),
            ]),
            new PropertyGroup(Loc.Get("Label_GroupDisplay"),
            [
                new PropertyRow(Loc.Get("PropertyRow_DisplayType"),    node.DisplayType),
                new PropertyRow(Loc.Get("PropertyRow_Persistence"),    node.Persistence),
                new PropertyRow(Loc.Get("PropertyRow_ActorDirection"), string.IsNullOrEmpty(node.ActorDirection) ? none : node.ActorDirection),
            ]),
            new PropertyGroup(Loc.Get("Label_GroupLogic"),
            [
                new PropertyRow(Loc.Get("PropertyRow_Conditions"), string.IsNullOrEmpty(node.ConditionExpression) ? none : node.ConditionExpression, PropertyValueStyle.Condition),
                new PropertyRow(Loc.Get("PropertyRow_Scripts"),    node.Scripts.Count == 0 ? none : string.Join(Environment.NewLine, node.Scripts), PropertyValueStyle.Script),
                new PropertyRow(Loc.Get("PropertyRow_Comments"),   string.IsNullOrEmpty(node.Comments) ? none : node.Comments),
            ]),
            new PropertyGroup(Loc.Get("Label_GroupVoice"),
            [
                new PropertyRow(Loc.Get("PropertyRow_ExternalVO"),  string.IsNullOrEmpty(node.ExternalVO) ? none : node.ExternalVO, PropertyValueStyle.Code),
                new PropertyRow(Loc.Get("PropertyRow_HasVO"),       node.HasVO.ToString()),
                new PropertyRow(Loc.Get("PropertyRow_HideSpeaker"), node.HideSpeaker.ToString()),
            ]),
        ];

        Links = node.Links.Select(BuildLinkRow).ToList();
        HasContent = true;
    }

    public void Clear() => HasContent = false;

    private static LinkRow BuildLinkRow(NodeLink link)
    {
        var extras = new List<string>();
        if (link.RandomWeight != 1f)
            extras.Add($"{Loc.Get("Link_WeightPrefix")}{link.RandomWeight:0.##}");
        if (!string.IsNullOrEmpty(link.QuestionNodeTextDisplay) && link.QuestionNodeTextDisplay != "ShowOnce")
            extras.Add(link.QuestionNodeTextDisplay);
        var arrow = $"{Loc.Get("Link_Arrow")} {link.ToNodeId}";
        var detail = extras.Count == 0 ? Loc.Get("NodeDetail_None") : $"[{string.Join(", ", extras)}]";
        return new LinkRow(arrow, detail);
    }
}
