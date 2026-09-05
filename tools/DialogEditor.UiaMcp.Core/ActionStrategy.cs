namespace DialogEditor.UiaMcp.Core;

public enum ActionKind { Activate, Expand, SetValue, Focus }

/// <summary>
/// How to operate one element. <paramref name="Route"/> is "Pattern", "SyntheticClick",
/// "FocusAndType", "Focus" or "NotOperable".
/// </summary>
public record ActionPlan(
    string Route,
    string? Pattern,
    bool NeedsScrollIntoView,
    string? Warning,
    string? Reason);

/// <summary>
/// Decides how to operate an element, preferring a real UI Automation pattern and falling
/// back to synthetic input only when none exists — always with a warning.
///
/// The warning is the point. An element that can only be reached by clicking its rectangle
/// is a defect a screen-reader user hits too, so the harness has to keep that signal
/// visible instead of quietly routing around it. Every fallback here is expected to
/// disappear as issue #15's phases land; docs/uia-fallback-inventory.md tracks them.
/// </summary>
public static class ActionStrategy
{
    private const string Issue = "issue #15";

    public static ActionPlan Plan(ElementInfo element, ActionKind action)
    {
        var scroll = element.IsOffscreen && element.Patterns.Contains("ScrollItem");

        // Offscreen with no way to bring it into view: nothing below can work, because both
        // a synthetic click and a focus need a real on-screen position.
        if (element.IsOffscreen && !element.Patterns.Contains("ScrollItem"))
        {
            return NotOperable(
                $"'{Describe(element)}' is offscreen and exposes no ScrollItem pattern, so it " +
                "cannot be brought into view. Scroll its container first, then re-run read_tree.");
        }

        return action switch
        {
            ActionKind.Focus => PlanFocus(element, scroll),
            ActionKind.Activate => PlanActivate(element, scroll),
            ActionKind.Expand => PlanExpand(element, scroll),
            ActionKind.SetValue => PlanSetValue(element, scroll),
            _ => NotOperable($"Unsupported action '{action}'."),
        };
    }

    private static ActionPlan PlanFocus(ElementInfo e, bool scroll) =>
        e.IsFocusable
            ? new ActionPlan("Focus", null, scroll, null, null)
            : NotOperable($"'{Describe(e)}' is not keyboard focusable.");

    private static ActionPlan PlanActivate(ElementInfo e, bool scroll)
    {
        // Invoke first: it is what "activate" means. SelectionItem only changes selection,
        // so it is a fallback rather than a peer.
        foreach (var p in new[] { "Invoke", "Toggle", "SelectionItem" })
            if (e.Patterns.Contains(p))
                return new ActionPlan("Pattern", p, scroll, null, null);

        return Clickable(e)
            ? new ActionPlan("SyntheticClick", null, scroll, FallbackWarning(e, "Invoke, Toggle or SelectionItem"), null)
            : NotOperable($"'{Describe(e)}' exposes no actionable pattern and is not clickable.");
    }

    private static ActionPlan PlanExpand(ElementInfo e, bool scroll)
    {
        if (e.Patterns.Contains("ExpandCollapse"))
            return new ActionPlan("Pattern", "ExpandCollapse", scroll, null, null);

        return Clickable(e)
            ? new ActionPlan("SyntheticClick", null, scroll, FallbackWarning(e, "ExpandCollapse"), null)
            : NotOperable($"'{Describe(e)}' exposes no ExpandCollapse pattern and is not clickable.");
    }

    private static ActionPlan PlanSetValue(ElementInfo e, bool scroll)
    {
        if (e.Patterns.Contains("Value"))
            return new ActionPlan("Pattern", "Value", scroll, null, null);

        return e.IsFocusable
            ? new ActionPlan("FocusAndType", null, scroll, FallbackWarning(e, "Value"), null)
            : NotOperable($"'{Describe(e)}' exposes no Value pattern and is not focusable, " +
                          "so its text cannot be set.");
    }

    // An element with no pattern is still reachable by clicking, provided it is a real
    // interactive target. Focusability is the proxy: labels and decorations are not.
    // MenuItem is called out because the app's own menu items are not focusable yet must
    // still be clickable — they expose ScrollItem only (issue #15 finding 4's sibling).
    private static bool Clickable(ElementInfo e) => e.IsFocusable || e.ControlType == "MenuItem";

    private static string FallbackWarning(ElementInfo e, string expected) =>
        $"{e.ControlType} '{Describe(e)}' exposes no {expected} pattern; used synthetic input " +
        $"at its bounding rectangle instead. That is an addressability defect, not a normal " +
        $"code path — see {Issue}. Patterns present: [{string.Join(",", e.Patterns)}].";

    private static string Describe(ElementInfo e) =>
        e.Name.Length > 0 ? e.Name
        : e.AutomationId.Length > 0 ? $"<unnamed, id={e.AutomationId}>"
        : $"<unnamed {e.ControlType}>";

    private static ActionPlan NotOperable(string reason) => new("NotOperable", null, false, null, reason);
}
