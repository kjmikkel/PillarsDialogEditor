# Contributing

Thanks for wanting to help with Pillars Dialog Editor. This page covers how to build and test
the code, and the rules every change has to follow. Everyone taking part in the project is
expected to follow the [code of conduct](CODE_OF_CONDUCT.md).

Before designing a feature or reviewing a screen, read [who the tools are for](docs/audience.md):
the Reader and the Modder come first, and the page says what to do when they conflict.

## Issues first

Everything goes through [GitHub Issues](https://github.com/kjmikkel/PillarsDialogEditor/issues):
bugs, feature ideas, design questions and deferred work. There is no other tracker.

- **Search before you file.** Search open *and* closed issues. If you find a match, add a
  comment with your case there instead of opening a duplicate.
- **For anything bigger than a small fix, open or comment on an issue before writing code**, so
  we can agree on the approach first.
- **The repository is public.** Leave local paths, user names, machine names and files from
  your game install out of issues, pull requests and commits.
- **Reference the issue in commits and pull requests**, e.g. `fix: … (#123)`.

Security problems are the exception: report those privately, as described in [SECURITY.md](SECURITY.md).

## Building and testing

You need the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) on Windows.
Python 3.12 and `pytest` are only needed for the generator tools in `tools/`.

```powershell
dotnet build DialogEditor.slnx
dotnet test DialogEditor.slnx --filter "Category!=Gui&Category!=GameData&Requires!=AudioDevice"
```

The filter matters:

| Category | Why it's filtered out |
|---|---|
| `Category=Gui` | Launches the real app on your desktop and drives it through UI Automation. Run it on purpose, and don't touch the mouse or keyboard while it runs. |
| `Category=GameData` | Opt-in checks against a real game install. See [docs/game-data-tests.md](docs/game-data-tests.md). They report as skipped when no install is configured. |
| `Requires=AudioDevice` | Needs a sound device, which CI runners don't have. |

The test suite runs serially by design: some tests share process-wide settings and
localisation state.

To run the Python generator tests:

```powershell
cd tools/catalogue-gen; python -m pytest -q
cd tools/lexicon-gen;   python -m pytest -q
```

To build release archives, see [docs/building.md](docs/building.md).

## Rules for every change

### Test-driven development

Non-trivial logic is written red/green:

1. **Red:** write a test that describes the behaviour, run it, and watch it fail for the
   right reason.
2. **Green:** write the least code that makes it pass.
3. **Refactor:** clean up while the tests stay green.

Tests live in `DialogEditor.Tests`, laid out to mirror the project they test. A bug fix starts
with a test that reproduces the bug.

### No hard-coded user-visible text

Every string a user can see must come from a resource dictionary or `.resx` file, never inline
in XAML or C#. That covers labels, tooltips, status messages, errors, placeholders and help
text, so the app can be translated without touching code. Text that is deliberately English
(such as crash-report text meant for the issue tracker) is marked with `[NotLocalised("why")]`.

### Every control has a tooltip

Every interactive control (buttons, icon-only actions, menu and toolbar items, inputs,
checkboxes, dropdowns) needs a `ToolTip` that explains in plain language what it does. Symbols
like ⌂, ⊞ or + don't explain themselves. The only exception is a control whose label says it
all in context, such as **OK** and **Cancel** on a confirmation dialog. When in doubt, add the
tooltip.

### Keep the app drivable by UI Automation

End-to-end checks drive the app through Windows UI Automation, so controls must be findable by
their automation name. Menu items get it from their localised header, and other controls may
need `AutomationProperties.Name`. Don't suppress automation peers. Never add remote-control
endpoints or in-app test backdoors for this: if supporting automation would ever weaken the
app's security, security wins.

### Log every caught exception

In production code, every caught exception is logged with `AppLog.Error(...)` or
`AppLog.Warn(...)`. Bare `catch { }` blocks aren't allowed. The one exception is
`OperationCanceledException`, which means the user cancelled and is swallowed silently. Test
cleanup code may ignore failures.

### Windows

Every new `Window` sets `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.

### Changelog

Don't edit `CHANGELOG.md`. Release notes are written when a version is released.

## Pull requests

- Branch from `main` and keep each pull request to one issue.
- CI must be green: the .NET build and tests, the Python generator tests, the source-archive
  check and whitespace hygiene.
- Say in the description which issue it closes, what changed, and how you tested it.

By contributing, you agree that your contribution is licensed under the repository's
[LICENSE](LICENSE).
