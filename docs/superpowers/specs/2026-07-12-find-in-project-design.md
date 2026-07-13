# Find in Project (read-only) — Design

**Date:** 2026-07-12
**Status:** Approved
**Gap:** `Gaps.md` ▸ Smaller Writer/UX Backlog ▸ "Project-wide find (read-only)"

## Problem

`FindReplaceViewModel` (Ctrl+F) searches only the **currently open** conversation's
canvas nodes. There is no way to answer "where did I mention X across the whole
project?" — the read-only counterpart to Batch Replace. This adds a project-wide,
navigable, read-only find.

## Decisions (settled in brainstorming, 2026-07-12)

1. **Scope: effective text of patched conversations** — the true read-only mirror of
   Batch Replace. For each conversation the project patches, search the *effective*
   snapshot (vanilla base + the writer's edits), not just patched text. Needs a game
   folder open. Chosen over patched-text-only (misses vanilla lines in edited
   conversations) and over every-game-conversation (too heavy; that is the separate
   Speaker-line-browser gap).
2. **Primary language for node text; translations opt-in.** Effective node text
   (Default/Female) is primary-language only — that is what "effective" cleanly means,
   since base text per non-primary language is not readily available. Writer-authored
   translation text (all languages) is searchable via an opt-in toggle, drawn from
   `patch.Translations` and labeled per language (these are patched-only overlays, a
   deliberately different semantic from the primary-language effective matches).
3. **Coverage is a set of toggles**, mirroring Batch Replace's field-scope UX:
   - Default text + Female text — always searched.
   - Link / choice text — toggle (comes free from the same snapshot walk).
   - Translation text (all languages) — toggle.
   - Node comments — toggle, **default off** (editor metadata, mixes with dialogue).
4. **Navigable results.** Double-click / Enter on a result opens that conversation and
   selects the node — reusing the existing conversation-switch flow (with its
   unsaved-changes guard).
5. **Read-only, live-snapshot aware.** No save-before-scan: the open conversation is
   searched via its live snapshot (unsaved edits included), exactly as
   `ProjectVoRowScanner` does.

## Architecture

### `ProjectFindService` (DialogEditor.ViewModels/Services)

Pure logic over an injected `IGameDataProvider`; mirrors `ProjectVoRowScanner.BuildRows`:

```
IReadOnlyList<FindMatchRow> Search(
    DialogProject project, IGameDataProvider provider, string primaryLanguage,
    ProjectFindQuery query,
    string? openConversationName = null, ConversationEditSnapshot? openSnapshot = null)
```

Walk: for each `(convName, patch)` in `project.Patches` —
- if `convName == openConversationName && openSnapshot is not null` → use `openSnapshot`;
- else load `provider.FindConversation(convName)` → `ConversationSnapshotBuilder.Build`
  → `PatchApplier.Apply(base, patch, ignoreConflicts: true)`; an unreadable
  conversation is caught, logged via `AppLog.Warn`, and skipped (never fatal).

For each node in the snapshot, test the enabled fields for a substring match (case per
`query.CaseSensitive`, `StringComparison.Ordinal`/`OrdinalIgnoreCase`):
- Default text, Female text (always);
- link/choice text (if `InLinkChoice`) — same fields Batch Replace's `InLinkChoiceText`
  covers;
- node comment (if `InNodeComments`);
- each language's translation text in `patch.Translations` (if `InTranslations`),
  labeled with the language code (primary language label `""`).

A field that contains the query yields exactly **one** `FindMatchRow(ConversationName,
NodeId, FieldLabel, Language, Snippet)`, whose snippet shows the first occurrence —
find is a locator, not an occurrence counter, so repeated matches in the same field are
not split into multiple rows (YAGNI). Rows are sorted by `ConversationName` (Ordinal) →
`NodeId` → `FieldLabel` → `Language`.

**Snippet:** the matched substring with up to ~30 chars of surrounding context on each
side, single-lined (newlines → spaces), ellipsized when truncated. Pure helper,
unit-tested independently.

### Query model

```csharp
public sealed record ProjectFindQuery(
    string Text,
    bool CaseSensitive = false,
    bool InLinkChoice = false,
    bool InTranslations = false,
    bool InNodeComments = false);
```

`FindMatchRow`: `record (string ConversationName, int NodeId, string FieldLabel,
string Language, string Snippet)`. `FieldLabel` is a localized display string
(resolved in the VM/row builder, not hard-coded).

### `ProjectFindViewModel` (DialogEditor.ViewModels)

Holds `SearchText`, `CaseSensitive`, `InLinkChoice`, `InTranslations`,
`InNodeComments`, a `SearchCommand` (enabled when `SearchText` non-empty), an
`IReadOnlyList<FindMatchRow> Results`, and a status string
(`Loc.FormatCount("FindInProject_Matches", n)` / a no-matches string). The VM is given
the data it needs to run the walk (project, provider, primary language, and a getter
for the open conversation's name + live snapshot) via a small injected accessor so it
stays testable without the full MainWindow.

Navigation: a `NavigateToMatch(FindMatchRow)` raises `RequestNavigate?.Invoke(convName,
nodeId)` (a delegate/event), which MainWindow wires to the conversation-switch +
node-select flow. If the node no longer exists after the switch, MainWindow shows
`Status_FindInProject_NodeGone`.

### View: `FindInProjectWindow` (DialogEditor.Avalonia/Views)

Non-modal, `Icon` set, same visual family as `FlowAnalyticsWindow` / the Validate Text
window. Contents: a search `TextBox` (Watermark), the four checkboxes (Case sensitive,
Link/choice text, Translations, Node comments — each with a `ToolTip.Tip` +
`AutomationProperties.HelpText`), a Search button, a status line, and a read-only
results list (Conversation · Node · Field · Language · Snippet). Double-click and Enter
invoke navigation; a `FocusHintBar` per the workhorse-window convention. All strings
from `Strings.axaml`.

### MainWindow wiring

- **Edit ▸ Find in Project…** menu item, placed near Batch Replace, gated by a
  `CanFindInProject` command condition (open project + game folder loaded), with a
  tooltip naming the conditions — the same gate shape as Batch VO all.
- **Ctrl+Shift+F** opens the window (verified free; Ctrl+F stays the within-conversation
  find). Wire in `MainWindow.axaml.cs` `OnKeyDownTunnel` alongside the other shortcuts.
- The window is created with the VM wired to the live project/provider and an
  open-conversation accessor; `RequestNavigate` routes through `OnConversationSelected`
  (dirty-guard preserved) then `Canvas.SelectedNode = <node with matching Id>`.

## Error handling

- Unreadable conversation during the walk: `AppLog.Warn` + skip (never fatal).
- Navigation to a vanished node: localized status message, no exception.
- No game folder / no project: the command is disabled (gate), tooltip explains.
- No matches: status line says so; empty results list.

## Testing (TDD, serial suite)

`ProjectFindService`:
- match in Default / Female / link-choice / node-comment / translation fields, each
  gated by its toggle (off → not searched);
- case-sensitive vs insensitive;
- translation matches carry the right language label; primary label is `""`;
- open conversation uses the passed live snapshot (unsaved edit found; on-disk value
  not);
- unreadable conversation skipped, other conversations still returned;
- snippet extraction: context window, newline flattening, ellipsis, match at
  start/end of field;
- ordering.

`ProjectFindViewModel`:
- query → results; empty search disables the command;
- toggles flow into the query;
- `NavigateToMatch` raises `RequestNavigate` with the correct (conversation, nodeId).

## Out of scope

- Whole-word and regex matching (case toggle only, mirroring Batch Replace).
- Per-occurrence rows / match counts within a field.
- Searching vanilla conversations the project doesn't patch (Speaker-line-browser gap).
- Non-primary-language *effective* (base+translation) text — only writer translation
  overlays are searched.
- Replace (this is the read-only counterpart; Batch Replace owns mutation).
