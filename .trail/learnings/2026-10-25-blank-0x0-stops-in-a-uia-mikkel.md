---
title: "Blank 0x0 stops in a UIA Tab walk are usually foreground loss, not real tab stops"
author: Mikkel
date: 2026-10-25
branch: fix/239-onboarding-empty-tab-stops
cost: { minutes: 6, tokens: 39295 }
id: l-681c8653
scope: repo
status: active
touches:
  - DialogEditor.Tests/Controls/ThemeOnboardingWindowTests.cs
  - DialogEditor.Avalonia.Shared/ThemeOnboardingWindow.axaml
  - DialogEditor.Avalonia/App.axaml.cs
  - docs/ui-ux/reports/2026-10-24-whole-app/shots/walk/firstrun-onboarding-tab3.png
---

## Decided
GitHub issue 239 (blank tab stops in `ThemeOnboardingWindow`) was not an app defect. In the running app the Tab cycle is Theme ComboBox ↔ Continue, both named and visible. `ThemeOnboardingWindowTests.TabCycle_HasOnlyVisibleNamedStops` guards it.

## Watch out
In a UIA keyboard walk, a step that reports a blank, unnamed `0,0 0x0` focused element usually means the window under test lost the foreground: `AutomationElement.FocusedElement` has nothing to report and that Tab press is dropped (the next step repeats the previous control instead of advancing). Tell it apart from a real stop: a real stop recurs at the same position every cycle, and the screenshot shows an active title bar with a visible focus ring. Before filing or fixing a "blank tab stop" finding from the 2026-10-24 UI/UX pass, re-walk it live and check for that pattern.
