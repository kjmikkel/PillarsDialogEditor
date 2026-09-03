# PillarsDialogEditor

## Development Approach

Follow strict red/green TDD for all non-trivial logic:

1. **Red** — write a failing test that describes the desired behaviour before writing any implementation code
2. **Green** — write the minimum implementation to make the test pass
3. **Refactor** — clean up without breaking the tests

Never write implementation code for a feature before a failing test exists for it. Tests live in a `DialogEditor.Tests` project mirroring the structure of `DialogEditor.Core`.

## Localisation

No user-visible text may be hard-coded inline in XAML or C#. All strings — labels, tooltips, status messages, error text, placeholder text, legend copy — must be defined in a resource dictionary or `.resx` file so the application can be translated without touching code or markup.

## UI/UX Guidelines

Every interactive control — buttons, icon-only actions, toolbar items, canvas controls, input fields, checkboxes, dropdowns — must carry a detailed `ToolTip` property that explains its purpose and effect in plain language. One-word labels and symbols (⌂, ⊞, ?, +) are not self-explanatory to new users. Tooltips are mandatory; omitting them on new controls is a defect.

The only exception is controls whose purpose is 100% self-explanatory from their label alone in context — for example, **OK** and **Cancel** buttons on a confirmation dialog. When in doubt, add the tooltip.

## UI Automation Support

The app must stay drivable from the outside via Windows UI Automation — the
`running-the-app` skill and `tools/ui-automation/DriveApp.ps1` rely on it for
end-to-end GUI verification. In practice:

- Interactive controls must be discoverable by their UIA Name (menu items get
  this from their localised `Header`; other controls may need
  `AutomationProperties.Name`). Don't suppress or strip automation peers.
- If a verification run can't find a control by name, treat that as a defect to
  fix in the app, not something to script around.
- The boundary: support is **passive and OS-level only** (accessibility APIs,
  synthetic input on the local desktop). Never add remote-control endpoints,
  network test hooks, or in-app automation backdoors on this rule's account —
  if supporting automation would ever require weakening the app's security
  posture, security wins and this rule yields.

## Error Handling

In **production code**, every caught exception must be logged via `AppLog.Error(...)` or `AppLog.Warn(...)` before or after any user-facing status update. The sole exception is `OperationCanceledException`, which represents deliberate cancellation and must be swallowed silently. Bare `catch { }` blocks are not permitted in production code.

This rule does not apply to `DialogEditor.Tests`: best-effort cleanup in test teardown (e.g. `try { File.Delete(...) } catch { /* best-effort */ }`) may swallow silently — a cleanup failure there is noise, not a defect worth logging infrastructure.

## Changelog

`CHANGELOG.md` is **frozen until the initial public release**. Do not add, edit, or
back-fill entries before then — pre-release churn is not changelog-worthy and the file
ships effectively empty (or with a single "unreleased" placeholder). **Remove this rule
when the initial version is published**, after which every release appends its entries.

## Issue Tracking

All issues — defects, feature requests, design gaps, deferred work — are registered as
**GitHub Issues** on `kjmikkel/PillarsDialogEditor`. There is no local issue file.

- File with `gh issue create` (the `gh` CLI is authenticated; the GitHub MCP server is not
  reliably available). Reference issues in commits as `#NNN`.
- The repository is **public**, so issue text is world-readable. Do not paste absolute local
  paths, machine names, or anything from the user's game installs into an issue body.
- `BUGS.md` and `Gaps.md` are **retired and read-only**. They remain in the tree as the
  historical record of pre-launch work and are still linked from test docstrings and specs,
  so do not delete or restructure them — but never add, edit, or re-open an entry in either.
  Anything still open there is migrated to a GitHub Issue when it is next worked on.

## Worktree Cleanup

Before removing a worktree, always run `git -C <worktree-path> status --short` and inspect the output. If there are staged or unstaged changes, determine whether they represent work that should be preserved. If yes, commit them to the branch before removing the worktree.
