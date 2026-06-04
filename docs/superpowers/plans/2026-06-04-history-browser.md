# History Browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a dialog writer browse the open project file's git history as a readable timeline and open any commit in the existing compare window (which already provides diff + selective bring-in).

**Architecture:** A read-only `ProjectHistoryService` shells `git log --follow` via the existing `IGitRunner`, parsing commits into presentation-free `CommitInfo` records. A `HistoryViewModel` exposes them; a `HistoryWindow` lists them; "Compare with my copy" opens `DiffWindow` with its right endpoint preset to the selected commit via a new optional `DiffViewModel` parameter.

**Tech Stack:** C#, .NET 8, Avalonia (headless tests), CommunityToolkit.Mvvm, xUnit. Git is abstracted behind `IGitRunner`; strings live in `DialogEditor.Avalonia/Resources/Strings.axaml` via the `Loc` provider.

> **Test command:** `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~<name>"`. The suite runs serially by design. Build a single project with `dotnet build <project>`.

> **Test doubles:** new test files define a local `FakeGit : IGitRunner` (matching `ProjectVersionLoaderTests`): a `Func<string[], GitResult> Handler` and a recorded `List<string[]> Calls`. The field separator git emits for `%x1f` is the unit-separator char `'\u001f'`.

---

## File Structure

- `DialogEditor.Patch/Diff/GitRepoPath.cs` — shared repo-root/relative-path resolver (extracted from the loader).
- `DialogEditor.Patch/Diff/CommitInfo.cs` — commit record.
- `DialogEditor.Patch/Diff/ProjectHistoryService.cs` — `git log --follow` → `[CommitInfo]`.
- `DialogEditor.Patch/Diff/ProjectVersionLoader.cs` — refactored to use the shared resolver.
- `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` — optional `initialRightRef`.
- `DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs` — commit list + compare command.
- `DialogEditor.Avalonia/Converters/CommitDateConverter.cs` — `DateTimeOffset` → short date.
- `DialogEditor.Avalonia/Views/HistoryWindow.axaml(.cs)` — timeline window.
- `DialogEditor.Avalonia/Views/MainWindow.axaml(.cs)` — "History…" entry point.
- `DialogEditor.Avalonia/Resources/Strings.axaml` — labels, tooltips, states.
- `DialogEditor.Avalonia/App.axaml` — register the converter.

---

## Task 1: Extract `GitRepoPath` shared resolver

**Files:**
- Create: `DialogEditor.Patch/Diff/GitRepoPath.cs`
- Modify: `DialogEditor.Patch/Diff/ProjectVersionLoader.cs:33-51`
- Test: `DialogEditor.Tests/Patch/Diff/GitRepoPathTests.cs`

- [ ] **Step 1: Write the failing test**

Create `DialogEditor.Tests/Patch/Diff/GitRepoPathTests.cs`:

```csharp
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class GitRepoPathTests
{
    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    [Fact]
    public void ResolvesRelativePathAgainstRepoRoot()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var path = Path.Combine(root, "sub", "proj.dialogproject");
        var git  = new FakeGit { Handler = a => a is ["rev-parse", "--show-toplevel"]
            ? new GitResult(0, root + "\n", "") : new GitResult(0, "", "") };

        var (workingDir, relative) = GitRepoPath.ResolveRepoRelative(git, path);

        Assert.Equal("sub/proj.dialogproject", relative);
        Assert.Equal(Path.GetDirectoryName(Path.GetFullPath(path)), workingDir);
    }

    [Fact]
    public void NotARepo_Throws()
    {
        var path = Path.Combine(Path.GetTempPath(), "proj.dialogproject");
        var git  = new FakeGit { Handler = _ => new GitResult(128, "", "fatal: not a git repo") };

        var ex = Assert.Throws<DiffException>(() => GitRepoPath.ResolveRepoRelative(git, path));
        Assert.Equal(DiffExceptionKind.NotARepo, ex.Kind);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~GitRepoPathTests"`
Expected: FAIL — `GitRepoPath` does not exist (compile error).

- [ ] **Step 3: Create the helper**

Create `DialogEditor.Patch/Diff/GitRepoPath.cs`:

