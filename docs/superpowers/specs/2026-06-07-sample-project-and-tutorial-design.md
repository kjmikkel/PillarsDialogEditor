# Sample Project & Beginner Tutorial — Design

**Date:** 2026-06-07
**Status:** Approved (pending spec review)
**Topic:** A guided, safe sandbox that teaches the full editor workflow — especially the
version-control features — to newcomers for whom version control is unfamiliar.

---

## Goal & motivation

Version control (Branches, History, Compare, Attribution) is the part of the editor most
likely to overwhelm a new user, because the *concept* is unfamiliar in the abstract. The
fastest way to make it click is a throwaway sandbox where a newcomer can click the tools
and watch them work, with nothing of theirs at stake.

We deliver two coupled things:

1. An in-app **Create Sample Project…** command that generates a real, install-matched
   sample `.dialogproject` for whichever game folder is loaded (PoE1 *or* PoE2), seeds a
   small git history so the VC tools have something to show, and opens it.
2. A standalone **walkthrough document** that drives a complete beginner through the
   end-to-end workflow using that sample.

Because the sample is generated from the user's *own* installed game data (Approach A,
chosen during brainstorming), it always matches their baselines — the canvas renders and
`F5` never throws a spurious conflict on the very first try.

---

## Scope

**In scope**

- A `Create Sample Project…` command, available only when a game folder is loaded.
- A `SampleProjectService` that builds the sample `DialogProject` and seeds its git
  history, with no UI dependencies.
- A new top-level **Help** menu (always rendered last) hosting the command.
- A standalone Markdown tutorial covering the full workflow for both games.
- All new user-facing strings localized; all caught exceptions logged.

**Out of scope (now)**

- An in-app guided tour / first-run highlight overlay. Recorded under *Future
  enhancements*; the standalone doc covers the need immediately ("doc now, in-app later").
- Shipping canned/embedded sample data or a pre-built `.git` repo (rejected: baseline
  drift across editions). The sample is always generated from the loaded game.

---

## Deliverable 1 — Create Sample Project command

### Target conversations (fixed, hand-picked)

The sample targets the **same conversation each time** per game — early, core scenes that
are stable across editions and unlikely to ever be removed or re-patched at this stage in
the games' lives:

| Game | Conversation | Why |
|------|--------------|-----|
| PoE1 | **`companion_cv_eder_intro`** (Eder, first meeting in Gilded Vale) | Early, branching, recognizable; good canvas content. |
| PoE2 | **`companion_eder_hub`** (Eder's companion conversation hub) | Eder-centric, so the deterministic anchor node and the "keep Eder's tone" translator note stay coherent (the awakening scenes mix in other speakers). |

These are stored as constants in `SampleProjectService` (`Poe1SampleConversation`,
`Poe2SampleConversation`), keyed off `IGameDataProvider.GameId` (`"poe1"` / `"poe2"`).
The exact `ConversationFile.Name` value for each is resolved from the loaded game's
conversation browser during implementation (see *Open items*). If the configured
conversation is not found in the loaded folder, the command aborts with a localized
"sample conversation not available" status rather than crashing.

### Component: `SampleProjectService` (in `DialogEditor.Patch`)

Lives alongside `DiffEngine` and the git services it composes. Pure and testable; no UI.
Dependencies passed in: `IGameDataProvider` (Core) and `IGitRunner` (Patch).

Responsibilities:

- **`BuildSampleProject(IGameDataProvider provider)` → `DialogProject`**
  1. Select the target `ConversationFile` by the game-keyed constant.
  2. `LoadConversation` → baseline `ConversationEditSnapshot`.
  3. Produce an edited snapshot applying the four demo edits (below).
  4. `DiffEngine.Diff(name, baseSnap, editedSnap, provider.Language)` → `ConversationPatch`.
  5. `DialogProject.Empty(sampleName).WithPatch(patch)` (+ translator note / translation
     carried by the patch). Return it.

- **`SeedHistory(string repoDir, string projectFilePath, IGameDataProvider provider)`**
  Best-effort. Uses `IGitRunner` to lay down the curated history (below). Git optional:
  if git is absent or a command fails, leaves whatever succeeded and signals partial
  completion to the caller (which surfaces a localized status). Never throws to the UI.

### The four demo edits (each teaches a feature)

| Edit | Teaches |
|------|---------|
| Change one node's **DefaultText** | Compare highlighting + a translatable line for export. |
| Add **a new node + a link** to it | Node addition in the diff and on the canvas. |
| **Remove a node** | Node deletion in the diff (and how Compare/Attribution surface removals). |
| Add a **translator note** to a node | The translation-export "writer comment" column. |

The removed node is a leaf (no nodes link *from* it onward) so the deletion can't orphan a
branch of the conversation — keeping the sandbox tidy and the canvas readable.

### Git history shape

So every VC tool has something to show, and `main` is the end state:

```
main:   C1 "Initial sample"      (text edit)
            │
        C2 "Reshape the scene"   (add a node + link + translator note; remove a leaf node)
            │
            ├── experiment:  C3 "Try an alternate greeting" (one more text edit)
            │
        (HEAD back on main)
```

- **History** → a real timeline (C1, C2).
- **Attribution** → different last-editing commits per node.
- **Branches** → two local branches (`main`, `experiment`).
- **Compare** → `main` ↔ `experiment` shows a genuine difference; `main` ↔ C1 shows the
  added node.

### Output layout

The command writes into a user-chosen **empty** folder:

```
<folder>/
  sample-poe1.dialogproject   (or sample-poe2)
  .git/                       (curated history, only if git is installed)
```

After writing and seeding, the command **opens the sample project** in the editor so the
user lands directly in it.

### Component: command + menu wiring

- A relay command on `MainWindowViewModel` (e.g. `CreateSampleProjectCommand`),
  `CanExecute` = a game folder is loaded. Orchestrates: prompt for an empty target folder
  → `BuildSampleProject` → write via `DialogProjectSerializer` → `SeedHistory` → open the
  project. Owns status text and logging.
- **MainWindow**: a new top-level **Help** menu, positioned **last** among the menus —
  a small getting-started *hub* rather than a one-item menu:
  - **Create Sample Project…** (`ToolTip` explaining it). Reuses the existing
    `ProcessGitRunner`. Disabled (greyed) with an explanatory tooltip when no game folder
    is loaded.
  - **Open Walkthrough…** — opens the shipped beginner walkthrough document (Deliverable 2)
    via the OS default handler, with a fallback to the project's online docs URL if the
    bundled file isn't found. Always enabled (no game folder required).
  - Leaves room for a future **About…** item (tracked as a gap in `Gaps.md`); not built
    here.

### Error handling

| Situation | Behavior |
|-----------|----------|
| No game folder loaded | Command disabled; tooltip "Open a game folder first." |
| Chosen folder not empty | Refuse with a localized status; never clobber existing files. |
| Target conversation not found | Localized "sample conversation not available"; abort cleanly; `AppLog.Warn`. |
| Conversation load/parse fails | `AppLog.Error`; localized status; abort cleanly. |
| Git not installed / git command fails | Still create and open the project; status: "Sample created. Install Git to try the version-control tools." Seeding is best-effort; `AppLog.Warn` on partial failure. |
| User cancels the folder picker | `OperationCanceledException` swallowed silently (per CLAUDE.md). |
| Walkthrough document can't be opened (missing locally and URL launch fails) | `AppLog.Warn`; localized status pointing the user to the docs location. |

### Localization & logging

All new user-facing text (menu header, item, tooltips, every status) defined in
`Strings.axaml`. `DialogEditor.Patch` stays log-free (it sits below `AppLog`); the VM logs.
Every caught exception is logged via `AppLog.Warn`/`AppLog.Error` except
`OperationCanceledException`.

---

## Deliverable 2 — Beginner tutorial document

- **One document**, in `docs/` and shipped alongside the README, with **PoE1/PoE2
  callouts** only where the two games differ (e.g. the PoE2 speaker name-picker).
- **Voice:** hand-held, imperative "do this now on the sample," distinct from the README's
  reference tone.
- **Through-line:** the same companion across both games — *meet Eder in Gilded Vale*
  (PoE1) / *reunite with Eder at Port Maje* (PoE2).

### Outline

1. **What this is / safety promise** — nothing here touches the real game or your real
   work; this is a sandbox you can delete freely.
2. **Open your game folder** (and the one-time backup prompt).
3. **Create the sample project** (Help ▸ Create Sample Project…) — what just got made.
4. **Browse & edit** — open the Eder conversation, change a line, add a node.
5. **Save** to the project.
6. **Test in-game (`F5`) and restore (`F6`)** — emphasize the backup safety net.
7. **Translate** — export for translation using the sample's translator note.
8. **Trying version control safely** — the dedicated VC section: Branches (switch to
   `experiment` and back), History, Attribution, Compare. Explicit that git is optional and
   that experimenting can't harm anything.
9. **Where to go next** — pointer to the README reference and the Patch Manager.

**In-app entry point:** reachable any time from **Help ▸ Open Walkthrough…** (see the menu
wiring above), so a user who created the sample can open the matching guide without leaving
the editor.

No automated test for the prose content. A manual link/step check before release.

---

## Testing strategy (TDD)

Red/green/refactor throughout; tests in `DialogEditor.Tests` mirroring structure.

- **`SampleProjectService`** with a **fake `IGameDataProvider`** (returns a small canned
  conversation) and **fake `IGitRunner`**:
  - `BuildSampleProject` produces a `DialogProject` whose patch has the expected text
    modification, the added node, the **deleted node**, the translation entry, and the
    translator note.
  - Target-conversation selection is keyed correctly by `GameId`.
  - Missing target conversation → defined failure (no throw to UI).
  - `SeedHistory` issues the expected git **command sequence** — each commit preceded by
    its own stage step, with the project file rewritten between commits:
    `init → add → commit (C1) → add → commit (C2) → checkout -b experiment → add →
    commit (C3) → checkout main` — asserted locale-safely via the fake runner's recorded
    calls.
  - Git-missing → a `DialogProject` is still produced and no history is required.
- **Command enablement**: `CreateSampleProjectCommand.CanExecute` is false with no game
  loaded, true once a provider is set. The **Open Walkthrough** command is always enabled.
- **Walkthrough launcher**: the open is delegated through an injectable seam (e.g. a
  `Func<string,bool>` document-opener) so a test can assert it targets the bundled file and
  falls back to the docs URL when the file is absent — without launching a real process.
- All new strings present in `Strings.axaml` (exercised via `StubStringProvider` keys).

---

## Open items (resolve at implementation, not blockers)

- The exact `ConversationFile.Name` constants for the two Eder conversations, read from the
  loaded game's conversation browser. Selection criteria are fixed (above); only the literal
  identifier is to be confirmed.

---

## Future enhancements (not now)

- **In-app guided tour** — a first-run experience that highlights controls step-by-step,
  building on this sample. The standalone doc is the stepping stone.
