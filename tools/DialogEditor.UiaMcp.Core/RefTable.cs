namespace DialogEditor.UiaMcp.Core;

/// <summary>
/// Maps stable "ref_N" handles to tree-local element ids. Each read_tree call starts a
/// new generation; a handle carrying an older generation is rejected rather than
/// silently resolved against a tree that has since changed.
/// </summary>
public sealed class RefTable
{
    private readonly Dictionary<string, string> _byRef = new(StringComparer.Ordinal);

    public int Generation { get; private set; }

    public IReadOnlyList<string> Mint(IEnumerable<ElementInfo> elements)
    {
        Generation++;
        _byRef.Clear();

        var refs = new List<string>();
        var n = 0;
        foreach (var el in elements)
        {
            var handle = $"ref_{++n}";
            _byRef[handle] = el.Id;
            refs.Add(handle);
        }
        return refs;
    }

    public bool TryResolve(string reference, out string elementId, out string error)
    {
        elementId = "";
        error = "";

        // Callers may echo back a generation-qualified handle, e.g. "ref_3@2".
        var handle = reference;
        var at = reference.IndexOf('@');
        if (at >= 0)
        {
            handle = reference[..at];
            if (!int.TryParse(reference[(at + 1)..], out var gen) || gen != Generation)
            {
                error = $"Stale ref '{reference}': the tree has changed since it was minted " +
                        $"(held generation {reference[(at + 1)..]}, current {Generation}). " +
                        "Re-run read_tree and use a fresh ref.";
                return false;
            }
        }

        if (_byRef.TryGetValue(handle, out var id))
        {
            elementId = id;
            return true;
        }

        error = $"Unknown ref '{reference}'. Re-run read_tree to mint refs for the current tree.";
        return false;
    }
}