```csharp
namespace DialogEditor.Patch.Diff;

/// Resolves a project file's location within its git repository. Shared by the
/// read-only git readers (ProjectVersionLoader, ProjectHistoryService).
public static class GitRepoPath
{
    /// Returns the directory to run git in (the project's folder) and the project's
    /// repo-root-relative, forward-slashed path. Throws DiffException(NotARepo) when
    /// the path is not inside a git repository (or git is unavailable).
    public static (string WorkingDir, string Relative) ResolveRepoRelative(
        IGitRunner git, string projectFilePath)
    {
        var dir = Path.GetDirectoryName(Path.GetFullPath(projectFilePath))
                  ?? throw new DiffException("Project path has no directory.", DiffExceptionKind.Unknown);

        var root = git.Run(dir, "rev-parse", "--show-toplevel");
        if (!root.Ok)
            throw new DiffException("Not a git repository (or git is not installed).", DiffExceptionKind.NotARepo);

        var repoRoot = root.StdOut.Trim();
        var relative = Path.GetRelativePath(repoRoot, Path.GetFullPath(projectFilePath))
                           .Replace('\\', '/');
        return (dir, relative);
    }
}
```

- [ ] **Step 4: Refactor the loader to use it**

In `DialogEditor.Patch/Diff/ProjectVersionLoader.cs`, replace `ReadAtRef` (`:33-51`) with:

```csharp
    private string ReadAtRef(string gitRef, string projectFilePath)
    {
        var (dir, relative) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);

        var show = git.Run(dir, "show", $"{gitRef}:{relative}");
        if (!show.Ok)
            throw new DiffException($"Could not read '{relative}' at '{gitRef}': {show.StdErr.Trim()}", DiffExceptionKind.BadRef);

        return show.StdOut;
    }
```

- [ ] **Step 5: Run the new test + the loader's existing tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~GitRepoPathTests|FullyQualifiedName~ProjectVersionLoaderTests"`
Expected: PASS (helper tests + all loader tests — behaviour unchanged).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Patch/Diff/GitRepoPath.cs DialogEditor.Patch/Diff/ProjectVersionLoader.cs DialogEditor.Tests/Patch/Diff/GitRepoPathTests.cs
git commit -m "refactor: extract GitRepoPath resolver shared by git readers"
```

---

## Task 2: `CommitInfo` + `ProjectHistoryService`

**Files:**
- Create: `DialogEditor.Patch/Diff/CommitInfo.cs`
- Create: `DialogEditor.Patch/Diff/ProjectHistoryService.cs`
- Test: `DialogEditor.Tests/Patch/Diff/ProjectHistoryServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Patch/Diff/ProjectHistoryServiceTests.cs`:

```csharp
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProjectHistoryServiceTests
{
    private const char US = '\u001f';   // unit separator git emits for %x1f

    private sealed class FakeGit : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return Handler(args);
        }
    }

    private static string ProjPath() =>
        Path.Combine(Path.GetTempPath(), $"hist_{Guid.NewGuid():N}.dialogproject");

    private static FakeGit GitWithLog(string logStdout)
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        return new FakeGit
        {
            Handler = a =>
                a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
                a.Length > 0 && a[0] == "log"         ? new GitResult(0, logStdout, "") :
                                                        new GitResult(0, "", ""),
        };
    }

    private static string Line(string shortSha, string sha, string author, string isoDate, string subject)
        => string.Join(US, shortSha, sha, author, isoDate, subject);

    [Fact]
    public void ParsesCommits_IncludingDateAndSpacedSubject()
    {
        var stdout =
            Line("a1b2c3d", "a1b2c3d4e5", "Mia", "2026-05-30T14:03:11+02:00", "Fix greeting branch logic") + "\n" +
            Line("0099887", "00998877665", "Jon", "2026-05-28T09:00:00+00:00", "Add tavern small talk");
        var svc = new ProjectHistoryService(GitWithLog(stdout));

        var commits = svc.Load(ProjPath());

        Assert.Equal(2, commits.Count);
        Assert.Equal("a1b2c3d4e5", commits[0].Sha);
        Assert.Equal("a1b2c3d",    commits[0].ShortSha);
        Assert.Equal("Mia",        commits[0].Author);
        Assert.Equal("Fix greeting branch logic", commits[0].Subject);
        Assert.Equal(new DateTimeOffset(2026, 5, 30, 14, 3, 11, TimeSpan.FromHours(2)), commits[0].Date);
    }

    [Fact]
    public void UsesFollowAndProjectPath()
    {
        var git = GitWithLog("");
        new ProjectHistoryService(git).Load(ProjPath());

        var log = Assert.Single(git.Calls, c => c.Length > 0 && c[0] == "log");
        Assert.Contains("--follow", log);
        Assert.Equal("--", log[^2]);   // path passed after a -- terminator
    }

    [Fact]
    public void EmptyLog_ReturnsEmptyList()
    {
        var svc = new ProjectHistoryService(GitWithLog(""));
        Assert.Empty(svc.Load(ProjPath()));
    }

    [Fact]
    public void NotARepo_Throws()
    {
        var git = new FakeGit { Handler = _ => new GitResult(128, "", "fatal") };
        var ex = Assert.Throws<DiffException>(() => new ProjectHistoryService(git).Load(ProjPath()));
        Assert.Equal(DiffExceptionKind.NotARepo, ex.Kind);
    }

    [Fact]
    public void LogFailsAfterValidRepo_Throws()
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit
        {
            Handler = a => a is ["rev-parse", "--show-toplevel"]
                ? new GitResult(0, root + "\n", "")
                : new GitResult(128, "", "fatal: bad revision"),
        };
        Assert.Throws<DiffException>(() => new ProjectHistoryService(git).Load(ProjPath()));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ProjectHistoryServiceTests"`
