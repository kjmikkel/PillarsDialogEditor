using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class ConversationViewModelConnectModeTests
{
    public ConversationViewModelConnectModeTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    private static ConversationViewModel MakeVm() =>
        new(new StubDispatcher()) { IsEditable = true };

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    [Fact]
    public void BeginConnect_SetsConnectingStateAndSelectsSource()
    {
        var vm = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new LayoutPoint(0, 0));

        var result = vm.BeginConnect(node);

        Assert.True(result);
        Assert.True(vm.IsConnecting);
        Assert.Same(node, vm.ConnectionSource);
        Assert.True(node.IsConnectionSource);
        Assert.Same(node, vm.SelectedNode);
    }

    [Fact]
    public void BeginConnect_RaisesStartedEvent()
    {
        var vm = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new LayoutPoint(0, 0));

        ConnectModeEventArgs? raised = null;
        vm.ConnectModeChanged += (_, e) => raised = e;

        vm.BeginConnect(node);

        Assert.NotNull(raised);
        Assert.Equal(ConnectModeChange.Started, raised!.Change);
        Assert.Same(node, raised.Source);
        Assert.Null(raised.Target);
    }

    [Fact]
    public void BeginConnect_NotEditable_ReturnsFalseAndDoesNotEnterConnectMode()
    {
        var vm = MakeVm();
        vm.IsEditable = false;
        var node = MakeNode(1);
        vm.Nodes.Add(node);

        var result = vm.BeginConnect(node);

        Assert.False(result);
        Assert.False(vm.IsConnecting);
        Assert.Null(vm.ConnectionSource);
    }

    [Fact]
    public void BeginConnect_AlreadyConnecting_ReturnsFalseAndKeepsOriginalSource()
    {
        var vm = MakeVm();
        var first = MakeNode(1);
        var second = MakeNode(2);
        vm.AddNode(first, new LayoutPoint(0, 0));
        vm.AddNode(second, new LayoutPoint(200, 0));
        vm.BeginConnect(first);

        var result = vm.BeginConnect(second);

        Assert.False(result);
        Assert.Same(first, vm.ConnectionSource);
        Assert.False(second.IsConnectionSource);
    }

    [Fact]
    public void TryBeginConnect_NoSelection_ReturnsFalse()
    {
        var vm = MakeVm();

        Assert.False(vm.TryBeginConnect());
        Assert.False(vm.IsConnecting);
    }

    [Fact]
    public void TryBeginConnect_DelegatesToSelectedNode()
    {
        var vm = MakeVm();
        var node = MakeNode(1);
        vm.AddNode(node, new LayoutPoint(0, 0));
        vm.SelectNode(node);

        var result = vm.TryBeginConnect();

        Assert.True(result);
        Assert.Same(node, vm.ConnectionSource);
    }

    [Fact]
    public void TryConfirmConnection_ValidTarget_CreatesConnectionAndExitsConnectMode()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        var target = MakeNode(2);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.AddNode(target, new LayoutPoint(200, 0));
        vm.BeginConnect(source);
        vm.SelectNode(target);

        var result = vm.TryConfirmConnection();

        Assert.True(result);
        Assert.False(vm.IsConnecting);
        Assert.Null(vm.ConnectionSource);
        Assert.False(source.IsConnectionSource);
        Assert.Contains(vm.Connections, c => c.Source == source.Output && c.Target == target.Input);
    }

    [Fact]
    public void TryConfirmConnection_ValidTarget_RaisesConnectedEvent()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        var target = MakeNode(2);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.AddNode(target, new LayoutPoint(200, 0));
        vm.BeginConnect(source);
        vm.SelectNode(target);

        ConnectModeEventArgs? raised = null;
        vm.ConnectModeChanged += (_, e) => raised = e;

        vm.TryConfirmConnection();

        Assert.NotNull(raised);
        Assert.Equal(ConnectModeChange.Connected, raised!.Change);
        Assert.Same(source, raised.Source);
        Assert.Same(target, raised.Target);
    }

    [Fact]
    public void TryConfirmConnection_SelfTarget_NoOpStaysConnecting()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.BeginConnect(source); // BeginConnect also selects the source node

        var raised = false;
        vm.ConnectModeChanged += (_, _) => raised = true;

        var result = vm.TryConfirmConnection();

        Assert.True(result);
        Assert.True(vm.IsConnecting);
        Assert.Same(source, vm.ConnectionSource);
        Assert.Empty(vm.Connections);
        Assert.False(raised);
    }

    [Fact]
    public void TryConfirmConnection_DuplicateTarget_NoOpStaysConnecting()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        var target = MakeNode(2);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.AddNode(target, new LayoutPoint(200, 0));
        vm.AddConnection(source.Output, target.Input);
        vm.BeginConnect(source);
        vm.SelectNode(target);

        var raised = false;
        vm.ConnectModeChanged += (_, _) => raised = true;

        var result = vm.TryConfirmConnection();

        Assert.True(result);
        Assert.True(vm.IsConnecting);
        Assert.Single(vm.Connections);
        Assert.False(raised);
    }

    [Fact]
    public void CancelConnect_ClearsStateAndRaisesCancelledEvent()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.BeginConnect(source);

        ConnectModeEventArgs? raised = null;
        vm.ConnectModeChanged += (_, e) => raised = e;

        var result = vm.CancelConnect();

        Assert.True(result);
        Assert.False(vm.IsConnecting);
        Assert.Null(vm.ConnectionSource);
        Assert.False(source.IsConnectionSource);
        Assert.NotNull(raised);
        Assert.Equal(ConnectModeChange.Cancelled, raised!.Change);
        Assert.Same(source, raised.Source);
        Assert.Null(raised.Target);
    }

    [Fact]
    public void DeleteNode_OnConnectionSource_CancelsConnectModeFirst()
    {
        var vm = MakeVm();
        var source = MakeNode(1);
        vm.AddNode(source, new LayoutPoint(0, 0));
        vm.BeginConnect(source);

        vm.DeleteNode(source);

        Assert.False(vm.IsConnecting);
        Assert.Null(vm.ConnectionSource);
        Assert.DoesNotContain(source, vm.Nodes);
    }
}
