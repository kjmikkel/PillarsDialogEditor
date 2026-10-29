---
title: Headless tests no longer write the real layout.json; DockLayoutStore.DefaultPath is redirected in the test module initializer
author: Mikkel
date: 2026-10-29
branch: claude/cool-pasteur-dbace8
cost: { minutes: 4, tokens: 70604 }
id: l-61bd2923
scope: repo
status: active
touches:
  - DialogEditor.Tests/Docking/DockLayoutIsolationTests.cs
  - DialogEditor.Avalonia/Docking/DockLayoutStore.cs
  - DialogEditor.Tests/TestSettingsIsolation.cs
---

## Decided
GitHub issue 343. `DockLayoutStore.DefaultPath` is now a `public static` with an `internal` setter, and `DialogEditor.Tests/TestSettingsIsolation.cs` sets it to a per-run temp file, alongside `AppSettings.DefaultSettingsPath` and `AppLog.LogPath`. This supersedes the "watch out" in the Oct 28 learning about headless tests and `layout.json`; the UI automation backup in `DriveApp.ps1` is still needed for real GUI runs.

## Tried and ruled out
An AsyncLocal per-test override (the `AppSettings.SettingsPathOverride` style) does not work for a value set in a module initializer: the value does not reach the execution contexts xUnit runs tests on. A plain static is the established choice here.

## Watch out
`Docking/DockLayoutIsolationTests` has two tests: the real file stays untouched, and the redirected file IS written on close. Keep both. Without the second, the first would pass silently if `MainWindow.OnClosing` stopped saving.
`trail check` flags the `using DialogEditor.Avalonia.Docking;` line in `TestSettingsIsolation.cs` against an unrelated HelpText learning; that is a false positive.
