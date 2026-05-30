# Next Steps — pick one at a time

Queued follow-ups from the diff-viewer work (2026-05-30). Each is self-contained:
start a fresh session, read this entry, and tackle it. Nothing here is in progress.

State at time of writing: the **read-only diff viewer (Spec 1) is complete** on `main`
(all 9 tasks, ~892 tests passing). See `docs/superpowers/specs/2026-05-30-diff-viewer-design.md`
and `docs/superpowers/plans/2026-05-30-diff-viewer.md`.

---

## TODO 1 — Spec 2: Selective apply (the second half of diff viewing)

**Goal:** Let the user, from the diff view, choose specific changes and **apply** them into
their working-copy `.dialogproject` (cherry-pick from one endpoint to the other). This is the
"full apply" half of the original "diff + full apply" request; the viewer was Spec 1, apply was
deferred to Spec 2 during brainstorming.

**Build on (already exists):**
- Diff model: `DialogEditor.Patch/Diff/ProjectDiff.cs` + `ConversationChange.cs` (per-conversation Added/Removed/Modified node ids).
- `DialogEditor.Patch/Diff/ProjectVersionLoader.cs` (load a project at an endpoint).
- `DialogEditor.Patch/GitConflict/MergeBuilder.cs` — the overlay/merge engine to reuse for applying chosen changes.
- `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` + `DialogEditor.Avalonia/Views/DiffWindow.axaml` — where apply UI would attach.

**First step:** invoke `superpowers:brainstorming` for "selective apply". 

**Open questions to resolve in brainstorming (don't assume):**
- Apply **target/direction**: the only writable target is the working-copy `.dialogproject`, so apply pulls selected changes from the *other* endpoint into the working copy. Confirm.
- **Granularity**: per-conversation, or per-node/field (checkboxes on individual changes).
- **Overlap with conflict resolution**: how is this different from the merge/conflict tooling we already built? (That's triggered by git markers + resolves mine/theirs; this is a proactive cherry-pick.) Reuse `MergeBuilder`, or a dedicated apply path.
- **Undo integration** and whether apply writes immediately or loads-dirty (mirror the conflict-resolution "open in memory, save on demand" decision).

Then: spec → `superpowers:writing-plans` → subagent-driven execution (Sonnet implementers, per saved model-selection preference).

---

## TODO 2 — Final whole-feature review of the diff viewer

**Goal:** One independent review of the completed read-only diff viewer. Each task was
spec-checked inline as it landed, but there was no whole-feature pass.

**Scope (the diff-viewer commits on `main`, "Diff Task 1–9" series):**
- `DialogEditor.Patch/Diff/*` — `IGitRunner`/`ProcessGitRunner`, `DiffEndpoint`, `DiffException`, `ProjectVersionLoader`, `ProjectDiff`/`ConversationChange`.
- `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`, `DiffStatus.cs`, `NodeViewModel.DiffStatus`.
- `DialogEditor.Avalonia/Views/DiffWindow.axaml(.cs)`, `Converters/DiffStatusToBrushConverter.cs`, `Converters/IsNullConverter.cs`, the `ConversationView.axaml` tint overlay, `MainWindow` menu wiring.
(Find the commit range with `git log --oneline` — they're the most recent `feat:` commits; last is `e553ab1` "read-only diff canvas overlay".)

**How:** either run `/code-review ultra` (multi-agent cloud review — user-triggered, billed), or
ask the assistant to dispatch a final reviewer subagent over those files.

**Focus areas:**
- `ProjectDiff` signature-based semantics — does "Added/Removed/Modified" map intuitively for real two-version diffs?
- `DiffViewModel.BuildDiffCanvas` reconstruction + **ghost-removed-node injection** edge cases.
- The **shared** `ConversationView.axaml` tint overlay — confirm zero visual/layout bleed into the normal editor (overlay border is `Transparent` for `Unchanged`).
- Git error handling (missing git, bad ref, not-a-repo) and the no-game-folder path.

---

## Also outstanding (recorded in `Gaps.md`, not part of the two above)
- **Before/after node-text detail** in the diff canvas — deferred from Task 9 (the canvas tinting shipped; per-node old-vs-new text via `TextDiff` was left out).
- **Branch/history navigation** (browse git log, switch branches, attribution) — not started.
- Minor conflict-resolution display limitations (see `Gaps.md`).
