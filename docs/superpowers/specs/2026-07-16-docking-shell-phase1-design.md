# Design: Visual Studio–style Docking Shell (Phase 1)

**Date:** 2026-07-16
**Status:** Design — approved in brainstorming, pending spec review
**Phase:** 1 of 2. Phase 2 (migrating the standalone analysis windows into dockable tools) is
deferred to its own spec/plan cycle and is **out of scope** here.

## Purpose

Replace the editor's fixed 5-column panel layout (a hand-rolled browser/canvas/details grid
with ad-hoc pin/collapse) with a real Visual Studio–style docking system: tool panels the user
can drag, drop via guide diamonds, tab together, float into separate windows, auto-hide (pin)
to an edge, and whose arrangement persists across sessions. Take behavioral cues from Visual
Studio.

## Scope (Phase 1)

The following become dockable, using the `Dock.Avalonia` framework:

- **Conversations** browser (currently the left pane) → a `Tool`.
- **Canvas** (the Nodify dialogue graph) → the central `Document` (single document in v1).
- **Node Details** (currently the right pane) → a `Tool`.
- **Condition/Script search** (currently a collapsible sub-column of the canvas) → a `Tool`,
  tabbed with Node Details by default.

Full VS docking behaviors are in scope: drag-to-dock with guide diamonds, floating windows,
auto-hide/pin, tab groups, layout persistence, and a **Reset Layout** command.

**Out of scope (Phase 2 / later):**
- Converting the standalone analysis windows (Flow Analytics, Reputation & Disposition Balance,
  Find in Project) into dockable tools.
- Multiple conversations open as document tabs (`CanCreateDocument = false` in v1).

## Decisions (settled in brainstorming)

1. **Adopt `Dock.Avalonia`** (wieslawsoltes) rather than hand-roll. Full VS docking is exactly
   what it provides; hand-rolling drag-guides/floating/auto-hide/persistence is months of work
   reinventing a mature library. The existing pin/collapse code is *replaced* by it.
2. **Thin wrapper tools.** The existing panel VMs (`GameBrowserViewModel`,
   `ConversationViewModel`, `NodeDetailViewModel`, `ConditionSearchViewModel`) stay **Dock-free**;
   small `Tool`/`Document` wrapper VMs (in `DialogEditor.Avalonia`) host them as content. This
   keeps `DialogEditor.ViewModels` free of any Dock dependency — the layered architecture holds.
3. **Full VS behaviors** in v1 (float, auto-hide, tabs, drag-guides, persistence, reset).
4. **Default layout:** Conversations left; Canvas centre document; Node Details + Condition
   search tabbed on the right (Node Details active). A new **View** menu reopens closed tools
   and resets the layout.

## Architecture

`MainWindow`'s content grid (browser / splitter / canvas / splitter / details, plus the
condition-search sub-column) is **removed** and replaced by a single `dock:DockControl` bound to
a `Layout` (`IRootDock`) built by an `EditorDockFactory`.

### Packages

