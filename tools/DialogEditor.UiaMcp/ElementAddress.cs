using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Turns "either a ref or a selector" into one element. Shared by every acting tool so the
/// ambiguity contract is enforced in exactly one place.
/// </summary>
internal static class ElementAddress
{
    public static bool TryResolve(
        EditorSession session, UiaTree tree, string? reference, Selector selector,
        out ElementInfo element, out string error)
    {
        element = null!;
        error = "";

        if (!string.IsNullOrWhiteSpace(reference))
        {
            if (!session.Refs.TryResolve(reference, out var elementId, out var refError))
            {
                error = $"Error(StaleRef): {refError}";
                return false;
            }

            // Re-read the element's current properties: enabled/offscreen state may have
            // moved since the ref was minted, and the plan depends on both.
            var found = new Resolver(tree).Flatten().FirstOrDefault(e => e.Id == elementId);
            if (found is null)
            {
                // Two different causes, and the caller cannot tell them apart from the
                // ref alone: the element really went away, or the ref belongs to another
                // window's tree. Saying only "re-run read_tree" would send them back to
                // the same wrong window to hit the same wall.
                error = $"Error(StaleRef): ref '{reference}' does not resolve in this window. " +
                        "Either the element is gone, or the ref was minted against a " +
                        "different window — pass the same `window` argument you used for " +
                        "read_tree, or re-run read_tree here.";
                return false;
            }
            element = found;
            return true;
        }

        if (selector.IsEmpty && selector.WithinPane is null)
        {
            error = "Error(NotFound): pass either a ref, or at least one of name / controlType / automationId.";
            return false;
        }

        var result = new Resolver(tree).Resolve(selector);
        if (result.Element is null)
        {
            error = $"Error({result.ErrorKind}): {result.ErrorMessage}";
            return false;
        }
        element = result.Element;
        return true;
    }
}