Expected: FAIL — `CommitInfo` / `ProjectHistoryService` do not exist (compile error).

- [ ] **Step 3: Create `CommitInfo`**

Create `DialogEditor.Patch/Diff/CommitInfo.cs`:

```csharp
namespace DialogEditor.Patch.Diff;

/// One commit in a project's history. Presentation-free: Date is the parsed
/// author date; display formatting is the view layer's job.
public record CommitInfo(
    string         Sha,
    string         ShortSha,
    string         Author,
    DateTimeOffset Date,
    string         Subject);
```

- [ ] **Step 4: Create `ProjectHistoryService`**

Create `DialogEditor.Patch/Diff/ProjectHistoryService.cs`:

```csharp
using System.Globalization;

namespace DialogEditor.Patch.Diff;

/// Reads the git history of a project file (read-only). Testable via IGitRunner.
public class ProjectHistoryService(IGitRunner git)
{
    // %h short sha, %H full sha, %an author, %aI strict-ISO author date, %s subject.
    // 0x1f (unit separator) between fields so subjects with spaces parse cleanly.
    private const string Format = "--format=%h%x1f%H%x1f%an%x1f%aI%x1f%s";

    public IReadOnlyList<CommitInfo> Load(string projectFilePath)
    {
        var (dir, relative) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);

        var log = git.Run(dir, "log", "--follow", "--date=iso-strict", Format, "--", relative);
        if (!log.Ok)
            throw new DiffException(
                $"Could not read git history for '{relative}': {log.StdErr.Trim()}", DiffExceptionKind.Unknown);

        var commits = new List<CommitInfo>();
        foreach (var raw in log.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;

            var f = line.Split('\u001f');
            if (f.Length < 5) continue;   // defensive: skip malformed lines
            if (!DateTimeOffset.TryParse(f[3], CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind, out var date)) continue;

            commits.Add(new CommitInfo(Sha: f[1], ShortSha: f[0], Author: f[2], Date: date, Subject: f[4]));
        }
        return commits;
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ProjectHistoryServiceTests"`
Expected: PASS (5 tests).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Patch/Diff/CommitInfo.cs DialogEditor.Patch/Diff/ProjectHistoryService.cs DialogEditor.Tests/Patch/Diff/ProjectHistoryServiceTests.cs
git commit -m "feat: ProjectHistoryService reads project git history into CommitInfo"
```

---

## Task 3: `DiffViewModel.initialRightRef`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs:110-134`
- Test: `DialogEditor.Tests/ViewModels/DiffViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `DialogEditor.Tests/ViewModels/DiffViewModelTests.cs` (the file already has a `FakeGit` and `StubDispatcher` and a helper that writes a temp project; use a fresh temp path + a `FakeGit` returning branches `main\n` and a log with one commit `abc123`). Add:

```csharp
    [Fact]
    public void InitialRightRef_MatchingEnumeratedCommit_IsPreselected()
    {
        var path = WriteTempProject(DialogProject.Empty("p"));
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(DialogProject.Empty("p")),
                               branchOutput: "main\n", logOutput: "abc123 first\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider: null, "en", initialRightRef: "abc123");

        Assert.True(vm.RightEndpoint!.Endpoint is DiffEndpoint.GitRef { Ref: "abc123" });
    }

    [Fact]
    public void InitialRightRef_NotEnumerated_IsSynthesizedAndSelected()
    {
        var path = WriteTempProject(DialogProject.Empty("p"));
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(DialogProject.Empty("p")),
                               branchOutput: "main\n", logOutput: "abc123 first\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider: null, "en", initialRightRef: "deadbeef");

        Assert.True(vm.RightEndpoint!.Endpoint is DiffEndpoint.GitRef { Ref: "deadbeef" });
        Assert.Contains(vm.EndpointOptions, o => o.Endpoint is DiffEndpoint.GitRef { Ref: "deadbeef" });
    }
