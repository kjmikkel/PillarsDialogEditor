# Branch management — design

**Date:** 2026-06-05
**Status:** Approved (design)

## Context

`Gaps.md` / `NEXT-STEPS.md` list **branch switching** as the last open VCS gap and
the **first git write operation** in the app. Everything git-related shipped so
far is read-only: `IGitRunner` runs `git show` / `git log` / `git blame`, and
`ProjectVersionLoader` reads a ref into memory without ever touching the working
tree. Branch switching is different — `git checkout` rewrites the `.dialogproject`
**on disk, under the open editor**. That is the entire risk story.

This spec covers the full branch-management feature: **switch, create, rename,
and delete** local branches, surfaced in a dedicated window. Remote operations
(fetch/push/pull) and branch *merging* are out of scope (the existing git-conflict
resolver already handles merge *results*).

## Goal

Let a dialog writer manage the local git branches of the open project from a
single window — see which branches exist and which is current, switch between
them, create a new branch, rename, or delete one — without learning git
vocabulary and without any path that silently loses work. Every destructive or
write operation either succeeds safely or stops and explains itself in plain
language, always offering the writer the next step.

## Guiding principle

Revealed across the brainstorming decisions: **safety first, plain language,
always offer the escape hatch, never take a destructive action silently.** The
two dangerous operations (switching with a dirty tree; deleting an unmerged
branch) both follow the same shape — *refuse, explain, offer the safe resolution
with informed consent.*

## Architecture (Approach A)

Three layers, mirroring how **History** and **Attribution** shipped:

```
BranchesWindow (Avalonia)  ──VM hooks──▶  BranchesViewModel (orchestration)
                                                │
                                                ├─▶ GitBranchService (pure git, typed results)
                                                └─▶ host callbacks ──▶ MainWindowViewModel
                                                       (guard unsaved edits, reload from disk)
```

- `GitBranchService` lives in `DialogEditor.Patch/Diff/` beside
  `ProjectHistoryService`, takes `IGitRunner`, resolves repo dir + relative path
  via the existing `GitRepoPath.ResolveRepoRelative` helper, and **never returns
  raw git text** — every operation returns a **typed result**.
- `BranchesViewModel` owns the branch list and the four commands. Create / rename
  / delete are self-contained (service + confirm callbacks). **Switch** is
  coordinated with `MainWindowViewModel` through two host callbacks, because it is
  the only operation that touches the editor's in-memory state and the open file
  on disk.
- The view never opens windows or runs git directly; it binds to the VM and wires
  host callbacks in code-behind — the established pattern (`ShowGitConflictResolution`,
  `HistoryViewModel.CompareWithCommit`).

### Rejected alternatives

- **Branches panel inside the History window** — welds read-write management onto
  a deliberately read-only timeline; muddies a shipped, focused tool.
- **Menu-only, no window** — no single place to see branch state; awkward home for
  create/rename/delete.

## Key decision: typed results (the linchpin)

