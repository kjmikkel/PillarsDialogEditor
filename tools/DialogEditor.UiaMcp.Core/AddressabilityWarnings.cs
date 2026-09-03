namespace DialogEditor.UiaMcp.Core;

public record AddressabilityWarning(string Kind, string Detail);

/// <summary>
/// Reports what a scope cannot address, so verification runs surface accessibility
/// defects instead of hiding them.
///
/// This exists because the friction is load-bearing: an element that cannot be found
/// by name is a defect a screen-reader user would hit too. A harness that quietly
/// routed around it would let the app get less accessible while runs got greener.
/// Every warning here corresponds to a finding in
/// docs/2026-09-03-uia-addressability-audit.md.
/// </summary>
public static class AddressabilityWarnings
{
    private static readonly string[] TypeNamePrefixes = { "Avalonia.", "System.Windows.", "System.Controls." };

    public static IReadOnlyList<AddressabilityWarning> Inspect(IReadOnlyList<ElementInfo> elements)
    {
        var warnings = new List<AddressabilityWarning>();

        foreach (var group in elements.Where(e => e.Name.Length > 0)
                     .GroupBy(e => e.Name, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            var ids = string.Join(", ", group.Select(e => $"'{e.AutomationId}'").Distinct());
            var types = string.Join("/", group.Select(e => e.ControlType).Distinct());
            warnings.Add(new AddressabilityWarning("CollidingName",
                $"'{group.Key}' matches {group.Count()} elements ({types}; automationIds {ids}) — " +
                "a bare name lookup here is ambiguous."));
        }

        foreach (var group in elements
                     .Where(e => TypeNamePrefixes.Any(p => e.Name.StartsWith(p, StringComparison.Ordinal)))
                     .GroupBy(e => e.Name, StringComparer.Ordinal))
        {
            warnings.Add(new AddressabilityWarning("TypeNameLeak",
                $"{group.Count()} element(s) expose the framework type name '{group.Key}' as their " +
                "accessible name — meaningless to a screen reader and untranslatable."));
        }

        foreach (var group in elements.Where(e => e.IsFocusable && e.Name.Length == 0)
                     .GroupBy(e => e.ControlType, StringComparer.Ordinal))
        {
            warnings.Add(new AddressabilityWarning("UnnamedFocusable",
                $"{group.Count()} focusable {group.Key} element(s) have no accessible name — " +
                "unreachable by name."));
        }

        foreach (var group in elements.Where(e => e.IsFocusable && e.Patterns.Count == 0)
                     .GroupBy(e => e.ControlType, StringComparer.Ordinal))
        {
            warnings.Add(new AddressabilityWarning("NoPatterns",
                $"{group.Count()} focusable {group.Key} element(s) expose no UI Automation pattern — " +
                "not programmatically operable; only a synthetic click can reach them."));
        }

        return warnings;
    }
}
