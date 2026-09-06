namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// A top-level window of the app under automation, flattened to plain data so this
/// assembly stays net8.0 and unit-testable.
/// </summary>
public record WindowInfo(
    string Id,
    string Title,
    string AutomationId,
    /// <summary>
    /// Avalonia sets this to the Window subclass name ('SettingsWindow', 'AboutWindow'),
    /// so it is a locale-stable key available on every window today — unlike AutomationId,
    /// which no Window carries yet. Defaults to empty so the many existing constructions
    /// stay valid.
    /// </summary>
    string ClassName = "",
    bool IsMain = false,
    bool IsModal = false,
    /// <summary>
    /// The native window handle. Needed because foregrounding and screen capture must
    /// target the RESOLVED window: Process.MainWindowHandle is the main window by
    /// definition, so it is always the wrong one for a dialog.
    /// </summary>
    nint Handle = 0);

public record WindowResolveResult(WindowInfo? Window, string? ErrorKind, string? ErrorMessage)
{
    public static WindowResolveResult Ok(WindowInfo w) => new(w, null, null);
    public static WindowResolveResult Error(string kind, string message) => new(null, kind, message);
}

public sealed class WindowResolver(IReadOnlyList<WindowInfo> windows)
{
    private static readonly (string Field, Func<WindowInfo, string> Key)[] Tiers =
    [
        ("automationId", w => w.AutomationId),
        ("className", w => w.ClassName),
        ("title", w => w.Title),
    ];

    public WindowResolveResult Resolve(string? selector)
    {
        if (selector is null)
        {
            var main = windows.FirstOrDefault(w => w.IsMain);
            return main is null
                ? WindowResolveResult.Error("NotFound", "No main window in this session.")
                : WindowResolveResult.Ok(main);
        }

        // Three tiers, most-stable first. AutomationId is a declared contract; ClassName
        // is locale-stable but incidental (a C# class rename moves it silently); the title
        // is localised and moves with the UI language. A window is matched on the first
        // tier that hits, so a window merely TITLED like another's class cannot outrank it.
        foreach (var (field, key) in Tiers)
        {
            // An empty key never matches. Every Window in the app currently has an empty
            // AutomationId, so without this an empty selector would "match" all of them
            // and report ambiguity instead of asking for a real selector.
            var hits = windows
                .Where(w => key(w).Length > 0 && string.Equals(key(w), selector, StringComparison.Ordinal))
                .ToList();

            if (hits.Count == 1) return WindowResolveResult.Ok(hits[0]);
            if (hits.Count > 1) return Ambiguous(selector, hits, field);
        }

        return WindowResolveResult.Error("NotFound",
            $"No window matched '{selector}' by automationId, className or title. " +
            $"Open windows: {Describe()}.");
    }

    /// <summary>
    /// Resolve, then guard. This is the entry point every tool should use: keeping the
    /// two steps separate at each call site is how one of them ends up missing the guard
    /// and silently acting into a blocked window.
    ///
    /// A resolve failure is reported in preference to the modal, because a selector that
    /// matches nothing cannot be guarded — and NotFound lists the open windows, the modal
    /// among them, so the caller still learns about it.
    /// </summary>
    public WindowResolveResult ResolveForUse(string? selector)
    {
        var resolved = Resolve(selector);
        return resolved.ErrorKind is not null ? resolved : GuardModal(resolved.Window!);
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

    /// <summary>
    /// A one-line census for read_tree, empty while only the main window is open. #16
    /// notes that a run can currently open a dialog and never notice; this is how the
    /// caller DISCOVERS one rather than having to guess it appeared. Silent in the
    /// single-window case on purpose — a banner printed every time is one the reader
    /// learns to skip, precisely when it starts carrying information.
    /// </summary>
    public string Summary() =>
        windows.Count <= 1 ? "" : $"windows({windows.Count}): {Describe()}";

    private static WindowResolveResult Ambiguous(string selector, List<WindowInfo> hits, string field)
    {
        var listed = string.Join(", ", hits.Select(w =>
            $"title='{w.Title}' automationId='{w.AutomationId}' className='{w.ClassName}'"));
        return WindowResolveResult.Error("Ambiguous",
            $"{hits.Count} windows share the {field} '{selector}'; refusing to guess which one you " +
            $"meant. Candidates: {listed}.");
    }

    private string Describe() =>
        string.Join(", ", windows.Select(w =>
            $"'{w.Title}' (automationId='{w.AutomationId}' className='{w.ClassName}'" +
            $"{(w.IsMain ? ", main" : "")}{(w.IsModal ? ", modal" : "")})"));
}
