# Sample Project & Beginner Tutorial Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an in-app **Help ▸ Create Sample Project…** command that generates an
install-matched sample `.dialogproject` from the loaded game (PoE1 or PoE2), seeds a small
git history for the version-control tools, and opens it — plus **Help ▸ Open Walkthrough…**
and a standalone beginner tutorial document.

**Architecture:** A pure, testable `SampleProjectService` (in `DialogEditor.Patch`) builds
the sample by loading the target conversation, applying four demo edits via `DiffEngine`,
and producing three project versions for a `main`/`experiment` git history seeded through
the existing `IGitRunner`. `MainWindowViewModel` orchestrates (folder pick → build → seed →
open) behind two `[RelayCommand]`s. A new **Help** menu (rendered last) hosts both commands.

**Tech Stack:** C# / .NET 8, CommunityToolkit.Mvvm, Avalonia 11.3, xUnit. Spec:
`docs/superpowers/specs/2026-06-07-sample-project-and-tutorial-design.md`.

---

## Conventions for every task

- **TDD:** write the failing test first, watch it fail, implement minimally, watch it pass, commit.
- **Test runner:** `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter <Name>` (the suite runs serially by design — see `project_flaky_test_appsettings`).
- **Localization:** no user-visible UI string is hard-coded; VMs call `Loc.Get(key)` / `Loc.Format(key, args)`. Tests configure `Loc.Configure(new StubStringProvider())` (returns the key verbatim). **Sample *content* strings** (dialogue text, git commit messages, the sample author identity) are *data*, not UI chrome — they live as constants in `SampleProjectService`, not in `Strings.axaml`.
- **Logging:** every caught exception is logged via `AppLog.Warn`/`AppLog.Error` (except `OperationCanceledException`). `DialogEditor.Patch` cannot reference `AppLog`; the service stays log-free and the VM logs.
- **Commits:** end every commit message with the trailer `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>`.

---

## ⚠ Two external values the project owner must confirm

These are genuine external inputs, not code placeholders. The unit tests use a **fake game
provider** and so do **not** depend on the real conversation names; the build is fully
testable before these are finalized. Confirm them during Task 1 / Task 7:

1. **The exact `ConversationFile.Name` for each Eder conversation**, read from the loaded
   game's conversation browser — *Eder, first meeting (Gilded Vale)* for PoE1 and *Eder
   reunion (Port Maje)* for PoE2. Set them in the `Poe1SampleConversation` /
   `Poe2SampleConversation` constants (Task 1, Step 3).
2. **The public docs URL** for the walkthrough fallback (`WalkthroughUrl`, Task 4, Step 3).

---

## File Structure

**Create:**
- `DialogEditor.Patch/SampleProjectService.cs` — the service + `SampleBuild`/`SampleCommit`/`SampleSeedResult`/`SampleConversationNotFoundException`.
- `DialogEditor.Tests/Helpers/FakeGameDataProvider.cs` — a reusable in-memory `IGameDataProvider` for tests.
- `DialogEditor.Tests/Patch/SampleProjectServiceTests.cs`.
- `docs/walkthrough.md` — the beginner tutorial.

**Modify:**
- `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` — `CreateSampleProjectCommand`, `OpenWalkthroughCommand`, the `WalkthroughOpener` seam, and a `NotifyCanExecuteChanged` where `_provider` is set.
- `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs` — command tests.
- `DialogEditor.Avalonia/Resources/Strings.axaml` — new copy.
- `DialogEditor.Avalonia/Views/MainWindow.axaml` — the Help menu (last).
- `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj` — ship `docs/walkthrough.md` to output.
- `Gaps.md`, `docs/superpowers/NEXT-STEPS.md` — mark shipped.

---

## Task 1: `SampleProjectService.BuildSample` — the four demo edits

**Files:**
- Create: `DialogEditor.Tests/Helpers/FakeGameDataProvider.cs`
- Create: `DialogEditor.Tests/Patch/SampleProjectServiceTests.cs`
- Create: `DialogEditor.Patch/SampleProjectService.cs`

- [ ] **Step 1: Create the reusable fake provider**

