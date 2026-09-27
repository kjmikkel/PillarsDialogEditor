using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Views;

namespace DialogEditor.Avalonia.Docking;

/// A guided-tour target that lives inside a Dock tool: the tool's Id (so a closed or
/// backgrounded tool can be revealed before highlighting) and the view type Dock renders
/// for it (the only stable handle on that content — it has no compile-time x:Name).
public sealed record DockedTourTarget(string DockToolId, Type ViewType);

/// Maps a guided-tour step's opaque TargetName (GuidedTourStep lives in the Avalonia-free
/// ViewModels project, so it cannot reference controls) to a live Control. Issue #10.
///
/// Why not FindControl(name): Dock instantiates tool content lazily from
/// Application.DataTemplates, so the Browser / Canvas / Node Details views carry no x:Name in
/// MainWindow's name scope and a name lookup silently returns null — which is how the tour's
/// Browser/Details steps were lost when the docking shell landed. Those targets are found by
/// view TYPE instead. Everything else (chrome declared directly in MainWindow.axaml, e.g.
/// HelpToggle) keeps the classic named lookup.
///
/// Kept separate from MainWindow so the mapping, and its agreement with the dock layout and
/// App.axaml's templates, is unit-testable (TourTargetResolverTests).
public static class TourTargetResolver
{
    public static DockedTourTarget? DockedTargetFor(string targetName) => targetName switch
    {
        "BrowserPanel" => new(EditorDockFactory.BrowserId, typeof(GameBrowserView)),
        "CanvasView"   => new(EditorDockFactory.CanvasId,  typeof(ConversationView)),
        "DetailPanel"  => new(EditorDockFactory.DetailsId, typeof(NodeDetailView)),
        _              => null,
    };

    /// Resolves the live control for a tour step, or null when it does not exist right now
    /// (tool closed, or Dock has not realised its content yet). Callers must treat null as
    /// "not now", not "never".
    ///
    /// <paramref name="floatingRoots"/> are the floating dock host windows: dragging a tool
    /// out reparents its content into a separate TopLevel that the main window's visual tree
    /// can no longer see.
    public static Control? Resolve(string targetName, Control mainRoot, IEnumerable<Visual> floatingRoots)
    {
        if (DockedTargetFor(targetName) is { } docked)
            return FindFirstOfType(docked.ViewType, mainRoot.GetVisualDescendants())
                ?? floatingRoots.Select(r => FindFirstOfType(docked.ViewType, r.GetVisualDescendants()))
                                .FirstOrDefault(c => c is not null);

        return mainRoot.FindControl<Control>(targetName);
    }

    /// First visual in <paramref name="candidates"/> that is an instance of
    /// <paramref name="viewType"/>. Shared with MainWindow's FindDockedView&lt;T&gt;.
    public static Control? FindFirstOfType(Type viewType, IEnumerable<Visual> candidates) =>
        candidates.OfType<Control>().FirstOrDefault(viewType.IsInstanceOfType);
}
