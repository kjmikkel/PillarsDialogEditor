using System.Text;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Walks the app's menu tree, opening each ancestor along the way.
///
/// Scoping to the app's own menu bar is load-bearing: a window-wide MenuItem search also
/// returns the title bar's OS "System" item, which is indistinguishable by control type and
/// whose popup wedged the original audit probe. That scoping is now done by AutomationId
/// (issue #15 finding 5); it used to need ClassName='Menu' because the element was anonymous.
///
/// Still outstanding: the app's top-level MenuItems expose neither Invoke nor ExpandCollapse,
/// so opening one needs synthetic input, and ActionStrategy reports that as the defect it is
/// (issue #15 findings 1–2 territory; see docs/uia-fallback-inventory.md rows 1–2).
/// </summary>
internal static class MenuNavigator
{
    public static bool TryWalk(
        EditorSession session, string[] path,
        out ElementInfo leaf, out string log, out string error)
    {
        // All three out params must be assigned before ANY return, including the failure
        // paths below — otherwise this does not compile (CS0177).
        leaf = null!;
        error = "";
        log = "";
        var sb = new StringBuilder();

        // Start from a known state. Opening a menu relies on a synthetic click, which —
        // unlike invoking a pattern — is STATEFUL: if a previous call left a popup open,
        // the next click merely dismisses that popup instead of opening the menu we asked
        // for, and the walk then reports the target as missing with no children. Found the
        // hard way: an invoke_menu that errored on a disabled item left File open, and the
        // following Help walk failed with "Available: ".
        DismissOpenMenus(session);

        var tree = session.Tree();

        var appMenu = FindAppMenu(tree);
        if (appMenu is null)
        {
            error = "Error(NotFound): the app's menu bar (AutomationId='MainMenu') was not " +
                    "found. If the app predates issue #15 finding 5 it has no such id — " +
                    "rebuild it.";
            return false;
        }

        if (path.Length == 0)
        {
            error = "Error(NotFound): pass a menu path, e.g. [\"File\", \"Save Project\"].";
            return false;
        }

        var current = appMenu;
        for (var i = 0; i < path.Length; i++)
        {
            var segment = path[i];

            // Match an AutomationId FIRST, then the display Name. Ids are stable and
            // non-localised (issue #15 finding 4, fixed), so 'MenuHelp_About' keeps working
            // when the label is reworded or the UI language changes, whereas 'About…'
            // depends on both — including the ellipsis character.
            var children = tree.ChildrenOf(current.Id);
            var next = children.FirstOrDefault(c =>
                          c.AutomationId.Length > 0 &&
                          string.Equals(c.AutomationId, segment, StringComparison.Ordinal))
                       ?? children.FirstOrDefault(c =>
                          string.Equals(c.Name, segment, StringComparison.Ordinal));

            if (next is null)
            {
                var available = string.Join(", ", children
                    .Where(c => c.Name.Length > 0 || c.AutomationId.Length > 0)
                    .Select(c => c.AutomationId.Length > 0 ? $"'{c.AutomationId}' ('{c.Name}')" : $"'{c.Name}'"));
                error = $"Error(NotFound): no menu item '{segment}' under " +
                        $"'{Label(current)}'. Available (id then label): {available}. " +
                        "Prefer the AutomationId: labels are localised and use the ellipsis " +
                        "character '…', not three dots.";
                return false;
            }

            // Open every ancestor, but never the leaf: opening the leaf IS invoking it, and
            // that decision belongs to the caller.
            if (i < path.Length - 1)
            {
                session.Foreground();
                var opened = ElementOperator.Execute(tree, next, ActionKind.Expand);
                if (opened.StartsWith("Error(", StringComparison.Ordinal))
                {
                    error = opened;
                    return false;
                }
                sb.Append($"opened '{segment}': {opened}");
            }

            current = next;
        }

        leaf = current;
        log = sb.ToString();
        return true;
    }

    /// <summary>
    /// The app's own menu bar, by AutomationId.
    ///
    /// Scoping to it is load-bearing: a window-wide MenuItem search also returns the title
    /// bar's OS "System" item, which is indistinguishable by control type and whose popup
    /// wedged the original audit probe. This used to match ClassName='Menu' because the
    /// element was anonymous; issue #15 finding 5 gave it an id, so the framework class name
    /// is no longer part of the contract.
    /// </summary>
    internal static ElementInfo? FindAppMenu(UiaTree tree) =>
        new Resolver(tree).Flatten()
            .FirstOrDefault(e => e is { ControlType: "Menu", AutomationId: "MainMenu" });

    /// <summary>
    /// Closes any open menu popup so a walk starts from a known state. Two Escapes because
    /// a submenu leaves two levels open; Escape on a closed menu bar is harmless.
    /// </summary>
    internal static void DismissOpenMenus(EditorSession session)
    {
        session.Foreground();
        System.Windows.Forms.SendKeys.SendWait("{ESC}");
        Thread.Sleep(150);
        System.Windows.Forms.SendKeys.SendWait("{ESC}");
        Thread.Sleep(250);
    }

    private static string Label(ElementInfo e) => e.Name.Length > 0 ? e.Name : "the menu bar";
}
