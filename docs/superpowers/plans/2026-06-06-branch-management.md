# Branch Management Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add full local-branch management (switch, create, rename, delete) for the open `.dialogproject`, surfaced in a dedicated Branches window, with safe write-semantics that never silently lose work.

**Architecture:** A new `IGitRunner`-backed `GitBranchService` (in `DialogEditor.Patch`) returns *typed results* (never raw git text). A `BranchesViewModel` orchestrates; switching coordinates with `MainWindowViewModel` through two host callbacks (guard unsaved edits, reload from disk). A dedicated `BranchesWindow` mirrors the shipped History/Attribution tools. A shared `GitMissing` exception kind gives every git feature a distinct "git isn't installed" message.

**Tech Stack:** C# / .NET 8, CommunityToolkit.Mvvm, Avalonia 11.3, xUnit. Spec: `docs/superpowers/specs/2026-06-05-branch-management-design.md`.

---

## Conventions for every task

- **TDD:** write the failing test first, watch it fail, implement minimally, watch it pass, commit.
- **Test runner:** `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter <Name>` (the suite runs serially by design — see `project_flaky_test_appsettings`).
- **Localization:** no user-visible string is hard-coded; VMs call `Loc.Get(key)` / `Loc.Format(key, args)`. Tests configure `Loc.Configure(new StubStringProvider())` (returns the key verbatim), so VM tests assert non-empty `StatusText` without needing real copy.
- **Logging:** every caught exception is logged via `AppLog.Warn`/`AppLog.Error` (except `OperationCanceledException`). `DialogEditor.Patch` cannot reference `AppLog` (it sits below ViewModels) — services stay log-free; the VM logs.
- **Commits:** end every commit message with the trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## File Structure

**Create:**
- `DialogEditor.Patch/Diff/GitBranchService.cs` — git branch ops + `BranchInfo`/`BranchOpStatus`/`BranchOpResult` types.
- `DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs` — orchestration + `BranchRowViewModel`/`PendingCommit`.
- `DialogEditor.Avalonia/Views/BranchesWindow.axaml(.cs)` — the window.
- `DialogEditor.Avalonia/Views/CommitConsentDialog.axaml(.cs)` — file-list + message consent dialog.
- `DialogEditor.Avalonia/Views/BranchNameDialog.axaml(.cs)` — name prompt (New / Rename).
- `DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml(.cs)` — strong delete confirm.
- Tests mirroring each under `DialogEditor.Tests/`.

**Modify:**
- `DialogEditor.Patch/Diff/DiffException.cs` — add `GitMissing` kind.
- `DialogEditor.Patch/Diff/ProcessGitRunner.cs` — classify a missing git executable.
- `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` — `EnsureNoUnsavedEditsAsync`, `ReloadCurrentProjectFromDisk`, cancel-path TCS completion.
- `DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs`, `DiffViewModel.cs`, `BlameViewModel.cs` — map `GitMissing` to its own message.
- `DialogEditor.Avalonia/Views/MainWindow.axaml(.cs)` — Branches… entry + host-callback wiring.
- `DialogEditor.Avalonia/Resources/Strings.axaml` — all new copy.
- `Gaps.md`, `docs/superpowers/NEXT-STEPS.md` — mark shipped.

---

## Task 1: Shared `GitMissing` exception kind

**Files:**
- Modify: `DialogEditor.Patch/Diff/DiffException.cs`
- Modify: `DialogEditor.Patch/Diff/ProcessGitRunner.cs`
- Test: `DialogEditor.Tests/Patch/Diff/ProcessGitRunnerTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using System.ComponentModel;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProcessGitRunnerTests
{
    [Fact]
    public void ClassifyStartFailure_ExecutableNotFound_IsGitMissing()
    {
        // Win32 error 2 == ERROR_FILE_NOT_FOUND (git not on PATH).
        var ex = ProcessGitRunner.ClassifyStartFailure(new Win32Exception(2));
        Assert.Equal(DiffExceptionKind.GitMissing, ex.Kind);
    }

    [Fact]
    public void ClassifyStartFailure_OtherError_IsUnknown()
    {
        var ex = ProcessGitRunner.ClassifyStartFailure(new InvalidOperationException("boom"));
        Assert.Equal(DiffExceptionKind.Unknown, ex.Kind);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter ProcessGitRunnerTests`
Expected: FAIL — `GitMissing` does not exist / `ClassifyStartFailure` not defined.

- [ ] **Step 3: Add the `GitMissing` kind**

In `DiffException.cs`, extend the enum:

```csharp
public enum DiffExceptionKind { Unknown, NotARepo, BadRef, FileNotFound, ReadFailed, ParseFailed, GitMissing }
```

- [ ] **Step 4: Add the classifier and use it in `ProcessGitRunner`**

Replace the `catch` block body in `ProcessGitRunner.Run` and add the helper:

