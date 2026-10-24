---
title: A static event raised off the UI thread silently stops headless Avalonia layout for the whole test run
author: Mikkel
date: 2026-10-24
branch: feat/270-privacy-mode
cost: { minutes: 1, tokens: 12788 }
id: l-7f1bce53
scope: repo
status: active
touches:
  - DialogEditor.ViewModels/Services/PrivacyMode.cs
---

## Watch out
- Symptom: unrelated headless render tests (TableColumnsTests, CanvasCardRenderTests, FlowAnalyticsWindowTests, StackedInputAlignmentTests) fail intermittently in one full run: 0 realised rows, widths unchanged after FontScaleApplier.Apply, CaptureRenderedFrame() null, ContainerFromIndex null. They pass in isolation and on rerun, so they look like load flakiness. They are not.
- Cause: `PrivacyMode.Changed` (DialogEditor.ViewModels/Services/PrivacyMode.cs) is static. View-models created by Avalonia tests subscribe on the headless UI thread and stay alive until GC; a plain [Fact] on the xUnit thread then raises the event and the handler updates bound properties cross-thread. Avalonia swallows the binding error, but layout stops for the rest of the session. GC timing makes it intermittent.
## Decided
- `PrivacyMode.WeakSubscribe` captures `SynchronizationContext.Current` and posts to it when raised from another context, because calling the subscriber inline from the raising thread is what broke layout (test: PrivacyModeWeakSubscribeTests.WeakSubscribe_RaisedFromAnotherContext_PostsToTheSubscribersContext). Any new static change event that view-models subscribe to needs the same treatment, otherwise the same six-test CI failure comes back.
