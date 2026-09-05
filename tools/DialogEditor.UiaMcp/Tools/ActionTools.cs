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
        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Button")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to, e.g. 'Node Details'")] string? withinPane = null,
        [Description("0-based index; the only way to accept an ambiguous match")] int? nth = null)
    {
        if (!ElementAddress.TryResolve(session, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground();
        return ElementOperator.Execute(session.Tree(), element, ActionKind.Activate);
    }

    [McpServerTool, Description("Give an element keyboard focus, scrolling it into view if needed.")]
    public string Focus(
        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Edit")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to")] string? withinPane = null,
        [Description("0-based index")] int? nth = null)
    {
        if (!ElementAddress.TryResolve(session, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground();
        return ElementOperator.Execute(session.Tree(), element, ActionKind.Focus);
    }

    [McpServerTool, Description(
        "Send a keyboard shortcut to the app window. Uses SendKeys syntax: '^w' is Ctrl+W, " +
        "'+^s' is Ctrl+Shift+S, '{ESC}' is Escape. For literal text pass literalText instead.")]
    public string SendKeys(
        [Description("SendKeys shortcut syntax, e.g. '^w' or '{ESC}'")] string? keys = null,
        [Description("Literal text to type; metacharacters are escaped for you")] string? literalText = null)
    {
        if (string.IsNullOrEmpty(keys) && literalText is null)
            return "Error(NotFound): pass either keys (shortcut syntax) or literalText.";
        if (!string.IsNullOrEmpty(keys) && literalText is not null)
            return "Error(NotFound): pass keys OR literalText, not both — they have different escaping.";

        // SendKeys only reaches the app when its window is foreground.
        session.Foreground();

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
        [Description("A ref_N from read_tree or find")] string? reference = null,
        [Description("Exact UIA Name")] string? name = null,
        [Description("Control type, e.g. Edit")] string? controlType = null,
        [Description("Automation id")] string? automationId = null,
        [Description("Pane to scope the search to")] string? withinPane = null,
        [Description("0-based index")] int? nth = null)
    {
        if (!ElementAddress.TryResolve(session, reference,
                new Selector(name, controlType, automationId, withinPane, nth),
                out var element, out var error))
            return error;

        session.Foreground();
        return ElementOperator.Execute(session.Tree(), element, ActionKind.SetValue, text);
    }
}
