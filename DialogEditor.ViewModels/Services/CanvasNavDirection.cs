namespace DialogEditor.ViewModels.Services;

/// Keyboard traversal directions on the conversation canvas. Topological, not
/// spatial: Child follows a link forward, Parent goes back, siblings are the
/// children of the same primary parent (see CanvasNavigationService).
public enum CanvasNavDirection
{
    Parent,
    Child,
    PreviousSibling,
    NextSibling,
}
