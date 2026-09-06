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

    /// <summary>
    /// Patterns that represent an ACTION on the element. Scroll and ScrollItem are excluded
    /// deliberately: they only bring something into view, which nearly everything supports,
    /// so counting them would make every label look operable.
    /// </summary>
    private static readonly string[] ActionablePatterns =
        { "Invoke", "Toggle", "SelectionItem", "ExpandCollapse", "Value", "RangeValue", "Selection" };

    private static bool IsOperable(ElementInfo e) =>
        e.IsFocusable || e.Patterns.Any(p => ActionablePatterns.Contains(p));

    public static IReadOnlyList<AddressabilityWarning> Inspect(IReadOnlyList<ElementInfo> elements)
    {
        var warnings = new List<AddressabilityWarning>();

        foreach (var group in elements.Where(e => e.Name.Length > 0)
                     .GroupBy(e => e.Name, StringComparer.Ordinal)
                     .Where(g => g.Count() > 1))
        {
            // Judge ambiguity only among elements that can actually be OPERATED. An element
            // exposing no actionable pattern and not focusable can never be the target of an
            // action, so it cannot be "the one you meant" — a Text label and a Window's own
            // TitleBar both fall out of consideration for the same reason.
            //
            // This is signal quality, not neatness. After the issue #15 finding-1 fix named
            // the conversation rows, the Conversations pane alone produced 37 label pairs —
            // enough to bury the real collisions. Same lesson as the ComboBox false positive:
            // a warning that cries wolf teaches the reader to skim past them.
            var targets = group.Where(IsOperable).ToList();

            if (targets.Count <= 1)
            {
                // At most one operable element, so nothing to disambiguate. Duplicated
                // static TEXT is still a defect — assistive technology announces it twice —
                // but only Text qualifies: a Window and its own TitleBar, or two splitter
                // Thumbs, are chrome rather than labels and reporting them was a false
                // positive of exactly the kind this method keeps trying to avoid.
                //
                // A zero-size element is excluded: the app deliberately pairs a hidden
                // LiveSetting region with the visible status label, so the change is
                // ANNOUNCED while the visible one is what gets READ.
                var visibleLabels = group.Where(e => e is { ControlType: "Text", HasSize: true }).ToList();
                if (visibleLabels.Count > 1)
                {
                    warnings.Add(new AddressabilityWarning("DuplicateLabel",
                        $"'{group.Key}' appears on {visibleLabels.Count} visible Text elements — " +
                        "assistive technology will announce it more than once."));
                }
                continue;
            }

            var ids = string.Join(", ", targets.Select(e => $"'{e.AutomationId}'").Distinct());
            var types = string.Join("/", targets.Select(e => e.ControlType).Distinct());
            warnings.Add(new AddressabilityWarning("CollidingName",
                $"'{group.Key}' matches {targets.Count} interactive elements ({types}; " +
                $"automationIds {ids}) — a bare name lookup here is ambiguous."));
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
