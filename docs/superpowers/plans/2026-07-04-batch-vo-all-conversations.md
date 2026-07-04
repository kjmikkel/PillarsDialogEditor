# Project-Wide Batch VO Import Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A Test-menu "Batch import VO (all conversations)…" command that scans every patched conversation in the open project and feeds the existing `BatchVoImportDialog` in its dormant multi-conversation mode, per `docs/superpowers/specs/2026-07-04-batch-vo-all-conversations-design.md`.

**Architecture:** New static `ProjectVoRowScanner` mirrors `VoOrphanScanner`'s conversation walk (`project.Patches`, live canvas snapshot for the open conversation, `PatchApplier.Apply(ignoreConflicts: true)` for the rest) but emits `BatchVoRowViewModel` rows. A new `BatchImportVoAllCommand` on `MainWindowViewModel` runs the scan on `Task.Run` and hands rows to a `ShowBatchVoImportAll` delegate wired in `MainWindow.axaml.cs`, which opens `BatchVoImportDialog` with `isSingleConversation: false`.

**Tech Stack:** xUnit, CommunityToolkit.Mvvm (`[RelayCommand]`/`[ObservableProperty]`), Avalonia resource dictionaries, `Loc` string provider.

## Global Constraints

- No user-visible text hard-coded in XAML or C# — everything via `Strings.axaml` keys (`Loc.Get`/`Loc.Format` in C#, `{DynamicResource}` in XAML).
- Strict red/green TDD; observe each failing test before implementing.
- Every caught exception logged via `AppLog.Error(...)`/`AppLog.Warn(...)`; `OperationCanceledException` swallowed silently; no bare `catch { }`.
- Every new interactive control carries `ToolTip.Tip` **and** `AutomationProperties.HelpText` (structural tests enforce the mirroring).
- `CHANGELOG.md` is frozen — do not touch it.
- `StubStringProvider` echoes keys (`Loc.Get("K")` → `"K"`; `Loc.Format("K", …)` → `"K"` since the echoed key has no `{0}`). Tests assert key echoes.
- Scope: "all conversations" = the project's patched conversations (`project.Patches`) only.

---

### Task 1: `ProjectVoRowScanner`

**Files:**
- Create: `DialogEditor.ViewModels/Services/ProjectVoRowScanner.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (one new string)
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs:704` (reuse the new string; fixes a latent hard-coded-text rule violation)
- Test: `DialogEditor.Tests/Services/ProjectVoRowScannerTests.cs` (**create**)