```csharp
        catch (Exception ex) when (ex is not DiffException)
        {
            throw ClassifyStartFailure(ex);
        }
    }

    /// Maps a Process.Start failure to a DiffException. A Win32 "file not found"
    /// (error 2) means the git executable isn't on PATH → GitMissing; anything
    /// else stays generic. Exposed for unit testing (we can't summon a missing git).
    public static DiffException ClassifyStartFailure(Exception ex) =>
        ex is System.ComponentModel.Win32Exception { NativeErrorCode: 2 }
            ? new DiffException($"git is not installed or not on PATH: {ex.Message}", DiffExceptionKind.GitMissing)
            : new DiffException($"git is not available: {ex.Message}", DiffExceptionKind.Unknown);
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter ProcessGitRunnerTests`
Expected: PASS (both facts).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Patch/Diff/DiffException.cs DialogEditor.Patch/Diff/ProcessGitRunner.cs DialogEditor.Tests/Patch/Diff/ProcessGitRunnerTests.cs
git commit -m "feat: distinguish missing-git from other git failures (GitMissing kind)"
```

---

## Task 2: `GitBranchService` — types, `List`, current-branch marker

**Files:**
- Create: `DialogEditor.Patch/Diff/GitBranchService.cs`
- Test: `DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class GitBranchServiceTests
{
    private static string ProjPath() =>
        Path.Combine(Path.GetTempPath(), $"branch_{Guid.NewGuid():N}.dialogproject");

    private static readonly string Root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

    private sealed class FakeGit : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public required Func<string[], GitResult> Handler;
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return Handler(args);
        }
    }

    // Answers repo-resolution + current-branch; delegates the rest to `rest`.
    private static FakeGit Git(Func<string[], GitResult?> rest) => new()
    {
        Handler = a =>
            a is ["rev-parse", "--show-toplevel"]    ? new GitResult(0, Root + "\n", "") :
            a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "main\n", "") :
            rest(a) ?? new GitResult(0, "", ""),
    };

    [Fact]
    public void List_ReturnsBranches_WithCurrentFlagged()
    {
        var git = Git(a => a is ["for-each-ref", ..] ? new GitResult(0, "main\nfeature/x\n", "") : null);

        var branches = new GitBranchService(git).List(ProjPath());

        Assert.Equal(2, branches.Count);
        Assert.Equal("main", branches[0].Name);
        Assert.True(branches[0].IsCurrent);
        Assert.Equal("feature/x", branches[1].Name);
        Assert.False(branches[1].IsCurrent);
    }

    [Fact]
    public void List_NotARepo_Throws()
    {
        var git = new FakeGit { Handler = _ => new GitResult(128, "", "fatal: not a git repository") };
        var ex = Assert.Throws<DiffException>(() => new GitBranchService(git).List(ProjPath()));
        Assert.Equal(DiffExceptionKind.NotARepo, ex.Kind);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: FAIL — `GitBranchService` not defined.

- [ ] **Step 3: Create the service with types, `List`, and current-branch helper**

```csharp
namespace DialogEditor.Patch.Diff;

/// One local branch. Presentation-free.
public record BranchInfo(string Name, bool IsCurrent);

public enum BranchOpStatus
{
    Ok,
    BlockedByLocalChanges,    // tracked modifications block checkout → offer commit-all
    BlockedByUntrackedFiles,  // untracked files would be overwritten → cannot auto-fix (case A)
    NotMerged,                // safe delete refused → offer force-delete
    NameInvalid,              // create/rename: name fails git ref-format rules
    NameExists,               // create/rename: a branch with that name already exists
    GitMissing,               // git executable not installed / not on PATH
    NotARepo,                 // git present, but the project is not inside a git repo
    GitFailed                 // any other non-zero exit; Detail carries stderr for the log
}

public record BranchOpResult(BranchOpStatus Status, string? Detail = null)
{
    public static readonly BranchOpResult Success = new(BranchOpStatus.Ok);
}

/// Local-branch operations over a project file's git repo. Returns typed results
/// (never raw git text); the VM maps Status to localized copy. Testable via IGitRunner.
public class GitBranchService(IGitRunner git)
{
    public IReadOnlyList<BranchInfo> List(string projectFilePath)
    {
        var (dir, _) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);
        var current  = CurrentBranch(dir);

        var res = git.Run(dir, "for-each-ref", "refs/heads", "--format=%(refname:short)");
        if (!res.Ok)
            throw new DiffException($"Could not list branches: {res.StdErr.Trim()}", DiffExceptionKind.Unknown);

        var branches = new List<BranchInfo>();
        foreach (var raw in res.StdOut.Split('\n'))
        {
            var name = raw.Trim();
            if (name.Length == 0) continue;
            branches.Add(new BranchInfo(name, IsCurrent: name == current));
        }
        return branches;
    }

    // null when detached (HEAD) or unreadable.
    private string? CurrentBranch(string dir)
    {
        var res  = git.Run(dir, "rev-parse", "--abbrev-ref", "HEAD");
        var name = res.StdOut.Trim();
        return res.Ok && name.Length > 0 && name != "HEAD" ? name : null;
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Diff/GitBranchService.cs DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs
git commit -m "feat: GitBranchService.List with current-branch marker"
```

---

## Task 3: `GitBranchService.Checkout` + porcelain failure classification

**Files:**
- Modify: `DialogEditor.Patch/Diff/GitBranchService.cs`
- Test: `DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs`

- [ ] **Step 1: Write the failing tests** (append to the class)

```csharp
    [Fact]
    public void Checkout_Success_ReturnsOk()
    {
        var git = Git(a => a is ["checkout", "feature/x"] ? new GitResult(0, "", "") : null);
        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");
        Assert.Equal(BranchOpStatus.Ok, r.Status);
    }

    [Fact]
    public void Checkout_BlockedByTrackedChanges_IsBlockedByLocalChanges()
    {
        var git = Git(a =>
            a is ["checkout", ..]          ? new GitResult(1, "", "would be overwritten") :
            a is ["status", "--porcelain"] ? new GitResult(0, " M conv.dialogproject\n", "") : null);
        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");
        Assert.Equal(BranchOpStatus.BlockedByLocalChanges, r.Status);
    }

    [Fact]
    public void Checkout_BlockedByUntrackedOnly_IsBlockedByUntrackedFiles()
    {
        var git = Git(a =>
            a is ["checkout", ..]          ? new GitResult(1, "", "would be overwritten") :
            a is ["status", "--porcelain"] ? new GitResult(0, "?? newfile.txt\n", "") : null);
        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");
        Assert.Equal(BranchOpStatus.BlockedByUntrackedFiles, r.Status);
    }

    [Fact]
    public void Checkout_FailsWithCleanTree_IsGitFailed()
    {
        var git = Git(a =>
            a is ["checkout", ..]          ? new GitResult(1, "", "some other error") :
            a is ["status", "--porcelain"] ? new GitResult(0, "", "") : null);
        var r = new GitBranchService(git).Checkout(ProjPath(), "feature/x");
        Assert.Equal(BranchOpStatus.GitFailed, r.Status);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: FAIL — `Checkout` not defined.

- [ ] **Step 3: Implement `Checkout`, the guard helper, and classification** (add to the service)

```csharp
    public BranchOpResult Checkout(string projectFilePath, string branch)
        => Guarded(projectFilePath, dir =>
        {
            var res = git.Run(dir, "checkout", branch);
            return res.Ok ? BranchOpResult.Success : ClassifyCheckoutFailure(dir, res);
        });

    // Runs `op` against the resolved repo dir, mapping a DiffException (not-a-repo /
    // git-missing) to a typed result instead of throwing. Mutating ops use this so the
    // VM can branch on Status; List() throws (the VM ctor catches it, like History).
    private BranchOpResult Guarded(string projectFilePath, Func<string, BranchOpResult> op)
    {
        try
        {
            var (dir, _) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);
            return op(dir);
        }
        catch (DiffException ex)
        {
            return new BranchOpResult(ex.Kind switch
            {
                DiffExceptionKind.GitMissing => BranchOpStatus.GitMissing,
                DiffExceptionKind.NotARepo   => BranchOpStatus.NotARepo,
                _                            => BranchOpStatus.GitFailed,
            }, ex.Message);
        }
    }

    // Locale-safe: a failed checkout is classified by git status --porcelain, not by
    // parsing English stderr. Tracked modifications → offer commit; only untracked → case A.
    private BranchOpResult ClassifyCheckoutFailure(string dir, GitResult checkout)
    {
        var status = git.Run(dir, "status", "--porcelain");
        bool hasTracked = false, hasUntracked = false;
        foreach (var raw in status.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0) continue;
            if (line.StartsWith("??")) hasUntracked = true;
            else                       hasTracked   = true;
        }
        if (hasTracked)   return new BranchOpResult(BranchOpStatus.BlockedByLocalChanges,  checkout.StdErr.Trim());
        if (hasUntracked) return new BranchOpResult(BranchOpStatus.BlockedByUntrackedFiles, checkout.StdErr.Trim());
        return new BranchOpResult(BranchOpStatus.GitFailed, checkout.StdErr.Trim());
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: PASS (all checkout facts).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Diff/GitBranchService.cs DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs
git commit -m "feat: GitBranchService.Checkout with porcelain-based block classification"
```

---

## Task 4: `GitBranchService.ListUncommittedChanges` + `CommitAll`

**Files:**
- Modify: `DialogEditor.Patch/Diff/GitBranchService.cs`
- Test: `DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void ListUncommittedChanges_ExcludesUntracked_ReturnsTrackedPaths()
    {
        var git = Git(a => a is ["status", "--porcelain"]
            ? new GitResult(0, " M conv.dialogproject\nA  added.json\n?? scratch.tmp\n", "") : null);

        var files = new GitBranchService(git).ListUncommittedChanges(ProjPath());

        Assert.Equal(new[] { "conv.dialogproject", "added.json" }, files);
    }

    [Fact]
    public void CommitAll_IssuesCommitDashA_ReturnsOk()
    {
        string[]? committed = null;
        var git = Git(a => { if (a.Length > 0 && a[0] == "commit") committed = a; return a[0] == "commit" ? new GitResult(0, "", "") : null; });

        var r = new GitBranchService(git).CommitAll(ProjPath(), "my message");

        Assert.Equal(BranchOpStatus.Ok, r.Status);
        Assert.Equal(new[] { "commit", "-a", "-m", "my message" }, committed);
    }

    [Fact]
    public void CommitAll_Failure_IsGitFailed()
    {
        var git = Git(a => a.Length > 0 && a[0] == "commit" ? new GitResult(1, "", "nothing to commit") : null);
        var r = new GitBranchService(git).CommitAll(ProjPath(), "msg");
        Assert.Equal(BranchOpStatus.GitFailed, r.Status);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: FAIL — methods not defined.

- [ ] **Step 3: Implement both methods** (add to the service)

```csharp
    public IReadOnlyList<string> ListUncommittedChanges(string projectFilePath)
    {
        var (dir, _) = GitRepoPath.ResolveRepoRelative(git, projectFilePath);
        var res = git.Run(dir, "status", "--porcelain");
        if (!res.Ok)
            throw new DiffException($"Could not read git status: {res.StdErr.Trim()}", DiffExceptionKind.Unknown);

        var files = new List<string>();
        foreach (var raw in res.StdOut.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length < 4) continue;       // porcelain entries are "XY <path>"
            if (line.StartsWith("??")) continue; // untracked excluded (tracked-only commit)
            files.Add(line[3..].Trim());
        }
        return files;
    }

    public BranchOpResult CommitAll(string projectFilePath, string message)
        => Guarded(projectFilePath, dir =>
        {
            var res = git.Run(dir, "commit", "-a", "-m", message);
            return res.Ok ? BranchOpResult.Success
                          : new BranchOpResult(BranchOpStatus.GitFailed, res.StdErr.Trim());
        });
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Diff/GitBranchService.cs DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs
git commit -m "feat: GitBranchService.ListUncommittedChanges (tracked-only) + CommitAll"
```

---

## Task 5: `GitBranchService.Create`

**Files:**
- Modify: `DialogEditor.Patch/Diff/GitBranchService.cs`
- Test: `DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    // Helper: validation answers for create/rename. valid=check-ref-format ok; exists=show-ref ok.
    private static FakeGit GitForNameOps(bool valid, bool exists, Func<string[], GitResult?> rest) => new()
    {
        Handler = a =>
            a is ["rev-parse", "--show-toplevel"]      ? new GitResult(0, Root + "\n", "") :
            a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "main\n", "") :
            a is ["check-ref-format", ..]              ? new GitResult(valid  ? 0 : 1, "", "") :
            a is ["show-ref", ..]                      ? new GitResult(exists ? 0 : 1, "", "") :
            rest(a) ?? new GitResult(0, "", ""),
    };

    [Fact]
    public void Create_InvalidName_IsNameInvalid()
    {
        var git = GitForNameOps(valid: false, exists: false, _ => null);
        Assert.Equal(BranchOpStatus.NameInvalid, new GitBranchService(git).Create(ProjPath(), "bad name").Status);
    }

    [Fact]
    public void Create_ExistingName_IsNameExists()
    {
        var git = GitForNameOps(valid: true, exists: true, _ => null);
        Assert.Equal(BranchOpStatus.NameExists, new GitBranchService(git).Create(ProjPath(), "feature/x").Status);
    }

    [Fact]
    public void Create_Valid_IssuesCheckoutDashB()
    {
        string[]? created = null;
        var git = GitForNameOps(valid: true, exists: false, a =>
        {
            if (a is ["checkout", "-b", ..]) { created = a; return new GitResult(0, "", ""); }
            return null;
        });

        var r = new GitBranchService(git).Create(ProjPath(), "feature/new");

        Assert.Equal(BranchOpStatus.Ok, r.Status);
        Assert.Equal(new[] { "checkout", "-b", "feature/new" }, created);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: FAIL — `Create` not defined.

- [ ] **Step 3: Implement `Create` + name validators** (add to the service)

```csharp
    public BranchOpResult Create(string projectFilePath, string newName)
        => Guarded(projectFilePath, dir =>
        {
            if (!IsValidName(dir, newName)) return new BranchOpResult(BranchOpStatus.NameInvalid);
            if (BranchExists(dir, newName)) return new BranchOpResult(BranchOpStatus.NameExists);
            var res = git.Run(dir, "checkout", "-b", newName);  // creates from HEAD AND switches; working tree unchanged
            return res.Ok ? BranchOpResult.Success : new BranchOpResult(BranchOpStatus.GitFailed, res.StdErr.Trim());
        });

    private bool IsValidName(string dir, string name)
        => git.Run(dir, "check-ref-format", "--branch", name).Ok;

    private bool BranchExists(string dir, string name)
        => git.Run(dir, "show-ref", "--verify", "--quiet", $"refs/heads/{name}").Ok;
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Diff/GitBranchService.cs DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs
git commit -m "feat: GitBranchService.Create (validate, checkout -b)"
```

---

## Task 6: `GitBranchService.Rename`

**Files:**
- Modify: `DialogEditor.Patch/Diff/GitBranchService.cs`
- Test: `DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Rename_Other_IssuesBranchDashM_FromTo()
    {
        string[]? renamed = null;
        var git = GitForNameOps(valid: true, exists: false, a =>
        {
            if (a is ["branch", "-m", ..]) { renamed = a; return new GitResult(0, "", ""); }
            return null;
        });

        var r = new GitBranchService(git).Rename(ProjPath(), "old", "new");

        Assert.Equal(BranchOpStatus.Ok, r.Status);
        Assert.Equal(new[] { "branch", "-m", "old", "new" }, renamed);
    }

    [Fact]
    public void Rename_Current_NullFrom_IssuesBranchDashM_ToOnly()
    {
        string[]? renamed = null;
        var git = GitForNameOps(valid: true, exists: false, a =>
        {
            if (a is ["branch", "-m", ..]) { renamed = a; return new GitResult(0, "", ""); }
            return null;
        });

        new GitBranchService(git).Rename(ProjPath(), null, "renamed");

        Assert.Equal(new[] { "branch", "-m", "renamed" }, renamed);
    }

    [Fact]
    public void Rename_DuplicateName_IsNameExists()
    {
        var git = GitForNameOps(valid: true, exists: true, _ => null);
        Assert.Equal(BranchOpStatus.NameExists, new GitBranchService(git).Rename(ProjPath(), "old", "taken").Status);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: FAIL — `Rename` not defined.

- [ ] **Step 3: Implement `Rename`** (add to the service)

```csharp
    public BranchOpResult Rename(string projectFilePath, string? from, string to)
        => Guarded(projectFilePath, dir =>
        {
            if (!IsValidName(dir, to)) return new BranchOpResult(BranchOpStatus.NameInvalid);
            if (BranchExists(dir, to)) return new BranchOpResult(BranchOpStatus.NameExists);
            var res = from is null
                ? git.Run(dir, "branch", "-m", to)        // rename the current branch
                : git.Run(dir, "branch", "-m", from, to); // rename a named branch
            return res.Ok ? BranchOpResult.Success : new BranchOpResult(BranchOpStatus.GitFailed, res.StdErr.Trim());
        });
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Diff/GitBranchService.cs DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs
git commit -m "feat: GitBranchService.Rename (current and named branch)"
```

---

## Task 7: `GitBranchService.Delete` (safe + force)

**Files:**
- Modify: `DialogEditor.Patch/Diff/GitBranchService.cs`
- Test: `DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public void Delete_Safe_Success()
    {
        var git = Git(a => a is ["branch", "-d", "feature/x"] ? new GitResult(0, "", "") : null);
        Assert.Equal(BranchOpStatus.Ok, new GitBranchService(git).Delete(ProjPath(), "feature/x", force: false).Status);
    }

    [Fact]
    public void Delete_SafeRefusedUnmerged_IsNotMerged()
    {
        var git = Git(a => a is ["branch", "-d", ..] ? new GitResult(1, "", "not fully merged") : null);
        Assert.Equal(BranchOpStatus.NotMerged, new GitBranchService(git).Delete(ProjPath(), "feature/x", force: false).Status);
    }

    [Fact]
    public void Delete_Force_IssuesDashD()
    {
        string[]? deleted = null;
        var git = Git(a => { if (a is ["branch", "-D", ..]) { deleted = a; return new GitResult(0, "", ""); } return null; });

        var r = new GitBranchService(git).Delete(ProjPath(), "feature/x", force: true);

        Assert.Equal(BranchOpStatus.Ok, r.Status);
        Assert.Equal(new[] { "branch", "-D", "feature/x" }, deleted);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: FAIL — `Delete` not defined.

- [ ] **Step 3: Implement `Delete`** (add to the service)

```csharp
    public BranchOpResult Delete(string projectFilePath, string branch, bool force)
        => Guarded(projectFilePath, dir =>
        {
            var res = git.Run(dir, "branch", force ? "-D" : "-d", branch);
            if (res.Ok) return BranchOpResult.Success;
            // Safe (-d) refusal is almost always "not fully merged" → offer force.
            return force
                ? new BranchOpResult(BranchOpStatus.GitFailed, res.StdErr.Trim())
                : new BranchOpResult(BranchOpStatus.NotMerged, res.StdErr.Trim());
        });
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitBranchServiceTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Diff/GitBranchService.cs DialogEditor.Tests/Patch/Diff/GitBranchServiceTests.cs
git commit -m "feat: GitBranchService.Delete (safe -d, force -D)"
```

---

## Task 8: `BranchesViewModel` — load + status states

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/BranchesViewModelTests.cs` (create)

- [ ] **Step 1: Write the failing test**

```csharp
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class BranchesViewModelTests
{
    public BranchesViewModelTests() => Loc.Configure(new StubStringProvider());

    private static readonly string Root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);
    private static string ProjPath() => Path.Combine(Path.GetTempPath(), $"bvm_{Guid.NewGuid():N}.dialogproject");

    private sealed class FakeGit : IGitRunner
    {
        public required Func<string[], GitResult> Handler;
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static FakeGit Git(Func<string[], GitResult?> rest) => new()
    {
        Handler = a =>
            a is ["rev-parse", "--show-toplevel"]      ? new GitResult(0, Root + "\n", "") :
            a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "main\n", "") :
            rest(a) ?? new GitResult(0, "", ""),
    };

    private static GitBranchService TwoBranches() =>
        new(Git(a => a is ["for-each-ref", ..] ? new GitResult(0, "main\nfeature/x\n", "") : null));

    [Fact]
    public void Ctor_LoadsBranches()
    {
        var vm = new BranchesViewModel(TwoBranches(), ProjPath());
        Assert.True(vm.HasBranches);
        Assert.Equal(2, vm.Branches.Count);
        Assert.True(vm.Branches[0].IsCurrent);
    }

    [Fact]
    public void Ctor_NotARepo_SetsStatus_NoBranches()
    {
        var svc = new GitBranchService(new FakeGit { Handler = _ => new GitResult(128, "", "fatal") });
        var vm  = new BranchesViewModel(svc, ProjPath());
        Assert.False(vm.HasBranches);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public void Switch_DisabledUntilSelected()
    {
        var vm = new BranchesViewModel(TwoBranches(), ProjPath());
        Assert.False(vm.SwitchCommand.CanExecute(null));
        vm.Selected = vm.Branches[1];
        Assert.True(vm.SwitchCommand.CanExecute(null));
    }

    [Fact]
    public void Delete_DisabledForCurrentBranch()
    {
        var vm = new BranchesViewModel(TwoBranches(), ProjPath());
        vm.Selected = vm.Branches[0];          // current
        Assert.False(vm.DeleteCommand.CanExecute(null));
        vm.Selected = vm.Branches[1];          // not current
        Assert.True(vm.DeleteCommand.CanExecute(null));
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesViewModelTests`
Expected: FAIL — `BranchesViewModel` not defined.

- [ ] **Step 3: Create the VM skeleton with load + command stubs**

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Patch.Diff;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class BranchRowViewModel(BranchInfo info) : ObservableObject
{
    public string Name      => info.Name;
    public bool   IsCurrent => info.IsCurrent;
}

/// The set of files a commit-then-switch will commit, plus a default message.
public record PendingCommit(IReadOnlyList<string> Files, string DefaultMessage);

/// Lists and manages the open project's local git branches. Switching coordinates
/// with the host (unsaved-edits guard + reload) via callbacks; the VM never opens windows.
public partial class BranchesViewModel : ObservableObject
{
    private readonly GitBranchService _service;
    private readonly string _projectFilePath;

    [ObservableProperty] private BranchRowViewModel? _selected;
    [ObservableProperty] private string _statusText = "";

    public ObservableCollection<BranchRowViewModel> Branches { get; } = [];
    public bool HasBranches => Branches.Count > 0;

    // ── Host callbacks ──
    public Func<Task<bool>>?                   EnsureNoUnsavedEdits      { get; set; }
    public Action?                             ReloadProjectFromDisk     { get; set; }
    public Func<PendingCommit, Task<string?>>? RequestCommitConfirmation { get; set; }
    public Func<string, Task<bool>>?           ConfirmForceDelete        { get; set; }
    public Func<string?, Task<string?>>?       RequestBranchName         { get; set; }

    public BranchesViewModel(GitBranchService service, string projectFilePath)
    {
        _service = service;
        _projectFilePath = projectFilePath;
        LoadBranches();
    }

    private void LoadBranches()
    {
        Branches.Clear();
        try
        {
            foreach (var b in _service.List(_projectFilePath))
                Branches.Add(new BranchRowViewModel(b));
            StatusText = Branches.Count == 0 ? Loc.Get("Branches_StatusNone") : "";
        }
        catch (DiffException ex)
        {
            AppLog.Warn($"BranchesViewModel: could not list branches: {ex.Message}");
            StatusText = ex.Kind switch
            {
                DiffExceptionKind.GitMissing => Loc.Get("Branches_StatusGitMissing"),
                DiffExceptionKind.NotARepo   => Loc.Get("Branches_StatusNotARepo"),
                _                            => Loc.Get("Branches_StatusError"),
            };
        }
        OnPropertyChanged(nameof(HasBranches));
        NotifyCommands();
    }

    private void NotifyCommands()
    {
        SwitchCommand.NotifyCanExecuteChanged();
        RenameCommand.NotifyCanExecuteChanged();
        DeleteCommand.NotifyCanExecuteChanged();
    }

    partial void OnSelectedChanged(BranchRowViewModel? value) => NotifyCommands();

    private bool CanActOnSelection => Selected is not null;
    private bool CanDelete         => Selected is { IsCurrent: false };

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task SwitchAsync() => Task.CompletedTask;   // implemented in Task 9–10

    [RelayCommand]
    private Task CreateAsync() => Task.CompletedTask;   // implemented in Task 11

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private Task RenameAsync() => Task.CompletedTask;   // implemented in Task 11

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private Task DeleteAsync() => Task.CompletedTask;   // implemented in Task 11
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesViewModelTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs DialogEditor.Tests/ViewModels/BranchesViewModelTests.cs
git commit -m "feat: BranchesViewModel load + status/command-enable states"
```

---

## Task 9: `BranchesViewModel.SwitchAsync` — happy path + cancel-at-guard

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/BranchesViewModelTests.cs`

- [ ] **Step 1: Write the failing tests** (append; extend the `Git` helper usage to answer checkout)

```csharp
    [Fact]
    public async Task Switch_HappyPath_ChecksOut_GuardsThenReloads()
    {
        var order = new List<string>();
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["checkout", "feature/x"]) { order.Add("checkout"); return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath())
        {
            EnsureNoUnsavedEdits  = () => { order.Add("guard"); return Task.FromResult(true); },
            ReloadProjectFromDisk = () => order.Add("reload"),
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "guard", "checkout", "reload" }, order);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task Switch_CancelledAtGuard_DoesNotCheckout()
    {
        var checkedOut = false;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["checkout", ..]) { checkedOut = true; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath())
        {
            EnsureNoUnsavedEdits = () => Task.FromResult(false),   // user cancelled
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.False(checkedOut);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesViewModelTests`
Expected: FAIL — `SwitchAsync` is a no-op.

- [ ] **Step 3: Implement `SwitchAsync` (happy + guard) and `FinishSwitch`**

Replace the `SwitchAsync` stub:

```csharp
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task SwitchAsync()
    {
        var target = Selected!.Name;

        if (EnsureNoUnsavedEdits is not null && !await EnsureNoUnsavedEdits())
            return;   // cancelled at the unsaved-edits gate

        var result = _service.Checkout(_projectFilePath, target);
        FinishSwitch(result, target);
    }

    private void FinishSwitch(BranchOpResult result, string target)
    {
        switch (result.Status)
        {
            case BranchOpStatus.Ok:
                ReloadProjectFromDisk?.Invoke();
                LoadBranches();
                StatusText = Loc.Format("Branches_StatusSwitched", target);
                break;
            case BranchOpStatus.BlockedByUntrackedFiles:
                StatusText = Loc.Get("Branches_StatusBlockedUntracked");
                break;
            default:
                AppLog.Warn($"BranchesViewModel: switch to '{target}' failed: {result.Status} {result.Detail}");
                StatusText = Loc.Get("Branches_StatusSwitchFailed");
                break;
        }
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesViewModelTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs DialogEditor.Tests/ViewModels/BranchesViewModelTests.cs
git commit -m "feat: BranchesViewModel.SwitchAsync happy path + unsaved-edits guard"
```

---

## Task 10: `BranchesViewModel.SwitchAsync` — blocked → commit consent → retry; untracked case-A

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/BranchesViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    // Checkout blocks once (tracked), succeeds after a commit; status reports tracked changes.
    private static GitBranchService BlockingThenCommitting(List<string> log) => new(Git(a =>
    {
        if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
        if (a is ["status", "--porcelain"]) return new GitResult(0, " M conv.dialogproject\n", "");
        if (a is ["checkout", "feature/x"])
        {
            log.Add("checkout");
            return log.Contains("commit") ? new GitResult(0, "", "") : new GitResult(1, "", "would be overwritten");
        }
        if (a.Length > 0 && a[0] == "commit") { log.Add("commit"); return new GitResult(0, "", ""); }
        return null;
    }));

    [Fact]
    public async Task Switch_Blocked_OffersCommit_ThenRetriesAndReloads()
    {
        var log = new List<string>();
        PendingCommit? shown = null;
        var vm = new BranchesViewModel(BlockingThenCommitting(log), ProjPath())
        {
            EnsureNoUnsavedEdits      = () => Task.FromResult(true),
            ReloadProjectFromDisk     = () => log.Add("reload"),
            RequestCommitConfirmation = p => { shown = p; return Task.FromResult<string?>("commit msg"); },
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.NotNull(shown);
        Assert.Contains("conv.dialogproject", shown!.Files);
        Assert.Equal(new[] { "checkout", "commit", "checkout", "reload" }, log);
    }

    [Fact]
    public async Task Switch_Blocked_ConsentCancelled_DoesNotCommit()
    {
        var log = new List<string>();
        var vm = new BranchesViewModel(BlockingThenCommitting(log), ProjPath())
        {
            EnsureNoUnsavedEdits      = () => Task.FromResult(true),
            RequestCommitConfirmation = _ => Task.FromResult<string?>(null),   // cancelled
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.DoesNotContain("commit", log);
    }

    [Fact]
    public async Task Switch_BlockedByUntracked_ShowsCaseAStatus_NoCommit()
    {
        var committed = false;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            if (a is ["checkout", ..]) return new GitResult(1, "", "would be overwritten");
            if (a is ["status", "--porcelain"]) return new GitResult(0, "?? new.tmp\n", "");
            if (a.Length > 0 && a[0] == "commit") { committed = true; return new GitResult(0, "", ""); }
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath())
        {
            EnsureNoUnsavedEdits      = () => Task.FromResult(true),
            RequestCommitConfirmation = _ => Task.FromResult<string?>("x"),
        };
        vm.Selected = vm.Branches[1];

        await vm.SwitchCommand.ExecuteAsync(null);

        Assert.False(committed);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesViewModelTests`
Expected: FAIL — block handling not implemented.

- [ ] **Step 3: Extend `SwitchAsync` with the commit-then-switch branch**

Replace `SwitchAsync` with:

```csharp
    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task SwitchAsync()
    {
        var target = Selected!.Name;

        if (EnsureNoUnsavedEdits is not null && !await EnsureNoUnsavedEdits())
            return;

        var result = _service.Checkout(_projectFilePath, target);

        if (result.Status == BranchOpStatus.BlockedByLocalChanges)
        {
            IReadOnlyList<string> files;
            try { files = _service.ListUncommittedChanges(_projectFilePath); }
            catch (DiffException) { files = []; }

            var message = RequestCommitConfirmation is null
                ? null
                : await RequestCommitConfirmation(new PendingCommit(files, Loc.Get("Branches_DefaultCommitMessage")));
            if (message is null) return;   // consent cancelled / no handler

            var commit = _service.CommitAll(_projectFilePath, message);
            if (commit.Status != BranchOpStatus.Ok)
            {
                AppLog.Warn($"BranchesViewModel: commit before switch failed: {commit.Detail}");
                StatusText = Loc.Get("Branches_StatusCommitFailed");
                return;
            }
            result = _service.Checkout(_projectFilePath, target);
        }

        FinishSwitch(result, target);
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesViewModelTests`
Expected: PASS (all switch facts).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs DialogEditor.Tests/ViewModels/BranchesViewModelTests.cs
git commit -m "feat: BranchesViewModel commit-then-switch with consent + case-A handling"
```

---

## Task 11: `BranchesViewModel` — Create / Rename / Delete commands

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/BranchesViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    [Fact]
    public async Task Create_PromptsName_AndCreates()
    {
        string[]? created = null;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["check-ref-format", ..]) return new GitResult(0, "", "");
            if (a is ["show-ref", ..]) return new GitResult(1, "", "");
            if (a is ["checkout", "-b", ..]) { created = a; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { RequestBranchName = _ => Task.FromResult<string?>("feature/new") };

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "checkout", "-b", "feature/new" }, created);
    }

    [Fact]
    public async Task Create_NameExists_SetsStatus()
    {
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["check-ref-format", ..]) return new GitResult(0, "", "");
            if (a is ["show-ref", ..]) return new GitResult(0, "", "");   // already exists
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { RequestBranchName = _ => Task.FromResult<string?>("main") };

        await vm.CreateCommand.ExecuteAsync(null);

        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    [Fact]
    public async Task Delete_NotMerged_AsksForceConfirm_ThenForceDeletes()
    {
        string[]? forced = null;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["branch", "-d", ..]) return new GitResult(1, "", "not fully merged");
            if (a is ["branch", "-D", ..]) { forced = a; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { ConfirmForceDelete = _ => Task.FromResult(true) };
        vm.Selected = vm.Branches[1];

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.Equal(new[] { "branch", "-D", "feature/x" }, forced);
    }

    [Fact]
    public async Task Delete_NotMerged_ConfirmDeclined_DoesNotForce()
    {
        var forced = false;
        var svc = new GitBranchService(Git(a =>
        {
            if (a is ["branch", "-d", ..]) return new GitResult(1, "", "not fully merged");
            if (a is ["branch", "-D", ..]) { forced = true; return new GitResult(0, "", ""); }
            if (a is ["for-each-ref", ..]) return new GitResult(0, "main\nfeature/x\n", "");
            return null;
        }));
        var vm = new BranchesViewModel(svc, ProjPath()) { ConfirmForceDelete = _ => Task.FromResult(false) };
        vm.Selected = vm.Branches[1];

        await vm.DeleteCommand.ExecuteAsync(null);

        Assert.False(forced);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesViewModelTests`
Expected: FAIL — commands are no-ops.

- [ ] **Step 3: Implement the three commands + shared result handler**

Replace the `CreateAsync` / `RenameAsync` / `DeleteAsync` stubs and add `ApplyMutationResult`:

```csharp
    [RelayCommand]
    private async Task CreateAsync()
    {
        if (RequestBranchName is null) return;
        var name = await RequestBranchName(null);
        if (string.IsNullOrWhiteSpace(name)) return;
        ApplyMutationResult(_service.Create(_projectFilePath, name), "Branches_StatusCreated", name);
    }

    [RelayCommand(CanExecute = nameof(CanActOnSelection))]
    private async Task RenameAsync()
    {
        if (RequestBranchName is null) return;
        var from = Selected!.Name;
        var name = await RequestBranchName(from);
        if (string.IsNullOrWhiteSpace(name) || name == from) return;
        ApplyMutationResult(_service.Rename(_projectFilePath, from, name), "Branches_StatusRenamed", name);
    }

    [RelayCommand(CanExecute = nameof(CanDelete))]
    private async Task DeleteAsync()
    {
        var name   = Selected!.Name;
        var result = _service.Delete(_projectFilePath, name, force: false);

        if (result.Status == BranchOpStatus.NotMerged)
        {
            var ok = ConfirmForceDelete is not null && await ConfirmForceDelete(name);
            if (!ok) return;
            result = _service.Delete(_projectFilePath, name, force: true);
        }
        ApplyMutationResult(result, "Branches_StatusDeleted", name);
    }

    private void ApplyMutationResult(BranchOpResult result, string successKey, string name)
    {
        if (result.Status == BranchOpStatus.Ok)
        {
            LoadBranches();
            StatusText = Loc.Format(successKey, name);
            return;
        }
        AppLog.Warn($"BranchesViewModel: operation failed: {result.Status} {result.Detail}");
        StatusText = result.Status switch
        {
            BranchOpStatus.NameInvalid => Loc.Get("Branches_StatusNameInvalid"),
            BranchOpStatus.NameExists  => Loc.Get("Branches_StatusNameExists"),
            BranchOpStatus.GitMissing  => Loc.Get("Branches_StatusGitMissing"),
            BranchOpStatus.NotARepo    => Loc.Get("Branches_StatusNotARepo"),
            _                          => Loc.Get("Branches_StatusError"),
        };
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesViewModelTests`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/BranchesViewModel.cs DialogEditor.Tests/ViewModels/BranchesViewModelTests.cs
git commit -m "feat: BranchesViewModel Create/Rename/Delete commands"
```

---

## Task 12: `MainWindowViewModel.EnsureNoUnsavedEditsAsync`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (near `GuardDirtyThen` / `CancelPendingNavigation`, ~line 138–163)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write the failing tests** (append; reuse the file's existing `MakeVm` + reflection helpers)

```csharp
    private static void SetModified(MainWindowViewModel vm, bool value)
    {
        vm.IsModified = value;
        // CurrentConversationName must be non-null for the dirty guard to engage.
        typeof(MainWindowViewModel)
            .GetProperty("CurrentConversationName")!
            .SetValue(vm, value ? "conv" : null);
    }

    [Fact]
    public async Task EnsureNoUnsavedEdits_Clean_ReturnsTrueImmediately()
    {
        var vm = MakeVm();
        Assert.True(await vm.EnsureNoUnsavedEditsAsync());
    }

    [Fact]
    public async Task EnsureNoUnsavedEdits_Dirty_Discard_ReturnsTrue()
    {
        var vm = MakeVm();
        SetModified(vm, true);

        var task = vm.EnsureNoUnsavedEditsAsync();      // pends on the dialog decision
        vm.DiscardAndProceed();                          // host's "Discard" path

        Assert.True(await task);
    }

    [Fact]
    public async Task EnsureNoUnsavedEdits_Dirty_Cancel_ReturnsFalse()
    {
        var vm = MakeVm();
        SetModified(vm, true);

        var task = vm.EnsureNoUnsavedEditsAsync();
        vm.CancelPendingNavigation();                    // host's "Cancel" path

        Assert.False(await task);
    }
```

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter EnsureNoUnsavedEdits`
Expected: FAIL — method not defined.

- [ ] **Step 3: Add the awaitable guard + complete the TCS on cancel**

Add the field and method beside `GuardDirtyThen`:

```csharp
    private TaskCompletionSource<bool>? _unsavedDecision;

    /// Awaitable form of the unsaved-edits guard, for flows that must continue on the
    /// same call stack (branch switching). Returns true to proceed (clean, or after
    /// Save/Discard), false if the user Cancelled. Reuses the existing
    /// UnsavedChangesRequested dialog plumbing.
    public Task<bool> EnsureNoUnsavedEditsAsync()
    {
        if (!(IsModified && CurrentConversationName is not null))
            return Task.FromResult(true);

        _unsavedDecision = new TaskCompletionSource<bool>();
        _pendingAction = () => { _unsavedDecision?.TrySetResult(true); _unsavedDecision = null; };
        UnsavedChangesRequested?.Invoke();
        return _unsavedDecision.Task;
    }
```

Then update `CancelPendingNavigation` to complete a pending decision as `false`:

```csharp
    public void CancelPendingNavigation()
    {
        _pendingFile   = null;
        _pendingAction = null;
        _unsavedDecision?.TrySetResult(false);
        _unsavedDecision = null;
    }
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter EnsureNoUnsavedEdits`
Expected: PASS. (`SaveAndProceed`/`DiscardAndProceed` already call `Proceed()`, which invokes `_pendingAction` → completes the task `true`.)

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat: awaitable EnsureNoUnsavedEditsAsync over the unsaved-edits guard"
```

---

## Task 13: `MainWindowViewModel.ReloadCurrentProjectFromDisk`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (near `LoadProjectAsync` / attribution fields)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
    private static void SetProjectPath(MainWindowViewModel vm, string? path) =>
        typeof(MainWindowViewModel)
            .GetField("_projectPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, path);

    [Fact]
    public void Reload_FileExists_ReloadsProject()
    {
        var vm   = MakeVm();
        var path = TempProjectPath();
        File.WriteAllText(path, DialogProjectSerializer.Serialize(DialogProject.Empty("Reloaded")));
        SetProjectPath(vm, path);

        vm.ReloadCurrentProjectFromDisk();

        Assert.True(vm.IsProjectOpen);
        Assert.Equal("Reloaded", vm.CurrentProjectName);
    }

    [Fact]
    public void Reload_FileMissingOnBranch_ClosesProject()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("Open"));
        SetProjectPath(vm, Path.Combine(Path.GetTempPath(), $"gone_{Guid.NewGuid():N}.dialogproject"));

        vm.ReloadCurrentProjectFromDisk();

        Assert.False(vm.IsProjectOpen);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }
```

> Note: confirm the serializer entry points used here — `DialogProjectSerializer.Serialize(project)` and `DialogProject.Empty(name)` — match the calls already used in `MainWindowViewModelTests` (`MakeProjectWithTranslations` uses `DialogProject.Empty`). If `Serialize` has a different name in this codebase, use `DialogProjectSerializer.SaveToFile(path, project)` instead (it is already used in `SaveProject`).

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter Reload_`
Expected: FAIL — method not defined.

- [ ] **Step 3: Implement `ReloadCurrentProjectFromDisk`**

Add near `LoadProjectAsync`:

```csharp
    /// Re-reads the open project after the working tree changed underneath it (branch
    /// switch). If the file no longer exists on the new branch, closes the project.
    /// Invalidates the HEAD-based attribution cache so "last edited" recomputes.
    public void ReloadCurrentProjectFromDisk()
    {
        var path = _projectPath;
        if (path is null) return;

        if (!File.Exists(path))
        {
            AppLog.Info($"Project file not present on current branch: {path}");
            SetProject(null);
            _projectPath = null;
            CurrentProjectName = null;
            _attributionPath = null;   // force attribution rebuild next time
            StatusText = Loc.Format("Status_ProjectNotOnBranch", path);
            SaveProjectCommand.NotifyCanExecuteChanged();
            return;
        }

        _attributionPath = null;       // HEAD moved → stale blame
        _ = LoadProjectAsync(path, offerDeferred: false);
    }
```

> Note: `_attributionPath` is the field guarding the lazy attribution cache (see `LookupAttribution`). Confirm its exact name in the file; if different, null the correct field. `CurrentProjectName` is the existing observable property set by `FinishLoad`.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter Reload_`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat: ReloadCurrentProjectFromDisk (reload or close if gone; invalidate blame)"
```

---

## Task 14: Read-only VMs — distinct `GitMissing` message

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs`, `BlameViewModel.cs`, `DiffViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/HistoryViewModelTests.cs`

- [ ] **Step 1: Write the failing test** (append to `HistoryViewModelTests`)

```csharp
    [Fact]
    public void GitMissing_SetsDistinctStatus()
    {
        // The runner throws GitMissing (executable absent) before any result is returned.
        var git = new FakeGit { Handler = _ => throw new DiffException("no git", DiffExceptionKind.GitMissing) };
        var vm  = new HistoryViewModel(git, ProjPath());

        Assert.False(vm.HasCommits);
        Assert.Equal("History_StatusGitMissing", vm.StatusText);   // StubStringProvider returns the key
    }
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter GitMissing_SetsDistinctStatus`
Expected: FAIL — currently maps to `History_StatusError`.

- [ ] **Step 3: Update the three VMs' catch-mapping**

In `HistoryViewModel` replace the `StatusText = …` assignment in the catch with:

```csharp
            StatusText = ex.Kind switch
            {
                DiffExceptionKind.GitMissing => Loc.Get("History_StatusGitMissing"),
                DiffExceptionKind.NotARepo   => Loc.Get("History_StatusNotARepo"),
                _                            => Loc.Get("History_StatusError"),
            };
```

In `BlameViewModel` apply the same shape with `Blame_StatusGitMissing` / `Blame_StatusNotARepo` / `Blame_StatusError`.

In `DiffViewModel`, the `MapDiffError(DiffException ex, string endpointLabel)` method (~line 317) is a `ex.Kind switch`. Add a `GitMissing` arm above the `_` default:

```csharp
            DiffExceptionKind.GitMissing   => Loc.Get("Status_DiffGitMissing"),
```

(Keep the existing `NotARepo`/`BadRef`/`FileNotFound`/`ReadFailed`/`ParseFailed` arms unchanged.)

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "HistoryViewModelTests|BlameViewModelTests|DiffViewModelTests"`
Expected: PASS — including the existing `NotARepo`/error tests (unchanged behaviour for those kinds).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/HistoryViewModel.cs DialogEditor.ViewModels/ViewModels/BlameViewModel.cs DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Tests/ViewModels/HistoryViewModelTests.cs
git commit -m "feat: distinct git-not-installed message in History/Blame/Diff VMs"
```

---

## Task 15: Localized strings for branches + git-missing

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

No test (resource-only). The string *keys* are exercised by VM tests via `StubStringProvider`; this task supplies the real copy the app shows.

- [ ] **Step 1: Add the new keys**

Add a new section near the other window sections in `Strings.axaml`:

```xml
    <!-- ─── Branches window ────────────────────────────────────────────── -->
    <sys:String x:Key="Branches_WindowTitle">Branches</sys:String>
    <sys:String x:Key="Branches_CurrentTag">(current)</sys:String>
    <sys:String x:Key="Branches_Switch">Switch</sys:String>
    <sys:String x:Key="Branches_SwitchTip">Check out the selected branch. Unsaved edits are guarded first; if your saved work blocks the switch, you'll be offered to commit it.</sys:String>
    <sys:String x:Key="Branches_New">New…</sys:String>
    <sys:String x:Key="Branches_NewTip">Create a new branch from the current state and switch to it.</sys:String>
    <sys:String x:Key="Branches_Rename">Rename…</sys:String>
    <sys:String x:Key="Branches_RenameTip">Rename the selected branch.</sys:String>
    <sys:String x:Key="Branches_Delete">Delete</sys:String>
    <sys:String x:Key="Branches_DeleteTip">Delete the selected branch. Refuses an unmerged branch unless you confirm a force-delete. The current branch cannot be deleted.</sys:String>
    <sys:String x:Key="Branches_Close">Close</sys:String>

    <!-- {0} = branch name -->
    <sys:String x:Key="Branches_StatusSwitched">Switched to {0}.</sys:String>
    <sys:String x:Key="Branches_StatusCreated">Created and switched to {0}.</sys:String>
    <sys:String x:Key="Branches_StatusRenamed">Renamed to {0}.</sys:String>
    <sys:String x:Key="Branches_StatusDeleted">Deleted {0}.</sys:String>
    <sys:String x:Key="Branches_StatusNone">No branches yet.</sys:String>
    <sys:String x:Key="Branches_StatusNotARepo">This project isn't inside a Git repository.</sys:String>
    <sys:String x:Key="Branches_StatusGitMissing">Git isn't installed. Install Git to manage branches.</sys:String>
    <sys:String x:Key="Branches_StatusError">Couldn't read the project's branches.</sys:String>
    <sys:String x:Key="Branches_StatusSwitchFailed">Couldn't switch branches.</sys:String>
    <sys:String x:Key="Branches_StatusCommitFailed">Couldn't commit your changes, so the switch was cancelled.</sys:String>
    <sys:String x:Key="Branches_StatusBlockedUntracked">Some new files in the project folder would be overwritten by switching to this branch. A developer may need to move or remove them first.</sys:String>
    <sys:String x:Key="Branches_StatusNameInvalid">That isn't a valid branch name.</sys:String>
    <sys:String x:Key="Branches_StatusNameExists">A branch with that name already exists.</sys:String>
    <sys:String x:Key="Branches_DefaultCommitMessage">Save dialog edits before switching branches</sys:String>

    <!-- Commit-consent dialog -->
    <sys:String x:Key="CommitConsent_Title">Commit changes before switching</sys:String>
    <sys:String x:Key="CommitConsent_Intro">These files will be committed before switching:</sys:String>
    <sys:String x:Key="CommitConsent_MessageLabel">Message</sys:String>
    <sys:String x:Key="CommitConsent_Commit">Commit &amp; Switch</sys:String>
    <sys:String x:Key="CommitConsent_Cancel">Cancel</sys:String>

    <!-- Branch-name dialog -->
    <sys:String x:Key="BranchName_NewTitle">New branch</sys:String>
    <sys:String x:Key="BranchName_RenameTitle">Rename branch</sys:String>
    <sys:String x:Key="BranchName_Label">Branch name</sys:String>
    <sys:String x:Key="BranchName_Ok">OK</sys:String>
    <sys:String x:Key="BranchName_Cancel">Cancel</sys:String>

    <!-- Force-delete dialog. {0} = branch name -->
    <sys:String x:Key="ForceDelete_Title">Delete unmerged branch?</sys:String>
    <sys:String x:Key="ForceDelete_Message">Branch "{0}" has commits that aren't merged anywhere else. Deleting it will lose those commits permanently. Delete anyway?</sys:String>
    <sys:String x:Key="ForceDelete_Confirm">Delete permanently</sys:String>
    <sys:String x:Key="ForceDelete_Cancel">Cancel</sys:String>

    <!-- Branches menu entry -->
    <sys:String x:Key="Menu_Branches">Branches…</sys:String>
    <sys:String x:Key="Menu_BranchesTip">View and manage the local Git branches of the open project.</sys:String>

    <!-- Project no longer on the switched-to branch. {0} = path -->
    <sys:String x:Key="Status_ProjectNotOnBranch">The project file doesn't exist on this branch: {0}</sys:String>

    <!-- Distinct git-not-installed status for the read-only git tools -->
    <sys:String x:Key="History_StatusGitMissing">Git isn't installed. Install Git to browse history.</sys:String>
    <sys:String x:Key="Blame_StatusGitMissing">Git isn't installed. Install Git to see attribution.</sys:String>
    <sys:String x:Key="Status_DiffGitMissing">Git isn't installed. Install Git to compare versions.</sys:String>
```

> If `History_StatusNotARepo` / `Blame_StatusNotARepo` already exist in the file, do **not** duplicate them — only add keys that are missing. For `Diff_StatusGitMissing`, match the existing Diff status-key naming if it differs.

- [ ] **Step 2: Build to verify the XAML parses**

Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`
Expected: build succeeds (no duplicate-key or XAML errors).

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: localized strings for Branches window, dialogs, and git-missing"
```

---

## Task 16: `BranchesWindow` view + headless test

**Files:**
- Create: `DialogEditor.Avalonia/Views/BranchesWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/BranchesWindow.axaml.cs`
- Test: `DialogEditor.Tests/Views/BranchesWindowTests.cs` (create)

- [ ] **Step 1: Write the failing headless test**

Mirror `DialogEditor.Tests/Views/HistoryWindowTests.cs` (open it first for the exact headless-app fixture and assertion style). The test:

```csharp
using Avalonia.Controls;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class BranchesWindowTests
{
    // Use the same headless [Collection]/fixture HistoryWindowTests uses.
    private static readonly string Root = Path.GetTempPath().TrimEnd(Path.DirectorySeparatorChar);

    private sealed class FakeGit : IGitRunner
    {
        public required Func<string[], GitResult> Handler;
        public GitResult Run(string workingDirectory, params string[] args) => Handler(args);
    }

    private static BranchesViewModel TwoBranchesVm()
    {
        Loc.Configure(new StubStringProvider());
        var git = new FakeGit { Handler = a =>
            a is ["rev-parse", "--show-toplevel"]      ? new GitResult(0, Root + "\n", "") :
            a is ["rev-parse", "--abbrev-ref", "HEAD"] ? new GitResult(0, "main\n", "") :
            a is ["for-each-ref", ..]                  ? new GitResult(0, "main\nfeature/x\n", "") :
                                                          new GitResult(0, "", "") };
        return new BranchesViewModel(new GitBranchService(git),
            Path.Combine(Path.GetTempPath(), $"bw_{Guid.NewGuid():N}.dialogproject"));
    }

    [Fact]
    public void Populates_AndGatesButtons()
    {
        var win = new BranchesWindow(TwoBranchesVm());
        win.Show();

        var list = win.FindControl<ListBox>("BranchList")!;
        Assert.Equal(2, list.ItemCount);

        var switchBtn = win.FindControl<Button>("SwitchButton")!;
        var deleteBtn = win.FindControl<Button>("DeleteButton")!;
        Assert.False(switchBtn.IsEnabled);             // nothing selected

        var vm = (BranchesViewModel)win.DataContext!;
        vm.Selected = vm.Branches[0];                  // current branch
        Assert.True(switchBtn.IsEnabled);
        Assert.False(deleteBtn.IsEnabled);             // can't delete current
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesWindowTests`
Expected: FAIL — `BranchesWindow` not defined.

- [ ] **Step 3: Create the XAML**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.BranchesWindow"
        x:DataType="vm:BranchesViewModel"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Title="{DynamicResource Branches_WindowTitle}"
        Width="420" Height="380">
  <DockPanel Margin="12">
    <TextBlock DockPanel.Dock="Bottom" Text="{Binding StatusText}" TextWrapping="Wrap"
               IsVisible="{Binding StatusText, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"
               Margin="0,8,0,0"/>

    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Spacing="6" Margin="0,8,0,0">
      <Button x:Name="SwitchButton" Content="{DynamicResource Branches_Switch}"
              ToolTip.Tip="{DynamicResource Branches_SwitchTip}"
              Command="{Binding SwitchCommand}"/>
      <Button x:Name="NewButton" Content="{DynamicResource Branches_New}"
              ToolTip.Tip="{DynamicResource Branches_NewTip}"
              Command="{Binding CreateCommand}"/>
      <Button x:Name="RenameButton" Content="{DynamicResource Branches_Rename}"
              ToolTip.Tip="{DynamicResource Branches_RenameTip}"
              Command="{Binding RenameCommand}"/>
      <Button x:Name="DeleteButton" Content="{DynamicResource Branches_Delete}"
              ToolTip.Tip="{DynamicResource Branches_DeleteTip}"
              Command="{Binding DeleteCommand}"/>
      <Button x:Name="CloseButton" Content="{DynamicResource Branches_Close}"/>
    </StackPanel>

    <ListBox x:Name="BranchList" ItemsSource="{Binding Branches}"
             SelectedItem="{Binding Selected}">
      <ListBox.ItemTemplate>
        <DataTemplate x:DataType="vm:BranchRowViewModel">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="{Binding Name}"
                       FontWeight="{Binding IsCurrent, Converter={x:Static vm:BranchFontWeightConverter.Instance}}"/>
            <TextBlock Text="{DynamicResource Branches_CurrentTag}" Opacity="0.7"
                       IsVisible="{Binding IsCurrent}"/>
          </StackPanel>
        </DataTemplate>
      </ListBox.ItemTemplate>
    </ListBox>
  </DockPanel>
</Window>
```

> The `FontWeight` bold-for-current binding needs a converter. To avoid a new converter type, simpler alternative: drop the `FontWeight` binding and instead show the "(current)" tag only (already bound via `IsVisible`). Prefer that simpler markup unless a bold weight is wanted; if kept, add a tiny `IValueConverter` returning `FontWeight.Bold`/`Normal` and register it like the existing converters in `App.axaml`. **Choose the tag-only version to keep the task converter-free.**

Tag-only `DataTemplate` (use this):

```xml
        <DataTemplate x:DataType="vm:BranchRowViewModel">
          <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="{Binding Name}"/>
            <TextBlock Text="{DynamicResource Branches_CurrentTag}" Opacity="0.7"
                       IsVisible="{Binding IsCurrent}"/>
          </StackPanel>
        </DataTemplate>
```

- [ ] **Step 4: Create the code-behind** (mirrors `HistoryWindow.axaml.cs`)

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class BranchesWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000/AVLN3001).
    public BranchesWindow() => InitializeComponent();

    public BranchesWindow(BranchesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
```

> Host-callback wiring (`EnsureNoUnsavedEdits`, `ReloadProjectFromDisk`, `RequestCommitConfirmation`, `ConfirmForceDelete`, `RequestBranchName`) is done by the **opener** in `MainWindow` (Task 19), not here — mirroring how `HistoryWindow`'s `CompareWithCommit` is wired by its opener. The window itself only needs the Close handler.

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter BranchesWindowTests`
Expected: PASS.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/BranchesWindow.axaml DialogEditor.Avalonia/Views/BranchesWindow.axaml.cs DialogEditor.Tests/Views/BranchesWindowTests.cs
git commit -m "feat: BranchesWindow view with branch list and gated actions"
```

---

## Task 17: Commit-consent, branch-name, and force-delete dialogs

**Files:**
- Create: `DialogEditor.Avalonia/Views/CommitConsentDialog.axaml(.cs)`
- Create: `DialogEditor.Avalonia/Views/BranchNameDialog.axaml(.cs)`
- Create: `DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml(.cs)`
- Test: `DialogEditor.Tests/Views/CommitConsentDialogTests.cs` (create)

First open an existing simple dialog for the exact pattern: `DialogEditor.Avalonia/Views/ConversationNameDialog.axaml(.cs)` and its test `DialogEditor.Tests/Views/...`. Follow that pattern (a `Window` exposing a `Task<TResult?> ShowDialog(...)`-style helper or a result property + buttons).

- [ ] **Step 1: Write the failing test for the commit-consent dialog**

```csharp
using Avalonia.Controls;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class CommitConsentDialogTests
{
    [Fact]
    public void ShowsFiles_AndDefaultMessage()
    {
        Loc.Configure(new StubStringProvider());
        var dlg = new CommitConsentDialog(new PendingCommit(new[] { "a.dialogproject", "b.json" }, "default msg"));
        dlg.Show();

        var list = dlg.FindControl<ItemsControl>("FileList")!;
        Assert.Equal(2, list.ItemCount);

        var msg = dlg.FindControl<TextBox>("MessageBox")!;
        Assert.Equal("default msg", msg.Text);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter CommitConsentDialogTests`
Expected: FAIL — `CommitConsentDialog` not defined.

- [ ] **Step 3: Create `CommitConsentDialog.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.CommitConsentDialog"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Title="{DynamicResource CommitConsent_Title}"
        Width="460" SizeToContent="Height"
        WindowStartupLocation="CenterOwner">
  <StackPanel Margin="14" Spacing="8">
    <TextBlock Text="{DynamicResource CommitConsent_Intro}" TextWrapping="Wrap"/>
    <Border BorderBrush="#33000000" BorderThickness="1" CornerRadius="3" Padding="6" MaxHeight="160">
      <ScrollViewer>
        <ItemsControl x:Name="FileList">
          <ItemsControl.ItemTemplate>
            <DataTemplate>
              <TextBlock Text="{Binding}" FontFamily="Consolas,monospace"/>
            </DataTemplate>
          </ItemsControl.ItemTemplate>
        </ItemsControl>
      </ScrollViewer>
    </Border>
    <TextBlock Text="{DynamicResource CommitConsent_MessageLabel}"/>
    <TextBox x:Name="MessageBox"/>
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="6">
      <Button x:Name="CommitButton" Content="{DynamicResource CommitConsent_Commit}" IsDefault="True"/>
      <Button x:Name="CancelButton" Content="{DynamicResource CommitConsent_Cancel}" IsCancel="True"/>
    </StackPanel>
  </StackPanel>
</Window>
```

- [ ] **Step 4: Create `CommitConsentDialog.axaml.cs`** (result helper an opener can await)

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class CommitConsentDialog : Window
{
    public CommitConsentDialog() => InitializeComponent();

    public CommitConsentDialog(PendingCommit pending)
    {
        InitializeComponent();
        FileList.ItemsSource = pending.Files;
        MessageBox.Text = pending.DefaultMessage;
        CommitButton.Click += (_, _) => Close(MessageBox.Text);   // returns the message
        CancelButton.Click += (_, _) => Close(null);              // returns null = cancel
    }

    /// Shows modally over `owner`; resolves to the commit message, or null if cancelled.
    public Task<string?> ShowDialogAsync(Window owner) => ShowDialog<string?>(owner);
}
```

- [ ] **Step 5: Create `BranchNameDialog` and `ForceDeleteDialog`**

`BranchNameDialog.axaml` — a title (bound by the opener), a labelled `TextBox x:Name="NameBox"`, and OK/Cancel. Code-behind:

```csharp
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Views;

public partial class BranchNameDialog : Window
{
    public BranchNameDialog() => InitializeComponent();

    /// title from Strings (New vs Rename); prefill is the current name for rename, null for new.
    public BranchNameDialog(string title, string? prefill)
    {
        InitializeComponent();
        Title = title;
        NameBox.Text = prefill ?? "";
        OkButton.Click     += (_, _) => Close(string.IsNullOrWhiteSpace(NameBox.Text) ? null : NameBox.Text.Trim());
        CancelButton.Click += (_, _) => Close(null);
    }

    public Task<string?> ShowDialogAsync(Window owner) => ShowDialog<string?>(owner);
}
```

`ForceDeleteDialog.axaml` — the `ForceDelete_Message` (formatted with the branch name by the opener), Delete-permanently + Cancel. Code-behind:

```csharp
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Views;

public partial class ForceDeleteDialog : Window
{
    public ForceDeleteDialog() => InitializeComponent();

    public ForceDeleteDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        ConfirmButton.Click += (_, _) => Close(true);
        CancelButton.Click  += (_, _) => Close(false);
    }

    public Task<bool> ShowDialogAsync(Window owner) => ShowDialog<bool>(owner);
}
```

(Each needs a matching `.axaml` with `Icon`, the named controls, and `DynamicResource` text — follow `ConversationNameDialog.axaml`.)

- [ ] **Step 6: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter CommitConsentDialogTests`
Expected: PASS.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Views/CommitConsentDialog.axaml DialogEditor.Avalonia/Views/CommitConsentDialog.axaml.cs DialogEditor.Avalonia/Views/BranchNameDialog.axaml DialogEditor.Avalonia/Views/BranchNameDialog.axaml.cs DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml.cs DialogEditor.Tests/Views/CommitConsentDialogTests.cs
git commit -m "feat: commit-consent, branch-name, and force-delete dialogs"
```

---

## Task 18: MainWindow entry point + host-callback wiring

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (menu item)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (open + wire)

First open `MainWindow.axaml(.cs)` and find how **History** / **Attribution** are opened (the Versions menu and the `OpenHistory`/`OpenAttribution`-style handlers). Add Branches the same way.

- [ ] **Step 1: Add the menu item**

In the Versions menu in `MainWindow.axaml`, beside History/Attribution:

```xml
<MenuItem Header="{DynamicResource Menu_Branches}"
          ToolTip.Tip="{DynamicResource Menu_BranchesTip}"
          Click="OnOpenBranches"
          IsEnabled="{Binding IsProjectOpen}"/>
```

- [ ] **Step 2: Add the open handler with full host-callback wiring**

In `MainWindow.axaml.cs` (use the same field name the file uses for the VM — commonly `ViewModel` or `_vm`; match existing handlers):

```csharp
    private void OnOpenBranches(object? sender, global::Avalonia.Interactivity.RoutedEventArgs e)
    {
        var path = ViewModel.ProjectPath;
        if (path is null) return;

        var vm = new BranchesViewModel(new GitBranchService(new ProcessGitRunner()), path)
        {
            EnsureNoUnsavedEdits  = () => ViewModel.EnsureNoUnsavedEditsAsync(),
            ReloadProjectFromDisk = () => ViewModel.ReloadCurrentProjectFromDisk(),
        };

        var window = new BranchesWindow(vm);

        vm.RequestCommitConfirmation = pending => new CommitConsentDialog(pending).ShowDialogAsync(window);
        vm.RequestBranchName = prefill =>
        {
            var title = Loc.Get(prefill is null ? "BranchName_NewTitle" : "BranchName_RenameTitle");
            return new BranchNameDialog(title, prefill).ShowDialogAsync(window);
        };
        vm.ConfirmForceDelete = name =>
            new ForceDeleteDialog(Loc.Format("ForceDelete_Message", name)).ShowDialogAsync(window);

        window.Show(this);
    }
```

Add the needed `using`s: `DialogEditor.Patch.Diff;`, `DialogEditor.ViewModels;`, `DialogEditor.ViewModels.Resources;`, `DialogEditor.Avalonia.Views;`.

- [ ] **Step 3: Build and smoke-run**

Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`
Expected: builds. (No automated UI test here — the VM/dialog logic is covered by Tasks 8–11 and 16–17.)

- [ ] **Step 4: Run the full test suite**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: PASS (whole suite green).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat: Versions > Branches entry, opening the Branches window with host wiring"
```

---

## Task 19: Update Gaps.md and NEXT-STEPS.md

**Files:**
- Modify: `Gaps.md`
- Modify: `docs/superpowers/NEXT-STEPS.md`

- [ ] **Step 1: Mark branch switching shipped in `Gaps.md`**

Replace the "Remaining VCS gaps" sentence "**branch switching** (`git checkout` from the app) is not started" with a shipped description:

```markdown
**Branch management** (implemented): a Branches window (Versions ▸ Branches…) lists
local branches with the current one marked, and switches, creates (`checkout -b`),
renames, and deletes them. Switching guards unsaved editor edits, reloads the project
from disk afterwards, and — when saved-but-uncommitted changes block the checkout —
offers an informed-consent "commit changes, then switch" (tracked files only; the
file list is shown before you accept). An untracked-file block is surfaced (not
papered over); deleting an unmerged branch requires a force-delete confirmation; the
current branch can't be deleted. Git-not-installed now shows a distinct message
across Compare/History/Attribution/Branches.
```

- [ ] **Step 2: Move the entry to "Completed" in `NEXT-STEPS.md`**

Remove the "Branch switching" bullet from "Queued (not started)" and add under "Completed":

```markdown
- **Branch management** (2026-06-06) — switch/create/rename/delete local branches in a
  dedicated window; commit-then-switch with informed consent (tracked-only), case-A
  untracked block surfaced, force-delete behind a confirm. Distinct git-not-installed
  message added to History/Diff/Blame too. Spec/plan:
  `docs/superpowers/specs/2026-06-05-branch-management-design.md`,
  `docs/superpowers/plans/2026-06-06-branch-management.md`.
```

- [ ] **Step 3: Commit**

```bash
git add Gaps.md docs/superpowers/NEXT-STEPS.md
git commit -m "docs: mark branch management shipped in Gaps/NEXT-STEPS"
```

---

## Final verification

- [ ] Run the full suite once more: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj` — expect all green.
- [ ] Build the app: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj` — expect success.
- [ ] Manual smoke (optional, via `/run`): open a project in a git repo, open Versions ▸ Branches, create a branch, switch back, confirm the canvas reloads and the status line reports the switch.