Git communicates failure through exit codes + **English** stderr ("Your local
changes would be overwritten by checkout"). If the service returned raw text,
every consumer would re-parse English and the UX could not be localized
(violating the no-hard-coded-strings rule in `CLAUDE.md`). Mapping to an enum at
the service boundary means the VM decides *what to offer* from a stable, testable
signal. This mirrors the existing `DiffException` + `DiffExceptionKind` pattern.

```csharp
public record BranchInfo(string Name, bool IsCurrent);

public enum BranchOpStatus
{
    Ok,
    BlockedByLocalChanges,    // tracked modifications block checkout → offer commit-all
    BlockedByUntrackedFiles,  // untracked files would be overwritten → cannot auto-fix (case A)
    NotMerged,                // safe delete refused → offer force-delete behind strong confirm
    NameInvalid,              // create/rename: name fails git ref-format rules
    NameExists,               // create/rename: a branch with that name already exists
    NotARepo,                 // not a git repo / git missing
    GitFailed                 // any other non-zero exit; Detail carries stderr for the log
}

public record BranchOpResult(BranchOpStatus Status, string? Detail = null);
```

### Locale-safe failure classification

Rather than grepping git's English stderr (fragile across versions and locales),
checkout failures are classified via `git status --porcelain` (git's stable,
machine-readable script contract):

- Checkout fails, and porcelain shows **tracked** modifications (lines not
  starting with `??`) → `BlockedByLocalChanges`.
- Checkout fails, and porcelain shows **only untracked** entries (`??`) →
  `BlockedByUntrackedFiles`.
- Checkout fails with a clean tree → `GitFailed` (genuinely other; `Detail`
  logged).

## Components

### `GitBranchService` — `DialogEditor.Patch/Diff/GitBranchService.cs`

```csharp
public class GitBranchService(IGitRunner git)
{
    IReadOnlyList<BranchInfo> List(string projectFilePath);
    BranchOpResult            Checkout(string projectFilePath, string branch);
    BranchOpResult            Create(string projectFilePath, string newName);   // checkout -b
    BranchOpResult            Rename(string projectFilePath, string? from, string to);
    BranchOpResult            Delete(string projectFilePath, string branch, bool force);

    IReadOnlyList<string>     ListUncommittedChanges(string projectFilePath);   // tracked, for the consent dialog
    BranchOpResult            CommitAll(string projectFilePath, string message);// git commit -a (tracked only)
}
```

All methods resolve repo dir + relative path via `GitRepoPath`; a `rev-parse`
failure surfaces as `NotARepo`.

- **List** — `git for-each-ref refs/heads --format=%(refname:short)%1f%(HEAD)`.
  `%(HEAD)` is `*` for the current branch, space otherwise. Split on `0x1f`
  (`%1f`) like `ProjectHistoryService`. Empty repo (no commits yet) → empty list.
- **Checkout** — `git checkout <branch>`. On failure, classify via porcelain (see
  above). On success → `Ok` (the VM then reloads).
- **Create** — validate `newName` with `git check-ref-format --branch <newName>`
  (`NameInvalid` on non-zero); reject if it already exists (`NameExists`); else
  `git checkout -b <newName>`. Creating from the current HEAD points the new
  branch at the same commit, so the working tree is unchanged: **never blocks, no
  reload needed** — only the current-branch marker changes.
- **Rename** — validate the target name (same rules); `from is null` renames the
  current branch (`git branch -m <to>`), otherwise `git branch -m <from> <to>`.
  Does not touch the working tree.
- **Delete** — `git branch -d <branch>` (safe; git refuses an unmerged branch).
  `-d` failure → `NotMerged`. `force: true` runs `git branch -D <branch>`. The VM
  prevents deleting the **current** branch (git refuses it anyway). Does not touch
  the working tree.
- **ListUncommittedChanges** — `git status --porcelain` filtered to tracked
  entries (drop `??` lines), returning the file paths, for the commit-consent
  dialog.
- **CommitAll** — `git commit -a -m <message>`: commits all tracked
  modifications/deletions across the repo. **Untracked files are not staged**
  (avoids sweeping in build artifacts/temp files a writer can't vet). A commit
  failure (e.g. missing git identity) → `GitFailed` with `Detail` logged.

### `BranchesViewModel` — `DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs`

```csharp
public partial class BranchRowViewModel(BranchInfo info) : ObservableObject
{
    public string Name      => info.Name;
    public bool   IsCurrent => info.IsCurrent;
}

public record PendingCommit(IReadOnlyList<string> Files, string DefaultMessage);

public partial class BranchesViewModel : ObservableObject
{
    public ObservableCollection<BranchRowViewModel> Branches { get; }
    [ObservableProperty] private BranchRowViewModel? _selected;
    [ObservableProperty] private string _statusText;   // empty/error/result state

    public bool HasBranches => Branches.Count > 0;

    // ── Host callbacks (the VM cannot open windows or own editor state) ──
    /// Switch: ensure the editor has no unsaved in-memory edits. Returns false if
    /// the writer cancelled (abort the switch). Wraps the existing UnsavedChanges flow.
    public Func<Task<bool>>?                  EnsureNoUnsavedEdits { get; set; }
    /// Switch: re-read the open project from disk after the working tree changed.
    public Action?                            ReloadProjectFromDisk { get; set; }
    /// Commit-then-switch consent: show the file list + editable message; returns the
    /// chosen message, or null to cancel.
    public Func<PendingCommit, Task<string?>>? RequestCommitConfirmation { get; set; }
    /// Force-delete consent: strong, data-loss-aware confirm for an unmerged branch.
    public Func<string, Task<bool>>?          ConfirmForceDelete { get; set; }
    /// New / Rename: prompt for a branch name (reuses a small text dialog).
    public Func<string?, Task<string?>>?      RequestBranchName { get; set; }

    public BranchesViewModel(GitBranchService service, string projectFilePath);

    [RelayCommand(CanExecute = nameof(CanActOnSelection))] private Task SwitchAsync();
    [RelayCommand]                                          private Task CreateAsync();
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]  private Task RenameAsync();
    [RelayCommand(CanExecute = nameof(CanDelete))]          private Task DeleteAsync();

    private bool CanActOnSelection => Selected is not null;
    private bool CanDelete         => Selected is { IsCurrent: false };
}
```

- Ctor loads `Branches` via `service.List`. On `NotARepo` it catches, sets a
  localized `StatusText`, logs `AppLog.Warn`, leaves `Branches` empty. Empty list
  (no error) → `StatusText` = "no branches yet".
- Every command refreshes the list and re-evaluates `CanExecute` after running.
- All git/result outcomes map to a localized `StatusText`; nothing crashes; every
  caught exception is logged (`AppLog`), `OperationCanceledException` excepted.

### The switch pipeline (the high-risk path)

```
SwitchAsync(target):
  1. await EnsureNoUnsavedEdits()           // existing UnsavedChanges dialog
       └─ false (cancelled) → abort, StatusText unchanged
  2. result = service.Checkout(target)
       ├─ Ok                       → step 4 (reload)
       ├─ BlockedByLocalChanges    → step 3
       ├─ BlockedByUntrackedFiles  → localized "case A" message, abort
       └─ GitFailed / NotARepo     → localized error + AppLog, abort
  3. msg = await RequestCommitConfirmation(
              new PendingCommit(service.ListUncommittedChanges(path), defaultMsg))
       ├─ null (cancelled)         → abort
       └─ message → service.CommitAll(message)
            ├─ Ok → retry service.Checkout(target)
            │        ├─ Ok                      → step 4
            │        ├─ BlockedByUntrackedFiles → "case A" message, abort
            │        └─ else                    → localized error, abort
            └─ GitFailed → localized error + AppLog, abort
  4. ReloadProjectFromDisk()                 // see MainWindowViewModel below
     refresh Branches + current marker; localized success StatusText
```

**Case A** (`BlockedByUntrackedFiles`): committing all *tracked* changes cannot
clear an untracked-file-would-be-overwritten block, so this is surfaced — never
papered over — as: *"Some new files in the project folder would be overwritten by
switching to this branch. A developer may need to move or remove them first."*
This is a genuinely unusual situation where stopping and asking is the right
outcome.

**Commit consent**: the `RequestCommitConfirmation` dialog lists every tracked
file that will be committed (whole repo, from `ListUncommittedChanges`) plus an
editable default message. The file list *is* the consent mechanism — the writer
sees exactly what is being swept into the commit and can cancel.

### `MainWindowViewModel` changes — `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`

Two seams the switch pipeline needs, both reusing existing machinery:

1. **`Task<bool> EnsureNoUnsavedEditsAsync()`** — an awaitable wrapper over the
   existing `GuardDirtyThen` / `UnsavedChangesRequested` plumbing (via a
   `TaskCompletionSource`): returns `true` when there are no unsaved edits or the
   writer chose Save/Discard, `false` when they Cancelled. Wired as
   `BranchesViewModel.EnsureNoUnsavedEdits`. A contained refactor of code we are
   already touching; the existing synchronous `GuardDirtyThen` (used by New/Open)
   stays.
2. **`void ReloadCurrentProjectFromDisk()`** — re-reads the open project path:
   - File exists → call the existing `LoadProjectAsync(path, offerDeferred:false)`
     (already deserializes, handles git-conflict markers, updates status) **and
     invalidate the HEAD-based attribution cache** (`_attributionPath = null`) so
     "last edited" recomputes against the new branch's HEAD.
   - File gone on the new branch → close the open project (`SetProject(null)`,
     clear `_projectPath`/name) and set a localized status explaining the project
     does not exist on this branch.
   Wired as `BranchesViewModel.ReloadProjectFromDisk`.

`ProjectPath` (already public) supplies the path the Branches window is opened
against.

### `BranchesWindow` — `DialogEditor.Avalonia/Views/BranchesWindow.axaml(.cs)`

- `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"` (mandatory).
- A `ListBox` bound to `Branches`. Each row shows the branch name; the current
  branch is bold with a localized **"(current)"** tag (from `Strings.axaml`).
- Buttons **Switch**, **New…**, **Rename…**, **Delete**, each with a detailed
  `ToolTip` (mandatory). Switch/Rename disabled when no row is selected; Delete
  disabled when nothing is selected **or** the current branch is selected.
- Empty/error state `TextBlock` bound to `StatusText`, visible when `!HasBranches`.
- Code-behind constructs `new BranchesViewModel(new GitBranchService(new
  ProcessGitRunner()), projectPath)` and wires the host callbacks:
  - `EnsureNoUnsavedEdits`  → `mainVm.EnsureNoUnsavedEditsAsync`
  - `ReloadProjectFromDisk` → `mainVm.ReloadCurrentProjectFromDisk`
  - `RequestCommitConfirmation` → a commit-consent dialog (file list + message)
  - `ConfirmForceDelete`     → a strong, data-loss-aware confirm dialog
  - `RequestBranchName`      → a small name-prompt dialog (in the spirit of
    `ConversationNameDialog`)
- All strings from `Strings.axaml`.

### Commit-consent + name-prompt dialogs

- **Commit consent** — lists the tracked files to be committed (read-only) and an
  editable message box; **Commit & Switch** / **Cancel**. Returns the message or
  null. Headless-testable.
- **Branch-name prompt** — single text field with validation feedback;
  reuses/parallels the existing small-dialog pattern. May be the same dialog for
  New and Rename (Rename pre-fills the current name).
- **Force-delete confirm** — names the branch and warns its unmerged commits will
  be lost; defaults to Cancel.

### Entry point — `MainWindow.axaml(.cs)`

A **"Branches…"** command under Versions (beside History / Attribution). Opens
`BranchesWindow` for the current `ProjectPath`; disabled when no project is open.
Detailed tooltip, localized label.

## Data flow

```
MainWindow "Branches…"
  → BranchesWindow
      → BranchesViewModel(new GitBranchService(ProcessGitRunner), ProjectPath)
          → service.List → git for-each-ref → [BranchInfo]
      → user picks an action:
          Switch → EnsureNoUnsavedEdits → Checkout
                   (BlockedByLocalChanges → consent → CommitAll → retry)
                   → ReloadProjectFromDisk → refresh list
          New    → RequestBranchName → Create (checkout -b) → refresh list
          Rename → RequestBranchName → Rename → refresh list
          Delete → Delete(-d); NotMerged → ConfirmForceDelete → Delete(-D)
                   → refresh list
```

## Error handling

- All git/IO failures surface as a localized `StatusText`; never crash.
- Every caught exception logged via `AppLog.Warn`/`Error` (per `CLAUDE.md`);
  `OperationCanceledException` swallowed silently.
- `GitFailed.Detail` (raw stderr) is logged, never shown verbatim to the writer.

## Testing (TDD — red first)

1. **`GitBranchService`** (stub `IGitRunner`):
   - `List`: multi-branch `for-each-ref` stdout (`0x1f`-separated) → `BranchInfo`
     list with the current branch flagged; empty stdout → empty list.
   - `Checkout`: Ok; failure + porcelain showing tracked mods →
     `BlockedByLocalChanges`; failure + porcelain showing only `??` →
     `BlockedByUntrackedFiles`; failure + clean tree → `GitFailed`.
   - `Create`: invalid name → `NameInvalid`; existing name → `NameExists`; valid →
     `Ok` (issues `checkout -b`).
   - `Rename`: current (`from null`) and other; invalid/duplicate name mapping.
   - `Delete`: `-d` Ok; `-d` failure → `NotMerged`; `force` issues `-D`.
   - `ListUncommittedChanges`: drops `??` lines, returns tracked paths.
   - `CommitAll`: issues `commit -a`; failure → `GitFailed`.
   - `rev-parse` failure on any op → `NotARepo`.
2. **`BranchesViewModel`** (stub service + stub callbacks):
   - Switch happy path: `EnsureNoUnsavedEdits`→`Checkout(Ok)`→`ReloadProjectFromDisk`.
   - Switch cancelled at unsaved-edits gate → no checkout.
   - `BlockedByLocalChanges` → `RequestCommitConfirmation` (carrying the file list)
     → `CommitAll` → retry checkout → reload.
   - Commit consent cancelled → no commit, no checkout.
   - `BlockedByUntrackedFiles` → case-A `StatusText`, no commit attempt.
   - Create: invalid/duplicate name → localized `StatusText`; valid → list refreshed.
   - Rename happy path.
   - Delete: `CanDelete` false for the current branch; `NotMerged` →
     `ConfirmForceDelete` → force delete; declining the confirm → no delete.
   - `NotARepo` on load → `StatusText` set, `Branches` empty, `HasBranches` false.
3. **`MainWindowViewModel`**:
   - `EnsureNoUnsavedEditsAsync` → true when clean / after Save / after Discard;
     false after Cancel.
   - `ReloadCurrentProjectFromDisk`: normal reload re-reads the file and
     invalidates the attribution cache; missing file closes the project with a
     localized status.
4. **`BranchesWindow`** (headless Avalonia): list populates from a stub VM; current
   branch marked; Switch/Rename/Delete disabled with no selection; Delete disabled
   on the current branch; empty/error state shows when there are no branches.
5. **Commit-consent dialog** (headless): renders the file list and default
   message; returns the edited message on Commit, null on Cancel.

## Files

- Create: `DialogEditor.Patch/Diff/GitBranchService.cs`
- Create: `DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs`
- Create: `DialogEditor.Avalonia/Views/BranchesWindow.axaml(.cs)`
- Create: commit-consent dialog + branch-name prompt + force-delete confirm
  (`DialogEditor.Avalonia/Views/…`)
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
  (`EnsureNoUnsavedEditsAsync`, `ReloadCurrentProjectFromDisk`)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml(.cs)` (Branches… entry)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (labels, tooltips,
  status/result messages, the "(current)" tag, case-A copy, force-delete copy)
- Tests: `GitBranchServiceTests`, `BranchesViewModelTests`,
  `MainWindowViewModelTests` (additions), `BranchesWindowTests`, commit-consent
  dialog tests.
- Docs: update `Gaps.md` / `NEXT-STEPS.md` when shipped.

## Out of scope (YAGNI / later)

Remote branches (fetch / push / pull / tracking); merging branches (the
git-conflict resolver already handles merge *results*); committing **untracked**
files during commit-then-switch (deliberately tracked-only); a current-branch
indicator in the main window (possible follow-up); stash-based switch workflows
(rejected in favour of commit-then-switch); detached-HEAD authoring.
