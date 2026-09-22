# Cross-Vanilla Duplicate Detection — Design

**Date:** 2026-09-22
**Issue:** #14 — last open item ("Cross-vanilla comparison") under *Duplicate / near-duplicate
line detection*. Shipping this closes #14.
**Builds on:** `2026-07-13-duplicate-line-detection-design.md` (which deferred this item as
"the 'my lines vs everything' scope, which needs a hashing/blocking strategy over ~40k lines").
**Status:** Design approved; implementation pending.

## Problem

The duplicate sweep in Validate Text… compares the writer's own lines only against each
other. A writer who copies a *vanilla* line into a new node — verbatim, or barely reworded —
gets no warning. The artifact is the same copy-paste mistake the sweep already targets, just
with a source outside the project.

## Scope decisions (settled during brainstorming)

1. **Both tiers.** A writer line identical to a vanilla line (exact) *and* a writer line
   within the near threshold of a vanilla line (near) are reported, at the same threshold
   the writer-vs-writer tier uses.
2. **Opt-in toggle.** A **Compare against the base game** checkbox beside the
   Duplicate-lines header, off by default, persisted in settings like the female-text /
   other-language toggles. Disabled when no game folder is loaded.
3. **Only findings that involve the writer.** An exact group or near pair is kept only if at
   least one member is a writer line. Vanilla-vs-vanilla duplicates are the game's business,
   and skipping that pass is also what keeps the scan affordable.
4. **A node is never matched against its own vanilla original.** Closeness to one's own
   original is the definition of an edit, not a copy-paste artifact.
5. **Vanilla side is primary-language only.** Loading every language's string tables would
   multiply the load. The writer's other-language lines (when that toggle is on) are still
   compared against each other, as today. Vanilla *Female* text follows the existing
   female-text toggle (same string table file, so it is free).
6. **Near prefilter: character 3-gram count filter (approach A).** Lossless — the report is
   identical to what brute-force Levenshtein would produce. Rejected alternatives: word-level
   MinHash/LSH (lossy; a reword touching several short words escapes it, so the base-game
   tier would silently disagree with the exact writer-vs-writer tier) and parallel brute
   force (~10⁹ cell updates at the 0.75 preset; tens of seconds on modest hardware).

## Data sources

### Writer lines

Unchanged: `project.Patches[*].Translations[...]` via the existing candidate collection in
`DuplicateLineScanner`.

### Vanilla corpus — `VanillaLineCorpus` (new, `DialogEditor.ViewModels.Services`)

```csharp
public record VanillaLine(string ConversationName, int NodeId, string Text, bool IsFemale);

public static class VanillaLineCorpus
{
    public static IReadOnlyList<VanillaLine> Load(
        IGameDataProvider provider, CancellationToken ct = default);
}
```

- Walks `provider.EnumerateConversations()`, loads each conversation, and reads each node's
  entry from `conversation.Strings` — the `SpeakerLineScanner` walk, minus patch application:
  the corpus is the game as it is on disk. The provider loads in its current `Language`, which
  is the project's primary language (`MainWindowViewModel` passes `_provider.Language` as
  primary everywhere), so no language parameter is needed.
- Emits Default text and, when non-blank, Female text (both always; the scan decides whether
  to use Female — so toggling female text never forces a reload).
- Filters at load time with the scanner's own rules: blank lines and lines under **4 words**
  after normalization are dropped. This shrinks the corpus and the index substantially.
- `ct.ThrowIfCancellationRequested()` per conversation.
- An unreadable conversation is `AppLog.Warn`ed and skipped — never fatal (`SpeakerLineScanner`
  precedent).
- The only IO in the feature. Everything downstream is pure.

## Exclusions (recomputed on every scan)

