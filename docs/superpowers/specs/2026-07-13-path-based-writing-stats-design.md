# Path-Based Writing Stats — Design

**Date:** 2026-07-13
**Gap:** *Smaller Writer/UX Backlog ▸ Path-based writing stats* in `Gaps.md`
**Status:** Design approved; implementation pending.

## Problem

Flow Analytics counts words globally, but writers think in **playthroughs**: how long is the
longest read, how balanced are the opening choices, how much content sits down each branch,
who does the talking. This adds a playthrough lens over the open conversation.

## Scope decisions (settled during brainstorming)

1. **Home: the Flow Analytics window** (`Test ▸ Flow Analytics`, F7) — a new **"Playthrough
   stats"** section under the existing counts. No new window or menu item. Per-conversation,
   over the live canvas snapshot (like all of Flow Analytics).
2. **Per-branch model = per first player choice.** One row per top-level Player-choice node
   reachable from root — bounded (= number of opening choices), actionable ("is my opening
   menu balanced?"), and loop-safe. An overall header carries the whole-conversation extremes.
3. **Each opening-choice row shows both** its **content volume** (total words in all nodes
   reachable via that choice — "how much I wrote down here") and its **longest playthrough**
   (words + reading time — the worst-case single read through it).
4. **Female-variant rule (option 2 — significance gate).** Female text changes a node's word
   *weight*, not the graph. Two readings are computed over the identical graph; if their totals
   differ by **more than 10%**, the report shows both Default and Female figures, else only
   Default (avoids a noise column when female text is near-identical).
5. **Reading time = words ÷ 200 wpm**, shown `m:ss`. Hard-coded constant, no UI knob in v1.
6. **Words** = each node's Default-reading word count (Flow's `Split(' ')` rule); Player-choice
   text counts (the player reads it). Script nodes / empty text contribute nothing.

## The two readings

Over the same nodes + `Links` graph, each node has two weights:

- **Default reading** — the node's DefaultText word count.
- **Female reading** — the node's FemaleText word count when it has female text; otherwise its
  DefaultText word count (the in-game fallback).

Every path metric is the same graph walk run with each weight function, so computing both is
nearly free.

**Significance gate:** `HasSignificantFemaleVariant = |femaleTotal − defaultTotal| /
max(defaultTotal, 1) > 0.10`. When false, the female figures equal (or nearly equal) the
default ones and are suppressed in the UI; when true, both are shown side by side.

## Path algorithm

The graph can contain cycles (loops back to a hub). Enumerating paths or computing a longest
*simple* path is exponential/NP-hard, so:

1. **Break to a DAG.** A DFS from root (node 0) drops any **back-edge** — an edge to a node
   currently on the DFS stack (an ancestor). "A loop back to an earlier line is counted once."
   Deterministic, O(V+E), immune to blow-up.
2. **Longest / shortest playthrough** = longest / shortest weighted path from root to any
   terminal (a node with no forward edges), via a single topological-order pass on the DAG.
3. **Per opening-choice row**, for each top-level Player-choice node `c` directly reachable
   from root:
   - **Content volume** = sum of weights over the reachable-set of `c` (a visited-set BFS —
     cycle-safe by construction).
   - **Longest playthrough through `c`** = longest weighted path from `c` to a terminal on the
     DAG.
4. **Words-per-speaker** = one pass summing each node's weight grouped by `SpeakerGuid`
   (+ `SpeakerCategory` for fallback naming), under **both** readings — a Default and a Female
   total per speaker. Structure-independent. The Female total is shown under the same 10%
   significance gate as the other metrics.

