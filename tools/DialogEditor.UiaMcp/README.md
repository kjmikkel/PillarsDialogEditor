# DialogEditor.UiaMcp

An MCP server that drives the Dialog Editor's GUI from outside via Windows UI
Automation, and runs builds and tests.

- Design: `docs/superpowers/specs/2026-09-03-uia-mcp-server-design.md`
- Plans: `docs/superpowers/plans/2026-09-03-uia-mcp-server-foundation.md` (phases 1–2),
  `docs/superpowers/plans/2026-09-03-uia-mcp-server-actions.md` (phase 3)
- Audit that shaped it: `docs/2026-09-03-uia-addressability-audit.md`
- Fallbacks and how to retire them: `docs/uia-fallback-inventory.md`

## Security boundary

This server contains **no in-app component**. It drives the editor purely through
OS-level accessibility APIs and synthetic input on the local desktop, exactly as
`tools/ui-automation/DriveApp.ps1` does. The app has no remote-control endpoint,
network test hook, or automation backdoor, and none may be added on this server's
account — see the **UI Automation Support** rule in `CLAUDE.md`.

## Layout

| Project | Target | Contents |
|---|---|---|
| `DialogEditor.UiaMcp.Core` | `net8.0` | `SettingsGuard`, `Resolver`, `RefTable`, `Selector`, `ActionStrategy`, `SendKeysEscaper`, `AddressabilityWarnings`, output parsers, `IUiaTree` |
| `DialogEditor.UiaMcp` | `net8.0-windows` | MCP host, tool classes, `EditorSession`, `UiaTree`, `ActionStrategy` execution |
| `DialogEditor.UiaMcp.Tests` | `net8.0-windows` | `Gui`-traited live-app tests, excluded from the default run |

Core deliberately has **no** UI Automation reference. That is what lets
`DialogEditor.Tests` (which targets `net8.0`) reference and unit-test it; `IUiaTree`
speaks in plain `ElementInfo` records, never `AutomationElement`.

## Registering with Claude Code

```
claude mcp add dialog-editor-uia -- \
  "<repo>/tools/DialogEditor.UiaMcp/bin/Debug/net8.0-windows/DialogEditor.UiaMcp.exe"
```

Requires a Debug build and a real interactive desktop; GUI tools cannot run headless.

## Tools

| Tool | Purpose |
|---|---|
| `launch_app` | Launch the editor, back up settings, hold the session |
| `session_status` | Report the held session |
| `kill_app` | Kill the app and restore settings |
| `read_tree` | Tree with refs plus addressability warnings |
| `find` | Substring search across name, automation id, control type |
| `menu` | List menu items, scoped to the app's menu |
| `window_title` | Window title, which carries `[ProjectName]` and the dirty marker |
| `read_status_bar` | The `StatusLiveRegion` text |
| `build` | Build with parsed diagnostics |
| `run_tests` | Run tests with parsed failures |
| `invoke` | Activate an element by ref or selector |
| `focus` | Give an element keyboard focus |
| `send_keys` | Send a shortcut, or literal text with escaping handled |
| `set_value` | Set a text field via the Value pattern |
| `invoke_menu` | Invoke a menu command by path |
| `screenshot` | Capture the window as an inline PNG |

Actions are implemented as of phase 3. `DriveApp.ps1` remains available and is still the
route for anything not covered here — notably secondary windows (see below).

## Why warnings, not workarounds

`read_tree` reports what it cannot address. That is deliberate: an element unreachable
by name is a defect a screen-reader user hits too, so the harness must keep the signal
visible rather than route around it. Every warning maps to a finding in the audit and
is tracked by [issue #15](https://github.com/kjmikkel/PillarsDialogEditor/issues/15).

Likewise `menu` with a path reports that it had to synthesise a click, because Avalonia's
top-level `MenuItem`s implement neither `Invoke` nor `ExpandCollapse`.

## Behaviour worth knowing

**Settings safety.** `launch_app` snapshots `settings.json` to a file, never overwriting
an existing snapshot — if a previous run died, the older file holds the genuine settings.
`kill_app` kills the app *first*, then restores, because the app writes settings on exit
and would win the race.

**Startup recovery is the primary net, not the backstop.** .NET does not run `ProcessExit`
handlers on a hard kill, which is exactly how a wedged run dies. So the server restores
any stale snapshot it finds *at startup*, and reports that it did.

**Ambiguity is an error.** The resolver refuses to guess between multiple matches; it
returns every candidate with the field that would separate them. Pane scoping
(`withinPane`) is the primary disambiguator.

**Building through the server cannot rebuild the server.** Its own exe is part of
`DialogEditor.slnx` and is locked while it runs. `build` detects that and says so; pass a
specific `project`, or build from a shell with the server stopped.

## Fallbacks and blind spots

Some elements cannot be operated by pattern today, so the server uses synthetic input and
**says so in the result**, naming issue #15. `docs/uia-fallback-inventory.md` lists every
one, the test that will fail when the app is fixed, and how to remove it.

Three behaviours worth knowing before relying on the acting tools:

- **`invoke_menu` refuses a disabled command** rather than clicking it. A disabled item is a
  legitimate `CanExecute` state; clicking it would report success while nothing happened.
- **Ambiguous selectors are refused, not guessed.** `invoke` with
  `name="Avalonia.Controls.Viewbox"` returns all six candidates with their `nth` indices.
  Narrow with `controlType`, `automationId` or `withinPane` — reach for `nth` last.
- **Secondary windows are invisible.** The session tree is rooted at the main window, so
  dialogs and tool windows do not appear in `read_tree`/`find`, and `screenshot` captures
  only the main window's rect. `invoke_menu` can open a dialog it cannot then inspect —
  tracked as [#16](https://github.com/kjmikkel/PillarsDialogEditor/issues/16).

Two traps the code guards against, both found by running it rather than reading it:
synthetic clicking is **stateful** (a leftover open popup swallows the next click, so menu
walks send `{ESC}` first), and `SetForegroundWindow` **dismisses** an open popup (so nothing
foregrounds between opening a menu and clicking an item in it).

## Gui tests

`tools/DialogEditor.UiaMcp.Tests` covers the live layer. It needs a real interactive desktop
and takes over the foreground, so it is a separate project excluded from the default run:

    dotnet test tools/DialogEditor.UiaMcp.Tests --filter "Category=Gui"

Several of its tests deliberately assert that today's defects still exist. When issue #15
fixes them those tests fail — that failure is the signal to delete the matching fallback.

## Manual smoke test

`drive.ps1` speaks raw JSON-RPC over stdio, for checking tools without an MCP client.

```powershell
# Single tool
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Tool session_status

# A sequence against ONE server process, with per-tool arguments
$repo = ((Get-Location).Path -replace '\\','/')
pwsh tools/DialogEditor.UiaMcp/drive.ps1 -Script @"
[{"name":"launch_app","arguments":{"repoRoot":"$repo"}},
 {"name":"menu","arguments":{"path":["File"]}}]
"@
```

Gotchas the script documents in its own header: `-Arguments` must be **JSON** when starting
a new `pwsh` process (a hashtable is stringified to `System.Collections.Hashtable` across a
command line); it auto-calls `kill_app` at the end unless `-KeepAlive` is passed, so a run
cannot orphan the editor with your settings still mutated; and `-ImageOutDir` says where
image blocks are written, because a screenshot's reported dimensions are identical whether
the pixels are right or the frame is blank.
