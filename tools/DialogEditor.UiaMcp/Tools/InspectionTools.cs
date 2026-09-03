using System.ComponentModel;
using System.Text;
using System.Windows.Automation;
using DialogEditor.UiaMcp.Core;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class InspectionTools(EditorSession session)
{
    [McpServerTool, Description(
        "Dump the app's UI Automation tree with refs and addressability warnings. " +
        "Scope with withinPane to keep the output small.")]
    public string ReadTree(
        [Description("Pane name or automation id, e.g. 'Node Details' or 'RightPane'")] string? withinPane = null,
        [Description("'interactive' (focusable or pattern-bearing) or 'all'")] string filter = "interactive")
    {
        var resolver = new Resolver(session.Tree());
        var all = resolver.Flatten(withinPane);

        if (all.Count == 0 && withinPane is not null)
            return $"Error(NotFound): no pane named or identified as '{withinPane}'.";

        var shown = filter == "all"
            ? all
            : all.Where(e => e.IsFocusable || e.Patterns.Count > 0 || e.ControlType is "Pane" or "MenuItem").ToList();

        var refs = session.Refs.Mint(shown);

        var sb = new StringBuilder();
        sb.AppendLine($"generation={session.Refs.Generation}  elements={shown.Count} (of {all.Count} in scope)");
        for (var i = 0; i < shown.Count; i++)
        {
            var e = shown[i];
            sb.AppendLine($"{refs[i]} [{e.ControlType}] name='{e.Name}' id='{e.AutomationId}' " +
                          $"enabled={e.IsEnabled} offscreen={e.IsOffscreen} patterns=[{string.Join(",", e.Patterns)}]");
        }

        var warnings = AddressabilityWarnings.Inspect(all);
        if (warnings.Count > 0)
        {
            sb.AppendLine();
            sb.AppendLine("ADDRESSABILITY WARNINGS (see issue #15):");
            foreach (var w in warnings) sb.AppendLine($"  [{w.Kind}] {w.Detail}");
        }
        return sb.ToString();
    }

    [McpServerTool, Description(
        "The window title, which carries [ProjectName] and a bullet dirty marker.")]
    public string WindowTitle() => session.Status();

    [McpServerTool, Description("Read the status bar's live-region text.")]
    public string ReadStatusBar()
    {
        var all = new Resolver(session.Tree()).Flatten();
        var status = all.FirstOrDefault(e => e.AutomationId == "StatusLiveRegion");
        return status is null
            ? "Error(NotFound): no element with automationId 'StatusLiveRegion'."
            : status.Name;
    }

    [McpServerTool, Description("Find elements whose name, automation id or control type contains the query.")]
    public string Find([Description("Case-insensitive substring")] string query)
    {
        var all = new Resolver(session.Tree()).Flatten();
        var hits = all.Where(e =>
            e.Name.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.AutomationId.Contains(query, StringComparison.OrdinalIgnoreCase) ||
            e.ControlType.Contains(query, StringComparison.OrdinalIgnoreCase)).ToList();

        if (hits.Count == 0) return $"No element matched '{query}'.";

        var refs = session.Refs.Mint(hits);
        var sb = new StringBuilder($"generation={session.Refs.Generation}  matches={hits.Count}\n");
        for (var i = 0; i < hits.Count; i++)
        {
            var e = hits[i];
            sb.AppendLine($"{refs[i]} [{e.ControlType}] name='{e.Name}' id='{e.AutomationId}' enabled={e.IsEnabled}");
        }
        return sb.ToString();
    }

    [McpServerTool, Description(
        "List the app's menu items with enabled state. Omit path for the top-level bar. " +
        "Always scoped to the app's own menu, never the OS window menu.")]
    public string Menu([Description("Menu path, e.g. ['File']")] string[]? path = null)
    {
        var tree = session.Tree();

        // The app's Menu is anonymous (audit finding 5) — ClassName is the only handle,
        // and scoping here is what stops the OS 'System' menu being treated as a peer
        // of File/Edit/View/Test/Help.
        var appMenu = new Resolver(tree).Flatten()
            .FirstOrDefault(e => e.ClassName == "Menu" && e.ControlType == "Menu");
        if (appMenu is null)
            return "Error(NotFound): the app's menu (ClassName='Menu') was not found.";

        var sb = new StringBuilder();
        var current = appMenu;

        foreach (var segment in path ?? Array.Empty<string>())
        {
            var next = tree.ChildrenOf(current.Id)
                .FirstOrDefault(c => string.Equals(c.Name, segment, StringComparison.Ordinal));
            if (next is null)
            {
                var available = string.Join(", ",
                    tree.ChildrenOf(current.Id).Where(c => c.Name.Length > 0).Select(c => $"'{c.Name}'"));
                return $"Error(NotFound): no menu item '{segment}' under " +
                       $"'{(current.Name.Length > 0 ? current.Name : "the menu bar")}'. " +
                       $"Available: {available}. Note menu labels use the ellipsis character '…', not three dots.";
            }

            // Avalonia's top-level MenuItems implement NEITHER Invoke NOR ExpandCollapse,
            // so a popup's children do not exist in the tree until it is really opened.
            // A synthetic click is the only way in — reported, not hidden, because needing
            // it at all is an addressability defect (issue #15).
            session.Foreground();
            if (!TryOpen(tree, next, out var why))
                return $"Error(NotOperable): could not open menu '{segment}': {why}";
            sb.AppendLine($"note: opened '{segment}' with a synthetic click — it exposes no " +
                          "Invoke or ExpandCollapse pattern (issue #15).");

            current = next;
        }

        var items = tree.ChildrenOf(current.Id).Where(c => c.ControlType == "MenuItem").ToList();
        if (items.Count == 0)
            return sb + $"'{(current.Name.Length > 0 ? current.Name : "the menu bar")}' has no child menu items.";

        foreach (var item in items)
            sb.AppendLine($"{item.Name} | enabled={item.IsEnabled} | id='{item.AutomationId}'");
        return sb.ToString();
    }

    private static bool TryOpen(UiaTree tree, ElementInfo item, out string why)
    {
        why = "";
        var element = tree.Element(item.Id);

        if (item.Patterns.Contains("ExpandCollapse"))
        {
            ((ExpandCollapsePattern)element.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
            Thread.Sleep(600);
            return true;
        }

        try
        {
            var pt = element.GetClickablePoint();
            Win32.Click((int)pt.X, (int)pt.Y);
            Thread.Sleep(700);
            return true;
        }
        catch (NoClickablePointException)
        {
            why = "the element has no clickable point (it is offscreen or has zero size).";
            return false;
        }
    }
}