**Conventions (shared with `FlowAnalysisService`):** root is node 0; reachability is from the
root; a node unreachable from root contributes to words-per-speaker but never to a playthrough
(consistent with Flow's existing "Unreachable" issue, so the two sections never contradict).

**Edge cases:** empty snapshot → empty report; no node 0 → empty header/branches, but
words-per-speaker still computes.

## Components

### `PathStatsService` (pure, `DialogEditor.Core.Analytics`)

Beside `FlowAnalysisService`. `static PathStatsReport Analyze(ConversationEditSnapshot snapshot)`.
Numbers only — reading-time formatting is the ViewModel's job.

```csharp
public record SpeakerWordCount(
    string SpeakerGuid, SpeakerCategory Category, int DefaultWords, int FemaleWords);

public record BranchStat(
    int    ChoiceNodeId,
    string ChoiceText,
    int    DefaultContentWords,
    int    DefaultLongestWords,
    int    FemaleContentWords,
    int    FemaleLongestWords);

public record PathStatsReport(
    bool HasSignificantFemaleVariant,
    int  DefaultTotalWords,
    int  FemaleTotalWords,
    int  DefaultLongestWords,
    int  DefaultShortestWords,
    int  FemaleLongestWords,
    int  FemaleShortestWords,
    IReadOnlyList<SpeakerWordCount> WordsPerSpeaker,
    IReadOnlyList<BranchStat>       Branches);
```

`ChoiceText` is the choice node's full DefaultText (the VM truncates for display). `Branches`
are ordered by the choice node's id (stable). `WordsPerSpeaker` is ordered by `DefaultWords`
descending.

### `FlowAnalyticsViewModel` extensions

Alongside `Statistics` / `Issues` / `TokenIssues`:

- `bool HasPathStats`, `bool HasSignificantFemaleVariant`.
- Formatted overall header strings: longest / shortest / total playthrough, each
  `"{words} words · {m:ss}"`, with a Female figure appended only when significant.
- `ObservableCollection<SpeakerWordRowViewModel> WordsPerSpeaker` — resolves
  `SpeakerGuid → name` via `SpeakerNameService` (fallback to the localised category label),
  each row `"{name}: {defaultWords}"`, with the female total appended only when
  `HasSignificantFemaleVariant`.
- `ObservableCollection<PathBranchRowViewModel> Branches` — `ChoiceText` (truncated),
  `DefaultContent` / `DefaultLongest` (and `FemaleContent` / `FemaleLongest` when significant),
  each `"{words} words · {m:ss}"`, plus a `NavigateCommand` → the choice node (reuses the
  existing `_navigateToNode` action the issue rows use).
- `Refresh()` calls `PathStatsService.Analyze(snapshot)` after the existing analysis and
  populates the above.

Reading-time helper (VM): `m = words / 200` minutes → `mm:ss` (e.g. 350 words → `1:45`),
`0:00` for zero. A tiny pure method, unit-tested.

### `FlowAnalyticsWindow.axaml`

A **"Playthrough stats"** section below the current stats/issues:
- Header lines (longest / shortest / total playthrough), each with Default and — when
  `HasSignificantFemaleVariant` — a Female figure.
- The words-per-speaker list.
- The per-opening-choice rows: choice text, content + longest (Default), a Female column pair
  shown only when significant, and a **Go** button (navigate to the choice node).

Textual labels only (no colour-only encoding); every control carries `ToolTip.Tip` mirrored to
`AutomationProperties.HelpText` and an `AutomationProperties.Name` where the label isn't
self-describing; all strings are `{DynamicResource}` / `Loc.*` keys; reuses the window's
existing `FocusHintBar` and navigation.

## Testing (TDD, red first)

`PathStatsServiceTests` (Core):
- Longest / shortest playthrough on a two-ending graph → correct totals.
- Back-edge cut: a hub-loop graph terminates and counts the looped node once.
- Per opening-choice rows: content volume = reachable-set sum; longest = longest path through
  the choice.
- Words-per-speaker grouped by `SpeakerGuid` with correct Default and Female sums (a speaker
  with a >10%-different female line shows a distinct `FemaleWords`).
- Female gate: within-10% → `HasSignificantFemaleVariant == false`, female == default;
  >10% female difference → `true`, distinct female figures.
- Edge cases: empty snapshot → empty report; no node 0 → empty header/branches but
  words-per-speaker populated; an unreachable node counts for speaker words but no playthrough.

`FlowAnalyticsViewModelTests` (extend):
- `Refresh` populates `Branches` and `WordsPerSpeaker`; `HasPathStats` true for a non-empty report.
- Female columns gated by `HasSignificantFemaleVariant`.
- A branch row's `NavigateCommand` invokes the navigate action with the choice node id.
- Reading-time formatting `m:ss` correct for a known word count.

`FlowAnalyticsWindowTests` (smoke): the window constructs with a populated path-stats section.

## Deferred (YAGNI)

- A configurable reading speed / a UI knob.
- Path stats across conversations (playthroughs that jump conversation files).
- Per-ending enumeration and per-fork (recursive) branch breakdowns — the "per first player
  choice" model is v1.
- Condition-aware paths (simulating game state to prune unreachable-by-condition links) — this
  is the Playtest Mode gap's territory; path stats are over the *possible* graph.
