using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Controls;

/// <summary>
/// A <see cref="TreeView"/> whose rows are programmatically selectable and expandable through
/// UI Automation.
///
/// WHY THIS EXISTS — Avalonia 11.3's <c>TreeViewItemAutomationPeer</c> overrides only
/// <c>GetAutomationControlTypeCore()</c>; it implements no provider interfaces at all. So a
/// tree row exposes neither <c>ISelectionItemProvider</c> nor <c>IExpandCollapseProvider</c>,
/// and a screen reader or automation client cannot select or expand it — only synthetically
/// click its rectangle. By contrast <c>ListItemAutomationPeer</c> (used by <c>TabItem</c>)
/// does implement <c>ISelectionItemProvider</c>, which is why tabs are operable and tree rows
/// are not. Measured with <c>TryGetCurrentPattern</c>, not inferred.
///
/// This is an upstream framework gap that affects every Avalonia application. A report is
/// drafted and parked in
/// <see href="https://github.com/kjmikkel/PillarsDialogEditor/issues/17">issue #17</see> — not
/// yet filed with Avalonia. These three types are a local workaround and should be DELETED
/// once Avalonia ships selection and expand/collapse providers for <c>TreeViewItem</c>. See
/// <c>docs/uia-fallback-inventory.md</c> and issue #15 finding 1.
///
/// Only <see cref="CreateContainerForItemOverride"/> needs overriding here: Avalonia's
/// <c>TreeViewItem.CreateContainerForItemOverride</c> delegates back to its owning
/// <c>TreeView</c>, so containers at every depth come from this one method.
/// </summary>
public class ConversationTreeView : TreeView
{
    /// <summary>
    /// Look up the built-in <see cref="TreeView"/> theme rather than one keyed on this
    /// subclass. A templated Avalonia control resolves its ControlTheme by style key, which
    /// defaults to the concrete type — so without this the subclass matches no theme, gets no
    /// template, and renders nothing at all. (Found exactly that way: the tree went from 37
    /// rows to zero UIA elements.)
    /// </summary>
    protected override Type StyleKeyOverride => typeof(TreeView);

    // `protected`, not `protected internal`: the base member's internal half is scoped to
    // Avalonia's own assembly, so from here it is reachable only as protected (CS0507).
    protected override Control CreateContainerForItemOverride(
        object? item, int index, object? recycleKey) => new ConversationTreeViewItem();
}

/// <summary>
/// A <see cref="TreeViewItem"/> that supplies an automation peer able to select and expand it.
/// </summary>
public class ConversationTreeViewItem : TreeViewItem
{
    /// <summary>See <see cref="ConversationTreeView.StyleKeyOverride"/> — same reason.</summary>
    protected override Type StyleKeyOverride => typeof(TreeViewItem);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new ConversationTreeViewItemAutomationPeer(this);
}

/// <summary>
/// Adds <see cref="ISelectionItemProvider"/> and <see cref="IExpandCollapseProvider"/> to
/// Avalonia's tree-item peer. Modelled directly on Avalonia's own
/// <c>ListItemAutomationPeer</c>, which is the shape the framework already proves works
/// through the Win32 UIA bridge.
/// </summary>
public class ConversationTreeViewItemAutomationPeer : TreeViewItemAutomationPeer,
    ISelectionItemProvider, IExpandCollapseProvider
{
    public ConversationTreeViewItemAutomationPeer(TreeViewItem owner) : base(owner) { }

    private TreeViewItem Item => (TreeViewItem)Owner;

    // ── ISelectionItemProvider ────────────────────────────────────────────────
    public bool IsSelected => Item.IsSelected;

    public ISelectionProvider? SelectionContainer =>
        Item.Parent is Control parent ? GetOrCreate(parent).GetProvider<ISelectionProvider>() : null;

    public void Select()
    {
        EnsureEnabled();
        Item.IsSelected = true;
    }

    // A TreeView here is single-select, so adding to the selection IS selecting, and removing
    // is a plain deselect. Implemented rather than thrown so a client is not surprised.
    void ISelectionItemProvider.AddToSelection()
    {
        EnsureEnabled();
        Item.IsSelected = true;
    }

    void ISelectionItemProvider.RemoveFromSelection()
    {
        EnsureEnabled();
        Item.IsSelected = false;
    }

    // ── IExpandCollapseProvider ───────────────────────────────────────────────
    public ExpandCollapseState ExpandCollapseState => Item.ItemCount == 0
        ? ExpandCollapseState.LeafNode
        : Item.IsExpanded ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <summary>False: expanding a tree row reveals child rows, not a menu (macOS only).</summary>
    public bool ShowsMenu => false;

    public void Expand()
    {
        EnsureEnabled();
        Item.IsExpanded = true;
    }

    public void Collapse()
    {
        EnsureEnabled();
        Item.IsExpanded = false;
    }
}
