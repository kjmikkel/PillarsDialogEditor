# Design: Condition/Script Node Search & Highlight (Gap #2)

**Date:** 2026-07-16
**Status:** Design — approved in brainstorming, pending spec review
**Depends on:** [`CatalogueMatch` primitive](2026-07-16-catalogue-match-primitive-design.md)
**Sibling feature:** [Reputation & Disposition Balance](2026-07-16-rep-disposition-balance-design.md)

## Purpose

Let the writer, **within one conversation**, find and visually highlight the nodes that use a
particular condition or script — narrowing by its parameters — so they can see at a glance
where (for example) reputation/disposition is checked or set. Read-only visual aid; no data
is changed.

## User-facing behaviour

A **non-modal search panel** in the editor (dockable/collapsible), so the highlight persists
while the user pans, clicks, and edits the canvas.

Workflow:

1. **Pick an entry** — an autocomplete over the catalogue entries **for the current game**,
   covering both **conditions and scripts** (the same catalogues the condition/script editors
   use).
2. **Pin parameters (optional)** — once an entry is chosen, one input per parameter is shown
   (dropdown for enum/lookup params like Disposition/Faction; text/number otherwise). Each
   defaults to **unset = wildcard**. Set params must match exactly.
3. **Search** — matching nodes are highlighted, non-matching nodes are dimmed; a live **match
   count** ("7 nodes") is shown.
4. **Clear** — removes the highlight. Highlight also clears automatically when the conversation
   changes.

### Matching rule

A node is a **hit** if the query matches **any** of:
- a leaf in `node.Conditions`,
- a leaf in any of `node.Links[].Conditions`,
- an entry in `node.Scripts`.

Result granularity is **node-only** (decided in brainstorming) — the node is highlighted; the
specific link/connection is not separately marked in v1.

Wildcard/pin semantics (shared with Gap #1 via the `CatalogueMatch` primitive):
- Entry chosen, nothing pinned → every node using that entry is a hit.
- Entry + one of two params pinned → the pinned param must equal the node's value at that
  index; the unpinned param is a wildcard.

### Highlight style

**Highlight hits + dim the rest** (decided in brainstorming): matching nodes get an emphasis
brush/glow, non-matching nodes fade back. This is the clearest signal in a dense graph.

### Persistence / lifecycle

- Highlight **persists** across panning, selection, and editing.
- Clears on **Clear**, and on **conversation switch** (the newly opened conversation loads
  with all nodes unhighlighted).
- Re-running a search **replaces** the previous highlight.
- No mutation — the highlight is purely a view overlay.

## Architecture

### Components

1. **`NodeConditionSearchService` (Core, pure).** Input: a `ConversationEditSnapshot` and a
   `CatalogueMatch` query. Output: `IReadOnlySet<int>` of matching `NodeId`s. Walks the three
   match-sites (`Conditions`, `Links[].Conditions`, `Scripts`) using the shared primitive; a
   node matching in multiple sites appears once.

2. **`NodeViewModel.SearchMatchState` (new).** An observable enum
   `{ None, Match, Dimmed }`. This is a **new node-level visual concept** — today nodes carry
   only `IsSelected`, and connections carry `IsHighlighted`; this slots alongside without
   touching selection semantics. Styled in the node template via a converter (emphasis brush
   for `Match`, reduced opacity for `Dimmed`, unchanged for `None`), following the existing
   themed node-state pattern.

3. **`ConditionSearchViewModel` + panel view.** Entry autocomplete (catalogue filtered to the
   current game), dynamically generated parameter-pin inputs, Search/Clear commands, match
   count. On Search: build the `CatalogueMatch`, call the service against the current
   conversation's live snapshot, then set each `NodeViewModel.SearchMatchState`
   (`Match` for hits, `Dimmed` for the rest). On Clear / conversation-switch: reset all to
   `None`.

4. **Wiring in the conversation/editor VM.** Owns the active search state so it can:
   - reset all nodes to `None` when a conversation is loaded/switched, and
   - apply `Match`/`Dimmed` when a search runs.

### Parameter-pin inputs

Generated from the chosen entry's catalogue parameter metadata:
- Enum/`options` param → dropdown of the options + an explicit **"Any"** (wildcard) entry.
- Lookup param (`Faction`/`Disposition`/etc.) → the same lookup dropdown the editors use +
  **"Any"**.
- Plain string/number → text box; empty = wildcard.

A pinned value compares against the node's **raw stored parameter value** (the primitive does
not resolve GUIDs); the dropdowns already surface `(displayName → storedValue)`, so the pin
carries the stored value.

### Data flow

```
panel (entry + pins) → CatalogueMatch → NodeConditionSearchService.FindMatches(liveSnapshot)
   → HashSet<int> → editor VM sets NodeViewModel.SearchMatchState (Match / Dimmed)
   → node template renders emphasis / fade
Clear or conversation-switch → all nodes → None
```

## Cross-cutting requirements (project rules)

- **TDD** red/green for the search service and the highlight-state transitions.
- **Localisation** — panel labels, button text, "Any", match-count text, tooltips all in
  resources. No inline user-visible text.
- **Tooltips** — every control (entry picker, each parameter input, Search, Clear) carries a
  detailed `ToolTip`.
- **UI Automation** — panel controls discoverable by UIA Name
  (`AutomationProperties.Name` where no label provides one).
- **Error handling** — any caught exception logged via `AppLog.Warn/Error`.
- **`Gaps.md`** — mark this gap implemented when done.

## Testing (TDD)

**Core — `NodeConditionSearchService`:**
- Hit via node condition; hit via link condition; hit via script.
- A node matching in two sites is returned once.
- Entry-only query matches all users of that entry; one-param-pinned narrows correctly;
  pinned-miss excludes.
- GUID (faction/disposition) exact-value pin matches; no-match → empty set.
- Condition entry vs script entry resolve to the correct match-sites.

**ViewModel / highlight state:**
- After Search: hits → `Match`, all others → `Dimmed`.
- Clear → every node `None`.
- Conversation switch → every node `None` (no stale highlight from the previous conversation).
- Re-running a search replaces (not accumulates) the highlighted set.
- Match count equals the hit-set size.

## Explicitly out of scope (v1)

- **Multi-condition AND/OR** search. The primitive and per-node walk are shaped so a list of
  `CatalogueMatch` with AND/OR combinators can wrap them later, but v1 is single-entry.
- Marking the specific link/connection (node-only granularity).
- Cross-conversation search (this is per-conversation; the project-wide aggregate is Gap #1).
- Persisting the highlight across conversation switches or app restarts.
