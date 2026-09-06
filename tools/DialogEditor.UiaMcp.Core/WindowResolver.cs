namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// A top-level window of the app under automation, flattened to plain data so this
/// assembly stays net8.0 and unit-testable.
/// </summary>
public record WindowInfo(
    string Id,
    string Title,
    string AutomationId,
    bool IsMain = false,
    bool IsModal = false);

public record WindowResolveResult(WindowInfo? Window, string? ErrorKind, string? ErrorMessage)
{
    public static WindowResolveResult Ok(WindowInfo w) => new(w, null, null);
    public static WindowResolveResult Error(string kind, string message) => new(null, kind, message);
}

public sealed class WindowResolver(IReadOnlyList<WindowInfo> windows)
{
    public WindowResolveResult Resolve(string? selector)
    {
        if (selector is null)
        {
            var main = windows.FirstOrDefault(w => w.IsMain);
            return main is null
                ? WindowResolveResult.Error("NotFound", "No main window in this session.")
                : WindowResolveResult.Ok(main);
        }

        var byId = windows.Where(w =>
            string.Equals(w.AutomationId, selector, StringComparison.Ordinal)).ToList();
        if (byId.Count == 1) return WindowResolveResult.Ok(byId[0]);
        if (byId.Count > 1) return Ambiguous(selector, byId, "automationId");

        var byTitle = windows.Where(w =>
            string.Equals(w.Title, selector, StringComparison.Ordinal)).ToList();
        if (byTitle.Count == 1) return WindowResolveResult.Ok(byTitle[0]);
        if (byTitle.Count > 1) return Ambiguous(selector, byTitle, "title");

        return WindowResolveResult.Error("NotFound",
            $"No window matched '{selector}' by automationId or title. Open windows: {Describe()}.");
    }

    /// <summary>
    /// Refuses a resolve of <paramref name="target"/> while any modal is open.
    ///
    /// Why (#16): a modal holds input for the whole app, so an action dispatched at the
    /// main window returns success and does nothing, while Status() still reports the
    /// main window as normal. That is a FALSE GREEN — the failure mode this server exists
    /// to prevent, and the reason the policy is refusal rather than a warning an agent
    /// would skim past. The refusal names the modal, so a run that left a dialog open
    /// diagnoses itself.
    ///
    /// Hard refuse is cheap here because of how this app opens windows: the companions a
    /// run wants to inspect ALONGSIDE the main window — Find/Replace, Batch Replace, Diff,
    /// History, Blame, Flow Analytics, Patch Manager, Tag Reference — are all .Show(), so
    /// they never trip this. The ~20 ShowDialog windows are transactional: open, answer,
    /// close. Settings is the only modal where reading the main window through it is a
    /// plausible workflow, and it costs one extra close.
    ///
    /// Any modal is addressable, not merely "the" modal: modals nest (the Settings flow
    /// opens LanguageCodeDialog on top of itself) and UIA's .NET client exposes no owner
    /// chain to tell inner from outer, so keying this to identity would lock the caller
    /// out of the inner dialog.
    /// </summary>
    public WindowResolveResult GuardModal(WindowInfo target)
    {
        if (target.IsModal) return WindowResolveResult.Ok(target);

        var modal = windows.FirstOrDefault(w => w.IsModal);
        if (modal is null) return WindowResolveResult.Ok(target);

        return WindowResolveResult.Error("ModalOpen",
            $"'{modal.Title}' (automationId='{modal.AutomationId}') is modal, so it holds input " +
            $"for the whole app: anything dispatched at '{target.Title}' would report success and " +
            "do nothing. Close it, or address it directly with window=" +
            $"'{modal.AutomationId}'.");
    }

    private static WindowResolveResult Ambiguous(string selector, List<WindowInfo> hits, string field)
    {
        var listed = string.Join(", ", hits.Select(w => $"title='{w.Title}' automationId='{w.AutomationId}'"));
        return WindowResolveResult.Error("Ambiguous",
            $"{hits.Count} windows share the {field} '{selector}'; refusing to guess which one you " +
            $"meant. Candidates: {listed}.");
    }

    private string Describe() =>
        string.Join(", ", windows.Select(w =>
            $"'{w.Title}' (automationId='{w.AutomationId}'{(w.IsMain ? ", main" : "")})"));
}
