# Validate Text Tags — Project-Wide Sweep — Design

**Date:** 2026-07-09
**Status:** Approved
**Gap:** The deferred half of token/markup validation (`Gaps.md` › "Token autocomplete and
validation in node text editing"): Flow Analytics validates only the open conversation;
"a project-wide translation sweep is deferred."
**Builds on:** `2026-07-07-token-validation-design.md` (`TokenValidationService`, the
`Validation_*` message keys), the Validate Voice-Over window pattern, and the
`ConfirmSaveBeforeApply` dirty-guard seam.

## Problem

Token typos (`[Player Nmae]`) and unbalanced markup are caught live in the node detail
panel and per-conversation in Flow Analytics — but only for the conversation that is
open. A project accumulates edits (and, especially, translations, where token breakage
is most likely) across many conversations; nothing today can answer "does *any* text I
have written or translated in this project carry a tag problem?"

## Key insight from exploration

**A saved project is self-sufficient for the sweep.** Each `ConversationPatch` in
`DialogProject.Patches` already carries every piece of writer-touched text:

- `AddedNodes` — full `NodeEditSnapshot`s with `DefaultText`/`FemaleText`;
- `ModifiedNodes[].FieldChanges` — the `To` side for changed text fields;
- `Translations[lang]` — `NodeTranslation(NodeId, DefaultText, FemaleText)` per language.

No game folder and no canvas walk are needed (unlike the batch-VO scanner), and the
patches are already in memory — the scan is pure string work with **zero disk IO**, so
it runs synchronously and instantly.

## Decisions (settled during brainstorming)

- **Surface:** a new **Test ▸ Validate Text Tags…** window, sibling to Validate
  Voice-Over. (Rejected: extending Flow Analytics — it is architecturally a
  per-conversation snapshot consumer; the 2026-07-07 spec already pointed at a
  project-level walk. Rejected: CLI report — not requested.)
- **Saved state only, with a dirty guard.** The sweep reads saved patches. When the
  project has unsaved edits (`MainWindowViewModel.IsModified`), the menu action first
  shows a three-way consent dialog — **Save and scan** / **Scan saved state only** /
  **Cancel** — shown *only* when dirty; a clean project scans immediately. The window
  itself states that it reads the saved project.
- **No cross-conversation navigation in v1.** No precedent exists for
  open-conversation-and-select-node from a tool window; rows name the conversation and
  node id, and the existing per-conversation tools handle the fix-up. Deferred.

## Architecture

House pattern: pure logic in `DialogEditor.ViewModels`, thin window glue in
`DialogEditor.Avalonia`.

### 1. Scanner — `ProjectTextTagScanner` (pure, `DialogEditor.ViewModels/Services`)

```csharp
public sealed record TextTagIssueRow(
    string ConversationName, int NodeId, string Language, string Message);

public static class ProjectTextTagScanner
{
    public static IReadOnlyList<TextTagIssueRow> Scan(
        DialogProject project, string gameId,
        TokenValidationService? validator = null);
}
```

**Correction (implementation-time verification, 2026-07-09):** `DiffEngine` stores
**all** dialog text — including the primary language's Default/Female edits — in
`patch.Translations[primaryLanguage]`; `AddedNodes` are saved with their text zeroed
and `FieldChanges` never contains `DefaultText`/`FemaleText` (`DiffEngine.cs:21,69`).
The walk is therefore simpler than first drafted:

Per patch (skipping `IsEmpty` ones):
- **Translations (the whole story):** for every language, validate each
  `NodeTranslation`'s `DefaultText` and `FemaleText`. The primary language's entries
  *are* the writer's Default/Female text.
- **Added nodes (defensive only):** validate `DefaultText`/`FemaleText` when non-empty
  — always empty under the current schema, but a legacy or hand-edited patch might
  carry text here; three lines of insurance, no JSON decoding anywhere.
- `FieldChanges` is **not** walked (never contains dialog text).

The scanner takes a `primaryLanguage` parameter (from `_provider?.Language`, may be
empty when no game folder is loaded); rows in that language get `Language == ""` so
they display as "Default", matching Flow Analytics' convention. Other rows carry
their language code.
Messages are produced with the existing `Validation_UnknownToken_Suggest` /
`Validation_UnknownToken` / `Validation_UnbalancedMarkup` keys via `Loc.Format`
(identical formatting to the detail panel and Flow Analytics). An empty/unknown
`gameId` validates against the union of both games (the validator's existing
behaviour). Rows are ordered by conversation name, then node id, then language.

### 2. Window + VM — `TextTagValidationViewModel` / `TextTagValidationWindow`

Mirrors `VoValidationWindow`'s shape: opened from the Test menu, cached single
instance in `MainWindow.axaml.cs` (re-activating focuses the open window), rows
grouped by conversation with a per-row node id / language label ("Default" when
`Language == ""`) / message, a summary line ("N issues across M conversations"), and
a localized empty state. The scan runs synchronously in the VM constructor or a
`Refresh()` — no async machinery. A static caption notes the sweep reads the **saved**
project. A Refresh button re-scans (tooltip explains).

`MainWindowViewModel` gains `CreateTextTagValidationViewModel()` (gate: `_project` is
not null; uses `_activeGameId`), mirroring `CreateVoValidationViewModel()`.

### 3. Dirty guard

The menu handler in `MainWindow.axaml.cs`:

1. If `!vm.IsModified` → open the window (scan saved state).
2. If dirty → show a small three-button consent dialog (new `SaveBeforeScanDialog`, or
   the closest existing confirm-dialog style): **Save and scan** (calls the existing
   `SaveProject()` path, then opens), **Scan saved state only** (opens directly),
   **Cancel** (nothing). The dialog copy names the consequence plainly ("Unsaved
   changes will not be included in the scan.").

The decision flow lives behind an injectable seam on `MainWindowViewModel`
(`Func<Task<ScanDirtyChoice>>`-style, mirroring `ConfirmSaveBeforeApply`) so VM tests
never touch dialogs: `ScanDirtyChoice { SaveAndScan, ScanSavedOnly, Cancel }`.

### 4. Gating & chrome

- Menu item **Test ▸ Validate Text Tags…** directly after Validate Voice-Over;
  visible-but-disabled without an open project (tooltip names the condition — the
  batch-VO discoverability pattern).
- All strings in `Strings.axaml`; tooltips + mirrored `AutomationProperties.HelpText`;
  automation names on interactive controls; no stray hex (reuse existing `Brush.*`).

## Cross-cutting rules (CLAUDE.md)

- **Localisation** — every new label/tooltip/message is a resource; row messages reuse
  the existing `Validation_*` keys.
- **Tooltips / UIA** — mandatory on the menu item, Refresh button, and dialog buttons;
  window drivable by name.
- **Error handling** — the scanner is pure and non-throwing (undecodable field changes
  are skipped); any caught exception in glue is logged via `AppLog`; no bare catch.
- **Tests run serially** — VM tests use injected seams (no `AppSettings`/dialogs).

## Testing (TDD, red first)

**`ProjectTextTagScannerTests`:**
- Added node with `[Player Nmae]` in DefaultText → one row, `Language == ""`.
- FemaleText issues reported; clean FemaleText not.
- Modified node whose `FieldChanges["DefaultText"].To` carries a typo → row (and the
  JSON-string encoding is decoded correctly).
- Non-text field changes (e.g. `Persistence`) ignored; undecodable `To` skipped
  without throwing.
- Translation issue in `Translations["fr"]` → row with `Language == "fr"`.
- Multiple conversations → rows ordered by conversation, node, language.
- Clean project / empty patches → empty list.

**`TextTagValidationViewModelTests`:** rows populated from a stub project; summary
counts; empty state message key.

**`MainWindowViewModel` guard tests (injected seam):** dirty + SaveAndScan → save
invoked then window shown; dirty + ScanSavedOnly → shown without save; dirty +
Cancel → neither; clean → shown without consulting the seam.

**Window (headless):** constructs and shows with a populated and an empty VM.

## Out of scope / deferred

- Cross-conversation navigation from a result row to the node.
- Validating vanilla (unpatched) game text — the sweep covers writer-touched text only,
  by design (vanilla text is lenient territory per the 2026-07-07 spec).
- Auto-fix actions.
- Exporting the report to a file.
