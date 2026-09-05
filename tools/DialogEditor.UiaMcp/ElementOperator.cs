using System.Text;
using System.Windows.Automation;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Executes an ActionStrategy plan against live UI Automation. All pattern choice lives in
/// Core; this class only carries it out and reports what happened — including, verbatim, any
/// fallback warning, because a fallback that is not reported is the failure mode the design
/// exists to prevent.
/// </summary>
internal static class ElementOperator
{
    public static string Execute(UiaTree tree, ElementInfo element, ActionKind action, string? value = null)
    {
        var plan = ActionStrategy.Plan(element, action);
        if (plan.Route == "NotOperable")
            return $"Error(NotOperable): {plan.Reason}";

        var el = tree.Element(element.Id);
        var sb = new StringBuilder();

        if (plan.NeedsScrollIntoView && !TryScrollIntoView(el, out var scrollWhy))
            return $"Error(NotOperable): could not scroll '{element.Name}' into view: {scrollWhy}";

        if (plan.NeedsScrollIntoView) sb.AppendLine("scrolled into view first.");

        switch (plan.Route)
        {
            case "Pattern":
                if (!TryPattern(el, plan.Pattern!, value, out var patternWhy))
                    return $"Error(NotOperable): {plan.Pattern} pattern failed: {patternWhy}";
                sb.AppendLine($"ok: used the {plan.Pattern} pattern.");
                break;

            case "SyntheticClick":
                if (!TryClick(el, out var clickWhy))
                    return $"Error(NotOperable): {clickWhy}";
                sb.AppendLine("ok: clicked at the element's bounding rectangle.");
                break;

            case "FocusAndType":
                el.SetFocus();
                Thread.Sleep(200);
                System.Windows.Forms.SendKeys.SendWait("^a");
                System.Windows.Forms.SendKeys.SendWait(SendKeysEscaper.EscapeLiteral(value ?? ""));
                sb.AppendLine("ok: focused and typed (select-all first).");
                break;

            case "Focus":
                el.SetFocus();
                Thread.Sleep(200);
                sb.AppendLine("ok: focused.");
                break;
        }

        if (plan.Warning is { } w) sb.AppendLine($"WARNING: {w}");
        return sb.ToString();
    }

    private static bool TryScrollIntoView(AutomationElement el, out string why)
    {
        why = "";
        try
        {
            ((ScrollItemPattern)el.GetCurrentPattern(ScrollItemPattern.Pattern)).ScrollIntoView();
            Thread.Sleep(300);
            return true;
        }
        catch (InvalidOperationException ex) { why = ex.Message; return false; }
        catch (ElementNotAvailableException ex) { why = $"the element vanished: {ex.Message}"; return false; }
    }

    private static bool TryPattern(AutomationElement el, string pattern, string? value, out string why)
    {
        why = "";
        try
        {
            switch (pattern)
            {
                case "Invoke":
                    ((InvokePattern)el.GetCurrentPattern(InvokePattern.Pattern)).Invoke();
                    break;
                case "Toggle":
                    ((TogglePattern)el.GetCurrentPattern(TogglePattern.Pattern)).Toggle();
                    break;
                case "SelectionItem":
                    ((SelectionItemPattern)el.GetCurrentPattern(SelectionItemPattern.Pattern)).Select();
                    break;
                case "ExpandCollapse":
                    ((ExpandCollapsePattern)el.GetCurrentPattern(ExpandCollapsePattern.Pattern)).Expand();
                    break;
                case "Value":
                    var vp = (ValuePattern)el.GetCurrentPattern(ValuePattern.Pattern);
                    if (vp.Current.IsReadOnly) { why = "the Value pattern reports the element read-only."; return false; }
                    vp.SetValue(value ?? "");
                    break;
                default:
                    why = $"unsupported pattern '{pattern}'.";
                    return false;
            }
            Thread.Sleep(400);
            return true;
        }
        catch (InvalidOperationException ex) { why = ex.Message; return false; }
        catch (ElementNotAvailableException ex) { why = $"the element vanished: {ex.Message}"; return false; }
    }

    private static bool TryClick(AutomationElement el, out string why)
    {
        why = "";
        try
        {
            var pt = el.GetClickablePoint();
            Win32.Click((int)pt.X, (int)pt.Y);
            Thread.Sleep(500);
            return true;
        }
        catch (NoClickablePointException)
        {
            why = "the element has no clickable point (zero size, or still offscreen).";
            return false;
        }
    }
}
