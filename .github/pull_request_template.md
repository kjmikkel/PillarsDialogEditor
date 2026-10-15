Closes #

## What changed
<!-- A few lines on what this does and why. Link the spec or plan if there is one. -->

## How I tested it
<!-- Tests added or changed, and anything checked by hand in the app. -->

## Checklist
<!-- See CONTRIBUTING.md for the full rules. Tick what applies, and say why if something doesn't. -->
- [ ] New or changed behaviour has a test that failed before the change
- [ ] `dotnet test DialogEditor.slnx --filter "Category!=Gui&Category!=GameData&Requires!=AudioDevice"` passes
- [ ] No user-visible text is hard-coded: it's all in a resource dictionary or `.resx`
- [ ] Every new interactive control has a tooltip and can be found by its UI Automation name
- [ ] Every caught exception is logged with `AppLog.Error` / `AppLog.Warn`
- [ ] `CHANGELOG.md` is untouched
- [ ] Nothing private: no local paths, user names or files from a game install
