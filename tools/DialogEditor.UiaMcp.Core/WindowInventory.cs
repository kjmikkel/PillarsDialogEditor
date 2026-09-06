namespace DialogEditor.UiaMcp.Core;

/// <summary>A top-level or owned window as UIA reports it, before any interpretation.</summary>
public record RawWindow(
    string Id,
    string Title,
    string AutomationId,
    string ClassName,
    nint Handle);

/// <summary>The seam between window enumeration and live UI Automation.</summary>
public interface IWindowSource
{
    IReadOnlyList<RawWindow> TopLevel();
    IReadOnlyList<RawWindow> OwnedBy(string id);
}

public sealed class WindowInventory(IWindowSource source, nint mainHandle)
{
    public IReadOnlyList<WindowInfo> Windows()
    {
        var result = new List<WindowInfo>();
        foreach (var top in source.TopLevel()) Walk(top, isModal: false, result);
        return result;
    }

    /// <summary>
    /// Ownership IS the modality signal here, for want of a better one: Avalonia's window
    /// peer reports WindowPattern.IsModal as false even for a live ShowDialog window, and
    /// the owner's IsEnabled stays true. Both measured against the running app on
    /// 2026-09-06. A ShowDialog window nests under its owner in the UIA tree while a
    /// .Show() window is a root sibling, and every .Show() call in this app is the
    /// ownerless overload — so nesting and modality coincide exactly.
    ///
    /// The failure mode if that ever stops holding is an owned MODELESS window being
    /// treated as modal, which over-refuses rather than letting a false green through.
    /// </summary>
    private void Walk(RawWindow w, bool isModal, List<WindowInfo> into)
    {
        into.Add(new WindowInfo(w.Id, w.Title, w.AutomationId, w.ClassName,
            IsMain: w.Handle == mainHandle, IsModal: isModal, Handle: w.Handle));
        foreach (var owned in source.OwnedBy(w.Id)) Walk(owned, isModal: true, into);
    }
}
