# Speaker Line Browser — Design

**Date:** 2026-07-13
**Gap:** *Smaller Writer/UX Backlog ▸ Speaker line browser* in `Gaps.md`
**Status:** Design approved; implementation pending.

## Problem

A writer editing dialog for an established character has no way to check that their new
or edited lines match that character's established voice without manually opening every
conversation. The request: **"show every line spoken by this character across the project
(and vanilla)"** — a read-only, voice-consistency reading surface.

## Scope decisions (settled during brainstorming)

1. **Scan scope: whole game folder + the project's edits.** Not just the conversations
   the project patches — *every* vanilla conversation, with the writer's patch applied on
   top. This is the point of the feature: you compare your new lines against everything the
   character already says, most of which lives in conversations you never touched. No
   persistent cross-session index (that option was explicitly declined); the scan runs on
   demand and its result is held only while the window is open.
2. **Two entry points, one window:**
   - **Menu** — `Test ▸ Browse Speaker Lines…`, opens with no speaker pre-selected.
   - **From selected node** — a canvas context-menu item and node-detail-panel action,
     **"Show all lines by this speaker"**, which pre-selects the selected node's speaker.
3. **Row content: full line + location.** Each row shows conversation name, node id, the
   **full** (wrapping) line text, a variant label, and an origin badge. A female-variant
   line is emitted as its **own row** when the node carries female text. Double-click / Enter
   navigates to the node.
4. **Origin badge with filter.** Each row is tagged **Vanilla / Edited / New** (textual, not
   colour-only). A **"Only my lines"** toggle hides Vanilla rows (shows Edited + New).
5. **Primary/game language only for v1** (matches the detail panel and the other analysis
   tools). Non-primary-language browsing is deferred.

## Why this needs a new scan (not a reuse of the existing walk)

Three project-wide scanners already exist — `ProjectFindService`, `ProjectVoRowScanner`,
`VoOrphanScanner` — and all three share one walk: iterate `project.Patches`, use the live
canvas snapshot for the open conversation, load *vanilla base + patch* for the rest. **That
walk only ever visits conversations the project patches.** The Speaker Line Browser must
visit the whole game folder, so it needs a new walk over `provider.EnumerateConversations()`.

**Single-pass, full-map insight:** scanning the whole game for one speaker costs the same IO
as scanning it for all speakers — the per-file disk load dominates; the speaker match is a
dictionary compare. So the scan builds the complete `speakerGuid → lines` map in one pass and
the picker filters it in memory. Switching characters does **not** re-read the game folder.
This is not the declined persistent cache: the map is discarded when the window closes, and
**Refresh** re-scans to pick up edits made after opening.

Because a full-game load is heavier than the patched-only walks (PoE2 ≈ 1,000+ conversation
files; PoE1 ≈ 40,991 nodes), the scan runs **off the UI thread** with a busy indicator —
unlike the synchronous Find-in-Project.

## Components

### `SpeakerLineScanner` (pure service, `DialogEditor.ViewModels.Services`)

Mirrors `ProjectFindService`'s shape and precedents.

```csharp
public static SpeakerLineScanResult Scan(
    DialogProject project,
    IGameDataProvider provider,
    string primaryLanguage,
    string? openConversationName,
    ConversationEditSnapshot? openSnapshot,
    CancellationToken ct);
```

Walk:

- `foreach (var file in provider.EnumerateConversations())` — the whole game folder.
- The conversation whose name equals `openConversationName` is represented by `openSnapshot`
  (live, unsaved edits included).
- Otherwise: `baseSnap = ConversationSnapshotBuilder.Build(provider.LoadConversation(file))`;
  if `project.Patches` contains this conversation, `snap = PatchApplier.Apply(baseSnap, patch,
  ignoreConflicts: true)` — else `snap = baseSnap` (pure vanilla).
- An unreadable conversation is `AppLog.Warn`-logged and skipped (never fatal) — same
  contract as the other scanners.
- `ct.ThrowIfCancellationRequested()` is checked once per conversation so window-close /
  Refresh cancels a long scan promptly. `OperationCanceledException` is swallowed at the VM
  boundary (per the error-handling rule; the only silent-swallow exception).

Per node in each snapshot:

- Text source: `DefaultText` / `FemaleText`; an added node materialised from disk carries
  `[JsonIgnore]` null text, so fall back to the patch's primary-language translation entry
  (`ProjectVoRowScanner`/`ProjectFindService` precedent).
- **Skip nodes whose effective Default text is empty** (Script/automated actions, blank
  nodes) — no line to read.
- Emit a **Default** row always (when Default text is non-empty); emit a **Female** row *only*
  when the node carries female text.
- **Origin:** node id in the patch's `AddedNodes` → `New`; in `ModifiedNodes` → `Edited`;
  else `Vanilla`. Conversations not in `project.Patches` → all `Vanilla`.
- Speaker match is by `SpeakerGuid` (case-insensitive). `HideSpeaker` does not change *who*
  speaks the line, so it does not affect matching. Built-in Player/Narrator GUIDs match like
  any other via `SpeakerNameService`.

Output model:

```csharp
public enum LineVariant { Default, Female }
public enum LineOrigin  { Vanilla, Edited, New }

public record SpeakerLineRow(
    string SpeakerGuid,
    string ConversationName,
    int    NodeId,
    LineVariant Variant,
    string LineText,
    LineOrigin  Origin);

public record SpeakerLineScanResult(
    IReadOnlyList<SpeakerLineRow> Rows,
    IReadOnlyList<SpeakerLineCount> Speakers);   // guid + resolved name + count, name-sorted

public record SpeakerLineCount(string Guid, string Name, int Count);
```

