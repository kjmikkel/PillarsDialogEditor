using System.ComponentModel;
using System.Text;
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
        [Description("Window to inspect: automationId, className (e.g. 'SettingsWindow') or title. Defaults to the main window.")] string? window = null,
        [Description("Pane name or automation id, e.g. 'Node Details' or 'RightPane'")] string? withinPane = null,
        [Description("'interactive' (focusable or pattern-bearing) or 'all'")] string filter = "interactive")
    {
        if (!session.TryWindow(window, out _, out var tree, out var windowError))
            return windowError;

        var resolver = new Resolver(tree);
        var all = resolver.Flatten(withinPane);

        if (all.Count == 0 && withinPane is not null)
            return $"Error(NotFound): no pane named or identified as '{withinPane}'.";

        var shown = filter == "all"
            ? all
            : all.Where(e => e.IsFocusable || e.Patterns.Count > 0 || e.ControlType is "Pane" or "MenuItem").ToList();

        var refs = session.Refs.Mint(shown);

        var sb = new StringBuilder();

        // Announce other open windows before the tree. #16: a run could open a dialog and
        // never notice, then read the main window and report its state as the whole truth.
        var census = session.WindowsSummary();
        if (census.Length > 0) sb.AppendLine(census);

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
        // Guarded like the rest: under a modal the status bar shows whatever it showed
        // before the dialog opened, which reads as current and is not.
        if (!session.TryWindow(null, out _, out var tree, out var windowError))
            return windowError;

        var all = new Resolver(tree).Flatten();
        var status = all.FirstOrDefault(e => e.AutomationId == "StatusLiveRegion");
        return status is null
            ? "Error(NotFound): no element with automationId 'StatusLiveRegion'."
            : status.Name;
    }

    [McpServerTool, Description("Find elements whose name, automation id or control type contains the query.")]
    public string Find(
        [Description("Case-insensitive substring")] string query,
        [Description("Window to search: automationId, className or title. Defaults to the main window.")] string? window = null)
    {
        if (!session.TryWindow(window, out _, out var tree, out var windowError))
            return windowError;

        var all = new Resolver(tree).Flatten();
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
        // Listing a submenu OPENS it (ActionKind.Expand below), so this is an acting tool
        // in inspection's clothing and needs the same modal guard as invoke_menu.
        if (!session.TryWindow(null, out _, out var tree, out var windowError))
            return windowError;

        // The app's Menu is anonymous (audit finding 5) — ClassName is the only handle,
        // and scoping here is what stops the OS 'System' menu being treated as a peer
        // of File/Edit/View/Test/Help.
        ElementInfo container;
        var prefix = "";

        if (path is null || path.Length == 0)
        {
            var appMenu = MenuNavigator.FindAppMenu(tree);
            if (appMenu is null)
                return "Error(NotFound): the app's menu bar (AutomationId='MainMenu') was not found.";
            container = appMenu;
        }
        else
        {
            // Walk to the leaf, then open IT too, so its children are listed.
            if (!MenuNavigator.TryWalk(session, path, out var leaf, out var log, out var error))
                return error;
            prefix = log;

            var opened = ElementOperator.Execute(tree, leaf, ActionKind.Expand);
            if (opened.StartsWith("Error(", StringComparison.Ordinal)) return opened;
            prefix += opened;
            container = leaf;
        }

        var items = tree.ChildrenOf(container.Id).Where(c => c.ControlType == "MenuItem").ToList();
        if (items.Count == 0)
            return prefix + $"'{(container.Name.Length > 0 ? container.Name : "the menu bar")}' has no child menu items.";

        var sb = new StringBuilder(prefix);
        foreach (var item in items)
            sb.AppendLine($"{item.Name} | enabled={item.IsEnabled} | id='{item.AutomationId}'");
        return sb.ToString();
    }
}