```

This file's `MakeFakeGit` currently has no `logOutput` parameter — extend it. Find the existing `MakeFakeGit` in this test file and add a `string logOutput = ""` parameter, returning it for `args[0] == "log"`:

```csharp
    private static FakeGit MakeFakeGit(string projectDir, string? refContent,
        string branchOutput = "main\n", string logOutput = "")
        => new(args =>
        {
            if (args is ["rev-parse", "--show-toplevel"]) return new GitResult(0, projectDir + "\n", "");
            if (args.Length == 2 && args[0] == "show")
                return refContent is null ? new GitResult(128, "", "fatal: bad ref") : new GitResult(0, refContent, "");
            if (args.Length >= 1 && args[0] == "branch") return new GitResult(0, branchOutput, "");
            if (args.Length >= 1 && args[0] == "log")    return new GitResult(0, logOutput, "");
            return new GitResult(0, "", "");
        });
```

(If the existing `MakeFakeGit` signature differs from the above, preserve its existing parameters and only add `logOutput`. The log line format here is the diff viewer's own `%h %s`, not the history `%x1f` format.)

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~InitialRightRef"`
Expected: FAIL — `DiffViewModel` has no `initialRightRef` parameter (compile error).

- [ ] **Step 3: Add the parameter and selection logic**

In `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`, change the constructor signature (`:110-115`) to add the final parameter:

```csharp
    public DiffViewModel(
        IGitRunner        git,
        IDispatcher       dispatcher,
        string            projectFilePath,
        IGameDataProvider? provider = null,
        string            language  = "en",
        string?           initialRightRef = null)
```

Replace the endpoint-setup block (`:124-133`, from `EndpointOptions = BuildEndpointOptions();` through `Recompute();`) with:

```csharp
        var options = BuildEndpointOptions().ToList();
        var workingCopyOption = options.First(o => o.Endpoint is DiffEndpoint.WorkingCopy);

        EndpointOption right;
        if (initialRightRef is not null)
        {
            var match = options.FirstOrDefault(
                o => o.Endpoint is DiffEndpoint.GitRef g && g.Ref == initialRightRef);
            if (match is null)
            {
                // Commit older than the enumerated list (or otherwise absent):
                // synthesize an option so any historical commit is reachable.
                match = new EndpointOption(initialRightRef, new DiffEndpoint.GitRef(initialRightRef));
                options.Insert(1, match);   // just after the working-copy option
            }
            right = match;
        }
        else
        {
            right = options.FirstOrDefault(o => o.Endpoint is DiffEndpoint.GitRef) ?? workingCopyOption;
        }

        EndpointOptions = options;
        // Your copy on the left (the bring-in target); the other version on the right.
        LeftEndpoint  = workingCopyOption;
        RightEndpoint = right;

        Recompute();
```

Note: `EndpointOptions` is declared `IReadOnlyList<EndpointOption>`; assigning a `List<EndpointOption>` satisfies it.

- [ ] **Step 4: Run the new tests + the full DiffViewModel test class**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffViewModelTests"`
Expected: PASS — new tests plus all existing ones (null `initialRightRef` preserves the old default).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Tests/ViewModels/DiffViewModelTests.cs
git commit -m "feat: DiffViewModel accepts an initial right endpoint ref"
```

---

## Task 4: `HistoryViewModel`

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/HistoryViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/HistoryViewModelTests.cs`:

```csharp
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class HistoryViewModelTests
{
    private const char US = '\u001f';

    public HistoryViewModelTests() => Loc.Configure(new StubStringProvider());

    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static string ProjPath() =>
        Path.Combine(Path.GetTempPath(), $"histvm_{Guid.NewGuid():N}.dialogproject");

    private static FakeGit GitWithLog(string logStdout)
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        return new FakeGit
        {
            Handler = a =>
                a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
                a.Length > 0 && a[0] == "log"         ? new GitResult(0, logStdout, "") :
                                                        new GitResult(0, "", ""),
        };
    }

    private static string Line(string shortSha, string sha, string author, string iso, string subject)
        => string.Join(US, shortSha, sha, author, iso, subject);

    [Fact]
    public void LoadsCommitRows()
    {
        var git = GitWithLog(Line("a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "Greeting tweak"));
        var vm  = new HistoryViewModel(git, ProjPath());

        Assert.True(vm.HasCommits);
        Assert.Equal("a1b2c3d4", vm.Commits[0].Sha);
        Assert.Equal("Greeting tweak", vm.Commits[0].Subject);
    }

    [Fact]
    public void Compare_DisabledUntilSelected()
    {
        var git = GitWithLog(Line("a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "x"));
        var vm  = new HistoryViewModel(git, ProjPath());

        Assert.False(vm.CompareCommand.CanExecute(null));
        vm.Selected = vm.Commits[0];
        Assert.True(vm.CompareCommand.CanExecute(null));
    }

    [Fact]
    public void Compare_InvokesCallbackWithSelectedSha()
    {
        var git = GitWithLog(Line("a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "x"));
        var vm  = new HistoryViewModel(git, ProjPath());
        string? captured = null;
        vm.CompareWithCommit = sha => captured = sha;

        vm.Selected = vm.Commits[0];
        vm.CompareCommand.Execute(null);

        Assert.Equal("a1b2c3d4", captured);
    }

    [Fact]
    public void RepoError_SetsStatus_NoCommits()
    {
        var git = new FakeGit { Handler = _ => new GitResult(128, "", "fatal") };
        var vm  = new HistoryViewModel(git, ProjPath());

        Assert.False(vm.HasCommits);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public void EmptyHistory_SetsStatus()
    {
        var vm = new HistoryViewModel(GitWithLog(""), ProjPath());

        Assert.False(vm.HasCommits);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~HistoryViewModelTests"`
