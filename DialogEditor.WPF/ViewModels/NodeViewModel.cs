using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;
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
    public IReadOnlyList<string> ConditionStrings { get; }
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
        SpeakerName = resolved == "Unknown"
            ? node.SpeakerCategory switch
            {
                SpeakerCategory.Player   => "Player",
                SpeakerCategory.Narrator => "Narrator",
                SpeakerCategory.Script   => "Script",
                _                        => "Unknown"
            }
            : resolved;
        ListenerName = SpeakerNameService.Resolve(node.ListenerGuid);
        ConditionStrings = node.ConditionStrings;
        Scripts = node.Scripts;
        DisplayType = node.DisplayType;
        Persistence = node.Persistence;
        Links = node.Links;

        var suffix = node.IsPlayerChoice ? " \u2746" : string.Empty;
        Title = $"Node {node.NodeId} \u00b7 {SpeakerName}{suffix}";

        DefaultText = entry?.DefaultText ?? "[text unavailable \u2014 stringtable not found]";
        FemaleText = entry?.FemaleText ?? string.Empty;
        HasFemaleText = !string.IsNullOrEmpty(FemaleText);
        TextPreview = DefaultText.Length > 80 ? DefaultText[..80] + "\u2026" : DefaultText;

        var condPart = node.ConditionStrings.Count > 0
            ? $"\u2699 {node.ConditionStrings.Count} condition{(node.ConditionStrings.Count == 1 ? "" : "s")}"
            : "[ No conditions ]";
        FooterText = HasFemaleText ? condPart + "  \u2640" : condPart;

        Inputs = [Input];
        Outputs = [Output];
    }
}