`DialogEditor.Tests/Helpers/FakeGameDataProvider.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Helpers;

/// In-memory IGameDataProvider for tests. Only the members SampleProjectService and the
/// sample command touch are implemented; the rest throw so accidental use is obvious.
public sealed class FakeGameDataProvider : IGameDataProvider
{
    private readonly Dictionary<string, Conversation> _conversations;

    public FakeGameDataProvider(string gameId, string language, params Conversation[] conversations)
    {
        GameId = gameId;
        Language = language;
        _conversations = conversations.ToDictionary(c => c.Name);
    }

    public string GameName => "Fake";
    public string GameId   { get; }
    public IReadOnlyList<string> AvailableLanguages => [Language];
    public string Language { get; set; }

    public IReadOnlyList<ConversationFile> EnumerateConversations()
        => _conversations.Keys.Select(BuildNewConversationFile).ToList();

    public Conversation LoadConversation(ConversationFile file) => _conversations[file.Name];

    public ConversationFile BuildNewConversationFile(string name)
        => new(name, $"conversations/{name}.conversation");

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() => new Dictionary<string, string>();
    public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot) => throw new NotSupportedException();
    public string GetStringTablePath(ConversationFile file) => throw new NotSupportedException();
    public string GetStringTablePath(ConversationFile file, string language) => throw new NotSupportedException();
    public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots() => throw new NotSupportedException();
    public void InitializeConversationFile(ConversationFile file) => throw new NotSupportedException();
}
```

> Confirm the `ConversationFile` constructor shape by opening `DialogEditor.Core/GameData/ConversationFile.cs`. It is a record whose first parameter is `Name` and second is the relative path; adjust the `BuildNewConversationFile` body if the positional order differs.

- [ ] **Step 2: Write the failing test**

`DialogEditor.Tests/Patch/SampleProjectServiceTests.cs`:

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using Xunit;

namespace DialogEditor.Tests.Patch;

