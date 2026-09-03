# UI Automation MCP Server — Design

**Date:** 2026-09-03
**Status:** approved design, not yet implemented
**Related:** [issue #15](https://github.com/kjmikkel/PillarsDialogEditor/issues/15),
`docs/2026-09-03-uia-addressability-audit.md`, `.claude/skills/running-the-app`,
`tools/ui-automation/DriveApp.ps1`

## Problem

End-to-end GUI verification currently means hand-writing PowerShell against
`DriveApp.ps1` for every run. The script itself is sound, but using it carries a set of
memorised gotchas that are re-learned whenever they are forgotten: escape `[` as `{[}` in
SendKeys, filter Name lookups by `ControlType.Edit` because labels and TextBoxes collide,
`return ,$list` so empty lists survive the pipeline, top-level Avalonia menu items support
neither `Invoke` nor `ExpandCollapse` so menus need synthetic clicks. Failures surface as
PowerShell exceptions that must be re-parsed each time, and screenshots come back as file
paths to locate and open separately.

The 2026-09-03 addressability audit added a second, sharper problem: the app's UIA surface
is ambiguous in ways the script silently mishandles. `Invoke-ElementClick` resolves by
`FindFirst(Descendants, NameProperty == X)` — first match in tree order wins — and the
audit confirmed eight same-surface name collisions. A verification run can click the wrong
control and report success.

## Goals

1. Drive the app's GUI through structured tools instead of ad-hoc PowerShell, with the
   accumulated gotchas encoded once in code rather than recalled per session.
2. Invoke builds and tests through the same server, returning parsed results rather than
   raw log output.
3. Make ambiguity and unaddressability **loud**. Where the app cannot be driven properly
   today, the server must say so in its results rather than paper over it.
4. Stay unit-testable under the repo's red/green TDD rule wherever the logic is not I/O.

## Non-goals

- **No in-app component.** Nothing is added to `DialogEditor.Avalonia` on this design's
  account: no remote-control endpoint, no network test hook, no automation backdoor. The
  server drives the app from outside via OS-level accessibility APIs and synthetic input on
  the local desktop, exactly as `DriveApp.ps1` does, per the **UI Automation Support** rule
  in `CLAUDE.md`. If closing an automation gap would ever require weakening the app's
  security posture, the gap stays open.
- **No replacement of `DriveApp.ps1` on day one.** The script keeps working; the server is
  additive until it demonstrably covers the same ground.
- **No CI role.** GUI driving needs an interactive desktop and cannot run headless.
- **No new addressing scheme inside the app.** Fixing the app's identifiability is issue
  #15's job, not this server's.

## Constraints from the audit

These are measured facts about the live UIA tree, and they drive the design:

| Finding | Consequence for this design |
|---|---|
| 37 conversation `TreeItem`s have empty `Name` and **no supported patterns** | Selecting a conversation requires a synthetic click at its rect; must be reported as a defect, not hidden |
| Six pane-chrome buttons named `Avalonia.Controls.Viewbox`, ids duplicated across panes | Name and id are both insufficient; pane scoping is the disambiguator |
| 0 of 51 menu items carry an `AutomationId` | Menu addressing is coupled to localised label text, ellipsis character included |
| The OS system menu is an indistinguishable `MenuItem` peer of File/Edit/… | Menu enumeration must be scoped to the app's `Menu` element; clicking `System` wedges a run |
| Panes **are** reliably named (`LeftPane`, `Documents`, `RightPane`) | Pane scoping is available today and is the design's primary disambiguator |
| Eight same-surface `Name` collisions | The resolver must error on ambiguity, never take first match |

A further constraint comes from a real incident rather than the tree: at the time of the
audit the user's `settings.json` pointed `LastProjectPath` at a temp scratch project that no
longer existed — leaked state from an automation session that died before restoring. Crash
safety is therefore a first-class requirement, not cleanup code.

## Architecture

New project `tools/DialogEditor.UiaMcp`, added to `DialogEditor.slnx`. Target
`net8.0-windows` with `UseWPF` — that property is what supplies the `UIAutomationClient` /
`UIAutomationTypes` assemblies the server needs, the same ones `Initialize-DriveApp` loads
via `Add-Type`. (Note that `UseWPF` swaps in the WPF implicit-usings set, which does *not*
include `System.IO`.) Transport is stdio via the official `ModelContextProtocol` C# SDK:
`AddMcpServer().WithStdioServerTransport().WithToolsFromAssembly()`.

References `DialogEditor.Core` and `DialogEditor.Patch` so scratch projects are written
through the real `DialogProjectSerializer`, for the same reason `New-ScratchProject` does —
hand-rolled JSON risks a silent shape mismatch with the serializer's options.

Two assemblies, split on testability:

| Assembly | Contents | Unit-tested |
|---|---|---|
| `DialogEditor.UiaMcp` | `Program.cs`, tool classes (thin attribute-decorated adapters), `UiaTree` (the real UIA implementation) | No — pure I/O |
| `DialogEditor.UiaMcp.Core` | `Resolver`, `RefTable`, `Selector`, `SettingsGuard`, `BuildOutputParser`, `TestOutputParser`, `IUiaTree`, `ElementInfo` | Yes |

`IUiaTree` is the seam. It exposes only what the resolver needs — enumerate children of a
node, read `ElementInfo` (name, control type, automation id, class name, enabled,
offscreen, focusable, patterns, rect), and invoke a pattern or click a point. The real
implementation wraps `AutomationElement`; the test implementation is an in-memory tree.

**The fake tree is seeded from the audit's actual findings** — `Avalonia.Controls.Viewbox`
six times across two panes, `Language:` on both a `Text` and a `ComboBox`, 37 nameless
patternless `TreeItem`s, menu items with no `AutomationId`. The resolver's rules are thus
tested against the real shape of this app's ambiguity, not invented examples.

Logging goes to stderr only. stdout is the JSON-RPC channel and any stray write corrupts
the protocol stream.

## Session lifecycle

Three tools: `launch_app`, `kill_app`, `session_status`.

```
launch_app(project: "none" | "scratch" | <absolute path>, gameFolder?: string, force?: bool)
```

State held for the session's lifetime: the `Process`, the window's root
`AutomationElement`, and the settings snapshot. A spike confirmed UIA element references
stay valid across separate MCP tool calls for as long as the owning process lives, so
holding them in a DI singleton is sound.

One session per server process. A second `launch_app` while one is live is an **error**,
not an implicit kill, unless `force: true` — silently killing an app the caller believes is
still under inspection is worse than a refusal.

`SettingsGuard` owns the settings state machine, ported from `DriveApp.ps1` and hardened:

1. **On server startup**, before serving any tool: if a stale backup file exists, restore
   it and report that it did. This is the net that would have caught the leaked
   `LastProjectPath` described above.
2. Per launch: snapshot to a **file** (never overwriting an existing backup — an older one
   holds the genuine settings), then mutate `LastProjectPath`, then launch.
3. `kill_app` kills the process **first**, then restores. The app writes settings on exit
   and wins any race.
4. A process-exit handler restores as well, so a server crash cannot strand user state.
5. Every fatal error path restores before returning.

The settings path is **injected, not static**. `DialogEditor.Tests` already runs serially
because `AppSettings`/`Loc` are global state; a new static settings path would enlarge
exactly that problem.

`launch_app` waits for the main window to appear rather than sleeping a fixed interval (the
spike's hardcoded 6s sleep dominated its 6.8s launch time).

## Addressing and the resolver

Every element-taking tool accepts **either** a `ref` **or** a selector:

```
Selector {
  name?:         string    // exact match on UIA Name
  controlType?:  string    // e.g. "Button", "ComboBox", "MenuItem"
  automationId?: string
  withinPane?:   string    // pane Name or AutomationId, e.g. "Node Details" | "RightPane"
  nth?:          int       // 0-based; ONLY this suppresses an ambiguity error
}
```

Resolution order:

1. If `withinPane` is given, resolve that pane first (by `Name` or `AutomationId`) and
   search only its subtree. Panes are reliably named, so this is the primary disambiguator.
2. Match every criterion supplied. Criteria are ANDed.
3. **0 matches → `NotFound`**, carrying near-misses on name (case-insensitive
   substring matches, then Levenshtein distance <= 3, capped at 10), the surface searched,
   the panes currently open, and the active UI language.
4. **>1 match → `Ambiguous`**, listing every candidate with the field that would separate
   them.
5. Never silent first-match. This is the single most important departure from
   `Invoke-ElementClick`.

`RefTable` mints refs (`ref_12`) per `read_tree` call with a generation number. A ref from
an older generation, or one whose element no longer validates, returns `StaleRef` rather
than acting on the wrong element.

## GUI tool surface

**Reads**

| Tool | Purpose |
|---|---|
| `read_tree(withinPane?, filter: "interactive" \| "all", maxDepth?)` | The tree with refs, element properties, and addressability warnings |
| `find(query)` | Substring match across name/id/type; cheap alternative to a 190-element dump |
| `menu(path?: string[])` | Enumerate items with enabled state at that menu path (omit for the top-level bar); always scoped to the app's `Menu`, never the OS system menu |
| `window_title()` | Carries `[ProjectName]` and the `●` dirty marker |
| `read_status_bar()` | The `StatusLiveRegion` text |
| `screenshot()` | Returns the image in the tool result |

**Actions**

| Tool | Notes |
|---|---|
| `invoke(ref \| selector)` | Pattern-preferred with warned fallback — see below |
| `invoke_menu(path: string[])` | e.g. `["File", "Save Project"]`; accepts names or, once #15 finding 4 lands, ids |
| `send_keys(keys)` | Brings the window foreground first |
| `set_value(ref \| selector, text)` | `ValuePattern`, falling back to focus-and-type |
| `focus(ref \| selector)` | |

`invoke` is where the design's third goal lives. It uses a real UIA pattern (`Invoke`,
`Toggle`, `SelectionItem`, `ExpandCollapse`) when the element exposes one. When none
exists, it falls back to a synthetic click at the element's clickable point **and says so in
the result**, naming the defect and its issue reference. So clicking a conversation today
succeeds *and* reports that the element exposes no pattern, per audit finding 1.

`read_tree` emits the same class of warning structurally: unnamed focusable elements,
type-name leaks in `Name`, and colliding names within the returned scope.

This is what keeps the accessibility signal load-bearing. `DriveApp.ps1`'s friction is
useful — an element that cannot be found by name is a defect a screen-reader user would
also hit. A tool that routed around that friction silently would let the app become less
accessible while verification runs got greener.

**Offscreen handling.** `GetClickablePoint()` throws `NoClickablePointException` for
scrolled-out elements, and the 37 conversation rows live in a scrolling list where most are
offscreen at any moment. `invoke`, `set_value` and `focus` therefore scroll the target into
view first (`ScrollItemPattern` where exposed) and, where that is impossible, fail with
`NotOperable` stating why. `DriveApp.ps1` does not handle this case at all.

## Build and test tools

| Tool | Returns |
|---|---|
| `build(configuration?: "Debug" \| "Release", project?)` | Success flag, error and warning counts, and the first 20 diagnostics as `file:line: message` (errors before warnings), plus a truncation note when more exist |
| `run_tests(filter?, project?)` | Pass/fail/skip counts, plus failing test names with assertion messages |

Both spawn `dotnet` as child processes, need no GUI session, and work independently of one.
Results are parsed rather than raw: a Debug build of this solution emits eight warnings
across dozens of lines, and raw output from a 219-file test suite is considerably worse.

`run_tests` excludes the GUI category by default (see below).

`BuildOutputParser` and `TestOutputParser` are pure functions over captured output, unit
tested against recorded fixtures.

## Error contracts

Errors are returned as tool results with `isError`, never as protocol faults, so a caller
can recover. Each names its own remedy.

| Error | Carries | Remedy named |
|---|---|---|
| `NoSession` | — | call `launch_app` |
| `NotFound` | near-misses (substring, then edit distance <= 3, max 10), surface searched, open panes, active UI language | fix the name, or open the right pane |
| `Ambiguous` | all candidates + distinguishing field | add `controlType` / `withinPane` / `nth` |
| `StaleRef` | generation held vs current | re-run `read_tree` |
| `NotOperable` | patterns present, whether a clickable point exists | none — this is the #15 signal |
| `AppExited` | exit code, and confirmation settings were restored | relaunch |
| `NotBuilt` | expected exe path | call `build` |

`NotFound` reports open panes and UI language because audit finding 4 makes "not found"
ambiguous between *renamed*, *translated*, and *wrong surface open*. Reporting all three
collapses a debugging round trip that would otherwise look like a missing control.

## Testing strategy

**Tier 1 — pure unit, full red/green, in `DialogEditor.Tests`.** Mirrors the source layout.
Covers: resolver rules against the audit-seeded fake tree (scoping, AND-ing, the
zero-match and multi-match contracts, `nth`); `RefTable` generation and staleness;
selector parsing; `SettingsGuard`'s state machine against a temp directory including the
stale-backup-recovery and never-overwrite-an-existing-backup rules;
`BuildOutputParser`/`TestOutputParser` against recorded fixtures. This is where the TDD
rule applies in full — a failing test precedes every one of these.

**Tier 2 — live-app integration, excluded by default.** Marked `[Trait("Category","Gui")]`
and filtered out of the normal `dotnet test` run. It requires an interactive desktop,
costs seconds per launch, and cannot run headless; folding it into the default suite would
make the whole suite slow and flaky.

**Tier 3 — acknowledged gap.** The thin tool adapters and the real `UiaTree` are I/O
against a live process. They are not unit-testable, they are roughly a third of the code,
and they get tier-2 coverage only. TDD does not reach them.

There is a payoff: once `read_tree` emits addressability warnings, it becomes the
live-UIA-tree enforcement mechanism that the audit identified as necessary for #15 phases
2–4 but left as an open design question. That test runs in tier 2.

## Sequencing against issue #15

The two efforts are circular — this server would help verify the #15 fixes, while #15 is
what makes this server's addressing clean. The resolution is to interleave them and give
every workaround an expiry date: each fallback path in the server carries its issue
reference, and **deleting a fallback is part of the definition of done for the
corresponding #15 phase**. Without that rule the fallbacks calcify into permanent
furniture.

Implementation order, front-loaded so early steps stand alone:

1. **Session + build/test tools.** No addressing, no dependency on #15, useful immediately.
2. **`read_tree` / `find` + resolver.** Pure logic, full TDD.
3. **Actions**, pattern-preferred with warned fallback.
4. **As #15 phases land**, delete the corresponding fallback and promote the warnings into
   the tier-2 enforcement test.

Steps 1–2 carry none of the GUI-driving risk, so they remain valuable even if later steps
stall.

One coupling worth pulling forward: a robust `invoke_menu` wants #15 finding 4 (stable
`AutomationId`s on all 51 menu items), or it stays welded to localised label text. The
server accepts both forms so it does not block on that, but it is the highest-leverage
small fix in #15 for this tooling.

## Localisation

Nothing in this server is user-visible, so the localisation rule does not apply to its own
strings — but the rule is why the audit's naming findings matter. Automation names are
spoken to users, so any name added to the app in #15 must be a resource reference, and the
server's own diagnostics should quote the active UI language so a locale mismatch is
diagnosable rather than mysterious.
