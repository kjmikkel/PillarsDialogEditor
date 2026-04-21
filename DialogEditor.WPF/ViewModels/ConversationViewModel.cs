using System.Collections.ObjectModel;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;

namespace DialogEditor.WPF.ViewModels;

public partial class ConversationViewModel : ObservableObject
{
    public ObservableCollection<NodeViewModel> Nodes { get; } = [];
    public ObservableCollection<ConnectionViewModel> Connections { get; } = [];

    [ObservableProperty]
    private NodeViewModel? _selectedNode;

    partial void OnSelectedNodeChanged(NodeViewModel? value)
    {
        foreach (var connection in Connections)
        {
            connection.IsHighlighted = value is not null &&
                (connection.Source == value.Output || connection.Target == value.Input);
        }
    }

    public void Load(Conversation conversation)
    {
        Nodes.Clear();
        Connections.Clear();
        SelectedNode = null;

        var nodeMap = new Dictionary<int, NodeViewModel>();

        foreach (var node in conversation.Nodes)
        {
            var entry = conversation.Strings.Get(node.NodeId);
            var vm = new NodeViewModel(node, entry);
            vm.OnSelected = n => SelectedNode = n;
            nodeMap[node.NodeId] = vm;
            Nodes.Add(vm);
        }

        AutoLayoutService.Apply(conversation.Nodes, (id, x, y) =>
        {
            if (nodeMap.TryGetValue(id, out var vm))
                vm.Location = new Point(x, y);
        });

        foreach (var node in conversation.Nodes)
        {
            foreach (var link in node.Links)
            {
                if (nodeMap.TryGetValue(link.FromNodeId, out var src) &&
                    nodeMap.TryGetValue(link.ToNodeId, out var tgt))
                {
                    Connections.Add(new ConnectionViewModel(src.Output, tgt.Input));
                }
            }
        }
    }
}
