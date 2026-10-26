using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Nodify;

namespace DialogEditor.Avalonia.Controls;

/// <summary>
/// A <see cref="NodifyEditor"/> whose node containers are <see cref="CanvasNodeContainer"/>s,
/// so each canvas node is a real UI Automation element (GitHub issue 311).
/// </summary>
public class CanvasEditor : NodifyEditor
{
    /// <summary>
    /// Keep resolving the stock NodifyEditor theme: a templated control looks its ControlTheme
    /// up by style key, which defaults to the concrete type, so without this the subclass would
    /// match no theme and render nothing (same trap as ConversationTreeView).
    /// </summary>
    protected override Type StyleKeyOverride => typeof(NodifyEditor);

    protected override Control GetContainerForItemOverride() => new CanvasNodeContainer(this);

    protected override AutomationPeer OnCreateAutomationPeer() => new CanvasEditorAutomationPeer(this);
}

/// <summary>
/// The editor's peer. Nodify's template wraps the node containers in several peer-less
/// layers (zoom/pan decorators, panels), which stock <c>ItemsControlAutomationPeer</c> reports
/// as a chain of <c>NoneAutomationPeer</c>s, so the node peers sit deep in the tree. Flatten
/// those layers so the nodes are direct children of the canvas, the way a list's items are.
/// </summary>
public class CanvasEditorAutomationPeer : ItemsControlAutomationPeer
{
    public CanvasEditorAutomationPeer(CanvasEditor owner) : base(owner) { }

    protected override IReadOnlyList<AutomationPeer>? GetChildrenCore()
    {
        var result = new List<AutomationPeer>();
        Flatten(base.GetChildrenCore(), result);
        return result;
    }

    private static void Flatten(IReadOnlyList<AutomationPeer>? peers, List<AutomationPeer> into)
    {
        if (peers is null) return;
        foreach (var peer in peers)
        {
            if (peer is NoneAutomationPeer) Flatten(peer.GetChildren(), into);
            else into.Add(peer);
        }
    }
}
