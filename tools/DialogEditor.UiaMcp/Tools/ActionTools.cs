using System.ComponentModel;
using DialogEditor.UiaMcp.Core;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class ActionTools(EditorSession session)
{
    [McpServerTool, Description(
        "Activate an element: click a button, select a tab, toggle a checkbox. Prefers a real " +
        "UI Automation pattern and reports when it had to fall back to a synthetic click.")]
    public string Invoke(
        [Description("Window to act in: automationId, className (e.g. 'SettingsWindow') or title. Defaults to the main window.")] string? window = null,
        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Button")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to, e.g. 'Node Details'")] string? withinPane = null,
        [Description("0-based index; the only way to accept an ambiguous match")] int? nth = null)
    {
        if (!session.TryWindow(window, out var target, out var tree, out var windowError))
            return windowError;

        if (!ElementAddress.TryResolve(session, tree, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground(target);
        return ElementOperator.Execute(tree, element, ActionKind.Activate);
    }

    [McpServerTool, Description("Give an element keyboard focus, scrolling it into view if needed.")]
    public string Focus(
        [Description("Window to act in: automationId, className (e.g. 'SettingsWindow') or title. Defaults to the main window.")] string? window = null,
        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Edit")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to")] string? withinPane = null,
        [Description("0-based index")] int? nth = null)
    {
        if (!session.TryWindow(window, out var target, out var tree, out var windowError))
            return windowError;

        if (!ElementAddress.TryResolve(session, tree, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground(target);
        return ElementOperator.Execute(tree, element, ActionKind.Focus);
    }

    [McpServerTool, Description(
        "Send a keyboard shortcut to the app window. Uses SendKeys syntax: '^w' is Ctrl+W, " +
        "'+^s' is Ctrl+Shift+S, '{ESC}' is Escape. For literal text pass literalText instead.")]
    public string SendKeys(
        [Description("Window to act in: automationId, className (e.g. 'SettingsWindow') or title. Defaults to the main window.")] string? window = null,
        [Description("SendKeys shortcut syntax, e.g. '^w' or '{ESC}'")] string? keys = null,
        [Description("Literal text to type; metacharacters are escaped for you")] string? literalText = null)
    {
        if (string.IsNullOrEmpty(keys) && literalText is null)
            return "Error(NotFound): pass either keys (shortcut syntax) or literalText.";
        if (!string.IsNullOrEmpty(keys) && literalText is not null)
            return "Error(NotFound): pass keys OR literalText, not both — they have different escaping.";

        // SendKeys only reaches the app when the TARGET window is foreground — keystrokes
        // go wherever focus is, so foregrounding the main window here would type into it.
        if (!session.TryWindow(window, out var target, out _, out var windowError))
            return windowError;
        session.Foreground(target);

        var toSend = literalText is not null ? SendKeysEscaper.EscapeLiteral(literalText) : keys!;
        System.Windows.Forms.SendKeys.SendWait(toSend);
        Thread.Sleep(600);

        return literalText is not null
            ? $"ok: typed {literalText.Length} literal character(s) (escaped as '{toSend}')."
            : $"ok: sent '{keys}'.";
    }

    [McpServerTool, Description(
        "Set a text field's value. Prefers the UIA Value pattern and reports when it had to " +
        "fall back to focusing and typing.")]
    public string SetValue(
        [Description("The text to set")] string text,
        [Description("Window to act in: automationId, className (e.g. 'SettingsWindow') or title. Defaults to the main window.")] string? window = null,

        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Edit")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to")] string? withinPane = null,
        [Description("0-based index")] int? nth = null)
    {
        if (!session.TryWindow(window, out var target, out var tree, out var windowError))
            return windowError;

        if (!ElementAddress.TryResolve(session, tree, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground(target);
        return ElementOperator.Execute(tree, element, ActionKind.SetValue, text);
    }

    [McpServerTool, Description(
        "Invoke a menu command by path, e.g. ['File','Save Project']. Opens each ancestor menu, " +
        "then activates the leaf. Refuses when the leaf is disabled.")]
    public string InvokeMenu(
        [Description("Menu path from the top-level bar to the command")] string[] path)
    {
        // The menu bar belongs to the main window, so a modal blocks it: without this the
        // click dispatches, reports success and does nothing (#16). No `window` argument
        // here on purpose -- there is exactly one app menu, and it is never on a dialog.
        if (!session.TryWindow(null, out _, out _, out var windowError))
            return windowError;

        if (!MenuNavigator.TryWalk(session, path, out var leaf, out var log, out var error))
            return error;

        // A disabled command is a legitimate state, not a failure to route around — clicking
        // it anyway would report success while nothing happened.
        if (!leaf.IsEnabled)
            return log + $"Error(NotOperable): menu item '{leaf.Name}' is disabled " +
                   "(its command's CanExecute is false in the current app state).";

        // Deliberately NO session.Foreground() here. MenuNavigator has already brought the
        // window forward and opened the ancestor menus, and SetForegroundWindow on the main
        // window DISMISSES an open popup — after which the click lands on empty space where
        // the item used to be, while GetClickablePoint had already succeeded, so it reports
        // success and nothing happens. Found by checking that Help > About… opened no window.
        return log + ElementOperator.Execute(session.Tree(), leaf, ActionKind.Activate);
    }
}