public class SampleProjectServiceTests
{
    private sealed class OkGit : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => new(0, "", "");
    }

    // A 3-node line: 1 (root → 2), 2 (→ 3), 3 (leaf). Node 1 = anchor, node 3 = deletable leaf.
    private static Conversation ThreeNodeEder()
    {
        ConversationNode N(int id, int? linkTo) => new(
            NodeId: id, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: linkTo is int t ? [new NodeLink(id, t, [], 1f, "")] : [],
            Conditions: [], Scripts: [], DisplayType: "ConversationLine", Persistence: "None",
            ActorDirection: "", Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false);

        var nodes = new List<ConversationNode> { N(1, 2), N(2, 3), N(3, null) };
        var strings = new StringTable(new[]
        {
            new StringEntry(1, "Hello there.", ""),
            new StringEntry(2, "I am Eder.", ""),
            new StringEntry(3, "Farewell.", ""),
        });
        return new Conversation("Eder", nodes, strings);
    }

    [Fact]
    public void BuildSample_ProducesAllFourDemoEdits()
    {
        var provider = new FakeGameDataProvider("poe1", "en", ThreeNodeEder());
        var build = new SampleProjectService(new OkGit()).BuildSample(provider);

        Assert.Equal("sample-poe1.dialogproject", build.ProjectFileName);

        var patch = build.Final.Patches["Eder"];

        // Edit 2a — an added node.
        Assert.Contains(patch.AddedNodes, n => n.NodeId == 4);
        // Edit 2b — the leaf node removed.
        Assert.Contains(3, patch.DeletedNodeIds);
        // Edit 1 + added-node text — both land in Translations[en].
        Assert.True(patch.Translations["en"].Any(t => t.NodeId == 1));
        Assert.True(patch.Translations["en"].Any(t => t.NodeId == 4));
        // Edit 3 — translator note on the anchor.
        Assert.True(patch.NodeComments.ContainsKey(1));
        // The added link 1 → 4 and the removed link 2 → 3 are recorded.
        Assert.Contains(patch.ModifiedNodes, m => m.NodeId == 1 && m.AddedLinks.Any(l => l.ToNodeId == 4));
        Assert.Contains(patch.ModifiedNodes, m => m.NodeId == 2 && m.DeletedLinks.Any(l => l.ToNodeId == 3));
    }

    [Fact]
    public void BuildSample_ConversationMissing_Throws()
    {
        var provider = new FakeGameDataProvider("poe1", "en"); // no conversations
        Assert.Throws<SampleConversationNotFoundException>(
            () => new SampleProjectService(new OkGit()).BuildSample(provider));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter SampleProjectServiceTests`
Expected: FAIL — `SampleProjectService` / `SampleConversationNotFoundException` not defined.

- [ ] **Step 4: Create the service with types and `BuildSample`**

`DialogEditor.Patch/SampleProjectService.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch.Diff;

namespace DialogEditor.Patch;

public sealed class SampleConversationNotFoundException(string conversationName)
    : Exception($"Sample conversation '{conversationName}' was not found in the loaded game.");

public enum SampleSeedResult { Seeded, GitMissing, Partial }

/// One commit's worth of sample state. OnNewExperimentBranch marks where the history forks.
public record SampleCommit(string Message, DialogProject Project, bool OnNewExperimentBranch);

/// The full sample: the file name to write, the final (main-branch) project to open,
/// and the ordered commits used to seed history.
public record SampleBuild(string ProjectFileName, DialogProject Final, IReadOnlyList<SampleCommit> Commits);

/// Builds an install-matched sample .dialogproject from the loaded game and seeds a small
/// git history. Pure and testable (IGitRunner + IGameDataProvider injected); no UI, no logging.
public class SampleProjectService(IGitRunner git)
{
    // ⚠ Confirm these against the loaded game's conversation browser (see plan header).
    public const string Poe1SampleConversation = "companion_eder";   // Eder, Gilded Vale
    public const string Poe2SampleConversation = "companion_eder";   // Eder reunion, Port Maje

    // Sample *content* (data, not UI chrome) — intentionally literal, not localized.
    private const string SampleAuthorName  = "Dialog Editor Sample";
    private const string SampleAuthorEmail = "sample@dialogeditor.invalid";
    private const string EditedLineSuffix  = "  (try changing this line!)";
    private const string AltLineSuffix     = "  (an alternate greeting on the experiment branch)";
    private const string NewLineText        = "And this whole line was added as a sample.";
    private const string TranslatorNote     = "Sample translator note: keep Eder's tone warm and informal.";
    private const string Msg1 = "Initial sample — edit Eder's opening line";
    private const string Msg2 = "Reshape the scene — add a line, a note, and trim a dead end";
    private const string Msg3 = "experiment: try an alternate greeting";

    private static string ConversationFor(string gameId) =>
        gameId == "poe2" ? Poe2SampleConversation : Poe1SampleConversation;

    public SampleBuild BuildSample(IGameDataProvider provider)
    {
        var name = ConversationFor(provider.GameId);
        var file = provider.FindConversation(name)
                   ?? throw new SampleConversationNotFoundException(name);

        var conv = provider.LoadConversation(file);
        var lang = provider.Language;
        var baseSnap = ConversationSnapshotBuilder.Build(conv);

        var anchor = baseSnap.Nodes.OrderBy(n => n.NodeId).First();
        int? leafId = baseSnap.Nodes
            .Where(n => n.Links.Count == 0 && n.NodeId != anchor.NodeId)
            .OrderBy(n => n.NodeId)
            .LastOrDefault()?.NodeId;   // a deletable leaf, or null if the conversation has none
        var newId  = NodeIdAllocator.Next(baseSnap.Nodes.Select(n => n.NodeId));

        // ── Version 1 (C1): just the anchor's opening line changed.
        var v1 = WithAnchorText(baseSnap, anchor.NodeId, anchor.DefaultText + EditedLineSuffix);
        var p1 = ProjectFrom(name, baseSnap, v1, lang, withNote: false);

        // ── Version 2 (C2, = Final): add a node + link, remove the leaf, add a translator note.
        var newNode = new NodeEditSnapshot(
            newId, false, SpeakerCategory.Npc, "", "", NewLineText, "",
            anchor.DisplayType, anchor.Persistence, "", "", "", false, false, [], [], []);

        var v2Nodes = v1.Nodes
            .Select(n => n.NodeId == anchor.NodeId
                ? n with { Links = [.. n.Links, new LinkEditSnapshot(anchor.NodeId, newId, 1f, "", false)] }
                : n)
            .Where(n => leafId is null || n.NodeId != leafId)
            .Select(n => leafId is null ? n
                : n with { Links = n.Links.Where(l => l.ToNodeId != leafId).ToList() })
            .Append(newNode)
            .ToList();
        var v2 = new ConversationEditSnapshot(v2Nodes);
        var p2 = ProjectFrom(name, baseSnap, v2, lang, withNote: true, noteNodeId: anchor.NodeId);

        // ── Version 3 (C3, experiment): an alternate anchor greeting.
        var v3 = WithAnchorText(v2, anchor.NodeId, anchor.DefaultText + AltLineSuffix);
        var p3 = ProjectFrom(name, baseSnap, v3, lang, withNote: true, noteNodeId: anchor.NodeId);

        var fileName = provider.GameId == "poe2"
            ? "sample-poe2.dialogproject"
            : "sample-poe1.dialogproject";

        var commits = new List<SampleCommit>
        {
            new(Msg1, p1, OnNewExperimentBranch: false),
            new(Msg2, p2, OnNewExperimentBranch: false),
            new(Msg3, p3, OnNewExperimentBranch: true),
        };
        return new SampleBuild(fileName, p2, commits);
    }

    private static ConversationEditSnapshot WithAnchorText(
        ConversationEditSnapshot snap, int anchorId, string text) =>
        new(snap.Nodes.Select(n => n.NodeId == anchorId ? n with { DefaultText = text } : n).ToList());

    private static DialogProject ProjectFrom(
        string name, ConversationEditSnapshot baseSnap, ConversationEditSnapshot current,
        string lang, bool withNote, int noteNodeId = 0)
    {
        var patch = DiffEngine.Diff(name, baseSnap, current, lang);
        if (withNote)
            patch = patch with
            {
                NodeComments = new Dictionary<int, string> { [noteNodeId] = TranslatorNote }
            };
        return DialogProject.Empty("Sample").WithPatch(patch);
    }
}
```

> If the `ConversationNode` / `NodeLink` / `StringEntry` / `Conversation` constructor argument order in the test differs from your model, fix the test's `N(...)` builder — the model is the source of truth (`DialogEditor.Core/Models/`).

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter SampleProjectServiceTests`
Expected: PASS (both facts).

- [ ] **Step 6: Confirm the conversation constants**

Replace the `Poe1SampleConversation` / `Poe2SampleConversation` values with the real
`ConversationFile.Name`s from the loaded game (plan header item 1). Tests are unaffected.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Patch/SampleProjectService.cs DialogEditor.Tests/Helpers/FakeGameDataProvider.cs DialogEditor.Tests/Patch/SampleProjectServiceTests.cs
git commit -m "feat: SampleProjectService.BuildSample with the four demo edits"
```

---

## Task 2: `SampleProjectService.SeedHistory` — git command sequence

**Files:**
- Modify: `DialogEditor.Patch/SampleProjectService.cs`
- Modify: `DialogEditor.Tests/Patch/SampleProjectServiceTests.cs`

- [ ] **Step 1: Write the failing tests** (append to the test class)

```csharp
    private sealed class RecordingGit : IGitRunner
    {
        public List<string[]> Calls { get; } = [];
        public Func<string[], GitResult>? Handler { get; init; }
        public GitResult Run(string workingDirectory, params string[] args)
        {
            Calls.Add(args);
            return Handler?.Invoke(args) ?? new GitResult(0, "", "");
        }
    }

    private static SampleBuild TinyBuild() =>
        new("sample-poe1.dialogproject",
            DialogProject.Empty("Sample"),
            new List<SampleCommit>
            {
                new("c1", DialogProject.Empty("Sample"), false),
                new("c2", DialogProject.Empty("Sample"), false),
                new("c3", DialogProject.Empty("Sample"), true),
            });

    [Fact]
    public void SeedHistory_IssuesExpectedGitSequence()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var git = new RecordingGit();

        var result = new SampleProjectService(git).SeedHistory(dir, TinyBuild());

        Assert.Equal(SampleSeedResult.Seeded, result);
        Assert.Equal(new[]
        {
            new[] { "init", "-b", "main" },
            new[] { "config", "user.email", "sample@dialogeditor.invalid" },
            new[] { "config", "user.name", "Dialog Editor Sample" },
            new[] { "add", "-A" }, new[] { "commit", "-m", "c1" },
            new[] { "add", "-A" }, new[] { "commit", "-m", "c2" },
            new[] { "checkout", "-b", "experiment" },
            new[] { "add", "-A" }, new[] { "commit", "-m", "c3" },
            new[] { "checkout", "main" },
        }, git.Calls);
    }

    [Fact]
    public void SeedHistory_GitMissing_ReturnsGitMissing()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"sample_{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        var git = new RecordingGit
        {
            Handler = _ => throw new DiffException("no git", DiffExceptionKind.GitMissing)
        };

        var result = new SampleProjectService(git).SeedHistory(dir, TinyBuild());

        Assert.Equal(SampleSeedResult.GitMissing, result);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter SampleProjectServiceTests`
Expected: FAIL — `SeedHistory` not defined.

- [ ] **Step 3: Implement `SeedHistory`** (add to the service)

```csharp
    /// Best-effort: lays down main(C1,C2) + experiment(C3) and ends on main. Writes each
    /// version's JSON before staging it. Returns GitMissing if git isn't installed, Partial
    /// if any git step fails part-way (the caller then writes Final to guarantee an openable file).
    public SampleSeedResult SeedHistory(string repoDir, SampleBuild build)
    {
        var path = Path.Combine(repoDir, build.ProjectFileName);
        try
        {
            Run(repoDir, "init", "-b", "main");
            Run(repoDir, "config", "user.email", SampleAuthorEmail);
            Run(repoDir, "config", "user.name",  SampleAuthorName);

            var experimentCreated = false;
            foreach (var commit in build.Commits)
            {
                if (commit.OnNewExperimentBranch && !experimentCreated)
                {
                    Run(repoDir, "checkout", "-b", "experiment");
                    experimentCreated = true;
                }
                DialogProjectSerializer.SaveToFile(path, commit.Project);
                Run(repoDir, "add", "-A");
                Run(repoDir, "commit", "-m", commit.Message);
            }
            Run(repoDir, "checkout", "main");
            return SampleSeedResult.Seeded;
        }
        catch (DiffException ex) when (ex.Kind == DiffExceptionKind.GitMissing)
        {
            return SampleSeedResult.GitMissing;
        }
        catch (DiffException)
        {
            return SampleSeedResult.Partial;
        }
    }

    private void Run(string dir, params string[] args)
    {
        var r = git.Run(dir, args);
        if (!r.Ok)
            throw new DiffException($"git {string.Join(' ', args)} failed: {r.StdErr.Trim()}",
                                    DiffExceptionKind.Unknown);
    }
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter SampleProjectServiceTests`
Expected: PASS (all four facts).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/SampleProjectService.cs DialogEditor.Tests/Patch/SampleProjectServiceTests.cs
git commit -m "feat: SampleProjectService.SeedHistory (main/experiment, git-optional)"
```

