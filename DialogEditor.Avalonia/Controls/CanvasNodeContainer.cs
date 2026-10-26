using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Nodify;

namespace DialogEditor.Avalonia.Controls;

/// <summary>
/// A Nodify <see cref="ItemContainer"/> that exposes itself to UI Automation as a list item.
///
/// WHY THIS EXISTS (GitHub issue 311) — Nodify's <c>ItemContainer</c> overrides no
/// <c>OnCreateAutomationPeer</c>, so Avalonia gives it the default <c>NoneAutomationPeer</c>
/// and the node drops out of the UIA tree: its header text hangs straight off the canvas,
/// UIA hit-testing at a node returns the whole canvas, <c>GetClickablePoint()</c> fails and a
/// script or screen reader cannot select a node. With a peer the node is a named, selectable
/// <c>ListItem</c>. Created by <see cref="CanvasEditor"/>; delete both if Nodify ever ships a peer.
/// </summary>
public class CanvasNodeContainer : ItemContainer
{
    public CanvasNodeContainer(NodifyEditor editor) : base(editor) { }

    /// <summary>Use the stock ItemContainer theme (and our ItemContainerTheme based on it).</summary>
    protected override Type StyleKeyOverride => typeof(ItemContainer);

    protected override AutomationPeer OnCreateAutomationPeer() => new CanvasNodeAutomationPeer(this);
}

/// <summary>Selection-capable list-item peer for <see cref="CanvasNodeContainer"/>.</summary>
public class CanvasNodeAutomationPeer : ControlAutomationPeer, ISelectionItemProvider
{
    public CanvasNodeAutomationPeer(CanvasNodeContainer owner) : base(owner)
    {
        owner.PropertyChanged += (_, e) =>
        {
            if (e.Property == ItemContainer.IsSelectedProperty)
                RaisePropertyChangedEvent(
                    SelectionItemPatternIdentifiers.IsSelectedProperty,
                    e.OldValue, e.NewValue);
        };
    }

    private CanvasNodeContainer Container => (CanvasNodeContainer)Owner;

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.ListItem;

    protected override bool IsContentElementCore() => true;

    protected override bool IsControlElementCore() => true;

    public bool IsSelected => Container.IsSelected;

    public ISelectionProvider? SelectionContainer =>
        Container.Editor is Control editor ? GetOrCreate(editor).GetProvider<ISelectionProvider>() : null;

    public void Select()
    {
        EnsureEnabled();
        Container.Editor?.UnselectAll();
        Container.IsSelected = true;
    }

    public void AddToSelection()
    {
        EnsureEnabled();
        Container.IsSelected = true;
    }

    public void RemoveFromSelection()
    {
        EnsureEnabled();
        Container.IsSelected = false;
    }
}
