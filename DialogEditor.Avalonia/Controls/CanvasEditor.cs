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
/// The editor's peer: an items control, so screen readers announce the canvas as a list of
/// nodes. Nodify's template puts several peer-less layers (zoom/pan decorators, panels)
/// between it and the node peers; those are <c>NoneAutomationPeer</c>s, which the UIA control
/// view skips, so the nodes still read as the canvas's items.
///
/// Do NOT flatten those layers by overriding <c>GetChildrenCore</c> (tried for issue 311): a
/// peer's parent is whichever ancestor last listed it, and once a node is added the
/// <c>NodifyCanvas</c> layer re-lists and re-parents the nodes. The Win32 bridge walks
/// siblings through <c>GetParent().GetChildren()</c>, so the tree split and no node was
/// reachable. <c>CanvasNodeAutomationTests</c> walks the tree the way the bridge does.
/// </summary>
public class CanvasEditorAutomationPeer : ItemsControlAutomationPeer
{
    public CanvasEditorAutomationPeer(CanvasEditor owner) : base(owner) { }
}
