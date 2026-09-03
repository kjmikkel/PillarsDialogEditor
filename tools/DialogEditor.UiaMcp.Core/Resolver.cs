using System.Text;

namespace DialogEditor.UiaMcp.Core;

public record ResolveResult(ElementInfo? Element, string? ErrorKind, string? ErrorMessage)
{
    public static ResolveResult Ok(ElementInfo el) => new(el, null, null);
    public static ResolveResult Error(string kind, string message) => new(null, kind, message);
}

/// <summary>
/// Turns a Selector into exactly one element, or into an actionable error.
///
/// The contract that matters: MORE THAN ONE MATCH IS AN ERROR. DriveApp.ps1 resolves
/// with FindFirst on Name and takes tree order, and the 2026-09-03 audit found eight
/// same-surface name collisions — so that approach can click the wrong control and
/// still report success. Only an explicit Nth accepts an ambiguous match.
/// </summary>
public sealed class Resolver(IUiaTree tree, string? uiLanguage = null)
{
    private const int MaxNearMisses = 10;
    private const int MaxEditDistance = 3;

    public IReadOnlyList<ElementInfo> Flatten(string? withinPane = null)
    {
        var start = tree.Root;
        if (withinPane is not null)
        {
            var pane = FindPane(withinPane);
            if (pane is null) return Array.Empty<ElementInfo>();
            start = pane;
        }

        var all = new List<ElementInfo>();
        Walk(start, all);
        return all;
    }

    private void Walk(ElementInfo node, List<ElementInfo> into)
    {
        into.Add(node);
        foreach (var child in tree.ChildrenOf(node.Id)) Walk(child, into);
    }

    private ElementInfo? FindPane(string paneNameOrId)
    {
        var all = new List<ElementInfo>();
        Walk(tree.Root, all);
        return all.FirstOrDefault(e => e.ControlType == "Pane" &&
            (string.Equals(e.Name, paneNameOrId, StringComparison.Ordinal) ||
             string.Equals(e.AutomationId, paneNameOrId, StringComparison.Ordinal)));
    }

    public ResolveResult Resolve(Selector selector)
    {
        if (selector.WithinPane is not null && FindPane(selector.WithinPane) is null)
        {
            return ResolveResult.Error("NotFound",
                $"No pane named or identified as '{selector.WithinPane}'. Open panes: {OpenPanes()}.");
        }

        var candidates = Flatten(selector.WithinPane).Where(e => Matches(e, selector)).ToList();

        if (candidates.Count == 1) return ResolveResult.Ok(candidates[0]);

        if (candidates.Count > 1)
        {
            if (selector.Nth is { } n)
            {
                if (n >= 0 && n < candidates.Count) return ResolveResult.Ok(candidates[n]);
                return ResolveResult.Error("NotFound",
                    $"nth={n} is out of range: only {candidates.Count} elements matched.");
            }
            return ResolveResult.Error("Ambiguous", DescribeAmbiguity(candidates));
        }

        return ResolveResult.Error("NotFound", DescribeNotFound(selector));
    }

    private static bool Matches(ElementInfo e, Selector s) =>
        (s.Name is null || string.Equals(e.Name, s.Name, StringComparison.Ordinal)) &&
        (s.ControlType is null || string.Equals(e.ControlType, s.ControlType, StringComparison.Ordinal)) &&
        (s.AutomationId is null || string.Equals(e.AutomationId, s.AutomationId, StringComparison.Ordinal));

    private string OpenPanes() =>
        string.Join(", ", Flatten()
            .Where(e => e.ControlType == "Pane" && e.Name.Length > 0)
            .Select(e => $"'{e.Name}'")
            .Distinct());

    private static string DescribeAmbiguity(List<ElementInfo> candidates)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"{candidates.Count} elements matched; refusing to guess which one you meant.");
        sb.AppendLine("Narrow with controlType, automationId, withinPane, or nth. Candidates:");
        for (var i = 0; i < candidates.Count; i++)
        {
            var c = candidates[i];
            sb.AppendLine($"  nth={i}: [{c.ControlType}] name='{c.Name}' automationId='{c.AutomationId}' class='{c.ClassName}'");
        }
        return sb.ToString();
    }

    private string DescribeNotFound(Selector selector)
    {
        var sb = new StringBuilder();
        var scope = selector.WithinPane is null ? "the window" : $"pane '{selector.WithinPane}'";
        sb.AppendLine($"No element matched in {scope}.");

        if (selector.Name is { } wanted)
        {
            var misses = NearMisses(wanted, selector.WithinPane);
            if (misses.Count > 0)
            {
                sb.AppendLine("Did you mean one of these? (note that menu labels use the ellipsis character '…', not three dots)");
                foreach (var m in misses)
                    sb.AppendLine($"  [{m.ControlType}] '{m.Name}' automationId='{m.AutomationId}'");
            }
        }

        sb.AppendLine($"Open panes: {OpenPanes()}.");
        if (uiLanguage is not null)
            sb.AppendLine($"Active UI language: '{uiLanguage}' — names are matched against " +
                          "the localised text currently shown, so a locale change moves them.");
        return sb.ToString();
    }

    private List<ElementInfo> NearMisses(string wanted, string? withinPane)
    {
        var named = Flatten(withinPane).Where(e => e.Name.Length > 0).ToList();

        var substring = named
            .Where(e => e.Name.Contains(wanted, StringComparison.OrdinalIgnoreCase) ||
                        wanted.Contains(e.Name, StringComparison.OrdinalIgnoreCase))
            .ToList();

        var close = named
            .Except(substring)
            .Where(e => EditDistance(e.Name, wanted) <= MaxEditDistance)
            .ToList();

        return substring.Concat(close).Take(MaxNearMisses).ToList();
    }

    private static int EditDistance(string a, string b)
    {
        var prev = new int[b.Length + 1];
        var cur = new int[b.Length + 1];
        for (var j = 0; j <= b.Length; j++) prev[j] = j;

        for (var i = 1; i <= a.Length; i++)
        {
            cur[0] = i;
            for (var j = 1; j <= b.Length; j++)
            {
                var cost = char.ToLowerInvariant(a[i - 1]) == char.ToLowerInvariant(b[j - 1]) ? 0 : 1;
                cur[j] = Math.Min(Math.Min(cur[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
            }
            (prev, cur) = (cur, prev);
        }
        return prev[b.Length];
    }
}
