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

Every interactive control — buttons, icon-only actions, toolbar items, canvas controls — must carry a descriptive `ToolTip` property that explains its effect in plain language. One-word labels and symbols (⌂, ⊞, ?, +) are not self-explanatory to new users. Tooltips are mandatory; omitting them on new controls is a defect.

## Worktree Cleanup

Before removing a worktree, always run `git -C <worktree-path> status --short` and inspect the output. If there are staged or unstaged changes, determine whether they represent work that should be preserved. If yes, commit them to the branch before removing the worktree.