---

## Task 3: `MainWindowViewModel.CreateSampleProjectCommand`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write the failing test** (append; reuse the file's `MakeVm` helper and `FakeGameDataProvider`)

```csharp
    [Fact]
    public void CreateSampleProject_DisabledUntilGameLoaded()
    {
        var vm = MakeVm();
        Assert.False(vm.CreateSampleProjectCommand.CanExecute(null));

        typeof(MainWindowViewModel)
            .GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, new DialogEditor.Tests.Helpers.FakeGameDataProvider("poe1", "en"));

        Assert.True(vm.CreateSampleProjectCommand.CanExecute(null));
    }
```

> Confirm the private field is named `_provider` (it is, per `MainWindowViewModel`). If `MakeVm` lives in a partial/helper, add the test beside the existing reflection-based tests (e.g. the `Reload_` tests already use this pattern).

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter CreateSampleProject_DisabledUntilGameLoaded`
Expected: FAIL — `CreateSampleProjectCommand` not defined.

- [ ] **Step 3: Add the command** (in `MainWindowViewModel`)

Add these usings if absent: `using DialogEditor.Patch;`, `using DialogEditor.Patch.Diff;`.

```csharp
    private bool CanCreateSample() => _provider is not null;

    [RelayCommand(CanExecute = nameof(CanCreateSample))]
    private async Task CreateSampleProjectAsync()
    {
        if (_provider is null) return;

        string? folder;
        try { folder = await _folderPicker.PickFolderAsync(Loc.Get("Sample_SelectFolder")); }
        catch (OperationCanceledException) { return; }
        if (folder is null) return;   // cancelled

        if (Directory.EnumerateFileSystemEntries(folder).Any())
        {
            StatusText = Loc.Get("Sample_FolderNotEmpty");
            return;
        }

        var service = new SampleProjectService(new ProcessGitRunner());
        SampleBuild build;
        try
        {
            build = service.BuildSample(_provider);
        }
        catch (SampleConversationNotFoundException ex)
        {
            AppLog.Warn($"Create sample: {ex.Message}");
            StatusText = Loc.Get("Sample_ConversationMissing");
            return;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Create sample: build failed: {ex}");
            StatusText = Loc.Get("Sample_BuildFailed");
            return;
        }

        var projectPath = Path.Combine(folder, build.ProjectFileName);
        var seed = service.SeedHistory(folder, build);
        if (seed != SampleSeedResult.Seeded)
            DialogProjectSerializer.SaveToFile(projectPath, build.Final);  // guarantee an openable file

        StatusText = seed switch
        {
            SampleSeedResult.Seeded     => Loc.Format("Sample_Created", build.ProjectFileName),
            SampleSeedResult.GitMissing => Loc.Get("Sample_CreatedNoGit"),
            _                           => Loc.Get("Sample_CreatedHistoryPartial"),
        };
        if (seed == SampleSeedResult.Partial)
            AppLog.Warn("Create sample: git history seeding failed part-way; wrote the final project.");

        await LoadProjectAsync(projectPath, offerDeferred: false);
    }