Expected: FAIL — `HistoryViewModel` does not exist (compile error).

- [ ] **Step 3: Create the view model**

Create `DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One commit row. Presentation-free Date (DateTimeOffset); the view formats it.
public class CommitRowViewModel(CommitInfo commit)
{
    public string         Sha      => commit.Sha;
    public string         ShortSha => commit.ShortSha;
    public string         Author   => commit.Author;
    public DateTimeOffset Date     => commit.Date;
    public string         Subject  => commit.Subject;
}

/// Lists the open project file's git history; "Compare" opens the selected commit
/// in the compare window via a host callback (the VM layer can't open windows).
public partial class HistoryViewModel : ObservableObject
{
    [ObservableProperty] private CommitRowViewModel? _selected;
    [ObservableProperty] private string _statusText = "";

    public IReadOnlyList<CommitRowViewModel> Commits { get; }
    public bool HasCommits => Commits.Count > 0;

    /// Set by the host: open the compare window with this commit as the right endpoint.
    public Action<string>? CompareWithCommit { get; set; }

    public HistoryViewModel(IGitRunner git, string projectFilePath)
    {
        IReadOnlyList<CommitInfo> commits = [];
        try
        {
            commits = new ProjectHistoryService(git).Load(projectFilePath);
        }
        catch (DiffException ex)
        {
            AppLog.Warn($"HistoryViewModel: could not load history: {ex.Message}");
            StatusText = ex.Kind == DiffExceptionKind.NotARepo
                ? Loc.Get("History_StatusNotARepo")
                : Loc.Get("History_StatusError");
        }

        Commits = commits.Select(c => new CommitRowViewModel(c)).ToList();
        if (Commits.Count == 0 && StatusText.Length == 0)
            StatusText = Loc.Get("History_StatusNoHistory");
    }

    partial void OnSelectedChanged(CommitRowViewModel? value) => CompareCommand.NotifyCanExecuteChanged();

    [RelayCommand(CanExecute = nameof(CanCompare))]
    private void Compare() => CompareWithCommit?.Invoke(Selected!.Sha);

    private bool CanCompare => Selected is not null;
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~HistoryViewModelTests"`
Expected: PASS (5 tests). `StubStringProvider` returns the keys, so the status assertions just check non-empty.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs DialogEditor.Tests/ViewModels/HistoryViewModelTests.cs
git commit -m "feat: HistoryViewModel lists commits with a compare command"
```

---

## Task 5: `CommitDateConverter`

**Files:**
- Create: `DialogEditor.Avalonia/Converters/CommitDateConverter.cs`
- Test: `DialogEditor.Tests/Converters/CommitDateConverterTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Converters/CommitDateConverterTests.cs`:

```csharp
using System.Globalization;
using DialogEditor.Avalonia.Converters;
using Xunit;

namespace DialogEditor.Tests.Converters;

public class CommitDateConverterTests
{
    private static readonly CommitDateConverter Conv = new();

    [Fact]
    public void FormatsShortDate_Invariant()
    {
        var date = new DateTimeOffset(2026, 5, 30, 14, 3, 0, TimeSpan.Zero);
        var result = Conv.Convert(date, typeof(string), null, CultureInfo.InvariantCulture);
        Assert.Equal("05/30/2026", result);   // invariant short-date pattern
    }

    [Fact]
    public void FormatsShortDate_GivenCulture()
    {
        var date = new DateTimeOffset(2026, 5, 30, 14, 3, 0, TimeSpan.Zero);
        var de   = CultureInfo.GetCultureInfo("de-DE");
        var result = Conv.Convert(date, typeof(string), null, de);
        Assert.Equal("30.05.2026", result);
    }

