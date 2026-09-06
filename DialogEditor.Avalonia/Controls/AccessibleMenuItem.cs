using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Automation.Provider;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Controls;

/// <summary>
/// A <see cref="MenuItem"/> that can be opened and activated through UI Automation.
///
/// WHY THIS EXISTS — Avalonia 11.3's <c>MenuItemAutomationPeer</c> implements no provider
/// interfaces. It overrides <c>GetAccessKeyCore</c>, <c>GetAcceleratorKeyCore</c>,
/// <c>GetAutomationControlTypeCore</c> and <c>GetNameCore</c>, and nothing else — so a menu
/// item exposes <c>ScrollItem</c> only. A screen reader or automation client could not open a
/// menu or run a command; the sole route was synthesising a mouse click at the item's
/// rectangle. WPF's equivalent peer implements both <c>IInvokeProvider</c> and
/// <c>IExpandCollapseProvider</c>.
///
/// That the Win32 bridge forwards these patterns fine is proven by the title bar's OS
/// "System" item, which does expose <c>ExpandCollapse</c>. Measured with
/// <c>TryGetCurrentPattern</c>, not inferred.
///
/// This is an upstream framework gap affecting every Avalonia application, tracked in
/// <see href="https://github.com/kjmikkel/PillarsDialogEditor/issues/18">issue #18</see>.
/// These two types are a local workaround and should be DELETED once Avalonia ships the
/// providers. See <c>docs/uia-fallback-inventory.md</c>.
///
/// Every menu item in the app must be this type rather than a bare <c>MenuItem</c>;
/// <c>MenuItemAutomationIdTests</c> enforces that so a new one cannot regress silently.
/// </summary>
public class AccessibleMenuItem : MenuItem
{
    /// <summary>
    /// Resolve the built-in <see cref="MenuItem"/> theme rather than one keyed on this
    /// subclass. A templated Avalonia control finds its ControlTheme by style key, which
    /// defaults to the concrete type — without this the subclass matches no theme, gets no
    /// template, and renders nothing at all.
    /// </summary>
    protected override Type StyleKeyOverride => typeof(MenuItem);

    protected override AutomationPeer OnCreateAutomationPeer() =>
        new AccessibleMenuItemAutomationPeer(this);

    /// <summary>
    /// Generated child items (File ▸ Recent Projects binds <c>ItemsSource</c>) must also be
    /// operable, so containers this item creates are of this type too.
    /// </summary>
    protected override Control CreateContainerForItemOverride(
        object? item, int index, object? recycleKey) => new AccessibleMenuItem();
}

/// <summary>
/// Adds <see cref="IInvokeProvider"/> and <see cref="IExpandCollapseProvider"/> to Avalonia's
/// menu-item peer.
/// </summary>
public class AccessibleMenuItemAutomationPeer : MenuItemAutomationPeer,
    IInvokeProvider, IExpandCollapseProvider
{
    public AccessibleMenuItemAutomationPeer(MenuItem owner) : base(owner) { }

    /// <summary>
    /// Opens the submenu for a parent item, or activates a leaf. Mirrors WPF, where invoking
    /// a menu item with children means "show them".
    /// </summary>
    public void Invoke()
    {
        EnsureEnabled();

        if (Owner.HasSubMenu)
        {
            Owner.Open();
            return;
        }

        // Raise the routed Click rather than executing Command directly: MenuItem.OnClick
        // already runs the command, and going through the event also fires the Click="..."
        // handlers that several items use instead of a Command. One path, both mechanisms.
        Owner.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
    }

    public ExpandCollapseState ExpandCollapseState => !Owner.HasSubMenu
        ? ExpandCollapseState.LeafNode
        : Owner.IsSubMenuOpen ? ExpandCollapseState.Expanded : ExpandCollapseState.Collapsed;

    /// <summary>True for a parent item: expanding it does reveal a menu (used on macOS).</summary>
    public bool ShowsMenu => Owner.HasSubMenu;

    public void Expand()
    {
        EnsureEnabled();
        if (Owner.HasSubMenu) Owner.Open();
    }

    public void Collapse()
    {
        EnsureEnabled();
        Owner.Close();
    }
}
