# Test Coverage — Remaining Gaps Design Spec

**Date:** 2026-05-26
**Status:** Approved

---

## Overview

Fills the remaining unit-test gaps identified in `Gaps.md`:
- Four untested ViewModels (simple data holders + SettingsViewModel)
- `AutoLayoutService` graph layout algorithm
- `Poe1GameDataProvider` and `Poe2GameDataProvider` end-to-end (file enumeration, load, save, speaker names, initialisation)

The parsers and serializers they delegate to are already well-covered. These tests verify the integration layer and the parts that were genuinely absent.

---

## Architecture

Five new test files, one task each:

```
DialogEditor.Tests/
  ViewModels/
    SimpleViewModelTests.cs         — ConversationFolderViewModel, ConversationItemViewModel, PatchEntryViewModel
    SettingsViewModelTests.cs       — SettingsViewModel
  Services/
    AutoLayoutServiceTests.cs       — AutoLayoutService (pure algorithm, no I/O)
  GameData/
    Poe1GameDataProviderTests.cs    — Poe1GameDataProvider (filesystem, temp dirs)
    Poe2GameDataProviderTests.cs    — Poe2GameDataProvider (filesystem, temp dirs)
```

**Established patterns to follow:**
- `Loc.Configure(new StubStringProvider())` in every ViewModel test constructor
- `StubFolderPicker` / `StubFilePicker` for async picker dependencies
- `Path.Combine(Path.GetTempPath(), Path.GetRandomFileName())` for temp dirs, cleaned up in `IDisposable.Dispose()`
- Fixture data embedded as `private const string` constants (same as parser test files)
- `[Fact]` / xUnit, AAA pattern

---

## Task 1 — SimpleViewModelTests

File: `DialogEditor.Tests/ViewModels/SimpleViewModelTests.cs`

Three nested classes (using xUnit nested class pattern or separate `[Fact]` methods grouped by comments).

### ConversationFolderViewModel (4 tests)

```csharp
private static NodeLink Link(int from, int to) => new(from, to, []);
```

- `DisplayName_EmptyPath_ReturnsLocalizedRootFolder` — construct with empty `folderPath`; assert `DisplayName == Loc.Get("GameBrowser_RootFolder")` (or whatever key the VM uses; check the actual implementation)
- `DisplayName_NonEmptyPath_ReturnsFolderName` — construct with `"foo/bar"`; assert `DisplayName == "bar"` (just the folder name, not full path)
- `IsExpanded_DefaultsFalse`
- `IsExpanded_SetTrue_RaisesPropertyChanged`

### ConversationItemViewModel (2 tests)

- `DisplayName_IsNewFalse_ReturnsFileName` — `IsNew=false`; `DisplayName == file.Name`
- `DisplayName_IsNewTrue_AppendsSuffix` — `IsNew=true`; `DisplayName` contains `file.Name` and the localised "(New)" suffix

### PatchEntryViewModel (4 tests)

PatchEntryViewModel has two constructor overloads — success (project loaded) and error (failed to load).

- `SuccessPath_IsLoaded_True`
- `SuccessPath_ProjectName_FromProject`
- `ErrorPath_IsLoaded_False`
- `ErrorPath_ProjectName_IsDisplayPath` — verify `ProjectName == DisplayPath` when load failed

---

## Task 2 — SettingsViewModelTests

File: `DialogEditor.Tests/ViewModels/SettingsViewModelTests.cs`

`AppSettings` is a static utility that reads/writes a real config file. Tests that exercise `BrowseBackupDirectory` will write to `AppSettings` as an accepted side effect — the method under test calls `AppSettings.SetBackupPath(picked)`, which writes to a config file. This is acceptable for a settings class. Tests that only verify state (picker cancellation) have no side effect.

3 tests:

- `Constructor_BackupDirectory_InitialisedFromAppSettings` — construct VM; assert `BackupDirectory == AppSettings.GetBackupPath() ?? string.Empty`
- `BrowseBackupDirectory_PickerCancelled_DirectoryUnchanged` — `StubFolderPicker` returns `null`; assert `BackupDirectory` unchanged after awaiting command
- `BrowseBackupDirectory_PickerReturnsPick_DirectoryUpdated` — `StubFolderPicker` returns `"/some/path"`; assert `BackupDirectory == "/some/path"` after awaiting command