A vanilla line at `(conversation, nodeId)` is dropped when the project **owns that node's
text** — i.e. the node is in `patch.AddedNodes` or has an entry in any `patch.Translations`
list (the DiffEngine records every added or edited node's text there). A node in
`patch.ModifiedNodes` with *no* translation entry had only structural edits (links, scripts);
its text is still genuinely vanilla, so it stays in the corpus — a writer line copying it is a
real finding. This covers two cases with one rule:

- **The writer's own original.** An edited node's vanilla text is not a finding against the
  writer's version of it (scope decision 4).
- **An install the pack has already been applied to.** Here the on-disk "vanilla" copy of an
  added or edited node *is* the writer's line; without the rule every writer line would be an
  exact match with itself.

Exclusions are computed from the project at scan time, not at load time, so the cached corpus
stays valid while the project changes (ignore/un-ignore, saves).

Out of scope: other mods applied to the install. Their lines are indistinguishable from
vanilla on disk and are compared as such.

## Matching

`DuplicateScanOptions` gains `bool IncludeBaseGame = false`. `LineRef` gains
`bool FromBaseGame = false`. `DuplicateLineScanner.Scan` gains an optional
`IReadOnlyList<VanillaLine>? vanilla = null` parameter; it stays pure and IO-free. Vanilla
lines participate only when `options.IncludeBaseGame` **and** `vanilla` is non-null.

Vanilla candidates are normalized exactly as writer candidates are (trim, collapse whitespace,
lowercase) and always carry the primary-language label `""`, so they only ever meet
primary-language writer lines (existing rule 2, same-language comparison).

### Exact tier

Vanilla candidates join the existing `(language, normalized text)` grouping. A group is
reported when it spans ≥ 2 distinct nodes (existing rule 1) **and** contains at least one
writer line (scope decision 3). Its `Key` is the bare normalized text, as today, so exact
ignore entries behave identically. Vanilla members are sorted after writer members, so
`Members[0]` — the navigation target — is always a writer node.

### Near tier

1. **Writer vs writer** — the existing length-blocked pairwise pass, unchanged.
2. **Writer vs vanilla** — vanilla candidates not in a reported exact group are first
   collapsed to one representative per normalized text (lowest conversation/node), so two
   identical vanilla lines never yield two rows for the same writer line — and the index
   shrinks. For each writer primary-language candidate not in an exact group, query the
   `QGramIndex` built over those representatives, then run
   `Ratio` only on the returned candidates. Same threshold, same ignore-key check, same
   `NearDuplicatePair` shape with `A` = the writer line and `B` = the vanilla line.
3. **No vanilla vs vanilla pass.**

### `QGramIndex` (new, own file, `DialogEditor.ViewModels.Services`, internal)

A pure prefilter over a fixed list of normalized strings.

- **Build:** keep the strings sorted by length; for every string, count its character
  3-grams (a *multiset* — counts matter, see below) and add `(stringId, count)` to each
  gram's posting list. (Indexed strings are ≥ 4 words, so every one has grams.)
- **Query(`norm`, `threshold`) → candidate ids:**
  1. Walk the posting lists of `a`'s grams, accumulating per indexed string `b` the multiset
     intersection `shared(a, b) = Σ min(count_a(g), count_b(g))` into a dictionary.
  2. Take the **length window** — every indexed `b` with
     `min(La, Lb) ≥ threshold × max(La, Lb)` (the existing blocking rule), found by binary
     search on the length-sorted list.
  3. Keep each `b` in the window with `shared(a, b) ≥ M − 2 − 3k` (`shared` defaults to 0 when
     `b` was never reached in step 1), where `M = max(La, Lb)` and
     `k = ⌊(1 − threshold) × M⌋`.
- **Why the count bound is lossless (q-gram lemma):** with q = 3, a string of length `M` has
  `M − 2` grams, and one edit operation destroys at most 3 of them. So two strings within
  edit distance `k` share at least `M − 2 − 3k` grams, counted as a multiset. `Ratio ≥
  threshold` is exactly `editDistance ≤ (1 − threshold) × M`, so every pair the brute force
  would accept passes the filter. Counting *distinct* grams would break this bound, which is
  why the postings carry counts.
- **Short-line fallback falls out naturally:** when `M − 2 − 3k ≤ 0` the count bound excludes
  nothing, so step 3 returns that window member on the length bound alone — including one
  that shares no gram at all. Iterating the length window (rather than only the strings the
  posting walk reached) is what guarantees nothing is silently skipped.

The index is built once per scan over the post-exclusion vanilla candidates. The scan itself
(index + queries + surviving `Ratio` calls) runs off the UI thread (below), so its cost only
needs to be seconds-scale, not instant.

## Presentation

- **Location label:** `Describe(LineRef)` gains a base-game marker, e.g.
  `bia_companion #42 (base game)`, composed through the existing `Duplicate_Source` format
  with a new `Duplicate_Source_BaseGame` string (and `Duplicate_Source_BaseGameFemale` for a
  vanilla Female line).
- **Tier labels** are unchanged (**Exact** / **Near ~87%**): the location list already says
  which member is vanilla, and the ignore pane's tier labels stay valid.
- **Navigation** goes to the writer member (`Members[0]` for exact, `A` for near).
- **Ignore / Restore** are unchanged. Keys stay text-based and language/origin-free, so
  ignoring a vanilla-match exact group silences that text everywhere — consistent with the
  existing rule 3 of the scanner.

## ViewModel — `TextTagValidationViewModel`

- New optional constructor delegate, defaulted `null`:
  `Func<CancellationToken, Task<IReadOnlyList<VanillaLine>>>? loadVanilla`.
- `CanCompareBaseGame => loadVanilla is not null` (binds the checkbox's `IsEnabled`).
- New observable `IncludeBaseGame` (initialised from `duplicateOptions`, persisted through the
  existing `persistDuplicateOptions` callback, joins `DuplicateOptions`).
- New observable `IsLoadingBaseGame` (busy indicator).
- **Toggle off / no loader:** `RefreshDuplicates` stays synchronous and byte-identical to
  today — existing tests are untouched.
- **Toggle on:** the duplicate refresh takes an async path:
  1. bump a generation counter; set `IsLoadingBaseGame = true`;
  2. if the corpus is not cached, `await loadVanilla(ct)` and cache it for the VM's lifetime;
  3. `await Task.Run(() => dupScan(options, corpus))`;
  4. if the generation is still current, fill the rows; otherwise drop the result (a newer
     option change or Refresh has superseded it);
  5. `IsLoadingBaseGame = false` (for the current generation).
- The `dupScan` delegate gains the corpus parameter:
  `Func<DuplicateScanOptions, IReadOnlyList<VanillaLine>?, DuplicateLineReport>`. This is a
  signature change: the ~10 existing test call sites (`o => …`) are updated mechanically to
  `(o, _) => …` in the same commit. Chosen over a second parallel delegate, which would leave
  two ways to scan and a precedence rule between them.
- **Cancellation:** a `CancellationTokenSource` owned by the VM, exposed as a new `Cancel()`;
  `TextTagValidationWindow` gains an `OnClosed` override that calls it (the window has no
  close handling today). `OperationCanceledException` is swallowed silently.
- **Load failure:** any other exception is logged with `AppLog.Error`; the cached corpus stays
  unloaded (so the next toggle / Refresh retries), the scan runs writer-only, and a new
  observable `BaseGameLoadFailed` becomes true. The window shows
  `Duplicate_BaseGameLoadFailed` ("Couldn't read the base game's lines — only your own lines
  were compared.") under the toggles while it is set. (`DuplicateSummaryText` is not bound in
  the window, so it cannot carry this.)

## Wiring — `MainWindowViewModel`

In the `TextTagValidationViewModel` construction (the existing
`RequestTextTagValidationAsync` path):

- `loadVanilla`: `_provider is null ? null : ct => Task.Run(() =>
  VanillaLineCorpus.Load(provider, provider.Language, ct), ct)` — capturing the provider
  at window-open time.
- `dupScan` forwards the corpus to `DuplicateLineScanner.Scan`.
- `duplicateOptions` / `persistDuplicateOptions` gain `AppSettings.DuplicateIncludeBaseGame`
  (new setting, default `false`).

## View — `TextTagValidationWindow.axaml`

- A **Compare against the base game** `CheckBox` beside the existing Female / Other-languages
  toggles, `IsEnabled` bound to `CanCompareBaseGame`, with a detailed tooltip explaining that
  it also checks the writer's lines against every line in the installed game, that the first
  run reads the whole game and can take a few seconds, and that it needs a game folder.
- An indeterminate `ProgressBar` plus a "Reading the base game…" label, visible while
  `IsLoadingBaseGame`.
- All strings in `.resx` / the string resources (localisation rule); UIA Names come from the
  localised content.

## Testing (TDD, red first)

**`QGramIndexTests`**
- Property-style losslessness: over seeded random lines and systematically perturbed copies
  (substitutions, insertions, deletions, transpositions), at every threshold preset (0.75–0.95),
  every pair brute-force `Ratio` accepts is in `Query`'s result.
- Pruning: an unrelated line of similar length is not returned (the filter does real work).
- Repeated grams: a string with repeated 3-grams (e.g. "ha ha ha ha") still finds its near
  match — guards the multiset counting.
- Fallback: very short strings at the 0.75 preset (count bound ≤ 0) are returned on length alone.

**`DuplicateLineScannerTests`** (extend)
- Writer line identical to a vanilla line → an exact group with a `FromBaseGame` member;
  `Members[0]` is the writer line.
- Writer line near a vanilla line → a near pair, `A` writer, `B` vanilla.
- Two identical vanilla lines and no writer line → nothing reported.
- An edited node's own vanilla original → excluded.
- An added node present on disk (pack already applied) → excluded.
- A node in `ModifiedNodes` with no translation entry (structural edit only) → *not*
  excluded; a writer line copying its text is reported.
- Two identical vanilla lines near one writer line → exactly one near pair.
- Ignore entries suppress vanilla findings (exact key and near key pair).
- Vanilla Female lines are used only when `IncludeFemaleText` is on.
- `IncludeBaseGame` off, or `vanilla` null → report identical to today's.
- Other-language writer lines never pair with vanilla lines.

**`VanillaLineCorpusTests`** (fake provider, `SpeakerLineScannerTests` precedent)
- Default and Female text emitted; short and blank lines dropped.
- An unreadable conversation is skipped; the rest still load.
- A cancelled token throws `OperationCanceledException`.

**`TextTagValidationViewModelTests`** (extend)
- `CanCompareBaseGame` is false without a loader.
- Turning `IncludeBaseGame` on calls the loader once; later option changes and Refresh reuse
  the cached corpus.
- `IsLoadingBaseGame` is true during the load and false after.
- A superseded generation's result is discarded (controlled `TaskCompletionSource` loader).
- A throwing loader → `BaseGameLoadFailed` true, writer-only rows still shown, next toggle
  retries (and clears the flag on success).
- `IncludeBaseGame` round-trips through `persistDuplicateOptions`.
- Existing constructor calls remain valid apart from the mechanical `dupScan` lambda update.

**`TextTagValidationWindowTests`** (smoke): the window constructs with the checkbox and busy
indicator; the checkbox has a tooltip and is disabled without a loader.

**End-to-end** (`running-the-app` skill): against the Deadfire install, turn the toggle on,
record the corpus-load and scan times, and confirm a planted copy of a vanilla line is
reported with its base-game location.

## Deferred (YAGNI)

- Non-primary-language vanilla comparison.
- Distinguishing lines from other installed mods from true vanilla.
- Persisting the corpus across window opens / sessions (a disk cache keyed by game build).
- Vanilla-vs-vanilla duplicate reporting.
