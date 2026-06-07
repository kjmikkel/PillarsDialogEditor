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

## Error Handling

Every caught exception must be logged via `AppLog.Error(...)` or `AppLog.Warn(...)` before or after any user-facing status update. The sole exception is `OperationCanceledException`, which represents deliberate cancellation and must be swallowed silently. Bare `catch { }` blocks are not permitted.

## Changelog

`CHANGELOG.md` is **frozen until the initial public release**. Do not add, edit, or
back-fill entries before then — pre-release churn is not changelog-worthy and the file
ships effectively empty (or with a single "unreleased" placeholder). **Remove this rule
when the initial version is published**, after which every release appends its entries.

## Internal Tracking (pre-launch)

`BUGS.md` and `Gaps.md` are **temporary pre-launch** working files for solo development.
**Delete both before the initial public release** — at launch, anything still worth doing is
transferred to GitHub Issues for public scrutiny, and tracking lives there afterwards.

- `BUGS.md` — defect log; newest first; move a fixed entry to the *Fixed* section with the
  fixing commit hash rather than deleting it.
- `Gaps.md` — known design gaps / deferred features.

**Remove this rule when both files are removed.**

## Worktree Cleanup

Before removing a worktree, always run `git -C <worktree-path> status --short` and inspect the output. If there are staged or unstaged changes, determine whether they represent work that should be preserved. If yes, commit them to the branch before removing the worktree.