```

Then, **where `_provider` is assigned** after a successful game-folder open (search for
`_provider =` in the file), add immediately after the assignment:

```csharp
        CreateSampleProjectCommand.NotifyCanExecuteChanged();
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter CreateSampleProject_DisabledUntilGameLoaded`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat: CreateSampleProjectCommand (folder pick, build, seed, open)"
```

---

## Task 4: `MainWindowViewModel.OpenWalkthroughCommand`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs`

- [ ] **Step 1: Write the failing test** (append)

```csharp
    [Fact]
    public void OpenWalkthrough_TriesBundledFileThenUrl()
    {
        var vm = MakeVm();
        IReadOnlyList<string>? offered = null;
        vm.WalkthroughOpener = candidates => { offered = candidates; return true; };

        vm.OpenWalkthroughCommand.Execute(null);

        Assert.NotNull(offered);
        Assert.Contains(offered!, c => c.EndsWith("walkthrough.md"));   // bundled file first
        Assert.Contains(offered!, c => c.StartsWith("http"));          // URL fallback present
        Assert.True(offered!.Count >= 2 && offered![0].EndsWith("walkthrough.md"));
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter OpenWalkthrough_TriesBundledFileThenUrl`
Expected: FAIL — `WalkthroughOpener` / `OpenWalkthroughCommand` not defined.

