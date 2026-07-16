using DialogEditor.Core.Models;

namespace DialogEditor.Core.Search;

/// One parameter slot in a CatalogueMatch: a concrete value that must match, or a wildcard.
public readonly record struct ParameterPin(bool IsPinned, string? Value)
{
    public static ParameterPin Wildcard         => new(false, null);
    public static ParameterPin Pin(string value) => new(true, value);
}

/// A query against ONE catalogue entry (a condition OR a script). Matches on the reflection
/// FullName (keeps PoE1/PoE2 overloads separate) with parameters optionally pinned; unpinned
/// slots are wildcards. Pure; shared by the reputation/disposition and node-search features.
public sealed record CatalogueMatch(string ReflectionFullName, IReadOnlyList<ParameterPin> Pins)
{
    public bool Matches(string fullName, IReadOnlyList<string> parameters)
    {
        if (!string.Equals(fullName, ReflectionFullName, StringComparison.OrdinalIgnoreCase))
            return false;
        for (int i = 0; i < Pins.Count; i++)
        {
            if (!Pins[i].IsPinned) continue;
            if (i >= parameters.Count) return false;
            if (!string.Equals(parameters[i], Pins[i].Value, StringComparison.OrdinalIgnoreCase))
                return false;
        }
        return true;
    }

    public bool Matches(ConditionLeaf leaf) => Matches(leaf.FullName, leaf.Parameters);
    public bool Matches(ScriptCall call)    => Matches(call.FullName, call.Parameters);
}
