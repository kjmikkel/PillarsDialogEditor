# Changelog & About — Design

**Date:** 2026-06-07
**Status:** Approved (pending spec review)
**Topic:** Two Help-menu informational dialogs — an in-app **Changelog** reader and an
**About** dialog — answering "what changed in this build?" and "which build am I on?".

---

## Goal & motivation

Today the application version lives only in the `VERSION` file and is reported by
`dialog-patcher --version`; the GUI never surfaces it, and there is no in-app record of
what changed between builds. Two small, complementary dialogs close both gaps (tracked in
`Gaps.md` under *About / Version Info* and *Changelog / Release Notes*):

- **About** answers *which build am I on?* (version, license, credits, links).
- **Changelog** answers *what's different in it?* (human-curated, per-version release notes).

They are designed as a pair because they share the Help menu, the same window chrome, and a
single source of truth for the version string.

---

## Scope

**In scope**

- A bundled `CHANGELOG.md` (human-curated, "Keep a Changelog" style), appended after each
  release.
- A pure `ChangelogParser` turning that file into an ordered list of releases.
- A shared `AppVersion` helper so the GUI and CLI report the identical version.
- A `ChangelogWindow` (in-app reader) and an `AboutWindow`.
- **Help ▸ Changelog…** and **Help ▸ About…** menu items, commands, tooltips.
- All new strings localized; all caught exceptions logged.

**Out of scope (now)**

- **Version-awareness** — detecting an upgrade and highlighting / auto-showing "new since
  your last run". Rejected for now (chosen during brainstorming): it needs persisted
  last-seen state and a first-run-style auto-popup decision, the same reason the compare
  window's first-run intro was deferred. The manual reader can grow a version-aware layer
  later. Recorded under *Future enhancements*.
- A markdown rendering library (e.g. `Markdown.Avalonia`). Rejected: the changelog's shape
  is fixed and simple; a dependency-free structured parser is enough and is unit-testable.

---

## Decision: changelog source is frozen until initial release

The seeded `CHANGELOG.md` starts **fresh** — no back-fill of pre-release history. Until the
first public release the file is effectively empty (or a single "unreleased" placeholder),
because pre-release churn is not changelog-worthy. This is enforced as an explicit rule in
`CLAUDE.md` ("Changelog" section), **to be removed when the initial version is published**,
after which every release appends its entries. The reader and parser must therefore handle
an empty / placeholder changelog gracefully (see *Error handling*).

---

## Components

### `ChangelogParser` (pure, `DialogEditor.Patch`)

Lives alongside the other UI-free services. No UI or app-state dependencies; fully testable.

- **`Parse(string markdown)` → `IReadOnlyList<ChangelogRelease>`**, newest-first.
- `ChangelogRelease` = `{ string Version, string Date, IReadOnlyList<ChangelogSection> Sections }`.
- `ChangelogSection` = `{ string? Heading, IReadOnlyList<string> Entries }`.
- Understands the project's **"Keep a Changelog"** shape:
  - `## [version] — date` (or `## version - date`) → starts a new release.
  - `### Added` / `### Changed` / `### Fixed` (any `###` text) → starts a new **section**
    within the current release, carrying that heading as a label.
  - `-`/`*` bullet lines → entries of the current section. Bullets that appear under a
    release *before* any `###` heading go into a single leading section with a **null
    Heading** (rendered unlabelled), so a writer can keep a flat list if they prefer.
- Lines that don't fit the shape are ignored rather than throwing; a file with no
  recognizable release yields an empty list (the window then shows the "unavailable/empty"
  message). Malformed input never throws to the UI.

### `AppVersion` helper (shared)

Extract the version read currently inlined in `DialogEditor.PatchCli/Program.cs`
(`AssemblyInformationalVersionAttribute.InformationalVersion`, `?? "unknown"`) into one
shared helper so the **About dialog and `--version` report the identical string from the
same source**. The CLI is refactored to call it (behaviour-preserving). Failure to read
falls back to `"unknown"`, exactly as today.

### `ChangelogWindow` (Avalonia)

