using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.WPF.Resources;
using DialogEditor.WPF.Services;

namespace DialogEditor.WPF.ViewModels;

public partial class NodeViewModel : ObservableObject
{
    public int NodeId { get; }
    public bool IsPlayerChoice { get; }
    public SpeakerCategory SpeakerCategory { get; }
    public string SpeakerGuid { get; }
    public string ListenerGuid { get; }
    public string SpeakerName { get; }
    public string ListenerName { get; }
    public string Title { get; }
    public string TextPreview { get; }
    public string DefaultText { get; }
    public string FemaleText { get; }
    public bool HasFemaleText { get; }
    public string FooterText { get; }
    public string DisplayType { get; }
    public string Persistence { get; }
    public string ActorDirection { get; }
    public string Comments { get; }
    public string ExternalVO { get; }
    public bool HasVO { get; }
    public bool HideSpeaker { get; }
    public IReadOnlyList<string> ConditionStrings { get; }
    public string ConditionExpression { get; }
    public IReadOnlyList<string> Scripts { get; }
    public IReadOnlyList<NodeLink> Links { get; }

    public ConnectorViewModel Input { get; } = new();
    public ConnectorViewModel Output { get; } = new();
    public IReadOnlyList<ConnectorViewModel> Inputs { get; }
    public IReadOnlyList<ConnectorViewModel> Outputs { get; }

    [ObservableProperty]
    private Point _location;

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isSearchMatch = true;

    internal Action<NodeViewModel>? OnSelected { get; set; }

    partial void OnIsSelectedChanged(bool value)
    {
        if (value) OnSelected?.Invoke(this);
    }

    public NodeViewModel(ConversationNode node, StringEntry? entry)
    {
        NodeId = node.NodeId;
        IsPlayerChoice = node.IsPlayerChoice;
        SpeakerCategory = node.SpeakerCategory;
        SpeakerGuid = node.SpeakerGuid;
        ListenerGuid = node.ListenerGuid;
        var resolved = SpeakerNameService.Resolve(node.SpeakerGuid);
        SpeakerName = resolved ?? node.SpeakerCategory switch
        {
            SpeakerCategory.Player   => Loc.Get("Speaker_Player"),
            SpeakerCategory.Narrator => Loc.Get("Speaker_Narrator"),
            SpeakerCategory.Script   => Loc.Get("Speaker_Script"),
            _                        => Loc.Get("Speaker_Unknown")
        };
        ListenerName = SpeakerNameService.Resolve(node.ListenerGuid) ?? string.Empty;
        ConditionStrings = node.ConditionStrings;
        ConditionExpression = node.ConditionExpression;
        Scripts = node.Scripts;
        DisplayType = node.DisplayType;
        Persistence = node.Persistence;
        ActorDirection = node.ActorDirection;
        Comments = node.Comments;
        ExternalVO = node.ExternalVO;
        HasVO = node.HasVO;
        HideSpeaker = node.HideSpeaker;
        Links = node.Links;

        var suffix = node.IsPlayerChoice ? Loc.Get("Node_PlayerChoiceSuffix") : string.Empty;
        Title = Loc.Format("Node_Title", node.NodeId, SpeakerName, suffix);

        DefaultText = entry?.DefaultText ?? Loc.Get("Node_TextUnavailable");
        FemaleText = entry?.FemaleText ?? string.Empty;
        HasFemaleText = !string.IsNullOrEmpty(FemaleText);
        TextPreview = DefaultText.Length > 80 ? DefaultText[..80] + "…" : DefaultText;

        var count = node.ConditionStrings.Count;
        var condPart = count > 0
            ? Loc.Format(count == 1 ? "Node_ConditionSingular" : "Node_ConditionPlural", count)
            : Loc.Get("Node_NoConditions");
        FooterText = HasFemaleText ? condPart + Loc.Get("Node_FemaleTextSuffix") : condPart;

        Inputs = [Input];
        Outputs = [Output];
    }
}