Rows sorted by `ConversationName` (Ordinal) → `NodeId` → `Variant`, matching Find-in-Project.
`Speakers` lists only speakers with ≥ 1 line, name-sorted (via `SpeakerNameService.Resolve`),
used to populate the picker with counts.

### `SpeakerLineBrowserViewModel` (`DialogEditor.ViewModels`)

- Constructor takes the same collaborators as `ProjectFindViewModel` (`DialogProject`,
  `IGameDataProvider`, primary language, the open-conversation accessor
  `Func<(string? Name, ConversationEditSnapshot? Snapshot)>`), plus an optional
  `initialSpeakerGuid` for the "from selected node" entry point.
- Runs the scan via `Task.Run` (cancellable), exposes `IsBusy`, `StatusText`, the
  `Speakers` list (picker source), `SelectedSpeaker`, `OnlyMyLines` toggle, and the filtered
  `Rows`.
- Changing `SelectedSpeaker` or `OnlyMyLines` re-filters the in-memory result — no re-scan.
- `RefreshCommand` re-runs the scan.
- Navigation: `public event Action<string,int>? RequestNavigate;` and
  `NavigateTo(SpeakerLineRow row) => RequestNavigate?.Invoke(row.ConversationName, row.NodeId);`
  — identical to `ProjectFindViewModel`. The host wires it to
  `MainWindowViewModel.NavigateToFoundNode`, which is reused **unchanged** (it already
  switches conversation under the dirty guard and selects the node by id, including the
  new-conversation-only fallback).

### `SpeakerLineBrowserWindow` (`DialogEditor.Avalonia`)

- Non-modal, owned (`Show(this)`), carries the app icon.
- Speaker `ComboBox` (name + count), the "Only my lines" `ToggleButton`/`CheckBox`, a
  `Refresh` button, a busy indicator, a status line, and a scrolling results list.
- Result row template: conversation, node id, textual origin badge, variant label, and the
  **full wrapping** line text. `DoubleTapped` and Enter call `_vm.NavigateTo`, mirroring
  `FindInProjectWindow` code-behind.
- A `FocusHintBar` (matches the item-13 workhorse-window rollout).

### Host wiring (`MainWindowViewModel` + `MainWindow.axaml.cs`)

- `public Func<SpeakerLineBrowserViewModel, Task>? ShowSpeakerLineBrowser { get; set; }`
  delegate, set in `MainWindow.axaml.cs` to construct and `Show` the window — mirrors
  `ShowFindInProject`.
- `[RelayCommand(CanExecute = nameof(CanBrowseSpeakerLines))]` `BrowseSpeakerLines()` for the
  menu (no initial speaker) and an overload/parameterised path for the context action
  (initial speaker = selected node's `SpeakerGuid`). Gate: open project + game provider
  (same shape as `CanFindInProject`); `NotifyCanExecuteChanged` added at the same sites as
  `FindInProjectCommand`.
- The VM's `RequestNavigate` is wired to `NavigateToFoundNode` at construction.

## Localisation, tooltips, accessibility

- All labels, tooltips, badge text (`Vanilla`/`Edited`/`New`), variant labels, busy/empty/
  status strings are `{DynamicResource}` keys in `Strings.axaml`. Counts via `Loc.FormatCount`
  (e.g. `"312 lines across 47 conversations"`, `"Bao (212)"`). No inline strings.
- Every control carries a detailed `ToolTip.Tip` mirrored into `AutomationProperties.HelpText`
  (enforced by `AutomationHelpTextTests`); the picker, toggle, and refresh button carry
  `AutomationProperties.Name` (enforced by `AutomationNameTests`).
- The origin badge is **textual**, satisfying the Layer 2.5 non-colour-encoding rule; any tint
  reuses an existing `Brush.*` token (no new colours → `NoStrayHexTests` unaffected).

## Testing (TDD, red first)

`SpeakerLineScannerTests`:

- Speaker match across a **patched** and an **unpatched** conversation in one scan.
- Origin classification: a vanilla node → `Vanilla`; a `ModifiedNodes` node → `Edited`; an
  `AddedNodes` node → `New`.
- Female row emitted only when the node has female text; Default-only node → single row.
- Empty-Default node (Script/blank) → no row.
- The open conversation uses the live snapshot (unsaved edit visible) over the on-disk base.
- Cancellation: a cancelled token stops the walk (`OperationCanceledException`).

`SpeakerLineBrowserViewModelTests`:

- Picker populated with per-speaker counts, name-sorted, only speakers with lines.
- `OnlyMyLines` filter hides Vanilla rows; re-filtering does not re-scan.
- `NavigateTo` raises `RequestNavigate` with the row's conversation + node id.
- `initialSpeakerGuid` pre-selects that speaker.

`SpeakerLineBrowserWindowTests`: smoke/headless construction mirroring
`FindInProjectWindowTests`.

## Deferred (YAGNI)

- Non-primary-language browsing (the scan is single-language for v1).
- A persistent, cross-session speaker→lines index (explicitly declined; on-demand scan only).
- Reading-order reconstruction within a conversation — dialog is a branching graph, not a
  linear script, so rows are ordered by conversation + node id, not "play order".