Matches the existing `DiffHelpWindow` chrome: dark `Background`, app icon
(`avares://DialogEditor.Avalonia/Assets/app.ico`), `ShowInTaskbar="False"`, `CanResize`,
localized `Title`. Body is a `ScrollViewer` over an `ItemsControl` bound to the parsed
releases (per release: a version + date header, then a nested `ItemsControl` of sections —
each an optional `### `-style sub-header followed by its bullet entries), newest first,
plus a Close button. A null section heading renders no label (flat list). Reads the bundled
`CHANGELOG.md` and runs it through `ChangelogParser`.

### `AboutWindow` (Avalonia)

Same chrome. Shows: application name, **version** (via `AppVersion`), a one-line
description, license, credits, and link buttons (repository, online docs) opened via the OS
default handler through the same injectable document/URL-opener seam used by *Open
Walkthrough…*.

### Menu + command wiring

- Two relay commands on `MainWindowViewModel`: `ShowChangelogCommand`, `ShowAboutCommand`,
  both **always enabled**.
- **MainWindow**: append **Changelog…** and **About…** to the existing **Help** menu,
  after *Open Walkthrough…* — finally filling the "room left for a future About…" note from
  the sample/tutorial spec. Each item carries an explanatory `ToolTip` (per CLAUDE.md).

---

## Error handling (per CLAUDE.md)

| Situation | Behavior |
|-----------|----------|
| `CHANGELOG.md` missing or unreadable | `ChangelogWindow` shows a localized "changelog unavailable" message; `AppLog.Warn`; no crash. |
| `CHANGELOG.md` empty / no recognizable releases | Localized "no entries yet" message (expected pre-release state). |
| Malformed changelog content | Parser skips unrecognized lines; never throws to UI. |
| Version string unreadable | `AppVersion` returns `"unknown"` (mirrors the CLI). |
| About link / docs URL fails to open | `AppLog.Warn` + localized status; window stays open. |
| Any IO/permission failure reading the file | `AppLog.Error`/`Warn`; localized message; no crash. |

No `OperationCanceledException` paths are expected here; if any arise they are swallowed
silently per CLAUDE.md.

---

## Localization & logging

All new user-facing text (menu headers, both window titles, tooltips, About body, every
fallback/status message) defined in `Strings.axaml`; nothing hard-coded in XAML or C#.
`DialogEditor.Patch` (parser, helper) stays log-free; the ViewModel/window logs.

---

## Testing strategy (TDD)

Red/green/refactor throughout; tests in `DialogEditor.Tests` mirroring structure.

- **`ChangelogParser`**:
  - Multiple releases parsed newest-first with version, date, sections, and bullets.
  - `### Added`/`### Fixed` group into labelled sections; bullets before any `###` form a
    single leading section with a null heading.
  - Both heading punctuations (`—` and `-`) and both bullet markers (`-`, `*`).
  - Empty file → empty list; file with prose but no headings → empty list.
  - Malformed heading / stray lines → ignored, no throw.
- **`AppVersion`**: returns the assembly informational version; `"unknown"` fallback path.
- **Command enablement**: `ShowChangelogCommand` / `ShowAboutCommand` always enabled.
- **Link opener**: the About link/URL open is delegated through an injectable seam
  (`Func<string,bool>` document/URL opener, as in the walkthrough launcher) so a test
  asserts the target without launching a real process; failure path returns false →
  logged + status.
- All new strings present in `Strings.axaml` (exercised via `StubStringProvider` keys).

No automated test for changelog prose or About copy; a manual check before release.

---

## Open items (resolve at implementation, not blockers)

- Exact **license name, repository URL, and credits** text for About — sourced from the
  existing `README` / `LICENSE`.
- The bundling mechanism for `CHANGELOG.md` (same shipping path as the walkthrough doc) and
  the placeholder content of the initial frozen file.

---

## Future enhancements (not now)

- **Version-aware changelog** — persist the last-seen app version and, after an upgrade,
  badge new entries or auto-open the changelog once. Builds on this manual reader.
