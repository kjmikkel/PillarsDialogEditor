# PillarsDialogEditor

## Development Approach

Follow strict red/green TDD for all non-trivial logic:

1. **Red** — write a failing test that describes the desired behaviour before writing any implementation code
2. **Green** — write the minimum implementation to make the test pass
3. **Refactor** — clean up without breaking the tests

Never write implementation code for a feature before a failing test exists for it. Tests live in a `DialogEditor.Tests` project mirroring the structure of `DialogEditor.Core`.