**Interfaces:**
- Consumes (all existing): `VoPathResolver.Check(string speakerGuid, bool hasVO, string externalVO, bool hasFemaleText, int nodeId, string conversationName, string gameRoot, string activeGameId)` → `VoCheckResult?` (`Status`, `PrimaryWemPath`); `VoPathResolver.VoicesRoot(string gameRoot)`; `ConversationSnapshotBuilder.Build(Conversation)`; `PatchApplier.Apply(snapshot, patch, ignoreConflicts: true)`; `BatchVoRowViewModel(string conversationName, int nodeId, string textPreview, VoPresence voStatus, string destPrimaryPath, string destFemPath, bool isAliased = false)`.
- Produces (Task 2 relies on this exact name):
  - `static IReadOnlyList<BatchVoRowViewModel> ProjectVoRowScanner.BuildRows(DialogProject project, IGameDataProvider provider, string projectPath, string gameRoot, string activeGameId, string? openConversationName = null, ConversationEditSnapshot? openSnapshot = null)`
  - String key `BatchVoImport_NodeFallback` = `Node {0}`

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Services/ProjectVoRowScannerTests.cs`. The fixture mirrors `VoOrphanScannerTests` (same file, same helpers) but adds a fake **game root** because `VoPathResolver.Check` probes `<gameRoot>/PillarsOfEternityII_Data/StreamingAssets/Audio/Windows/Voices/English(US)/`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class ProjectVoRowScannerTests : IDisposable
{
    private readonly string _projectDir;
    private readonly string _projectPath;
    private readonly string _gameRoot;
    private readonly string _voicesRoot;

    private const string SpeakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    public ProjectVoRowScannerTests()
    {
        Loc.Configure(new StubStringProvider());
        _projectDir  = Path.Combine(Path.GetTempPath(), $"BatchScanTest_{Guid.NewGuid():N}");
        _projectPath = Path.Combine(_projectDir, "test.dialogproject");
        _gameRoot    = Path.Combine(_projectDir, "game");
        _voicesRoot  = VoPathResolver.VoicesRoot(_gameRoot);
        Directory.CreateDirectory(_voicesRoot);

        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { SpeakerGuid, "eder" }
        });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        try { Directory.Delete(_projectDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    /// Plants a game-side .wem so VoPathResolver reports Found for that node.
    private void PlantGameWem(string relative)
    {
        var full = Path.Combine(_voicesRoot, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(full)!);
        File.WriteAllText(full, "");
    }

    private static ConversationNode MakeNode(
        int id, bool hasVO = true, string externalVO = "", string speaker = SpeakerGuid) =>
        new(id, false, SpeakerCategory.Npc, speaker, "", [],
            [], [], "Conversation", "None",
            ActorDirection: "", Comments: "", ExternalVO: externalVO,
            HasVO: hasVO, HideSpeaker: false);

    private static Conversation MakeConv(string name, params ConversationNode[] nodes) =>
        new(name, nodes,
            new StringTable(nodes.Select(n => new StringEntry(n.NodeId, $"line {n.NodeId}", "")).ToList()));

    private static ConversationPatch EmptyPatch(string convName) =>
        new(convName, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private IReadOnlyList<BatchVoRowViewModel> Scan(
        DialogProject project, IGameDataProvider provider,
        string? openName = null, ConversationEditSnapshot? openSnap = null) =>
        ProjectVoRowScanner.BuildRows(
            project, provider, _projectPath, _gameRoot, "poe2", openName, openSnap);

    [Fact]
    public void RowsSpanAllPatchedConversations_SortedByConversationThenNode()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("bravo", MakeNode(2), MakeNode(1)),
            MakeConv("alpha", MakeNode(7)));
        var project = DialogProject.Empty("P")
            .WithPatch(EmptyPatch("bravo"))
            .WithPatch(EmptyPatch("alpha"));

        var rows = Scan(project, provider);

        Assert.Equal(["alpha", "bravo", "bravo"], rows.Select(r => r.ConversationName));
        Assert.Equal([7, 1, 2], rows.Select(r => r.NodeId));
    }

    [Fact]
    public void NodeWithoutVo_ProducesNoRow()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("conv", MakeNode(1, hasVO: false), MakeNode(2)));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));

        var rows = Scan(project, provider);

        var row = Assert.Single(rows);
        Assert.Equal(2, row.NodeId);
    }

    [Fact]
    public void VoStatus_ReflectsGameDisk_AndDestsLandInProjectVoFolder()
    {
        PlantGameWem(Path.Combine("eder", "conv_0001.wem"));   // node 1 Found, node 2 Missing
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("conv", MakeNode(1), MakeNode(2)));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));

        var rows = Scan(project, provider);

        Assert.Equal(VoPresence.Found,   rows.Single(r => r.NodeId == 1).VoStatus);
        Assert.Equal(VoPresence.Missing, rows.Single(r => r.NodeId == 2).VoStatus);
        var expectedDest = Path.Combine(_projectDir, "_vo", "eder", "conv_0001.wem");
        Assert.Equal(expectedDest, rows.Single(r => r.NodeId == 1).DestPrimaryPath);
        Assert.Equal(expectedDest[..^4] + "_fem.wem", rows.Single(r => r.NodeId == 1).DestFemPath);
    }

    [Fact]
    public void AliasedNode_IsFlaggedAndStillListed()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("conv", MakeNode(1, hasVO: false, externalVO: "eder/custom_take")));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));

        var rows = Scan(project, provider);

        var row = Assert.Single(rows);
        Assert.True(row.IsAliased);
    }

    [Fact]
    public void UnreadableConversation_IsSkipped_OthersSurvive()
    {
        var provider = new ThrowingProvider(
            new FakeGameDataProvider("poe2", "en", MakeConv("good", MakeNode(1))),
            throwFor: "bad");
        var project = DialogProject.Empty("P")
            .WithPatch(EmptyPatch("bad"))
            .WithPatch(EmptyPatch("good"));

        var rows = Scan(project, provider);

        var row = Assert.Single(rows);
        Assert.Equal("good", row.ConversationName);
    }

    [Fact]
    public void OpenConversation_UsesLiveSnapshotOverSavedState()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            MakeConv("conv", MakeNode(1)));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));
        // Canvas has an extra node 5 that only exists live (unsaved edit).
        var live = ConversationSnapshotBuilder.Build(MakeConv("conv", MakeNode(1), MakeNode(5)));

        var rows = Scan(project, provider, openName: "conv", openSnap: live);

        Assert.Equal([1, 5], rows.Select(r => r.NodeId));
    }

    [Fact]
    public void AddedNode_PreviewFallsBackToPatchTranslationText()
    {
        // A brand-new conversation: no vanilla base, one added node whose text
        // lives only in the patch's translations ([JsonIgnore] on snapshot text).
        var added = new NodeEditSnapshot(
            9, false, SpeakerCategory.Npc, SpeakerGuid, "", "", "",
            "Conversation", "None", "", "", "", true, false, [], [], []);
        var patch = new ConversationPatch("newconv", ConversationPatch.CurrentSchemaVersion,
            [added], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(9, "Fresh new line", "")]
            }
        };
        var provider = new FakeGameDataProvider("poe2", "en");
        var project  = DialogProject.Empty("P").WithPatch(patch);

        var rows = Scan(project, provider);

        var row = Assert.Single(rows);
        Assert.Equal("Fresh new line", row.TextPreview);
    }

    [Fact]
    public void NonPoe2Game_ReturnsNoRows()
    {
        var provider = new FakeGameDataProvider("poe1", "en",
            MakeConv("conv", MakeNode(1)));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("conv"));

        var rows = ProjectVoRowScanner.BuildRows(
            project, provider, _projectPath, _gameRoot, "poe1");

        Assert.Empty(rows);
    }

    /// Delegates to an inner provider but throws on LoadConversation for one
    /// conversation name — simulates an unreadable game file.
    private sealed class ThrowingProvider(FakeGameDataProvider inner, string throwFor) : IGameDataProvider
    {
        public string GameName => inner.GameName;
        public string GameId   => inner.GameId;
        public IReadOnlyList<string> AvailableLanguages => inner.AvailableLanguages;
        public string Language { get => inner.Language; set => inner.Language = value; }

        public IReadOnlyList<ConversationFile> EnumerateConversations() =>
            inner.EnumerateConversations()
                 .Append(inner.BuildNewConversationFile(throwFor)).ToList();

        public Conversation LoadConversation(ConversationFile file) =>
            file.Name == throwFor
                ? throw new IOException($"unreadable: {file.Name}")
                : inner.LoadConversation(file);

        public ConversationFile BuildNewConversationFile(string name) => inner.BuildNewConversationFile(name);
        public IReadOnlyDictionary<string, string> LoadSpeakerNames() => inner.LoadSpeakerNames();
        public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot) => throw new NotSupportedException();
        public string GetStringTablePath(ConversationFile file) => throw new NotSupportedException();
        public string GetStringTablePath(ConversationFile file, string language) => throw new NotSupportedException();
        public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots() => throw new NotSupportedException();
        public void InitializeConversationFile(ConversationFile file) => throw new NotSupportedException();
    }
}
```