- [ ] **Step 3: Add the seam + command** (in `MainWindowViewModel`)

```csharp
    private const string WalkthroughFileName = "walkthrough.md";
    // ⚠ Confirm the public docs URL (plan header item 2).
    private const string WalkthroughUrl = "https://github.com/OWNER/REPO/blob/main/docs/walkthrough.md";

    /// Test/extension seam: tries each candidate (bundled path, then URL) and returns true on
    /// the first that opens. Defaults to launching via the OS handler.
    public Func<IReadOnlyList<string>, bool>? WalkthroughOpener { get; set; }

    [RelayCommand]
    private void OpenWalkthrough()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "docs", WalkthroughFileName),
            WalkthroughUrl,
        };
        var opener = WalkthroughOpener ?? LaunchFirstAvailable;
        if (!opener(candidates))
        {
            AppLog.Warn("Open walkthrough: no candidate could be opened.");
            StatusText = Loc.Get("Walkthrough_OpenFailed");
        }
    }

    private static bool LaunchFirstAvailable(IReadOnlyList<string> candidates)
    {
        foreach (var c in candidates)
        {
            var isFile = !c.StartsWith("http", StringComparison.OrdinalIgnoreCase);
            if (isFile && !File.Exists(c)) continue;
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(c) { UseShellExecute = true });
                return true;
            }
            catch (Exception ex)
            {
                AppLog.Warn($"Open walkthrough: failed to launch '{c}': {ex.Message}");
            }
        }
        return false;
    }
```

- [ ] **Step 4: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter OpenWalkthrough_TriesBundledFileThenUrl`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat: OpenWalkthroughCommand with injectable document-opener seam"
```

---

## Task 5: Localized strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

No test (resource-only); the keys are exercised by the VM tests via `StubStringProvider`.

- [ ] **Step 1: Add the new keys**

Add near the other menu/window sections in `Strings.axaml`:

