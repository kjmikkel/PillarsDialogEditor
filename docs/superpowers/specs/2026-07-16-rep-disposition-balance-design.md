# Design: Reputation & Disposition Check Balance (Gap #1)

**Date:** 2026-07-16
**Status:** Design — approved in brainstorming, pending spec review
**Depends on:** [`CatalogueMatch` primitive](2026-07-16-catalogue-match-primitive-design.md)
**Sibling feature:** [Condition/Script Node Search & Highlight](2026-07-16-condition-script-node-search-design.md)

## Purpose

Give the writer a project-wide, read-only view of **how often each reputation and
disposition value is *checked*** across their conversations, so they can see whether certain
dispositions/factions are over-favored or ignored and take narrative action. The feature
**makes no changes** to any data.

Scope note: we tally **checks** (conditions), not **sets** (scripts). A value may be *set*
more or less often for legitimate narrative reasons; it's the balance of gate/branch checks
that reveals favoritism.

## User-facing behaviour

A new dedicated window, **Reputation & Disposition Balance**, with:

- A **Source** selector:
  - **Project's own changes** — only conversations the project has a patch for.
  - **On disk + changes** — every conversation in the game, with the project's edits applied.
- A **Scope** selector:
  - **Current conversation** — the open conversation only.
  - **All conversations** — every conversation in the chosen source.
- A **Refresh/Analyze** action.
- Two tables — **Dispositions** and **Reputations** — each row: *Value · Count · Share vs
  expected · Flag*.
- A status/summary line (totals, last analyzed time, and progress during the heavy sweep).

The four Source×Scope combinations:

| | Current conversation | All conversations |
|---|---|---|
| **Project's own changes** | open conversation's effective snapshot | every patched conversation, effective |
| **On disk + changes** | open conversation's effective snapshot | every game conversation + patches applied |

"Current conversation" is identical under either Source (it's always the open conversation's
effective snapshot); both combinations are still offered so the control pair reads
consistently and the user isn't surprised by a disabled state.

## What counts as one tally

- **Unit = the value checked** (decided in brainstorming). Bucket by the disposition axis
  (Benevolent, Cruel, Honest…) or the faction being checked (resolved to a name).
