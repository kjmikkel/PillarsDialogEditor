# History browser — design

**Date:** 2026-06-04
**Status:** Approved (design)

## Context

`Gaps.md` lists **branch/history navigation** ("browse git log, switch branches,
attribution") as the last open VCS gap. That entry is three independent
subsystems with very different risk profiles:

1. **History browser** (read-only) — browse the project's git history.
2. **Attribution / blame** (read-only, medium) — map `git blame` onto nodes.
3. **Branch switching** (read-write) — `git checkout`; the project's first git
   write op; highest risk.

This spec covers **sub-project 1, the history browser**, only. The other two get
their own spec → plan → implement cycles later.

## Goal

Let a dialog writer browse the git history of the open project file as a readable
timeline (commit message, author, date), and act on any commit by opening it in
the existing compare window — which already provides both diff-viewing and
selective bring-in. This delivers all three "layers" the user asked for (audit,
compare, restore) on one surface.

## Key reuse decision (Approach A)

The compare window (`DiffViewModel` / `DiffWindow`) already bundles diff-viewing
**and** selective bring-in, and is launched detached
(`new DiffWindow(vm).Show()`) with its right endpoint defaulting to the first git
ref. Therefore "compare this commit" and "bring back content" need **no new diff
or restore machinery** — they collapse into a single action: open the compare
window with the right endpoint preset to the selected commit. The only plumbing
change is an optional "initial right ref" on `DiffViewModel`.

Rejected alternatives: embedding a read-only diff in the history window
(duplicates the canvas, *loses* bring-in); a changed-conversation summary in the
history window (duplicates the compare window's left panel for marginal value).

## Components

### `CommitInfo` — `DialogEditor.Patch/Diff/CommitInfo.cs`

```csharp
public record CommitInfo(
    string Sha,
    string ShortSha,
    string Author,
    DateTimeOffset Date,
    string Subject);
```

Presentation-free: `Date` is parsed from git's machine-readable `%aI`, so all
formatting is deferred to the view layer (keeps service/VM tests
culture-independent).

### Shared path resolution — `DialogEditor.Patch/Diff/`

The repo-root + repo-relative-path resolution currently lives privately in
`ProjectVersionLoader.ReadAtRef` (`rev-parse --show-toplevel` →
`Path.GetRelativePath`). Extract it into a small shared helper
(`GitRepoPath.ResolveRepoRelative(IGitRunner git, string projectFilePath)` →
returns `(string RepoDir, string RelativePath)`, throwing `DiffException`
`NotARepo` when `rev-parse` fails). `ProjectVersionLoader` is refactored to call
it; `ProjectHistoryService` uses it too. DRY, in the spirit of improving code we
are already touching. No behaviour change for the loader.

### `ProjectHistoryService` — `DialogEditor.Patch/Diff/ProjectHistoryService.cs`

```csharp
public class ProjectHistoryService(IGitRunner git)
{
    public IReadOnlyList<CommitInfo> Load(string projectFilePath);
}
```

- Resolve repo dir + relative path via the shared helper.
- Run: `git log --follow --date=iso-strict
  --format=%h%x1f%H%x1f%an%x1f%aI%x1f%s -- <relative>`.
  The `0x1f` (unit separator, `%x1f`) splits fields robustly — subjects contain
  spaces and arbitrary punctuation.
- Parse each non-empty line into a `CommitInfo` (`%h`→ShortSha, `%H`→Sha,
  `%an`→Author, `%aI`→`DateTimeOffset.Parse` with `CultureInfo.InvariantCulture`
  / `DateTimeStyles.RoundtripKind`, `%s`→Subject). A line with fewer than 5
  fields is skipped defensively.
- Empty log (`Ok`, empty stdout) → empty list (drives the empty-state).
- `git log` failure after a valid repo → throw `DiffException` (`Unknown`),
  logged by the caller.
- Not-a-repo / git-missing → propagates the shared helper's / runner's
  `DiffException`.

### `DiffViewModel` change — `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`

Add an optional final constructor parameter `string? initialRightRef = null`.
After `BuildEndpointOptions()`:

- If `initialRightRef` is non-null, find the `EndpointOption` whose endpoint is a
  `GitRef` with that exact `Ref`. If found, set it as `RightEndpoint`.
- If not found (the commit is older than the 20 enumerated, or otherwise absent),
  synthesize `new EndpointOption(initialRightRef, new DiffEndpoint.GitRef(initialRightRef))`,
  insert it into the options list (after the working-copy option), and select it.
- When `initialRightRef` is null, behaviour is unchanged (defaults to first
  `GitRef`).

`Left` stays the working copy (the bring-in target), preserving the existing
left→right direction.

### `HistoryViewModel` — `DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs`

```csharp
public partial class CommitRowViewModel(CommitInfo commit) : ObservableObject
{
    public string         Sha      => commit.Sha;
    public string         ShortSha => commit.ShortSha;
    public string         Author   => commit.Author;
    public DateTimeOffset Date     => commit.Date;
    public string         Subject  => commit.Subject;
}

public partial class HistoryViewModel : ObservableObject
{
    public IReadOnlyList<CommitRowViewModel> Commits { get; }
    [ObservableProperty] private CommitRowViewModel? _selected;
    [ObservableProperty] private string _statusText;   // empty/error state

    public bool HasCommits => Commits.Count > 0;

    /// Host callback: open the compare window with this commit as the right
    /// endpoint. The VM layer cannot open Avalonia windows, so the host
    /// (HistoryWindow / MainWindow) wires this — mirrors CommitApply and
    /// ShowGitConflictResolution.
    public Action<string>? CompareWithCommit { get; set; }

    public HistoryViewModel(IGitRunner git, string projectFilePath);

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private void Compare();   // invokes CompareWithCommit(Selected!.Sha)
    private bool CanCompare => Selected is not null;
}
```

- Ctor loads commits via `ProjectHistoryService`. On `DiffException` it catches,
  sets a localized `StatusText` (mapping the kind: not-a-repo vs. generic
  failure), logs `AppLog.Warn`, and leaves `Commits` empty.
- Empty list (no exception) → `StatusText` = "no history for this project yet".
- `OnSelectedChanged` re-evaluates `CompareCommand.CanExecute`.

### `CommitDateConverter` — `DialogEditor.Avalonia/Converters/CommitDateConverter.cs`

`IValueConverter`: `DateTimeOffset` → `value.ToString("d", culture)` (the binding
supplies `culture`, which is the OS regional culture since the app never
overrides `CurrentCulture`). Returns `string.Empty` for non-`DateTimeOffset`
input. Unit-tested with explicit cultures.

### `HistoryWindow` — `DialogEditor.Avalonia/Views/HistoryWindow.axaml(.cs)`

- `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"` (mandatory).
- A `ListBox` bound to `Commits`. Each row: **date** (`CommitDateConverter`, with
  `ToolTip.Tip` = the full ISO 8601 timestamp incl. time), author, short SHA,
  subject. All column headers and the date tooltip from `Strings.axaml`.
- "Compare with my copy" `Button` bound to `CompareCommand`, with a detailed
  tooltip. Close button.
- Empty-state `TextBlock` bound to `StatusText`, visible when `!HasCommits`.
- Code-behind wires `vm.CompareWithCommit = sha => new DiffWindow(new DiffViewModel(
  new ProcessGitRunner(), new AvaloniaDispatcher(), projectPath, provider,
  language, initialRightRef: sha)).Show();` — same construction MainWindow already
  uses for the compare entry, plus the preset ref.

### Entry point — `DialogEditor.Avalonia/Views/MainWindow.axaml(.cs)`

A "History…" command beside the existing compare action. Opens `HistoryWindow`
for the current project path; disabled when no project is open. Detailed tooltip.
Localized label.

## Data flow

```
MainWindow "History…"
  → HistoryWindow
      → HistoryViewModel(git, projectPath)
          → ProjectHistoryService.Load → git log --follow → [CommitInfo]
      → user selects a commit
      → Compare → CompareWithCommit(sha)
          → new DiffWindow(new DiffViewModel(..., initialRightRef: sha)).Show()
              → existing diff + selective bring-in
```

## Error handling

- All git/IO failures surface as localized `StatusText`; never crash.
- Every caught exception logged via `AppLog.Warn`/`Error` (per CLAUDE.md).
- No expected `OperationCanceledException` path here.

## Testing (TDD)

1. **`ProjectHistoryService`** (stub `IGitRunner`):
   - Multi-commit stdout (fields `0x1f`-separated, one commit per line) → correct
     `CommitInfo` list, including parsed `DateTimeOffset` and a subject containing
     spaces and a literal separator-free punctuation string.
   - Empty stdout (Ok) → empty list.
   - `rev-parse` failure → `DiffException` (`NotARepo`).
   - `git log` failure after valid repo → `DiffException`.
2. **`GitRepoPath` helper**: resolves relative path against repo root; throws
   `NotARepo` on `rev-parse` failure. (Loader's existing tests must still pass.)
3. **`HistoryViewModel`**:
   - Loads rows from a stub service/runner.
   - `CompareCommand` disabled when `Selected` is null; enabled once set.
   - Invoking `CompareCommand` calls `CompareWithCommit` with the selected `Sha`.
   - Repo error → `StatusText` set, `Commits` empty, `HasCommits` false.
4. **`DiffViewModel`**:
   - `initialRightRef` matching an enumerated commit presets `RightEndpoint`.
   - `initialRightRef` not in the list → an option is synthesized and selected.
   - Null `initialRightRef` → unchanged default (first `GitRef`).
5. **`CommitDateConverter`**: formats a known `DateTimeOffset` under
   `InvariantCulture` and one other explicit culture; non-date input → empty.
6. **`HistoryWindow`** (headless Avalonia): list populates from a stub VM; compare
   button disabled until a row is selected; empty-state text shows when no
   commits.

## Files

- Create: `DialogEditor.Patch/Diff/CommitInfo.cs`
- Create: `DialogEditor.Patch/Diff/GitRepoPath.cs`
- Create: `DialogEditor.Patch/Diff/ProjectHistoryService.cs`
- Modify: `DialogEditor.Patch/Diff/ProjectVersionLoader.cs` (use shared helper)
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` (`initialRightRef`)
- Create: `DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs`
- Create: `DialogEditor.Avalonia/Converters/CommitDateConverter.cs`
- Create: `DialogEditor.Avalonia/Views/HistoryWindow.axaml(.cs)`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml(.cs)` (entry point)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (labels, tooltips, states)
- Modify: `DialogEditor.Avalonia/App.axaml` (register
  `<converters:CommitDateConverter x:Key="CommitDate"/>` in the resource
  dictionary alongside the existing converters)
- Tests: `ProjectHistoryServiceTests`, `GitRepoPathTests`, `HistoryViewModelTests`,
  `DiffViewModelTests` (additions), `CommitDateConverterTests`, `HistoryWindowTests`
- Docs: update `Gaps.md` / `NEXT-STEPS.md` when shipped.

## Out of scope (YAGNI / later sub-projects)

Per-conversation history filtering; branch switching (sub-project 3);
blame/attribution (sub-project 2); pagination beyond `git log`'s natural ordering;
first-run intro.
