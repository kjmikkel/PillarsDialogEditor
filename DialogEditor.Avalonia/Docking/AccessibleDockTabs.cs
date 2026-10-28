using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Dock.Avalonia.Controls;

namespace DialogEditor.Avalonia.Docking;

// WHY THESE EXIST (GitHub issue 264, finding F-10) — Dock.Avalonia 11.3.12.1's tab-item peers
// (DocumentTabStripItemAutomationPeer, ToolTabStripItemAutomationPeer; both internal sealed)
// override GetHelpTextCore to return a state dump such as
// "Selected=true; Active=false; CanClose=false; CanFloat=true; DockingState=Document" and never
// read AutomationProperties.HelpText, so Narrator reads that dump aloud on every dock tab. A
// style setting HelpText cannot fix it. Upstream: wieslawsoltes/Dock issue 1201 (tracked in issue 17).
//
// The tab strips create their containers with `new DocumentTabStripItem()`, so the fix needs a
// strip subclass that creates our item subclass, and DockTabTemplates.axaml swaps the strips into
// copies of Dock's DocumentControl and ToolControl templates. DELETE these types and that file
// once Dock's peers honour AutomationProperties.HelpText.

/// <summary>A <see cref="DocumentTabStrip"/> whose tabs get <see cref="DockTabItemAutomationPeer"/>.</summary>
public class AccessibleDocumentTabStrip : DocumentTabStrip
{
    // StyleKeyOverride is inherited (typeof(DocumentTabStrip)), so Dock's theme still applies.
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new AccessibleDocumentTabStripItem();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        => NeedsContainer<AccessibleDocumentTabStripItem>(item, out recycleKey);
}

/// <summary>A <see cref="ToolTabStrip"/> whose tabs get <see cref="DockTabItemAutomationPeer"/>.</summary>
public class AccessibleToolTabStrip : ToolTabStrip
{
    protected override Control CreateContainerForItemOverride(object? item, int index, object? recycleKey)
        => new AccessibleToolTabStripItem();

    protected override bool NeedsContainerOverride(object? item, int index, out object? recycleKey)
        => NeedsContainer<AccessibleToolTabStripItem>(item, out recycleKey);
}

public class AccessibleDocumentTabStripItem : DocumentTabStripItem
{
    protected override AutomationPeer OnCreateAutomationPeer()
        => new DockTabItemAutomationPeer(this, base.OnCreateAutomationPeer());
}

public class AccessibleToolTabStripItem : ToolTabStripItem
{
    protected override AutomationPeer OnCreateAutomationPeer()
        => new DockTabItemAutomationPeer(this, base.OnCreateAutomationPeer());
}

/// <summary>
/// Wraps Dock's own tab peer and delegates everything to it (name, automation id, class name,
/// control type, Invoke, selection), except the help text, which comes from
/// <see cref="AutomationProperties.HelpTextProperty"/> like any other control's. Delegating keeps
/// Dock's dockable-activation logic, which is internal and not ours to reimplement. Dock's peer
/// is never registered as the element's peer; it only answers these calls.
/// </summary>
public class DockTabItemAutomationPeer : ListItemAutomationPeer, IInvokeProvider, ISelectionItemProvider
{
    private readonly AutomationPeer _dockPeer;

    public DockTabItemAutomationPeer(TabStripItem owner, AutomationPeer dockPeer) : base(owner)
    {
        _dockPeer = dockPeer;
        // Dock raises this from the item only when the element's peer is its own type, which it
        // no longer is; raise it here so clients still see selection changes.
        owner.PropertyChanged += (_, e) =>
        {
            if (e.Property == ListBoxItem.IsSelectedProperty)
                RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, e.OldValue, e.NewValue);
        };
    }

    private T DockProvider<T>() where T : class =>
        _dockPeer.GetProvider<T>() ?? throw new InvalidOperationException($"Dock's tab peer no longer provides {typeof(T).Name}.");

    protected override string GetNameCore() => _dockPeer.GetName();
    protected override string? GetAutomationIdCore() => _dockPeer.GetAutomationId();
    protected override string GetClassNameCore() => _dockPeer.GetClassName();
    protected override AutomationControlType GetAutomationControlTypeCore() => _dockPeer.GetAutomationControlType();
    protected override string? GetHelpTextCore() => AutomationProperties.GetHelpText(Owner);

    public void Invoke() => DockProvider<IInvokeProvider>().Invoke();

    bool ISelectionItemProvider.IsSelected => DockProvider<ISelectionItemProvider>().IsSelected;
    ISelectionProvider? ISelectionItemProvider.SelectionContainer => DockProvider<ISelectionItemProvider>().SelectionContainer;
    void ISelectionItemProvider.Select() => DockProvider<ISelectionItemProvider>().Select();
    void ISelectionItemProvider.AddToSelection() => DockProvider<ISelectionItemProvider>().AddToSelection();
    void ISelectionItemProvider.RemoveFromSelection() => DockProvider<ISelectionItemProvider>().RemoveFromSelection();
}