    [Fact]
    public void NonDate_ReturnsEmpty()
    {
        Assert.Equal(string.Empty, Conv.Convert("nope", typeof(string), null, CultureInfo.InvariantCulture));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CommitDateConverterTests"`
Expected: FAIL — `CommitDateConverter` does not exist (compile error).

- [ ] **Step 3: Create the converter**

Create `DialogEditor.Avalonia/Converters/CommitDateConverter.cs`:

```csharp
using System.Globalization;
using Avalonia.Data.Converters;

namespace DialogEditor.Avalonia.Converters;

/// Formats a commit's DateTimeOffset as the culture's short date (the OS regional
/// format, since the app never overrides CurrentCulture). The full ISO timestamp
/// is shown separately as the row tooltip.
public sealed class CommitDateConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is DateTimeOffset d ? d.ToString("d", culture) : string.Empty;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CommitDateConverterTests"`
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Converters/CommitDateConverter.cs DialogEditor.Tests/Converters/CommitDateConverterTests.cs
git commit -m "feat: CommitDateConverter formats commit dates in the system short format"
```

---

## Task 6: `HistoryWindow` + strings + converter registration

**Files:**
- Create: `DialogEditor.Avalonia/Views/HistoryWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/HistoryWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia/App.axaml`
- Test: `DialogEditor.Tests/Views/HistoryWindowTests.cs`

- [ ] **Step 1: Add the strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, add near the other window strings:

```xml
    <!-- History browser -->
    <sys:String x:Key="HistoryWindow_Title">Project History</sys:String>
    <sys:String x:Key="HistoryWindow_ColDate">Date</sys:String>
    <sys:String x:Key="HistoryWindow_ColAuthor">Author</sys:String>
    <sys:String x:Key="HistoryWindow_ColCommit">Commit</sys:String>
    <sys:String x:Key="HistoryWindow_ColMessage">Message</sys:String>
    <sys:String x:Key="HistoryWindow_CompareButton">Compare with my copy</sys:String>
    <sys:String x:Key="HistoryWindow_CompareTooltip">Open the selected commit in the compare window, alongside your current working copy. From there you can view what changed and bring individual changes back into your copy.</sys:String>
    <sys:String x:Key="HistoryWindow_Close">Close</sys:String>
    <sys:String x:Key="History_StatusNoHistory">No git history found for this project file yet.</sys:String>
    <sys:String x:Key="History_StatusNotARepo">This project is not inside a git repository, so there is no history to show.</sys:String>
    <sys:String x:Key="History_StatusError">Could not read this project's git history. See the log for details.</sys:String>
    <sys:String x:Key="Menu_History">History…</sys:String>
    <sys:String x:Key="ToolTip_History">Browse the git commit history of the open project file. Select a commit to compare it with your current copy.</sys:String>
```

- [ ] **Step 2: Register the converter**

In `DialogEditor.Avalonia/App.axaml`, add alongside the existing converter entries (after `DiffStatusToBrushConverter`):

```xml
            <converters:CommitDateConverter x:Key="CommitDate"/>
```

- [ ] **Step 3: Write the failing test**

Create `DialogEditor.Tests/Views/HistoryWindowTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class HistoryWindowTests
{
    private const char US = '\u001f';

    public HistoryWindowTests() => Loc.Configure(new StubStringProvider());

    private sealed class FakeGit : IGitRunner
    {
        public Func<string[], GitResult> Handler = _ => new GitResult(0, "", "");
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static HistoryViewModel MakeVm(string logStdout)
    {
        var root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
        var git = new FakeGit
        {
            Handler = a =>
                a is ["rev-parse", "--show-toplevel"] ? new GitResult(0, root + "\n", "") :
                a.Length > 0 && a[0] == "log"         ? new GitResult(0, logStdout, "") :
                                                        new GitResult(0, "", ""),
        };
        var path = Path.Combine(root, $"histwin_{Guid.NewGuid():N}.dialogproject");
        return new HistoryViewModel(git, path);
    }

    [AvaloniaFact]
    public void List_PopulatesFromCommits()
    {
        var vm = MakeVm(string.Join(US, "a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "Tweak"));
        var window = new HistoryWindow(vm);
        window.Show();

        Assert.Equal(1, window.FindControl<ListBox>("CommitList")!.ItemCount);
    }

    [AvaloniaFact]
    public void CompareButton_DisabledUntilSelected()
    {
        var vm = MakeVm(string.Join(US, "a1b2c3d", "a1b2c3d4", "Mia", "2026-05-30T10:00:00+00:00", "Tweak"));
        var window = new HistoryWindow(vm);
        window.Show();

        var btn = window.FindControl<Button>("CompareButton")!;
        Assert.False(btn.Command!.CanExecute(null));

        vm.Selected = vm.Commits[0];
        Assert.True(btn.Command!.CanExecute(null));
    }
}
```

- [ ] **Step 4: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~HistoryWindowTests"`
Expected: FAIL — `HistoryWindow` does not exist (compile error).

- [ ] **Step 5: Create the window XAML**

Create `DialogEditor.Avalonia/Views/HistoryWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.HistoryWindow"
        Title="{StaticResource HistoryWindow_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="720" Height="480"
        WindowStartupLocation="CenterOwner"
        Background="#252525">

    <Grid Margin="16" RowDefinitions="*,Auto">

        <TextBlock Grid.Row="0"
                   Text="{Binding StatusText}"
                   Foreground="#c08a2a" FontSize="13" TextWrapping="Wrap"
                   IsVisible="{Binding !HasCommits}"
                   VerticalAlignment="Top"/>

        <ListBox Grid.Row="0" x:Name="CommitList"
                 ItemsSource="{Binding Commits}"
                 SelectedItem="{Binding Selected, Mode=TwoWay}"
                 Background="#1a1a1a" BorderThickness="0"
                 IsVisible="{Binding HasCommits}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid ColumnDefinitions="110,140,90,*" Margin="0,3">
                        <TextBlock Grid.Column="0"
                                   Text="{Binding Date, Converter={StaticResource CommitDate}}"
                                   ToolTip.Tip="{Binding Date}"
                                   Foreground="#cfcfcf" FontSize="12"/>
                        <TextBlock Grid.Column="1" Text="{Binding Author}"
                                   Foreground="#cfcfcf" FontSize="12" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Grid.Column="2" Text="{Binding ShortSha}"
                                   Foreground="#888" FontSize="12" FontFamily="Consolas,monospace"/>
                        <TextBlock Grid.Column="3" Text="{Binding Subject}"
                                   Foreground="#e8e8e8" FontSize="12" TextWrapping="Wrap"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <StackPanel Grid.Row="1" Orientation="Horizontal"
                    HorizontalAlignment="Right" Spacing="8" Margin="0,14,0,0">
            <Button x:Name="CompareButton"
                    Content="{StaticResource HistoryWindow_CompareButton}"
                    Command="{Binding CompareCommand}"
                    ToolTip.Tip="{StaticResource HistoryWindow_CompareTooltip}"
                    Background="#2d6a2d" Foreground="White" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
            <Button x:Name="CloseButton"
                    Content="{StaticResource HistoryWindow_Close}"
                    Background="#333" Foreground="White" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
        </StackPanel>

    </Grid>

</Window>
```

- [ ] **Step 6: Create the window code-behind**

Create `DialogEditor.Avalonia/Views/HistoryWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class HistoryWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public HistoryWindow() => InitializeComponent();

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
```

Note: the `CompareWithCommit` callback is wired by `MainWindow` (Task 7) where the project path, provider, and language are available — keeping window construction free of those dependencies, consistent with how `DiffWindow` is wired from `MainWindow`.

- [ ] **Step 7: Run the test to verify it passes + build the app**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~HistoryWindowTests"`
Expected: PASS (2 tests).
Run: `dotnet build DialogEditor.Avalonia`
Expected: Build succeeded (pre-existing AVLN3001 warnings on other dialogs are unrelated).

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.Avalonia/Views/HistoryWindow.axaml DialogEditor.Avalonia/Views/HistoryWindow.axaml.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/App.axaml DialogEditor.Tests/Views/HistoryWindowTests.cs
git commit -m "feat: HistoryWindow timeline with system-date column and ISO tooltip"
```

---

## Task 7: MainWindow entry point

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml:98-102` (add menu item)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs:306-320` (add handler)

This wires the UI entry point. It is verified by build + manual launch (the existing
compare entry has no headless test either; window behaviour is covered by Task 6).

- [ ] **Step 1: Add the menu item**

In `DialogEditor.Avalonia/Views/MainWindow.axaml`, after the Compare Versions `MenuItem` (`:99-102`), add:

```xml
                        <MenuItem Header="{StaticResource Menu_History}"
                                  Click="History_Click"
                                  IsEnabled="{Binding IsProjectOpen}"
                                  ToolTip.Tip="{StaticResource ToolTip_History}"/>
```

- [ ] **Step 2: Add the click handler**

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`, after `CompareVersions_Click` (`:320`), add:

```csharp
    private void History_Click(object? sender, RoutedEventArgs e)
    {
        var vm = (MainWindowViewModel)DataContext!;
        if (vm.ProjectPath is null) return;

        var historyVm = new HistoryViewModel(new ProcessGitRunner(), vm.ProjectPath);
        historyVm.CompareWithCommit = sha =>
        {
            var diffVm = new DiffViewModel(new ProcessGitRunner(), new AvaloniaDispatcher(),
                                           vm.ProjectPath, vm.Provider, vm.Provider?.Language ?? "en",
                                           initialRightRef: sha);
            diffVm.CommitApply      = applied => _ = vm.ApplyFromDiff(applied);
            diffVm.RequestUndoApply = () => vm.UndoApplyCommand.Execute(null);
            vm.ConfirmSaveBeforeApply = () => ShowSaveBeforeApplyDialogAsync(vm);
            new DiffWindow(diffVm).Show();
        };

        new HistoryWindow(historyVm).Show();
    }
```

This reuses the exact `DiffViewModel` wiring from `CompareVersions_Click` so bring-in,
undo, and the save-before-apply guard all work identically — just with the commit preset.

- [ ] **Step 3: Build the app**

Run: `dotnet build DialogEditor.Avalonia`
Expected: Build succeeded. (`ProcessGitRunner`, `AvaloniaDispatcher`, `DiffViewModel`, `DiffWindow`, `HistoryViewModel`, `HistoryWindow` are all in scope in this file — it already constructs the diff types in `CompareVersions_Click`.)

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat: add History… entry point opening the history browser"
```

---

## Task 8: Docs + full suite

**Files:**
- Modify: `Gaps.md:31`
- Modify: `docs/superpowers/NEXT-STEPS.md`

- [ ] **Step 1: Update `Gaps.md`**

In `Gaps.md`, change the "Remaining VCS gaps" line (`:31`) to record the history browser as shipped and narrow the remaining scope:

```markdown
**Remaining VCS gaps**: **branch switching** (`git checkout` from the app) and **blame/attribution** are not started. The **history browser** is implemented: a timeline of the open project file's git history (message, author, system-formatted date with an ISO tooltip); selecting a commit opens it in the compare window (diff + selective bring-in) via a preset right endpoint.
```

- [ ] **Step 2: Update `NEXT-STEPS.md`**

In `docs/superpowers/NEXT-STEPS.md`, move the history browser to **Completed** and reduce the branch/history entry to the two remaining sub-projects. Replace the "Branch/history navigation" queued entry with:

```markdown
### Branch/history navigation — remaining sub-projects
The **history browser** (sub-project 1) shipped 2026-06-04. Remaining:
- **Attribution / blame** (read-only) — map `git blame` onto nodes; mapping JSON
  blame lines back to structured nodes is the hard part.
- **Branch switching** (read-write) — `git checkout` reconciled with the open
  project + unsaved edits; the first git write op; design its write-semantics
  carefully. Benefits from the now-shipped history UI.
```

Add to the **Completed** list:

```markdown
- **History browser** (2026-06-04) — git history timeline for the open project;
  "Compare with my copy" opens a commit in the compare window. Spec/plan:
  `docs/superpowers/specs/2026-06-04-history-browser-design.md`,
  `docs/superpowers/plans/2026-06-04-history-browser.md`.
```

- [ ] **Step 3: Run the entire suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — full green.

- [ ] **Step 4: Commit**

```bash
git add Gaps.md docs/superpowers/NEXT-STEPS.md
git commit -m "docs: record history browser as shipped; narrow remaining VCS gaps"
```

---

## Self-Review Notes

- **Spec coverage:** `CommitInfo` (T2), `GitRepoPath` extraction + loader refactor (T1), `ProjectHistoryService` with `--follow`/`0x1f`/empty/not-a-repo (T2), `DiffViewModel.initialRightRef` with synthesis (T3), `HistoryViewModel` + `CommitRowViewModel` + host callback + error/empty states (T4), `CommitDateConverter` (T5), `HistoryWindow` with date column + ISO tooltip + app icon + tooltips + empty state + converter registration (T6), MainWindow entry point reusing the diff wiring (T7), docs (T8). All spec sections covered.
- **Type consistency:** `CommitInfo(Sha, ShortSha, Author, Date, Subject)` field order matches the `%h%x1f%H%x1f%an%x1f%aI%x1f%s` parse (`f[1]`=Sha, `f[0]`=ShortSha) across service and tests. `initialRightRef` is the final optional ctor param everywhere it's called. `CompareWithCommit`/`CompareCommand`/`Selected`/`HasCommits`/`StatusText` names consistent between VM, window XAML bindings, and tests. Control names `CommitList`/`CompareButton`/`CloseButton` consistent between XAML and tests. Converter key `CommitDate` consistent between App.axaml and the window binding.
- **No placeholders:** every code step shows full content; commands have expected output.