For `IAsyncRelayCommand`, use `await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null)` (same pattern as in `PatchManagerViewModelTests`).

---

## Task 3 — AutoLayoutServiceTests

File: `DialogEditor.Tests/Services/AutoLayoutServiceTests.cs`

`AutoLayoutService.Apply(IReadOnlyList<ConversationNode> nodes, Action<int, double, double> setLocation)` is a pure function — no I/O. Tests capture positions via a dictionary.

**Helper:**
```csharp
private static ConversationNode Node(int id, params int[] linkTargets)
    => new(id, false, SpeakerCategory.Npc, "", "",
           linkTargets.Select(t => new NodeLink(id, t, [])).ToList(),
           [], [], "Conversation", "None");

private static Dictionary<int, (double x, double y)> Capture(
    IReadOnlyList<ConversationNode> nodes)
{
    var positions = new Dictionary<int, (double, double)>();
    AutoLayoutService.Apply(nodes, (id, x, y) => positions[id] = (x, y));
    return positions;
}
```

**Constants:** `AutoLayoutService` uses `NodeWidth=220`, `HorizontalGap=200`, so layer-to-layer x-increment is `420`. Tests use relative assertions (layer 0 < layer 1 in x), not exact pixel values, except where the algorithm is fully deterministic.

7 tests:

- `Apply_EmptyList_NoCallbackInvoked` — pass `[]`; assert callback never called
- `Apply_SingleNode_PlacedAtLayer0` — one node with no links; `positions[0].x == 0`
- `Apply_LinearChain_SuccessiveLayers` — `Node(0, 1), Node(1, 2), Node(2)`; assert `pos[0].x < pos[1].x < pos[2].x`
- `Apply_BranchingTree_SiblingsSameLayer` — `Node(0, 1), Node(0, 2), Node(1), Node(2)`; assert `pos[1].x == pos[2].x`
- `Apply_MultipleRoots_BothAtLayer0` — `Node(0), Node(1)` (no links between them); assert `pos[0].x == pos[1].x == 0`
- `Apply_OrphanedNode_PlacedBeyondMainGraph` — `Node(0, 1), Node(1), Node(99)` (99 is orphaned, no incoming or outgoing links, but not root because it has no incoming links... wait: 99 has no incoming links so it IS a root). Correction: to get an orphan, create a cycle: `Node(0, 1), Node(1, 0), Node(99, [])` — 99 is not targeted by either 0 or 1. So 99 IS a root (not targeted). But with a cycle (0↔1), nodes 0 and 1 are both targeted, so neither is a root. `AssignLayers` handles the "no roots" case by using `nodes[0]` as the seed. Node 99 is not targeted so it becomes a root at layer 0 too. Actually, this is different from what I described as "orphaned" — the service assigns orphans after BFS. Let me redesign: **`Apply_CycleHandling_NoRootsDoesNotInfiniteLoop`** — `Node(0, 1), Node(1, 0)`; assert both nodes get positions and no exception.
- `Apply_CycleHandling_DoesNotInfiniteLoop` — `Node(0, 1), Node(1, 0)`; assert completes without exception and both nodes have positions

**Note:** The "orphaned node placed below" test should verify: `Node(0, 1), Node(1), Node(99)` where 99 has no incoming links (it IS a root), so it gets placed at layer 0. The true "orphaned" case in the algorithm is where a node has no incoming links AND is not reachable from any root — which happens only with cycles. Keep the cycle test and skip the "orphaned beyond" test, since the algorithm uses "any node without incoming edges" as a root. Final 7 tests:

1. `Apply_EmptyList_NoCallbackInvoked`
2. `Apply_SingleNode_PlacedAtLayer0`
3. `Apply_LinearChain_SuccessiveLayers`
4. `Apply_BranchingTree_SiblingsSameLayer`
5. `Apply_MultipleRoots_BothAtLayer0`
6. `Apply_Cycle_BothNodesGetPositions` — cycle A↔B; assert neither throws, both get positions
7. `Apply_TwoDisconnectedChains_BothRootsAtLayer0` — `Node(0, 1), Node(1), Node(2, 3), Node(3)`; pos[0].x == pos[2].x == 0

---

