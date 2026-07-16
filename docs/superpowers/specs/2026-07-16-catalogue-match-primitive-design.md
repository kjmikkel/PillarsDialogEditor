# Shared design: the `CatalogueMatch` primitive

**Date:** 2026-07-16
**Status:** Design — shared foundation for two features
**Consumers:**
- [Reputation & Disposition Balance](2026-07-16-rep-disposition-balance-design.md) (Gap #1)
- [Condition/Script Node Search & Highlight](2026-07-16-condition-script-node-search-design.md) (Gap #2)

This document describes a small primitive that **both** feature specs depend on. It is
**implemented once** in `DialogEditor.Core`; each feature is a thin fold over it. The two
feature specs restate the relevant contract but must not re-implement it.

## Why a shared primitive

Both features answer the same low-level question at every node:

> Does this `ConditionLeaf` / `ScriptCall` match catalogue entry *X*, with these
> parameters pinned (and the rest treated as wildcards)?

- **Gap #1** aggregates matches into per-value counts across a corpus.
- **Gap #2** collects matching `NodeId`s within one conversation to drive canvas highlighting.

Writing the parameter-matching logic (index alignment, wildcard-vs-pinned, case handling)
twice guarantees the two copies drift. One tested predicate keeps them honest.

## The three match-sites

Both features walk the **same three sites** on a `NodeEditSnapshot`
(`DialogEditor.Core/Editing/ConversationEditSnapshot.cs`):

1. `node.Conditions` — the node's own condition tree. Flatten with
   `ConditionNode.Leaves()` to get `ConditionLeaf(FullName, Parameters)`.
2. `node.Links[].Conditions` — each outgoing link's condition tree (nullable list).
3. `node.Scripts` — the node's `ScriptCall(FullName, Parameters, Category)` list.

Whether a given feature includes all three sites is the feature's decision (Gap #1 tallies
conditions only; Gap #2 searches all three). The primitive itself is agnostic — it matches a
single leaf/call.

## Data shapes (Core)

```csharp
// A pinned parameter slot: either a concrete value that must match, or a wildcard.
public readonly record struct ParameterPin(bool IsPinned, string? Value)
{
    public static ParameterPin Wildcard        => new(false, null);
    public static ParameterPin Pin(string value) => new(true, value);
}

// A query against ONE catalogue entry (a condition OR a script).
// ReflectionFullName is the C#-reflection signature, e.g.
//   "Boolean IsDisposition(Guid, Rank, Operator)"
// matching ScriptCatalogueEntry.ReflectionFullName / ConditionLeaf.FullName /
// ScriptCall.FullName. Pins is index-aligned to the entry's parameter list; a query with
// fewer pins than parameters treats the trailing slots as wildcards.
public sealed record CatalogueMatch(
    string ReflectionFullName,
    IReadOnlyList<ParameterPin> Pins);
```

## Semantics

`CatalogueMatch.Matches(fullName, parameters)` returns true iff:

1. **Method identity** — `fullName` equals `ReflectionFullName`, ordinal-case-insensitive.
   Anchoring on the reflection FullName (not the display method name) keeps PoE1 and PoE2
   overloads separate: both games have `IsReputation`, but with different signatures, and
   the catalogue already keys variants by `ReflectionFullName`.
2. **Every pinned slot matches** — for each index `i` where `Pins[i].IsPinned`, the call's
   `parameters[i]` equals `Pins[i].Value` (ordinal, case-insensitive; a missing index is a
   non-match). Wildcard slots impose no constraint.
3. **Wildcard-only query** — a `CatalogueMatch` with no pinned slots matches every call of
   that method.

A tiny adapter matches directly against a `ConditionLeaf` and a `ScriptCall`
(both expose `FullName` + `Parameters`), so callers don't reach into the records.

## What this primitive is NOT

- It does not resolve GUIDs to display names — pins compare against the **raw stored
  parameter value** (e.g. a faction GUID string). Display-name resolution is a presentation
  concern handled by each feature via the existing lookup/GameData services.
- It does not walk conversations — that's each feature's fold.
- It does not know about "reputation" or "disposition" — that classification lives in Gap #1.

## Testing (TDD, `DialogEditor.Tests`)

- Method identity: same signature matches; different signature (same display name, other
  game) does not.
- Pinned single param matches / misses; case-insensitivity.
- Wildcard-only query matches any call of the method.
- Fewer pins than parameters → trailing wildcards.
- Pin index beyond the call's parameter count → non-match, no exception.
- Adapters for `ConditionLeaf` and `ScriptCall` agree with the raw predicate.

## Extensibility note

Gap #2 ships single-entry search. A future multi-condition search can wrap a **list** of
`CatalogueMatch` with AND/OR combinators without changing this primitive or the per-node
walk — each `CatalogueMatch` still evaluates independently against a leaf/call.
