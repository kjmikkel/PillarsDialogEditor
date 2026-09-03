# DialogEditor.UiaMcp

An MCP server that drives the Dialog Editor's GUI from outside via Windows UI
Automation, and runs builds and tests.

- Design: `docs/superpowers/specs/2026-09-03-uia-mcp-server-design.md`
- Plan: `docs/superpowers/plans/2026-09-03-uia-mcp-server-foundation.md`
- Audit that shaped it: `docs/2026-09-03-uia-addressability-audit.md`

## Security boundary

This server contains **no in-app component**. It drives the editor purely through
OS-level accessibility APIs and synthetic input on the local desktop, exactly as
`tools/ui-automation/DriveApp.ps1` does. The app has no remote-control endpoint,
network test hook, or automation backdoor, and none may be added on this server's
account — see the **UI Automation Support** rule in `CLAUDE.md`.

## Layout

| Project | Target | Contents |
|---|---|---|
| `DialogEditor.UiaMcp.Core` | `net8.0` | `SettingsGuard`, `Resolver`, `RefTable`, `Selector`, `AddressabilityWarnings`, output parsers, `IUiaTree` |
| `DialogEditor.UiaMcp` | `net8.0-windows` | MCP host, tool classes, `EditorSession`, `UiaTree` |

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

Actions — clicking, typing, screenshots — are **not** implemented yet; that is the
next phase. Use `DriveApp.ps1` for those meanwhile.

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

Two gotchas the script documents in its own header: `-Arguments` must be **JSON** when
starting a new `pwsh` process (a hashtable is stringified to
`System.Collections.Hashtable` across a command line), and it auto-calls `kill_app` at
the end unless `-KeepAlive` is passed, so a run cannot orphan the editor with your
settings still mutated.