Notes for the implementer:
- `NodeEditSnapshot`'s positional parameters are `(NodeId, IsPlayerChoice, SpeakerCategory, SpeakerGuid, ListenerGuid, DefaultText, FemaleText, DisplayType, Persistence, ActorDirection, Comments, ExternalVO, HasVO, HideSpeaker, Links, Conditions, Scripts)` — see `DialogEditor.Core/Editing/ConversationEditSnapshot.cs:17`. Verify the argument order above compiles against it before running.
- If `DialogProject.WithPatch` chaining differs (see its definition in `DialogEditor.Patch`), match how `VoOrphanScannerTests.ProjectWithConvPatch` builds projects.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ProjectVoRowScannerTests"`
Expected: **build failure** — `'ProjectVoRowScanner' could not be found`.

- [ ] **Step 3: Implement**

Add to `DialogEditor.Avalonia/Resources/Strings.axaml`, next to the other `BatchVoImport_*` keys:

```xml
    <sys:String x:Key="BatchVoImport_NodeFallback">Node {0}</sys:String>
```

Create `DialogEditor.ViewModels/Services/ProjectVoRowScanner.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Builds batch-VO-import rows across every conversation the project patches
/// (design: docs/superpowers/specs/2026-07-04-batch-vo-all-conversations-design.md).
/// Mirrors VoOrphanScanner's walk: the conversation open on the canvas is
/// represented by its live snapshot so unsaved edits count; every other patched
/// conversation is loaded vanilla + patch (conflicts ignored — display
/// semantics); an unreadable conversation is skipped, never fatal.
/// </summary>
public static class ProjectVoRowScanner
{
    public static IReadOnlyList<BatchVoRowViewModel> BuildRows(
        DialogProject project,
        IGameDataProvider provider,
        string projectPath,
        string gameRoot,
        string activeGameId,
        string? openConversationName = null,
        ConversationEditSnapshot? openSnapshot = null)
    {
        var voRoot = VoPathResolver.VoicesRoot(gameRoot);
        var voDir  = Path.Combine(Path.GetDirectoryName(projectPath)!, "_vo");
        var rows   = new List<BatchVoRowViewModel>();

        foreach (var (convName, patch) in project.Patches)
        {
            ConversationEditSnapshot snap;
            if (convName == openConversationName && openSnapshot is not null)
            {
                snap = openSnapshot;
            }
            else
            {
                try
                {
                    var file     = provider.FindConversation(convName);
                    var baseSnap = file is not null
                        ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                        : new ConversationEditSnapshot([]);
                    snap = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                }
                catch (Exception ex)
                {
                    // Unreadable conversation: skip rather than fail the scan.
                    AppLog.Warn($"Batch VO scan: could not load '{convName}': {ex.Message}");
                    continue;
                }
            }

            // Added nodes carry no text in the snapshot ([JsonIgnore]) — their
            // text lives in the patch's translations (VoOrphanScanner precedent).
            var translations = (patch.Translations.GetValueOrDefault(provider.Language) ?? [])
                .ToDictionary(t => t.NodeId);

            foreach (var node in snap.Nodes)
            {
                var femaleText = node.FemaleText.Length > 0 ? node.FemaleText
                    : translations.TryGetValue(node.NodeId, out var ft) ? ft.FemaleText
                    : string.Empty;

                var check = VoPathResolver.Check(
                    node.SpeakerGuid, node.HasVO, node.ExternalVO, femaleText.Length > 0,
                    node.NodeId, convName, gameRoot, activeGameId);

                if (check is null || check.Status == VoPresence.NotApplicable) continue;
                if (check.PrimaryWemPath is null) continue;

                var rel         = Path.GetRelativePath(voRoot, check.PrimaryWemPath);
                var destPrimary = Path.Combine(voDir, rel);
                var destFem     = Path.Combine(voDir, rel[..^4] + "_fem.wem");

                var text = node.DefaultText.Trim();
                if (text.Length == 0 && translations.TryGetValue(node.NodeId, out var tr))
                    text = tr.DefaultText.Trim();
                var preview = text.Length == 0  ? Loc.Format("BatchVoImport_NodeFallback", node.NodeId)
                            : text.Length <= 60 ? text
                                                : text[..60] + "…";

                rows.Add(new BatchVoRowViewModel(
                    convName, node.NodeId, preview, check.Status, destPrimary, destFem,
                    isAliased: !string.IsNullOrEmpty(node.ExternalVO)));
            }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.OrdinalIgnoreCase)
            .ThenBy(r => r.NodeId)
            .ToList();
    }
}
```

