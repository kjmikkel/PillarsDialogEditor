using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class PendingConnectionViewModelTests
{
    public PendingConnectionViewModelTests() => Loc.Configure(new StubStringProvider());

    private static ConversationViewModel MakeCanvas(bool editable = true)
    {
        var vm = new ConversationViewModel(new StubDispatcher());
        vm.IsEditable = editable;
        return vm;
    }

    private static NodeViewModel MakeNode(int id)
    {
        var node = new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [],
                                        "Conversation", "None");
        return new NodeViewModel(node, new StringEntry(id, "text", ""));
    }

    // ── Start ─────────────────────────────────────────────────────────────

    [Fact]
    public void Start_SetsSource()
    {
        var canvas = MakeCanvas();
        var vm     = new PendingConnectionViewModel(canvas);
        var src    = new ConnectorViewModel();
        vm.StartCommand.Execute(src);
        Assert.Equal(src, vm.Source);
    }

    // ── CanComplete ───────────────────────────────────────────────────────

    [Fact]
    public void CanComplete_FalseWhenCanvasNotEditable()
    {
        var canvas = MakeCanvas(editable: false);
        var vm     = new PendingConnectionViewModel(canvas);
        Assert.False(vm.CompleteCommand.CanExecute(new ConnectorViewModel()));
    }

    [Fact]
    public void CanComplete_TrueWhenCanvasEditable()
    {
        var canvas = MakeCanvas(editable: true);
        var vm     = new PendingConnectionViewModel(canvas);
        Assert.True(vm.CompleteCommand.CanExecute(new ConnectorViewModel()));
    }

    [Fact]
    public void CanComplete_UpdatesWhenEditabilityChanges()
    {
        var canvas = MakeCanvas(editable: false);
        var vm     = new PendingConnectionViewModel(canvas);
        canvas.IsEditable = true;
        Assert.True(vm.CompleteCommand.CanExecute(new ConnectorViewModel()));
    }

    // ── Complete — adds connection ────────────────────────────────────────

    [Fact]
    public void Complete_AddsConnectionToCanvas()
    {
        var canvas = MakeCanvas();
        var n1     = MakeNode(1);
        var n2     = MakeNode(2);
        canvas.AddNode(n1, new(0, 0));
        canvas.AddNode(n2, new(200, 0));

        var vm = new PendingConnectionViewModel(canvas);
        vm.StartCommand.Execute(n1.Output);
        vm.CompleteCommand.Execute(n2.Input);

        Assert.Single(canvas.Connections);
    }

    [Fact]
    public void Complete_ClearsSource()
    {
        var canvas = MakeCanvas();
        var n1     = MakeNode(1);
        var n2     = MakeNode(2);
        canvas.AddNode(n1, new(0, 0));
        canvas.AddNode(n2, new(200, 0));

        var vm = new PendingConnectionViewModel(canvas);
        vm.StartCommand.Execute(n1.Output);
        vm.CompleteCommand.Execute(n2.Input);

        Assert.Null(vm.Source);
    }

    // ── Complete — duplicate prevention ───────────────────────────────────

    [Fact]
    public void Complete_DuplicateConnection_NotAddedTwice()
    {
        var canvas = MakeCanvas();
        var n1     = MakeNode(1);
        var n2     = MakeNode(2);
        canvas.AddNode(n1, new(0, 0));
        canvas.AddNode(n2, new(200, 0));

        var vm = new PendingConnectionViewModel(canvas);

        // First connection
        vm.StartCommand.Execute(n1.Output);
        vm.CompleteCommand.Execute(n2.Input);

        // Attempt duplicate
        vm.StartCommand.Execute(n1.Output);
        vm.CompleteCommand.Execute(n2.Input);

        Assert.Single(canvas.Connections);
    }

    // ── Complete — same source and target ─────────────────────────────────

    [Fact]
    public void Complete_SameSourceAndTarget_ClearsSourceWithoutAdding()
    {
        var canvas    = MakeCanvas();
        var connector = new ConnectorViewModel();

        var vm = new PendingConnectionViewModel(canvas);
        vm.StartCommand.Execute(connector);
        vm.CompleteCommand.Execute(connector);

        Assert.Null(vm.Source);
        Assert.Empty(canvas.Connections);
    }

    // ── Complete — null target ────────────────────────────────────────────

    [Fact]
    public void Complete_NullTarget_ClearsSourceWithoutAdding()
    {
        var canvas = MakeCanvas();
        var n1     = MakeNode(1);
        canvas.AddNode(n1, new(0, 0));

        var vm = new PendingConnectionViewModel(canvas);
        vm.StartCommand.Execute(n1.Output);
        vm.CompleteCommand.Execute(null);

        Assert.Null(vm.Source);
        Assert.Empty(canvas.Connections);
    }
}
