using System.Windows.Automation;
using DialogEditor.UiaMcp.Core;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Enumerates the app's windows for #16, and hands out the AutomationElement behind each
/// one so a caller can build a UiaTree rooted there (or screenshot its rect).
///
/// Two departures from what issue #16 proposed, both forced by what the app actually does
/// — measured against the running app on 2026-09-06:
///
/// 1. Root children are NOT the whole story. A window opened with ShowDialog(owner) is a
///    CHILD of its owner in the UIA tree, not a sibling at root. Enumerating only
///    RootElement's children finds About (.Show()) and misses Settings (ShowDialog) — that
///    is, it misses precisely the ~18 modal dialogs that motivated the issue.
///
/// 2. Modality cannot be read off the platform. WindowPattern.IsModal is false even for a
///    live ShowDialog window and the owner's IsEnabled stays true, because Avalonia's
///    window peer implements neither. WindowInventory infers it from ownership instead;
///    see its Walk() for why that holds here and how it fails safe.
///
/// Same logging note as UiaTree: stdout is the JSON-RPC stream, so stderr is the log.
/// </summary>
internal sealed class UiaWindowSource : IWindowSource
{
    private readonly Dictionary<string, AutomationElement> _elements = new(StringComparer.Ordinal);
    private readonly int _processId;
    private int _next;

    public UiaWindowSource(int processId) => _processId = processId;

    internal AutomationElement Element(string id) =>
        _elements.TryGetValue(id, out var el)
            ? el
            : throw new InvalidOperationException($"Unknown window id '{id}'.");

    public IReadOnlyList<RawWindow> TopLevel() =>
        Collect(AutomationElement.RootElement, new AndCondition(
            new PropertyCondition(AutomationElement.ProcessIdProperty, _processId),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window)));

    public IReadOnlyList<RawWindow> OwnedBy(string id) =>
        Collect(Element(id),
            new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Window));

    private List<RawWindow> Collect(AutomationElement parent, Condition condition)
    {
        var result = new List<RawWindow>();
        try
        {
            foreach (AutomationElement el in parent.FindAll(TreeScope.Children, condition))
                result.Add(Register(el));
        }
        catch (ElementNotAvailableException ex)
        {
            // A window closed mid-enumeration. A partial list is the honest answer —
            // the caller re-enumerates if it needs a fresh view.
            Console.Error.WriteLine($"Window enumeration truncated: {ex.Message}");
        }
        return result;
    }

    private RawWindow Register(AutomationElement el)
    {
        var id = $"w{++_next}";
        _elements[id] = el;
        var c = el.Current;
        return new RawWindow(id, c.Name ?? "", c.AutomationId ?? "", c.ClassName ?? "",
            c.NativeWindowHandle);
    }
}