- **Effective text.** For Source = *Project's own changes*, tally every rep/disposition check
  in the **effective** (vanilla base + the writer's patch) snapshot of each patched
  conversation — not only checks the patch introduced.
- **Rank/threshold** is **not** broken out in v1 — one total count per value. (Deferred:
  a per-rank breakdown column.)
- A value checked twice on the same node counts twice (each leaf is one check); this mirrors
  "how many gates reference it," which is the quantity of interest.

## Identifying rep/disposition checks

A condition leaf is a rep/disposition check iff its catalogue entry (looked up by
`ConditionLeaf.FullName` via `ConditionCatalogue`) is in the **`Faction`** category.

Known entries (from `conditions.json`), classified into two **domains**:

- **Disposition domain:** `DispositionEqual`, `DispositionGreaterOrEqual` (PoE1, value =
  `Axis` enum param), `IsDisposition` (PoE2, value = `Guid` param, `lookupKind: Disposition`).
- **Reputation domain:** `ReputationRankEquals`, `ReputationRankGreater` (PoE1, value = faction
  `Guid`), `ReputationRankByTagEquals` (PoE1, value = `FactionName`), `IsReputation` (PoE2,
  value = faction `Guid`).

Classification and the **value-parameter index** are derived from catalogue metadata, not
hard-coded per method where avoidable:

- **Domain** = disposition if the value parameter's `lookupKind` is `Disposition` **or** the
  method name contains "Disposition"; reputation if `lookupKind` is `Faction`/name is a
  Reputation method. A small explicit map covers the PoE1 enum-based methods whose value
  param is a plain enum (no lookupKind).
- **Value parameter** = the parameter carrying the faction/disposition identity: the one whose
  `lookupKind` is `Faction`/`Disposition`, else the first parameter. (For all known entries
  this is parameter index 0.)

This mapping lives in one small Core helper (`FactionCheckClassifier`) with tests, so adding a
future faction condition is a one-line data change.

## Domain enumeration (why 0-count rows exist)

A project targets a single game (the loaded game folder drives the PoE1/PoE2 data provider),
so exactly one game's catalogue and value-domain apply — there is no PoE1/PoE2 mixing within a
report.

The complete set of possible values comes from the catalogue/lookup, **not** from the data:

- **PoE1 dispositions** — the `Axis` parameter's enum `options` in `conditions.json`.
- **PoE2 dispositions & all factions** — the same `Disposition` / `Faction` lookup tables the
  condition-editor dropdowns already use (GameData lookup resolution).

Enumerating the full domain is what surfaces the strongest "ignored" signal: a disposition or
faction that is **never** checked appears as a `0` row, flagged **Ignored**.

Values encountered in data but absent from the domain (e.g. a stale GUID) are still shown, as
an "unresolved value" row, so nothing is silently dropped.

## Outlier rule — fair share (per domain)

Computed **independently within each domain** (a disposition count and a faction count are not
comparable):

```
expected = totalChecksInDomain / valueCountInDomain
flag(count) =
    Ignored if count == 0
    Over     if count >= OverFactor  * expected     (default OverFactor  = 2.0)
    Under    if count <= UnderFactor * expected      (default UnderFactor = 0.5, and count > 0)
    Normal   otherwise
```

`OverFactor`/`UnderFactor` are named constants (tunable later; not yet user-configurable).
The **Share vs expected** column shows `count / expected` (e.g. `2.4×`) so the user sees the
raw signal behind the flag. When `expected == 0` (empty domain) all rows are `Ignored`.

## Architecture

### Components

1. **`FactionCheckClassifier` (Core, pure).** Given a `ConditionLeaf` + `ConditionCatalogue`,
   returns `null` (not a faction check) or `(Domain, RawValue)` where `RawValue` is the stored
   value-parameter string. Also exposes the full domain value-set per game.

2. **`RepDispositionTallyService` (Core, pure, IO-free).** Input: an ordered set of
   `(conversationName, ConversationEditSnapshot)`, the catalogue, and a domain/value resolver.
   Walks `node.Conditions` and `node.Links[].Conditions` (scripts excluded), classifies each
   leaf, buckets by resolved value within its domain, seeds every domain value at 0, computes
   fair-share flags. Output: `RepDispositionReport`.

3. **Gather step (ViewModel/service layer, IO).** Turns the Source×Scope selection into the
   `(name, effectiveSnapshot)[]` the tally service consumes, following the
   `ProjectFindService` pattern:
   - Open conversation → **live snapshot** (unsaved edits included).
   - Other patched conversation → `PatchApplier.Apply(base, patch, ignoreConflicts: true)`.
   - *On disk + changes / All* → iterate **all** provider conversations, applying a patch when
     one exists; unreadable conversations are skipped with `AppLog.Warn`.

4. **`RepDispositionBalanceViewModel` + window.** Source/scope selectors, Refresh command,
   two `ObservableCollection` tables, status line, progress.

### Data model (Core)

```csharp
public enum FactionCheckDomain { Disposition, Reputation }
public enum BalanceFlag { Normal, Over, Under, Ignored }

public record BalanceRow(
    string DisplayValue,   // resolved name (or raw GUID if unresolved)
    int    Count,
    double ShareVsExpected,
    BalanceFlag Flag,
    bool   IsUnresolved);

public record RepDispositionReport(
    IReadOnlyList<BalanceRow> DispositionRows,   // sorted: flagged first, then count desc
    IReadOnlyList<BalanceRow> ReputationRows,
    int DispositionTotal,
    int ReputationTotal,
    int ConversationsAnalyzed);
```

### Data flow

```
[Source, Scope] → Gather → (name, effectiveSnapshot)[]
   → RepDispositionTallyService.Analyze(snapshots, catalogue, resolver)
   → RepDispositionReport → ViewModel → two tables + status
```

### Async / performance

*On disk + changes / All conversations* parses every game conversation — potentially hundreds
of files. This runs on a **background task**, is **cancellable**, and reports progress via the
status line. The other three combinations are cheap and can run synchronously. The tally
service itself is pure and fast; cost is entirely in the gather/parse step.

## Cross-cutting requirements (project rules)

- **TDD** red/green for all non-trivial logic (classifier, tally, fair-share, gather).
- **Localisation** — every label, header, tooltip, flag name, status/progress string in a
  resource dictionary / `.resx`. No inline user-visible text.
- **Tooltips** — every interactive control (both selectors, Refresh, table headers) carries a
  detailed `ToolTip`.
- **Window icon** — the new window sets
  `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **UI Automation** — controls discoverable by UIA Name (localised headers /
  `AutomationProperties.Name`).
- **Error handling** — every caught exception logged via `AppLog.Warn/Error`
  (except `OperationCanceledException`, swallowed silently — expected on cancel of the sweep).
- **`Gaps.md`** — mark this gap implemented when done.

## Testing (TDD)

**Core (pure):**
- Classifier: each known entry → correct domain + value; a non-faction condition → null;
  both games' overloads classified independently.
- Tally: leaf in node condition and in link condition both counted; scripts ignored; multiple
  checks of one value sum; every domain value seeded (0-rows present).
- Fair-share: boundaries at exactly `2×`, exactly `½×`, and `0`; empty domain → all `Ignored`;
  `ShareVsExpected` value correct.
- Unresolved value → shown as `IsUnresolved` row, not dropped.

**Gather:**
- Open conversation uses the live snapshot (unsaved edit reflected).
- Project-changes/All iterates only patched conversations; On-disk/All iterates every provider
  conversation; unreadable conversation skipped (warn), analysis continues.
- Current scope → exactly one conversation regardless of Source.

**ViewModel:**
- Report maps to the two tables with correct sort (flagged first).
- Cancellation of the sweep leaves the UI in a clean state.

## Explicitly out of scope (v1)

- Per-rank/threshold breakdown.
- Tallying *sets* (scripts).
- User-configurable thresholds.
- Any mutation or "jump to check" navigation (this window is a read-only report; navigation
  to specific checks is Gap #2's territory).