- `Dock.Avalonia`
- `Dock.Model.Mvvm` (the MVVM `Factory` / `Tool` / `Document` / `RootDock` base types)
- `Dock.Serializer.SystemTextJson` (matches the app's existing System.Text.Json use)

Pin to the Dock **11.3.x** line matching Avalonia **11.3.14** (the implementer confirms the
exact compatible version at add-time). `DockFluentTheme.axaml` is merged in `App.axaml`.

### Components

- **`EditorDockFactory : Factory`** (new, `DialogEditor.Avalonia/Docking/`). `CreateLayout()`
  builds the default tree (below); `InitLayout()` registers `ContextLocator` /
  `DockableLocator` / `HostWindowLocator` so serialized layouts re-hydrate live content by id.
  Constructed with references to the four live sub-VMs.
- **Wrapper dockables** (thin, `DialogEditor.Avalonia/Docking/`):
  - `CanvasDocument : Document` — hosts `ConversationViewModel`; `Title` tracks the open
    conversation name; stable `Id = "Canvas"`.
  - `BrowserTool : Tool` (`Id = "Browser"`), `DetailsTool : Tool` (`Id = "Details"`),
    `ConditionSearchTool : Tool` (`Id = "ConditionSearch"`) — each hosts its VM and exposes a
    localised `Title`.
  Each wrapper exposes its inner VM as a `Content`/`Context` property that the view DataTemplate
  binds to (rendering the existing `GameBrowserView` / `ConversationView` / `NodeDetailView` /
  `ConditionSearchView` unchanged).
- **`MainWindowViewModel`** — drops `IsBrowserExpanded` / `IsDetailExpanded` / `IsBrowserPinned`
  / `IsBrowserFlyoutOpen` and the condition-search toggle; gains `Layout` (`IRootDock`),
  `Factory`, and commands `ResetLayoutCommand` + per-tool `ShowToolCommand`. It still constructs
  and owns the four sub-VMs and performs all cross-panel wiring (`Canvas.ActiveGameId`,
  node-selection → details, `ConditionSearch` apply/clear) exactly as today — those operate on
  the same sub-VMs, now hosted inside dock tools.
- **`MainWindow.axaml`** — the content grid is replaced by `<dock:DockControl Layout="{Binding
  Layout}" Factory="{Binding Factory}" InitializeLayout="True" InitializeFactory="True"/>`. The
  menu bar gains a **View** menu.
- **DataTemplates** — map each wrapper VM type to a view that hosts the corresponding existing
  UserControl, so no panel view is rewritten.

### Default layout (built by `CreateLayout()`)

```
RootDock
└─ ProportionalDock (Horizontal)
   ├─ ToolDock (Left, ~0.18)        → BrowserTool
   ├─ ProportionalDockSplitter
   ├─ DocumentDock (CanCreateDocument = false)  → CanvasDocument (single)
   ├─ ProportionalDockSplitter
   └─ ToolDock (Right, ~0.24, auto-hide capable) → DetailsTool (active) + ConditionSearchTool  [tabbed]
```

## Data flow

```
Startup → MainWindowViewModel builds sub-VMs → EditorDockFactory(subVMs)
   → load layout.json (or CreateLayout default) → InitLayout re-attaches live content by id
   → DockControl renders it
User docks/floats/pins/tabs → Dock mutates the live IRootDock
App exit / Reset → serialize IRootDock → layout.json
```

## Persistence

- **Serializer:** `Dock.Serializer.SystemTextJson`. Dock serializes layout **structure**
  (dockable ids, proportions, pin/float state) — not the live VMs; content re-hydrates by id via
  the factory locators.
- **Location:** `%LOCALAPPDATA%\PillarsDialogEditor\layout.json` (same dir as `settings.json` /
  `app.log`).
- **Lifecycle:** load on startup; save on app exit and on Reset.
- **Robustness (load-bearing):** a missing / corrupt / version-incompatible `layout.json` must
  **never crash** — catch, `AppLog.Warn`, fall back to the `CreateLayout()` default. Persisted
  layouts can break across a Dock package upgrade or a tool-id change.
- **Reset Layout** (View menu) discards the saved layout and rebuilds the default. A tool added
  in a future version but absent from an older saved layout is reachable via the View menu or a
  Reset.

## Theming

- `DockFluentTheme.axaml` supplies the chrome (tab headers, grips, guide-diamond dock targets,
  splitters, floating-window title bars). A **Dock override dictionary**, merged *after* the Dock
  theme, maps Dock's themable brushes onto the app's existing `Brush.*` tokens (tab bg/fg,
  active-tab accent, splitter, dock-target glyphs, float title bar).
- **`NoStrayHexTests` compliance:** the override dictionary uses `{DynamicResource Brush.*}` /
  `{StaticResource Palette.*}` only — no raw hex. (Dock's own internal hex is inside the NuGet
  package, which the scanner does not touch.)
- **Live theme switching:** because the Dock chrome binds `DynamicResource Brush.*`, the existing
  runtime theme switch (Layer 2, four palettes) retints the dock chrome automatically.
- **Floating windows:** each floated tool opens in a Dock `HostWindow` (a separate top-level). It
  must (a) carry the app icon (`Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`, per the
  project window-icon rule) and (b) merge the app theme dictionaries, so a floated panel matches
  the main window and retints with it.

## Cross-cutting requirements (project rules)

- **TDD** for the non-trivial pure logic: factory default-layout shape, serialization
  round-trip, and the persistence-load fallback.
- **Localisation** — tool `Title`s, the View menu, and any Dock-surfaced strings come from
  `Strings.axaml` via `Loc`; `NoHardcodedUiStrings` applies. Dock's built-in context-menu strings
  (float/dock/close) are a **known localisation gap** noted here (they come from the package);
  revisit if the app is translated.
- **Tooltips** — new interactive controls (View menu items) carry `ToolTip`s.
- **UIA** — tools/tabs discoverable by `Title`; View-menu items by localised `Header`. Do not
  suppress Dock's automation peers.
- **Error handling** — every caught exception logged via `AppLog.Warn/Error`; no bare `catch{}`.
- **Window icon** — floating `HostWindow`s set `app.ico` (see Theming).
- **Tests run serially** — unchanged; new tests follow the existing `Loc.Configure` /
  `GameDataNameService.Clear()` isolation patterns.

## Behavior migrations (removed / changed)

- The browser/details **collapse strips + 📌 pin buttons** and their VM flags
  (`IsBrowserExpanded` / `IsDetailExpanded` / `IsBrowserPinned` / `IsBrowserFlyoutOpen`) are
  **removed** — Dock's pin/auto-hide replaces them. Related strings/automation names for those
  controls are pruned.
- The condition-search **`ToggleConditionSearchCommand`** and the **Edit ▸ "Find Nodes by
  Condition / Script…"** item are replaced by a **View ▸ Condition search** entry that shows/
  focuses the tool (Dock decides placement). `ConversationViewModel.IsConditionSearchVisible` and
  the in-canvas dock column are removed; `ConditionSearchViewModel` itself is unchanged and now
  lives in the `ConditionSearchTool`.
- The condition-search apply/clear highlight wiring (`ApplyConditionHighlight` /
  `ClearConditionHighlight` on `ConversationViewModel`) is unchanged — the tool still calls it.

## Testing

**Unit-testable (TDD):**
- `EditorDockFactory.CreateLayout()` produces the expected default: a root dock whose horizontal
  layout contains a left `ToolDock` with `BrowserTool`, a `DocumentDock` with one
  `CanvasDocument`, and a right `ToolDock` with `DetailsTool` + `ConditionSearchTool`; tools carry
  the expected `Id`s.
- Layout **serialize → deserialize round-trips** to an equivalent structure.
- The persistence loader **falls back to the default** on a corrupt/missing file (file read
  behind a seam), logging a warning; a valid file loads.

**GUI-verified (not unit-testable):** drag-to-dock (guide diamonds), float into a window,
auto-hide/pin, tab reordering, dock-chrome retint under a theme switch, and a floating window's
icon + theme.

## Explicitly out of scope (v1)

- Analysis windows (Flow Analytics, Rep/Disposition Balance, Find in Project) as dockable tools
  (Phase 2).
- Multiple open conversations as document tabs.
- Localising Dock's built-in float/dock/close context-menu strings.
- Per-perspective saved layouts (VS "window layouts" presets) beyond the single persisted layout
  + Reset.