## Task 4 — Poe1GameDataProviderTests

File: `DialogEditor.Tests/GameData/Poe1GameDataProviderTests.cs`

```csharp
public class Poe1GameDataProviderTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
    private readonly Poe1GameDataProvider _provider;

    public Poe1GameDataProviderTests()
    {
        // Create the directory skeleton that Poe1GameDataProvider expects
        Directory.CreateDirectory(_root);
        _provider = new Poe1GameDataProvider(_root);
    }

    public void Dispose() => Directory.Delete(_root, recursive: true);
}
```

**Directory structure created per-test as needed:**
- Conversations: `{root}/PillarsOfEternity_Data/data/conversations/`
- Stringtables: `{root}/PillarsOfEternity_Data/data/localized/en/text/conversations/`
- Characters file: `{root}/PillarsOfEternity_Data/data/localized/en/text/game/characters.stringtable`

**Fixture constant:** reuse the minimal 2-node XML from `Poe1ConversationParserTests` (already in the codebase). For the characters stringtable fixture, create a minimal XML with one entry (look at `Poe1SpeakerNameParser` to determine the exact format — it's a `.stringtable` XML).

8 tests:

- `EnumerateConversations_ReturnsConversationFiles` — write 2 `.conversation` files; assert 2 results
- `EnumerateConversations_IgnoresNonConversationFiles` — write 1 `.conversation` + 1 `.txt`; assert 1 result
- `LoadConversation_ReturnsConversationWithNodes` — write minimal XML; load; assert `Nodes.Count > 0`
- `SaveConversation_WritesFileToExpectedPath` — create a snapshot with 1 node; save; assert file exists
- `SaveConversation_RoundTrip_PreservesNodeCount` — load fixture; create snapshot; save; reload; assert same node count
- `LoadSpeakerNames_WithCharactersFile_ReturnsMappings` — write a minimal `characters.stringtable`; assert at least one mapping returned
- `LoadSpeakerNames_WithoutCharactersFile_ReturnsEmpty` — no file created; assert returns empty dictionary (no exception)
- `InitializeConversationFile_CreatesFileOnDisk` — call with a path in the conversations dir; assert file exists with non-empty content

---

## Task 5 — Poe2GameDataProviderTests

File: `DialogEditor.Tests/GameData/Poe2GameDataProviderTests.cs`

Same structure as Task 4 but for PoE2. Directory skeleton:
- Conversations: `{root}/PillarsOfEternityII_Data/exported/design/conversations/`
- Stringtables: `{root}/PillarsOfEternityII_Data/exported/localized/en/text/conversations/`
- Speakers: `{root}/PillarsOfEternityII_Data/exported/design/gamedata/speakers.gamedatabundle`

**Fixture:** reuse minimal 2-node JSON from `Poe2ConversationParserTests`. For `speakers.gamedatabundle`, look at `Poe2SpeakerNameParser` to determine the format and write a minimal fixture with one entry.

Same 8 tests as Task 4, adapted for PoE2:

- `EnumerateConversations_ReturnsConversationBundleFiles` (`.conversationbundle` extension)
- `EnumerateConversations_IgnoresNonConversationBundleFiles`
- `LoadConversation_ReturnsConversationWithNodes`
- `SaveConversation_WritesFileToExpectedPath`
- `SaveConversation_RoundTrip_PreservesNodeCount`
- `LoadSpeakerNames_WithSpeakersFile_ReturnsMappings`
- `LoadSpeakerNames_WithoutSpeakersFile_ReturnsEmpty`
- `InitializeConversationFile_CreatesFileOnDisk`

---

## TDD Order

1. `SimpleViewModelTests` → run → fix missing implementations if any
2. `SettingsViewModelTests` → run → implement
3. `AutoLayoutServiceTests` → run (service already exists; tests should pass or reveal bugs)
4. `Poe1GameDataProviderTests` → run → fix
5. `Poe2GameDataProviderTests` → run → fix

---

## Verification

1. `dotnet test` — all tests pass, 0 failures
2. New test count: +10 (simple VMs) + 3 (settings) + 7 (layout) + 8 (poe1) + 8 (poe2) = ~36 new tests
3. Confirm `Gaps.md` "ViewModel Test Coverage" entry is removed or revised to reflect closed status