In `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs:704`, replace the hard-coded fallback in `BuildBatchVoRows` (`$"Node {node.NodeId}"`) with the same resource:

```csharp
            var preview = raw.Length == 0  ? Loc.Format("BatchVoImport_NodeFallback", node.NodeId)
                        : raw.Length <= 60 ? raw
                                           : raw[..60] + "…";
```

(add `using DialogEditor.ViewModels.Resources;` if not already present — it almost certainly is, for the other `Loc` calls in the file).

- [ ] **Step 4: Run tests, then full suite**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ProjectVoRowScannerTests"` → PASS (8 tests).
Run: `dotnet test --nologo` → all pass. If a pre-existing test asserted the old literal `"Node {id}"` preview from `BuildBatchVoRows`, update that assertion to the stub echo `"BatchVoImport_NodeFallback"` (per the StubStringProvider constraint) — no other assertion changes.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/ProjectVoRowScanner.cs DialogEditor.Tests/Services/ProjectVoRowScannerTests.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs
git commit -m "feat(vo): project-wide batch VO row scanner

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `BatchImportVoAllCommand` on `MainWindowViewModel`

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (two new strings, one reworded value)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs` (append)

**Interfaces:**
- Consumes: Task 1 `ProjectVoRowScanner.BuildRows(project, provider, projectPath, gameRoot, activeGameId, openConversationName, openSnapshot)`.
- Produces (Task 3 relies on these exact names):
  - `MainWindowViewModel.BatchImportVoAllCommand` (generated by `[RelayCommand]` from `BatchImportVoAll`)
  - `Func<IReadOnlyList<BatchVoRowViewModel>, Task>? MainWindowViewModel.ShowBatchVoImportAll { get; set; }`
  - String keys `Status_BatchImportVoAllEmpty`, `Status_BatchImportVoAllFailed`

- [ ] **Step 1: Write the failing tests**

Append to `DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs` (the file already has `MakeVm`, `InjectProject`, `InjectProvider`; add the private-field helper if absent):

```csharp
    /// <summary>Sets a private string field (e.g. _projectPath, _activeGameId) via reflection.</summary>
    private static void SetPrivateField(MainWindowViewModel vm, string field, object? value)
    {
        var fi = typeof(MainWindowViewModel)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, value);
    }

    // ── BatchImportVoAllCommand ───────────────────────────────────────────

    private static MainWindowViewModel MakeVoAllReadyVm(FakeGameDataProvider provider)
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("P"));
        InjectProvider(vm, provider);
        SetPrivateField(vm, "_projectPath", Path.Combine(Path.GetTempPath(), "p.dialogproject"));
        SetPrivateField(vm, "_currentGameDirectory", Path.GetTempPath());
        SetPrivateField(vm, "_activeGameId", "poe2");
        return vm;
    }

    [Fact]
    public void BatchImportVoAll_DisabledWithoutProject()
    {
        var vm = MakeVm();
        Assert.False(vm.BatchImportVoAllCommand.CanExecute(null));
    }

    [Fact]
    public void BatchImportVoAll_DisabledWithoutPoe2GameFolder()
    {
        var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe1", "en"));
        SetPrivateField(vm, "_activeGameId", "poe1");
        Assert.False(vm.BatchImportVoAllCommand.CanExecute(null));
    }

    [Fact]
    public void BatchImportVoAll_DisabledWithoutSavedProjectPath()
    {
        var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe2", "en"));
        SetPrivateField(vm, "_projectPath", null);
        Assert.False(vm.BatchImportVoAllCommand.CanExecute(null));
    }

    [Fact]
    public void BatchImportVoAll_EnabledWithProjectAndPoe2Folder()
    {
        var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe2", "en"));
        Assert.True(vm.BatchImportVoAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task BatchImportVoAll_EmptyScan_ReportsViaStatusBar_AndSkipsDialog()
    {
        // Project has no patches at all → scanner returns zero rows.
        var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe2", "en"));
        var dialogShown = false;
        vm.ShowBatchVoImportAll = _ => { dialogShown = true; return Task.CompletedTask; };

        await vm.BatchImportVoAllCommand.ExecuteAsync(null);

        Assert.False(dialogShown);
        Assert.Equal("Status_BatchImportVoAllEmpty", vm.StatusText);
    }

    [Fact]
    public async Task BatchImportVoAll_WithVoicedNodes_HandsRowsToDialogDelegate()
    {
        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
        try
        {
            var node = new ConversationNode(
                1, false, SpeakerCategory.Npc, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "", [],
                [], [], "Conversation", "None",
                ActorDirection: "", Comments: "", ExternalVO: "",
                HasVO: true, HideSpeaker: false);
            var conv = new Conversation("conv", [node],
                new StringTable([new StringEntry(1, "line", "")]));
            var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe2", "en", conv));
            InjectProject(vm, DialogProject.Empty("P").WithPatch(
                new ConversationPatch("conv", ConversationPatch.CurrentSchemaVersion, [], [], [])));

            IReadOnlyList<BatchVoRowViewModel>? received = null;
            vm.ShowBatchVoImportAll = rows => { received = rows; return Task.CompletedTask; };

            await vm.BatchImportVoAllCommand.ExecuteAsync(null);

            Assert.NotNull(received);
            var row = Assert.Single(received!);
            Assert.Equal("conv", row.ConversationName);
            Assert.Equal(1, row.NodeId);
        }
        finally
        {
            ChatterPrefixService.Clear();
        }
    }
```

(If `MakeVoAllReadyVm`'s `Path.GetTempPath()` game directory triggers nothing — it doesn't need to exist for gating; `VoPathResolver.Check` only probes it with `File.Exists`, which is false → rows report `Missing`, which is fine for the delegate test.)

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MainWindowViewModelTests.BatchImportVoAll"`
Expected: **build failure** — `'MainWindowViewModel' does not contain a definition for 'BatchImportVoAllCommand'`.

- [ ] **Step 3: Implement**

Add to `DialogEditor.Avalonia/Resources/Strings.axaml`, next to the other `Status_*` keys:

```xml
    <sys:String x:Key="Status_BatchImportVoAllEmpty">No voiced nodes found in this project's conversations.</sys:String>
    <sys:String x:Key="Status_BatchImportVoAllFailed">Could not scan the project's conversations for voice-over — see the log.</sys:String>
```

Reword the existing `ToolTip_Menu_BatchImportVoAll` **value** (key unchanged, per spec — it must name the enablement conditions):

```xml
    <sys:String x:Key="ToolTip_Menu_BatchImportVoAll">Open the batch voice-over import dialog covering every conversation in the project. Requires an open, saved project and a Pillars of Eternity II game folder.</sys:String>
```

In `MainWindowViewModel.cs`, next to `CanValidateVO` (line ~214):

```csharp
    /// True when a saved project is open and a PoE2 game folder is loaded.
    /// Guards the project-wide "Batch import VO (all conversations)…" menu item.
    /// _projectPath is required because row destinations live in _vo/ next to
    /// the project file — an unsaved new project cannot batch-import.
    public bool CanBatchImportVoAll =>
        _project is not null
        && _provider is not null
        && _projectPath is not null
        && !string.IsNullOrEmpty(_currentGameDirectory)
        && string.Equals(_activeGameId, "poe2", StringComparison.OrdinalIgnoreCase);
```

Add the delegate property near the other view-wired delegates (search for `ShowBatchVoImport` on `ConversationViewModel` for the naming precedent; this one lives on `MainWindowViewModel`):

```csharp
    /// Opens the batch VO import dialog in multi-conversation mode.
    /// Wired by MainWindow.axaml.cs; null in unit tests that don't need the dialog.
    public Func<IReadOnlyList<BatchVoRowViewModel>, Task>? ShowBatchVoImportAll { get; set; }
```

Add the command (near `CreateVoValidationViewModel`, line ~410, keeping VO tooling together):

```csharp
    [RelayCommand(CanExecute = nameof(CanBatchImportVoAll))]
    private async Task BatchImportVoAll()
    {
        if (_project is null || _provider is null || _projectPath is null) return;
        // Capture locals: the scan runs on a worker thread and the fields are mutable.
        var project     = _project;
        var provider    = _provider;
        var projectPath = _projectPath;
        var gameRoot    = _currentGameDirectory;
        var gameId      = _activeGameId;
        var openName    = Canvas.Nodes.Count > 0 ? Canvas.ConversationName : null;
        var snapshot    = openName is not null ? Canvas.BuildSnapshot() : null;

        try
        {
            var rows = await Task.Run(() => ProjectVoRowScanner.BuildRows(
                project, provider, projectPath, gameRoot, gameId, openName, snapshot));

            if (rows.Count == 0)
            {
                StatusText = Loc.Get("Status_BatchImportVoAllEmpty");
                return;
            }

            if (ShowBatchVoImportAll is not null)
                await ShowBatchVoImportAll(rows);
            Detail.Refresh();   // the selected node's VO status row may have flipped to ✓
        }
        catch (OperationCanceledException) { /* deliberate cancellation — swallow silently */ }
        catch (Exception ex)
        {
            AppLog.Error("Project-wide batch VO scan failed", ex);
            StatusText = Loc.Get("Status_BatchImportVoAllFailed");
        }
    }
```

Raise `BatchImportVoAllCommand.NotifyCanExecuteChanged();` everywhere the gate's inputs change:

1. In `SetProject` (line ~232), with the other `NotifyCanExecuteChanged` calls.
2. After each `_projectPath = …` assignment (lines ~448, ~490, ~616 — `grep -n "_projectPath =" MainWindowViewModel.cs` to confirm).
3. At both `OnPropertyChanged(nameof(CanValidateVO))` sites (lines ~1133, ~1758 — game-folder load).

If `Canvas.ConversationName` or `Canvas.BuildSnapshot()` differ in name, mirror exactly what `CreateVoValidationViewModel` (line ~410) uses — same live-snapshot semantics.

- [ ] **Step 4: Run tests, then full suite**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MainWindowViewModelTests"` → PASS (all, including the 6 new).
Run: `dotnet test --nologo` → all pass.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/ViewModels/MainWindowViewModelTests.cs
git commit -m "feat(vo): project-wide batch VO import command with scan-and-report flow

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Menu item, dialog wiring, Gaps.md, verification

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (Test menu, after line ~168)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (delegate wiring, after the `vm.Canvas.ShowBatchVoImport` block at line ~140)
- Modify: `Gaps.md` (Voice-Over Integration section)

**Interfaces:**
- Consumes: Task 2 `BatchImportVoAllCommand`, `ShowBatchVoImportAll`; existing `BatchVoImportViewModel(rows, importer, isSingleConversation: false)` and `BatchVoImportDialog(batchVm, audioPlayer)`.
- Produces: nothing downstream — this is the final integration.

- [ ] **Step 1: Add the menu item**

In `MainWindow.axaml`, directly after the per-conversation `Menu_BatchImportVo` item (line ~165–168), add:

```xml
                        <!-- Project-wide variant (2026-07-04): scans every patched
                             conversation. Same visible-but-disabled discoverability
                             pattern as its per-conversation sibling above — the
                             MenuItem auto-disables from CanExecuteChanged. -->
                        <MenuItem Header="{DynamicResource Menu_BatchImportVoAll}"
                                  Command="{Binding BatchImportVoAllCommand}"
                                  ToolTip.Tip="{DynamicResource ToolTip_Menu_BatchImportVoAll}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_Menu_BatchImportVoAll}"/>
```

- [ ] **Step 2: Wire the delegate**

In `MainWindow.axaml.cs`, directly after the `vm.Canvas.ShowBatchVoImport = …` block (line ~140–148; the `voImporter` and `audioPlayer` locals are already in scope there):

```csharp
        vm.ShowBatchVoImportAll = async rows =>
        {
            var batchVm = new BatchVoImportViewModel(rows, voImporter, isSingleConversation: false);
            var dlg     = new BatchVoImportDialog(batchVm, audioPlayer);
            await dlg.ShowDialog(this);
        };
```

- [ ] **Step 3: Build and run the full suite**

Run: `dotnet build --nologo && dotnet test --nologo`
Expected: build clean; all tests pass (the structural suites — `AutomationHelpTextTests` etc. — validate the new menu item's Tip/HelpText mirroring automatically).

- [ ] **Step 4: Update Gaps.md**

In the Voice-Over Integration "Remaining gaps" list, replace the bullet
"**Project-wide batch VO import has no entry point** …" (keep the later
"Batch VO import entry point ✓ resolved (2026-07-04)" bullet as is, but delete its
final sentence `The separate "all conversations" gap above stays open (scope
decision 2026-07-04).`) with:

```markdown
- **Project-wide batch VO import ✓ implemented (2026-07-04):** "Batch import VO
  (all conversations)…" in the Test menu (the formerly orphaned
  `Menu_BatchImportVoAll` strings) scans every patched conversation via
  `ProjectVoRowScanner` (live canvas snapshot for the open conversation;
  unreadable conversations warned and skipped) and opens the existing
  `BatchVoImportDialog` in its multi-conversation mode
  (`isSingleConversation: false`, Conversation column visible). Scope is the
  project's patched conversations only — a VO-only change to an untouched
  vanilla conversation uses the per-conversation flow. Aliased rows stay
  listed-but-excluded. Empty scan reports via the status bar; gate requires an
  open, saved project and a PoE2 game folder.
  Spec: docs/superpowers/specs/2026-07-04-batch-vo-all-conversations-design.md.
```

- [ ] **Step 5: Manual verification** — `dotnet run --project DialogEditor.Avalonia`:

- [ ] Test menu shows "Batch import VO (all conversations)…" after the per-conversation item; disabled with no project open; tooltip names the conditions.
- [ ] With a PoE2 folder + project open: the item enables; invoking it opens the dialog with the Conversation column visible and rows from every patched conversation.
- [ ] A project whose patches contain no voiced nodes reports "No voiced nodes found in this project's conversations." in the status bar instead of opening the dialog.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs Gaps.md
git commit -m "feat(vo): wire project-wide batch VO import menu entry

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```
