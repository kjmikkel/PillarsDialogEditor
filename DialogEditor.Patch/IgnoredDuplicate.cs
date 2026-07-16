namespace DialogEditor.Patch;

/// Which tier a duplicate finding belongs to.
public enum DuplicateKind { Exact, Near }

/// An entry on the project's duplicate-ignore allowlist. Content-keyed so it
/// survives node renumbering and re-scans:
///   • Exact → Keys = [normalizedText] (one key).
///   • Near  → Keys = the two normalized texts, sorted (two keys).
/// DisplayText is a human-readable label built at ignore time, shown in the
/// Ignored-duplicates pane even after the underlying lines are edited apart.
/// Editor metadata only — never written to game files.
public record IgnoredDuplicate(DuplicateKind Kind, IReadOnlyList<string> Keys, string DisplayText);