```xml
    <!-- ─── Help menu + Sample project ──────────────────────────────────── -->
    <sys:String x:Key="Menu_Help">Help</sys:String>
    <sys:String x:Key="Menu_CreateSample">Create Sample Project…</sys:String>
    <sys:String x:Key="Menu_CreateSampleTip">Generate a small, safe practice project from your installed game — with a ready-made history so you can try the version-control tools. Opens it for you. Requires a game folder to be open.</sys:String>
    <sys:String x:Key="Menu_OpenWalkthrough">Open Walkthrough…</sys:String>
    <sys:String x:Key="Menu_OpenWalkthroughTip">Open the step-by-step beginner guide that walks you through editing, testing, translating, and version control using the sample project.</sys:String>

    <sys:String x:Key="Sample_SelectFolder">Choose an empty folder for the sample project</sys:String>
    <sys:String x:Key="Sample_FolderNotEmpty">Please choose an empty folder — the sample needs its own folder so it doesn't disturb existing files.</sys:String>
    <sys:String x:Key="Sample_ConversationMissing">Couldn't find the conversation the sample uses in this game installation.</sys:String>
    <sys:String x:Key="Sample_BuildFailed">Couldn't build the sample project.</sys:String>
    <!-- {0} = file name -->
    <sys:String x:Key="Sample_Created">Sample project {0} created and opened. Try Versions ▸ Branches and the other tools.</sys:String>
    <sys:String x:Key="Sample_CreatedNoGit">Sample project created and opened. Install Git to try the version-control tools.</sys:String>
    <sys:String x:Key="Sample_CreatedHistoryPartial">Sample project created and opened, but its practice history couldn't be completed.</sys:String>
    <sys:String x:Key="Walkthrough_OpenFailed">Couldn't open the walkthrough. You can find it in the app's docs folder.</sys:String>
```

> If the file uses `<x:String>` rather than `<sys:String>`, or a different `x:Key` quote style, match the surrounding entries exactly.

- [ ] **Step 2: Build to verify the XAML parses**

Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`
Expected: build succeeds (no duplicate-key or XAML errors).

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: localized strings for Help menu, sample project, and walkthrough"
```

---

## Task 6: Help menu wiring

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml`

- [ ] **Step 1: Add the Help menu as the last top-level item**

In `MainWindow.axaml`, after the final existing top-level `<MenuItem Header="{StaticResource Menu_Test}">…</MenuItem>` block (and any others), still inside the `<Menu>`, add:

```xml
                    <MenuItem Header="{StaticResource Menu_Help}">
                        <MenuItem Header="{StaticResource Menu_CreateSample}"
                                  ToolTip.Tip="{StaticResource Menu_CreateSampleTip}"
                                  Command="{Binding CreateSampleProjectCommand}"/>
                        <MenuItem Header="{StaticResource Menu_OpenWalkthrough}"
                                  ToolTip.Tip="{StaticResource Menu_OpenWalkthroughTip}"
                                  Command="{Binding OpenWalkthroughCommand}"/>
                    </MenuItem>
```

> The Create-Sample item auto-disables when no game folder is open (the command's
> `CanExecute`); the tooltip still shows. No code-behind handler is needed — both items bind
> directly to VM commands.

- [ ] **Step 2: Build to verify**

Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`
Expected: build succeeds.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml
git commit -m "feat: Help menu (last) with Create Sample Project and Open Walkthrough"
```

---

## Task 7: Beginner walkthrough document + shipping

**Files:**
- Create: `docs/walkthrough.md`
- Modify: `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`

- [ ] **Step 1: Write `docs/walkthrough.md`**

Write the tutorial following the spec outline
(`docs/superpowers/specs/2026-06-07-sample-project-and-tutorial-design.md`, "Outline").
Hand-held, imperative voice; PoE1/PoE2 callouts only where they differ. Required sections,
each with concrete click-by-click steps on the sample:

1. **What this is / safety promise** — nothing here touches the real game or your real work; the sample lives in its own folder you can delete anytime.
2. **Open your game folder** — File ▸ Open Game Folder; mention the one-time backup prompt.
3. **Create the sample project** — Help ▸ Create Sample Project…; pick an empty folder; what just got made (the Eder conversation with a few practice edits, and a practice history).
4. **Browse & edit** — open the Eder conversation (PoE1: *first meeting, Gilded Vale*; PoE2: *reunion, Port Maje*); change a line; add a node; (PoE2 callout) the speaker name-picker.
5. **Save** — File ▸ Save Project (`Ctrl+S`).
6. **Test in-game (`F5`) and restore (`F6`)** — the backup safety net; nothing is permanent.
7. **Translate** — File ▸ Export for Translation…; point out the sample's translator note as the writer-comment column.
8. **Trying version control safely** — the dedicated VC section: Versions ▸ Branches (switch to `experiment` and back), History, Attribution, Compare (`main` ↔ `experiment`). State plainly that Git is optional and that nothing here can harm the game or real work.
9. **Where to go next** — link to `README.md` and the Patch Manager.

- [ ] **Step 2: Ship the doc to the app output**

In `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`, add an item group so the doc is
copied next to the executable (matching the `Path.Combine(AppContext.BaseDirectory, "docs", "walkthrough.md")` lookup in Task 4):

```xml
  <ItemGroup>
    <Content Include="..\docs\walkthrough.md" Link="docs\walkthrough.md">
      <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
    </Content>
  </ItemGroup>
