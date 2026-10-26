---
title: Headless Avalonia never fires a DispatcherTimer from an empty RunJobs(); post a no-op job first
author: Mikkel
date: 2026-10-26
branch: feat/312-squiggle-delay
cost: { minutes: 10, tokens: 57218 }
id: l-3cf27f93
scope: repo
status: active
touches:
  - DialogEditor.Tests/Services/SpellSpanShiftTests.cs
  - DialogEditor.Avalonia/Behaviors/SpellSquiggles.cs
---

## Decided
GitHub issue 312: `SpellSquiggles` (`DialogEditor.Avalonia/Behaviors/SpellSquiggles.cs`) re-checks spelling on a one-shot `DispatcherTimer` 500 ms after the last edit. Its real-timer test, `SpellSquigglesBehaviorTests.TypingAMisspelling_SquiggleAppearsAfterThePause`, posts `Dispatcher.UIThread.Post(() => { })` before each `RunJobs()` in its wait loop. For deterministic tests the behaviour also exposes `HasPendingCheck` and `CheckNow` hooks.

## Tried and ruled out
A loop of `Dispatcher.UIThread.RunJobs()` plus `Thread.Sleep` never fires the timer. Headless installs `ManagedDispatcherImpl`, whose timers fire only from a real run loop. `RunJobs()` loops over queued jobs and promotes due `DispatcherTimer`s only inside `ExecuteJob`, after a job has run. With an empty queue no job runs, so nothing is promoted. Seen when decompiling Avalonia 11.3.22.

## Watch out
The same applies to every headless test that waits on a `DispatcherTimer`, including the headless render timer: a timer-driven change seems to "never happen" until something queues work.
