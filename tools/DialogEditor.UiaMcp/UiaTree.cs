using System.Windows.Automation;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// The only class that touches live UI Automation. Assigns each element a stable
/// tree-local id on first sight so Core can address elements without ever seeing an
/// AutomationElement.
///
/// Note on logging: this project does not reference DialogEditor.Core, so AppLog is not
/// available here. stderr is this process's log channel (stdout is the JSON-RPC stream),
/// so caught exceptions are written there rather than swallowed.
/// </summary>
internal sealed class UiaTree : IUiaTree
{
    private readonly Dictionary<string, AutomationElement> _elements = new(StringComparer.Ordinal);
    private int _next;

    public ElementInfo Root { get; }

    public UiaTree(AutomationElement root) => Root = Register(root);

    internal AutomationElement Element(string id) =>
        _elements.TryGetValue(id, out var el)
            ? el
            : throw new InvalidOperationException($"Unknown element id '{id}'.");

    private ElementInfo Register(AutomationElement el)
    {
        var id = $"e{++_next}";
        _elements[id] = el;

        var c = el.Current;

        string[] patterns;
        try
        {
            patterns = el.GetSupportedPatterns()
                .Select(p => p.ProgrammaticName.Replace("Identifiers.", "").Replace("Pattern", ""))
                .ToArray();
        }
        catch (ElementNotAvailableException ex)
        {
            // The element vanished between the property read and the pattern query.
            // An empty pattern list is the honest answer; do not fail the whole walk.
            Console.Error.WriteLine($"GetSupportedPatterns failed for '{c.Name}': {ex.Message}");
            patterns = Array.Empty<string>();
        }

        return new ElementInfo(
            id,
            c.Name ?? "",
            (c.ControlType?.ProgrammaticName ?? "").Replace("ControlType.", ""),
            c.AutomationId ?? "",
            c.ClassName ?? "",
            c.IsEnabled,
            c.IsOffscreen,
            c.IsKeyboardFocusable,
            patterns,
            HasSize: !c.BoundingRectangle.IsEmpty);
    }

    public IReadOnlyList<ElementInfo> ChildrenOf(string id)
    {
        var parent = Element(id);
        var result = new List<ElementInfo>();
        var walker = TreeWalker.ControlViewWalker;
        try
        {
            var child = walker.GetFirstChild(parent);
            while (child is not null)
            {
                result.Add(Register(child));
                child = walker.GetNextSibling(child);
            }
        }
        catch (ElementNotAvailableException ex)
        {
            // The element vanished mid-walk (a popup closed). A partial child list is
            // the correct answer here; the caller re-reads if it needs a fresh tree.
            Console.Error.WriteLine($"Child walk truncated: {ex.Message}");
        }
        return result;
    }
}