```

- [ ] **Step 3: Build and confirm the doc lands in output**

Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`
Then confirm `DialogEditor.Avalonia/bin/Debug/net8.0/docs/walkthrough.md` exists.
Expected: build succeeds; the file is present.

- [ ] **Step 4: Set the real `WalkthroughUrl`**

Replace the `OWNER/REPO` placeholder in `MainWindowViewModel.WalkthroughUrl` (Task 4) with
the project's real public docs URL (plan header item 2).

- [ ] **Step 5: Commit**

```bash
git add docs/walkthrough.md DialogEditor.Avalonia/DialogEditor.Avalonia.csproj DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs
git commit -m "docs: beginner walkthrough tutorial, shipped with the app"
```

---

## Task 8: Mark shipped in Gaps.md / NEXT-STEPS.md

**Files:**
- Modify: `Gaps.md`
- Modify: `docs/superpowers/NEXT-STEPS.md`

- [ ] **Step 1: Record the feature in `docs/superpowers/NEXT-STEPS.md`**

Under the "Completed" section add:

```markdown
- **Sample project + beginner tutorial** (2026-06-07) — Help ▸ Create Sample Project…
  generates an install-matched sample `.dialogproject` from the loaded game (Eder: Gilded
  Vale in PoE1, Port Maje in PoE2) with four demo edits (text change, added node, removed
  leaf, translator note) and a seeded `main`/`experiment` git history, then opens it.
  Help ▸ Open Walkthrough… opens a shipped step-by-step beginner doc (`docs/walkthrough.md`).
  Spec/plan: `docs/superpowers/specs/2026-06-07-sample-project-and-tutorial-design.md`,
  `docs/superpowers/plans/2026-06-07-sample-project-and-tutorial.md`.
```

> If `NEXT-STEPS.md` has no "Completed" section, add the bullet under the nearest equivalent
> (e.g. a "Done"/"Shipped" heading); follow the file's existing structure.

- [ ] **Step 2: Note the onboarding aid in `Gaps.md`**

The **About / Version Info** gap is already recorded. Under the "Feature Gaps" area, append a
short note that first-run onboarding is now partially addressed:

```markdown
### Onboarding
A **Create Sample Project** command (Help menu) plus a shipped **beginner walkthrough**
(`docs/walkthrough.md`) now give newcomers a safe, install-matched sandbox for learning the
editor and the version-control tools. The remaining onboarding idea is an **in-app guided
tour** (highlighting controls step-by-step), deferred — see the sample/tutorial spec's
"Future enhancements".
```

- [ ] **Step 3: Run the full suite and build**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`
Expected: all green.
Run: `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`
Expected: success.

- [ ] **Step 4: Commit**

```bash
git add Gaps.md docs/superpowers/NEXT-STEPS.md
git commit -m "docs: mark sample project + tutorial shipped; note onboarding gap"
```

---

## Final verification

- [ ] `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj` — all green.
- [ ] `dotnet build DialogEditor.Avalonia/DialogEditor.Avalonia.csproj` — success.
- [ ] Conversation constants (Task 1) and `WalkthroughUrl` (Task 4/7) set to real values.
- [ ] Manual smoke (optional, via `/run`): open a game folder, Help ▸ Create Sample Project…, pick an empty folder, confirm the project opens; then Versions ▸ Branches shows `main` + `experiment`, History shows the commits. Help ▸ Open Walkthrough… opens the doc.
```
