using System.Text;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Walks the app's menu tree, opening each ancestor along the way.
///
/// Two audit findings are baked in. The app's Menu is ANONYMOUS, so ClassName is the only
/// handle — and scoping to it is what stops the OS "System" MenuItem being treated as a peer
/// of File/Edit/View/Test/Help (clicking that one wedged the original audit probe). And the
/// app's top-level MenuItems expose neither Invoke nor ExpandCollapse, so opening them needs
/// synthetic input; ActionStrategy reports that as the defect it is (issue #15).
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

        var appMenu = new Resolver(tree).Flatten()
            .FirstOrDefault(e => e.ClassName == "Menu" && e.ControlType == "Menu");
        if (appMenu is null)
        {
            error = "Error(NotFound): the app's menu (ClassName='Menu') was not found.";
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
            var next = tree.ChildrenOf(current.Id)
                .FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));

            if (next is null)
            {
                var available = string.Join(", ",
                    tree.ChildrenOf(current.Id).Where(c => c.Name.Length > 0).Select(c => $"'{c.Name}'"));
                error = $"Error(NotFound): no menu item '{segment}' under " +
                        $"'{Label(current)}'. Available: {available}. Menu labels use the " +
                        "ellipsis character '…', not three dots, and are localised — no menu " +
                        "item carries an AutomationId (issue #15 finding 4).";
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
