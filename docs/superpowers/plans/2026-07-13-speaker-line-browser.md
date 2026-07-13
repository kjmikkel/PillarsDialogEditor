# Speaker Line Browser Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give writers a read-only window that lists every line a chosen character speaks across the whole game folder (vanilla) with the project's edits applied, for voice-consistency checking.

**Architecture:** A new pure `SpeakerLineScanner` walks *every* conversation (`provider.EnumerateConversations()` ∪ patched names), applies the project patch (live snapshot for the open conversation), and emits one `SpeakerLineRow` per non-empty line tagged with its origin (Vanilla/Edited/New). `SpeakerLineBrowserViewModel` runs the scan off-thread (cancellable), derives the speaker picker, and filters rows in memory. `SpeakerLineBrowserWindow` renders it. `MainWindowViewModel` gates + guards the open (reusing the `NavigateToFoundNode` jump and a browser-specific dirty-guard seam); a canvas context action pre-selects the selected node's speaker.

**Tech Stack:** C# / .NET, Avalonia, CommunityToolkit.Mvvm (`[ObservableProperty]`/`[RelayCommand]`), xUnit. Spec: `docs/superpowers/specs/2026-07-13-speaker-line-browser-design.md`.

## Global Constraints

- **TDD:** red → green → refactor. No implementation before a failing test (per `CLAUDE.md`).
- **Localisation:** no user-visible string hard-coded in C#/XAML; every label/tooltip/status/badge is a `{DynamicResource}`/`Loc.Get`/`Loc.FormatCount` key in `Strings.axaml`.
- **Tooltips + automation:** every interactive control carries a detailed `ToolTip.Tip` mirrored into `AutomationProperties.HelpText`; the picker/toggle/refresh/cancel carry `AutomationProperties.Name`. Enforced solution-wide by `AutomationNameTests`/`AutomationHelpTextTests`.
- **Window icon:** every `<Window>` sets `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **Error handling:** every caught exception logged via `AppLog.Warn/Error` except `OperationCanceledException`, which is swallowed silently. No bare `catch {}` in production.
- **No stray hex / named colours / static string or fontsize resources:** reuse existing `Brush.*` and `FontSize.*` tokens only (enforced by `NoStrayHexTests`, `NoNamedColourForegroundTests`, `NoStaticStringResourceTests`, `NoStaticFontSizeResourceTests`).
- **Tests run serially** (AppSettings/Loc global-state); do not add `[Collection]`/parallelism.
- **Menu placement:** `Edit ▸ Browse Speaker Lines…`, immediately after **Find in Project**.

---

## File Structure

- Create `DialogEditor.ViewModels/Services/SpeakerLineModels.cs` — enums + `SpeakerLineRow` record.
- Create `DialogEditor.ViewModels/Services/SpeakerLineScanner.cs` — the pure whole-game scan.
- Create `DialogEditor.ViewModels/ViewModels/SpeakerLineBrowserViewModel.cs` — scan orchestration, picker, filter, navigate event.
- Modify `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs` — context command + event.
- Modify `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` — gate, dirty-guard open, wiring.
- Create `DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml` + `.axaml.cs` — the window.
- Modify `DialogEditor.Avalonia/Views/MainWindow.axaml` (menu item) + `MainWindow.axaml.cs` (delegates/seam).
- Modify `DialogEditor.Avalonia/Views/ConversationView.axaml` — node context-menu item.
- Modify `DialogEditor.Avalonia/Views/SaveBeforeScanDialog.axaml(.cs)` — optional copy override.
- Modify `DialogEditor.Avalonia/Resources/Strings.axaml` — all new resource keys.
- Tests: `DialogEditor.Tests/Services/SpeakerLineScannerTests.cs`, `DialogEditor.Tests/ViewModels/SpeakerLineBrowserViewModelTests.cs`, `DialogEditor.Tests/ViewModels/MainWindowViewModelSpeakerLinesTests.cs`, `DialogEditor.Tests/Views/SpeakerLineBrowserWindowTests.cs`.

---

## Task 1: `SpeakerLineScanner` service + models

**Files:**
- Create: `DialogEditor.ViewModels/Services/SpeakerLineModels.cs`
- Create: `DialogEditor.ViewModels/Services/SpeakerLineScanner.cs`
- Test: `DialogEditor.Tests/Services/SpeakerLineScannerTests.cs`

**Interfaces:**
- Consumes: `DialogProject` (`.Patches : IReadOnlyDictionary<string, ConversationPatch>`), `IGameDataProvider` (`.EnumerateConversations()`, `.LoadConversation(file)`, `.FindConversation(name)`), `ConversationSnapshotBuilder.Build(Conversation)`, `PatchApplier.Apply(snapshot, patch, ignoreConflicts:true)`, `ConversationEditSnapshot`, `NodeEditSnapshot`, `ConversationPatch` (`.AddedNodes`, `.ModifiedNodes[].NodeId`, `.Translations`).
- Produces:
  - `enum LineVariant { Default, Female }`
  - `enum LineOrigin { Vanilla, Edited, New }`
  - `record SpeakerLineRow(string SpeakerGuid, string ConversationName, int NodeId, LineVariant Variant, string LineText, LineOrigin Origin)`
  - `static IReadOnlyList<SpeakerLineRow> SpeakerLineScanner.Scan(DialogProject project, IGameDataProvider provider, string primaryLanguage, string? openConversationName = null, ConversationEditSnapshot? openSnapshot = null, CancellationToken ct = default)`

- [ ] **Step 1: Write the models file**

Create `DialogEditor.ViewModels/Services/SpeakerLineModels.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

/// Which text of a node a row represents. A node with female text yields a
/// second, Female row in addition to its Default row.
public enum LineVariant { Default, Female }

/// A line's provenance relative to the project: unchanged game text, an edit
/// to an existing node, or a brand-new node the project adds.
public enum LineOrigin { Vanilla, Edited, New }

/// One spoken line by one speaker, located in a conversation, with provenance.
public record SpeakerLineRow(
    string      SpeakerGuid,
    string      ConversationName,
    int         NodeId,
    LineVariant Variant,
    string      LineText,
    LineOrigin  Origin);
```

- [ ] **Step 2: Write the failing tests**

Create `DialogEditor.Tests/Services/SpeakerLineScannerTests.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class SpeakerLineScannerTests
{
    private const string Bao = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    private static ConversationNode Node(int id, string speaker = Bao) =>
        new(id, false, SpeakerCategory.Npc, speaker, "", [], [], [], "Conversation", "None");

    private static Conversation Conv(string name, int id, string def, string fem = "", string speaker = Bao) =>
        new(name, [Node(id, speaker)], new StringTable([new StringEntry(id, def, fem)]));

    private static ConversationPatch EmptyPatch(string name) =>
        new(name, ConversationPatch.CurrentSchemaVersion, [], [], []);

    [Fact] // Both a patched and an unpatched conversation contribute rows in one scan
    public void Scan_CoversPatchedAndUnpatchedConversations()
    {
        var provider = new FakeGameDataProvider("poe2", "en",
            Conv("patched", 1, "line A"), Conv("vanilla", 2, "line B"));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("patched"));

        var rows = SpeakerLineScanner.Scan(project, provider, "en");

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.ConversationName == "patched" && r.NodeId == 1);
        Assert.Contains(rows, r => r.ConversationName == "vanilla" && r.NodeId == 2);
        // The conversation the project never patches is Vanilla.
        Assert.Equal(LineOrigin.Vanilla, rows.Single(r => r.ConversationName == "vanilla").Origin);
    }

    [Fact] // A node in ModifiedNodes is Edited
    public void Scan_ModifiedNode_TaggedEdited()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "edited line"));
        var mod = new NodeModification(1, new Dictionary<string, FieldChange>(), [], []);
        var patch = EmptyPatch("c") with { ModifiedNodes = null! };   // placeholder, replaced below
        patch = new ConversationPatch("c", ConversationPatch.CurrentSchemaVersion, [], [], [mod]);
        var project = DialogProject.Empty("P").WithPatch(patch);

        var rows = SpeakerLineScanner.Scan(project, provider, "en");

        Assert.Equal(LineOrigin.Edited, Assert.Single(rows).Origin);
    }

    [Fact] // A node in AddedNodes is New; its in-memory text is used
    public void Scan_AddedNode_TaggedNew()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "base"));
        var added = new NodeEditSnapshot(
            10, false, SpeakerCategory.Npc, Bao, "", "brand new line", "",
            "Conversation", "None", "", "", "", false, false, [], [], []);
        var patch = new ConversationPatch("c", ConversationPatch.CurrentSchemaVersion, [added], [], []);
        var project = DialogProject.Empty("P").WithPatch(patch);

        var rows = SpeakerLineScanner.Scan(project, provider, "en");

        var newRow = Assert.Single(rows, r => r.NodeId == 10);
        Assert.Equal(LineOrigin.New, newRow.Origin);
        Assert.Equal("brand new line", newRow.LineText);
    }

    [Fact] // Female text produces a second row; Default-only produces one
    public void Scan_FemaleText_EmitsExtraRow()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "he says", "she says"));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));

        var rows = SpeakerLineScanner.Scan(project, provider, "en");

        Assert.Equal(2, rows.Count);
        Assert.Contains(rows, r => r.Variant == LineVariant.Default && r.LineText == "he says");
        Assert.Contains(rows, r => r.Variant == LineVariant.Female  && r.LineText == "she says");
    }

    [Fact] // Empty Default text (Script/blank) yields no row
    public void Scan_EmptyText_Skipped()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, ""));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));

        Assert.Empty(SpeakerLineScanner.Scan(project, provider, "en"));
    }

    [Fact] // The open conversation uses the live snapshot, not the on-disk base
    public void Scan_OpenConversation_UsesLiveSnapshot()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "on disk"));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));
        var live = new ConversationEditSnapshot([new NodeEditSnapshot(
            1, false, SpeakerCategory.Npc, Bao, "", "unsaved edit", "",
            "Conversation", "None", "", "", "", false, false, [], [], [])]);

        var rows = SpeakerLineScanner.Scan(project, provider, "en",
            openConversationName: "c", openSnapshot: live);

        Assert.Equal("unsaved edit", Assert.Single(rows).LineText);
    }

    [Fact] // A blank speaker GUID is not attributable — skipped
    public void Scan_BlankSpeaker_Skipped()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "narrated", speaker: ""));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));

        Assert.Empty(SpeakerLineScanner.Scan(project, provider, "en"));
    }

    [Fact] // An already-cancelled token aborts the scan
    public void Scan_CancelledToken_Throws()
    {
        var provider = new FakeGameDataProvider("poe2", "en", Conv("c", 1, "x"));
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch("c"));

        Assert.Throws<OperationCanceledException>(() =>
            SpeakerLineScanner.Scan(project, provider, "en", ct: new CancellationToken(canceled: true)));
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~SpeakerLineScannerTests`
Expected: FAIL — `SpeakerLineScanner` does not exist (compile error).

- [ ] **Step 4: Implement the scanner**

Create `DialogEditor.ViewModels/Services/SpeakerLineScanner.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Whole-game read-only walk collecting every spoken line, per speaker, for the
/// Speaker Line Browser (voice-consistency checking).
/// Spec: docs/superpowers/specs/2026-07-13-speaker-line-browser-design.md
///
/// Unlike ProjectFindService / ProjectVoRowScanner (which walk only project.Patches),
/// this visits EVERY conversation the game exposes — the whole point is to compare the
/// writer's new lines against all the vanilla lines the character already speaks. The
/// open conversation is represented by its live snapshot so unsaved edits count; every
/// other conversation is loaded vanilla and (if the project patches it) has the patch
/// applied. New (not-on-disk) conversations the project adds are reached by unioning the
/// patch keys into the walk. An unreadable conversation is warned and skipped, never fatal.
/// The result is pure rows: name resolution / picker grouping is the ViewModel's job.
/// </summary>
public static class SpeakerLineScanner
{
    public static IReadOnlyList<SpeakerLineRow> Scan(
        DialogProject project,
        IGameDataProvider provider,
        string primaryLanguage,
        string? openConversationName = null,
        ConversationEditSnapshot? openSnapshot = null,
        CancellationToken ct = default)
    {
        var rows  = new List<SpeakerLineRow>();
        var files = provider.EnumerateConversations().ToDictionary(f => f.Name, StringComparer.Ordinal);

        // Union of on-disk conversations and patched names (the latter reaches new,
        // not-yet-on-disk conversations the project added).
        var names = files.Keys.Union(project.Patches.Keys, StringComparer.Ordinal);

        foreach (var name in names)
        {
            ct.ThrowIfCancellationRequested();

            var patch = project.Patches.GetValueOrDefault(name);

            ConversationEditSnapshot snap;
            if (name == openConversationName && openSnapshot is not null)
            {
                snap = openSnapshot;
            }
            else
            {
                try
                {
                    var file     = files.GetValueOrDefault(name) ?? provider.FindConversation(name);
                    var baseSnap = file is not null
                        ? ConversationSnapshotBuilder.Build(provider.LoadConversation(file))
                        : new ConversationEditSnapshot([]);   // new conversation: patch supplies nodes
                    snap = patch is not null
                        ? PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true)
                        : baseSnap;
                }
                catch (Exception ex)
                {
                    AppLog.Warn($"Speaker line scan: could not load '{name}': {ex.Message}");
                    continue;
                }
            }

            // Origin membership sets (empty when the conversation is unpatched → all Vanilla).
            var addedIds    = patch is null ? [] : patch.AddedNodes.Select(n => n.NodeId).ToHashSet();
            var modifiedIds = patch is null ? [] : patch.ModifiedNodes.Select(m => m.NodeId).ToHashSet();

            // Added nodes deserialized from disk carry [JsonIgnore] null text; fall back to
            // the primary-language translation entry (ProjectVoRowScanner precedent).
            var translations = (patch?.Translations.GetValueOrDefault(primaryLanguage) ?? [])
                .ToDictionary(t => t.NodeId);

            foreach (var node in snap.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.SpeakerGuid)) continue;   // unattributable

                var def = (!string.IsNullOrEmpty(node.DefaultText) ? node.DefaultText
                          : translations.TryGetValue(node.NodeId, out var pt) ? pt.DefaultText ?? "" : "").Trim();
                if (def.Length == 0) continue;   // no spoken line (Script/blank)

                var fem = (!string.IsNullOrEmpty(node.FemaleText) ? node.FemaleText
                          : translations.TryGetValue(node.NodeId, out var pf) ? pf.FemaleText ?? "" : "").Trim();

                var origin = addedIds.Contains(node.NodeId)    ? LineOrigin.New
                           : modifiedIds.Contains(node.NodeId) ? LineOrigin.Edited
                           : LineOrigin.Vanilla;

                rows.Add(new SpeakerLineRow(node.SpeakerGuid, name, node.NodeId, LineVariant.Default, def, origin));
                if (fem.Length > 0)
                    rows.Add(new SpeakerLineRow(node.SpeakerGuid, name, node.NodeId, LineVariant.Female, fem, origin));
            }
        }

        return rows
            .OrderBy(r => r.ConversationName, StringComparer.Ordinal)
            .ThenBy(r => r.NodeId)
            .ThenBy(r => r.Variant)
            .ToList();
    }
}
```

- [ ] **Step 5: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~SpeakerLineScannerTests`
Expected: PASS (all 8).

- [ ] **Step 6: Clean up the placeholder in the Edited test**

The `Scan_ModifiedNode_TaggedEdited` test above has a redundant placeholder line
(`var patch = EmptyPatch("c") with { ModifiedNodes = null! };`) immediately overwritten.
Delete that line so the test reads cleanly:

```csharp
        var mod = new NodeModification(1, new Dictionary<string, FieldChange>(), [], []);
        var patch = new ConversationPatch("c", ConversationPatch.CurrentSchemaVersion, [], [], [mod]);
```

Re-run the filter to confirm still green.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/Services/SpeakerLineModels.cs \
        DialogEditor.ViewModels/Services/SpeakerLineScanner.cs \
        DialogEditor.Tests/Services/SpeakerLineScannerTests.cs
git commit -m "feat(speaker-lines): whole-game SpeakerLineScanner with origin tagging"
```

---

## Task 2: `SpeakerLineBrowserViewModel`

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/SpeakerLineBrowserViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/SpeakerLineBrowserViewModelTests.cs`

**Interfaces:**
- Consumes: `SpeakerLineScanner.Scan(...)` (Task 1) via an injectable scan delegate; `SpeakerNameService.Resolve(guid)`; `ConversationEditSnapshot`; `Loc.Get`/`Loc.FormatCount`.
- Produces:
  - `record SpeakerPickerItem(string Guid, string DisplayName, int Count)` with `ToString() => "{DisplayName} ({Count})"`.
  - `class SpeakerLineBrowserViewModel : ObservableObject` with ctor
    `(DialogProject project, IGameDataProvider provider, string primaryLanguage, Func<(string? Name, ConversationEditSnapshot? Snapshot)> openConversationAccessor, string? initialSpeakerGuid = null, Func<string?, ConversationEditSnapshot?, CancellationToken, IReadOnlyList<SpeakerLineRow>>? scanner = null)`.
  - Members later tasks rely on: `Task ScanAsync()`, `ObservableCollection<SpeakerPickerItem> Speakers`, `SpeakerPickerItem? SelectedSpeaker`, `bool OnlyMyLines`, `bool IsBusy`, `string StatusText`, `IReadOnlyList<SpeakerLineRow> Rows`, `IRelayCommand RefreshCommand`, `IRelayCommand CancelScanCommand`, `event Action<string,int>? RequestNavigate`, `void NavigateTo(SpeakerLineRow row)`.

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/SpeakerLineBrowserViewModelTests.cs`:

```csharp
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class SpeakerLineBrowserViewModelTests
{
    private const string Bao   = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";
    private const string Aloth = "11111111-2222-3333-4444-555555555555";

    public SpeakerLineBrowserViewModelTests() => Loc.Configure(new StubStringProvider());

    private static readonly (string?, ConversationEditSnapshot?) NoOpen = (null, null);

    // A VM whose scan returns a fixed row set (bypasses IO); provider is unused by the stub.
    private static SpeakerLineBrowserViewModel VmWithRows(
        IReadOnlyList<SpeakerLineRow> rows, string? initial = null)
    {
        var provider = new FakeGameDataProvider("poe2", "en");
        return new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), provider, "en", () => NoOpen, initial,
            scanner: (_, _, _) => rows);
    }

    private static SpeakerLineRow Row(string speaker, string conv, int id,
        LineOrigin origin = LineOrigin.Vanilla, LineVariant variant = LineVariant.Default) =>
        new(speaker, conv, id, variant, $"{conv}:{id}", origin);

    [Fact]
    public async Task Scan_PopulatesSpeakers_WithCounts_NameSorted()
    {
        var vm = VmWithRows([
            Row(Bao, "c1", 1), Row(Bao, "c1", 2), Row(Aloth, "c2", 3),
        ]);
        await vm.ScanAsync();

        Assert.Equal(2, vm.Speakers.Count);
        Assert.Contains(vm.Speakers, s => s.Guid == Bao   && s.Count == 2);
        Assert.Contains(vm.Speakers, s => s.Guid == Aloth && s.Count == 1);
    }

    [Fact]
    public async Task Scan_SelectsFirstSpeaker_AndFiltersRows()
    {
        var vm = VmWithRows([Row(Bao, "c1", 1), Row(Aloth, "c2", 3)]);
        await vm.ScanAsync();

        Assert.NotNull(vm.SelectedSpeaker);
        Assert.All(vm.Rows, r => Assert.Equal(vm.SelectedSpeaker!.Guid, r.SpeakerGuid));
    }

    [Fact]
    public async Task InitialSpeakerGuid_PreSelectsThatSpeaker()
    {
        var vm = VmWithRows([Row(Bao, "c1", 1), Row(Aloth, "c2", 3)], initial: Aloth);
        await vm.ScanAsync();

        Assert.Equal(Aloth, vm.SelectedSpeaker!.Guid);
    }

    [Fact]
    public async Task OnlyMyLines_HidesVanillaRows_WithoutRescanning()
    {
        var scans = 0;
        var provider = new FakeGameDataProvider("poe2", "en");
        var rows = new[] { Row(Bao, "c1", 1, LineOrigin.Vanilla), Row(Bao, "c1", 2, LineOrigin.New) };
        var vm = new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), provider, "en", () => NoOpen, null,
            scanner: (_, _, _) => { scans++; return rows; });

        await vm.ScanAsync();
        Assert.Equal(2, vm.Rows.Count);

        vm.OnlyMyLines = true;
        Assert.Single(vm.Rows);
        Assert.Equal(LineOrigin.New, vm.Rows[0].Origin);
        Assert.Equal(1, scans);   // filtering did not trigger a re-scan
    }

    [Fact]
    public async Task ChangingSpeaker_RefiltersWithoutRescanning()
    {
        var scans = 0;
        var provider = new FakeGameDataProvider("poe2", "en");
        var rows = new[] { Row(Bao, "c1", 1), Row(Aloth, "c2", 3) };
        var vm = new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), provider, "en", () => NoOpen, null,
            scanner: (_, _, _) => { scans++; return rows; });
        await vm.ScanAsync();

        vm.SelectedSpeaker = vm.Speakers.Single(s => s.Guid == Aloth);
        Assert.All(vm.Rows, r => Assert.Equal(Aloth, r.SpeakerGuid));
        Assert.Equal(1, scans);
    }

    [Fact]
    public async Task NavigateTo_RaisesRequestNavigate_WithTarget()
    {
        var vm = VmWithRows([Row(Bao, "conv7", 7)]);
        await vm.ScanAsync();
        (string, int)? got = null;
        vm.RequestNavigate += (c, n) => got = (c, n);

        vm.NavigateTo(vm.Rows[0]);

        Assert.Equal(("conv7", 7), got);
    }

    [Fact]
    public void CancelScanCommand_DisabledWhenNotBusy()
    {
        var vm = VmWithRows([Row(Bao, "c1", 1)]);
        Assert.False(vm.CancelScanCommand.CanExecute(null));   // not scanning yet
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~SpeakerLineBrowserViewModelTests`
Expected: FAIL — `SpeakerLineBrowserViewModel` does not exist.

- [ ] **Step 3: Implement the ViewModel**

Create `DialogEditor.ViewModels/ViewModels/SpeakerLineBrowserViewModel.cs`:

```csharp
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One entry in the character picker: a speaker with a resolved display name and
/// its line count. ToString drives ComboBox display and text search.
public record SpeakerPickerItem(string Guid, string DisplayName, int Count)
{
    public override string ToString() => $"{DisplayName} ({Count})";
}

/// <summary>
/// Drives the Speaker Line Browser. Runs the whole-game <see cref="SpeakerLineScanner"/>
/// off the UI thread (cancellable), derives the character picker from the returned rows,
/// and filters those rows in memory when the selected speaker or the "only my lines"
/// toggle changes — so switching characters never re-reads the game folder. Refresh
/// re-scans. The scan function is injectable so tests can bypass IO.
/// Spec: docs/superpowers/specs/2026-07-13-speaker-line-browser-design.md
/// </summary>
public partial class SpeakerLineBrowserViewModel : ObservableObject
{
    private readonly DialogProject _project;
    private readonly IGameDataProvider _provider;
    private readonly string _primaryLanguage;
    private readonly Func<(string? Name, ConversationEditSnapshot? Snapshot)> _openAccessor;
    private readonly string? _initialSpeakerGuid;
    private readonly Func<string?, ConversationEditSnapshot?, CancellationToken, IReadOnlyList<SpeakerLineRow>> _scan;

    private IReadOnlyList<SpeakerLineRow> _allRows = [];
    private CancellationTokenSource? _cts;

    public SpeakerLineBrowserViewModel(
        DialogProject project,
        IGameDataProvider provider,
        string primaryLanguage,
        Func<(string? Name, ConversationEditSnapshot? Snapshot)> openConversationAccessor,
        string? initialSpeakerGuid = null,
        Func<string?, ConversationEditSnapshot?, CancellationToken, IReadOnlyList<SpeakerLineRow>>? scanner = null)
    {
        _project            = project;
        _provider           = provider;
        _primaryLanguage    = primaryLanguage;
        _openAccessor       = openConversationAccessor;
        _initialSpeakerGuid = initialSpeakerGuid;
        _scan = scanner ?? ((name, snap, ct) =>
            SpeakerLineScanner.Scan(_project, _provider, _primaryLanguage, name, snap, ct));
    }

    public ObservableCollection<SpeakerPickerItem> Speakers { get; } = [];

    public IReadOnlyList<SpeakerLineRow> Rows { get; private set; } = [];

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(CancelScanCommand))]
    private bool _isBusy;

    [ObservableProperty] private string _statusText = string.Empty;

    [ObservableProperty] private SpeakerPickerItem? _selectedSpeaker;
    [ObservableProperty] private bool _onlyMyLines;

    partial void OnSelectedSpeakerChanged(SpeakerPickerItem? value) => ApplyFilter();
    partial void OnOnlyMyLinesChanged(bool value) => ApplyFilter();

    /// Raised with (conversationName, nodeId) when the user activates a row.
    public event Action<string, int>? RequestNavigate;
    public void NavigateTo(SpeakerLineRow row) => RequestNavigate?.Invoke(row.ConversationName, row.NodeId);

    public async Task ScanAsync()
    {
        _cts?.Cancel();
        _cts = new CancellationTokenSource();
        var token = _cts.Token;

        IsBusy = true;
        StatusText = Loc.Get("SpeakerLines_Scanning");
        var (name, snap) = _openAccessor();

        try
        {
            var rows = await Task.Run(() => _scan(name, snap, token), token);
            _allRows = rows;

            var speakers = rows
                .GroupBy(r => r.SpeakerGuid, StringComparer.OrdinalIgnoreCase)
                .Select(g => new SpeakerPickerItem(
                    g.Key, SpeakerNameService.Resolve(g.Key) ?? g.Key, g.Count()))
                .OrderBy(s => s.DisplayName, StringComparer.OrdinalIgnoreCase)
                .ToList();

            Speakers.Clear();
            foreach (var s in speakers) Speakers.Add(s);

            SelectedSpeaker =
                (_initialSpeakerGuid is not null
                    ? speakers.FirstOrDefault(s =>
                          string.Equals(s.Guid, _initialSpeakerGuid, StringComparison.OrdinalIgnoreCase))
                    : null)
                ?? speakers.FirstOrDefault();

            ApplyFilter();   // covers the case where SelectedSpeaker did not change value
        }
        catch (OperationCanceledException)
        {
            StatusText = Loc.Get("SpeakerLines_Cancelled");   // deliberate cancel — swallowed
        }
        finally
        {
            IsBusy = false;
        }
    }

    [RelayCommand]
    private Task Refresh() => ScanAsync();

    private bool CanCancelScan() => IsBusy;

    [RelayCommand(CanExecute = nameof(CanCancelScan))]
    private void CancelScan() => _cts?.Cancel();

    private void ApplyFilter()
    {
        var guid = SelectedSpeaker?.Guid;
        IEnumerable<SpeakerLineRow> q = guid is null
            ? []
            : _allRows.Where(r => string.Equals(r.SpeakerGuid, guid, StringComparison.OrdinalIgnoreCase));
        if (OnlyMyLines) q = q.Where(r => r.Origin != LineOrigin.Vanilla);

        Rows = q.ToList();
        OnPropertyChanged(nameof(Rows));
        StatusText = Rows.Count == 0
            ? Loc.Get("SpeakerLines_NoLines")
            : Loc.FormatCount("SpeakerLines_LineCount", Rows.Count);
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~SpeakerLineBrowserViewModelTests`
Expected: PASS (all 7). (The `Loc` keys resolve through `StubStringProvider`, which echoes keys — assertions check row/speaker state, not string values.)

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/SpeakerLineBrowserViewModel.cs \
        DialogEditor.Tests/ViewModels/SpeakerLineBrowserViewModelTests.cs
git commit -m "feat(speaker-lines): SpeakerLineBrowserViewModel with picker, filter, cancel"
```

---

## Task 3: `MainWindowViewModel` + `ConversationViewModel` wiring

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelSpeakerLinesTests.cs`

**Interfaces:**
- Consumes: `SpeakerLineBrowserViewModel` (Task 2); `NodeViewModel.SpeakerGuid`; existing `MainWindowViewModel` members `IsModified`, `SaveProject()`, `Canvas` (`ConversationName`, `Nodes`, `BuildSnapshot()`), `NavigateToFoundNode(string,int)`, `_project`, `_provider`, `_currentGameDirectory`, `ScanDirtyChoice`.
- Produces on `ConversationViewModel`: `event Action<string>? RequestBrowseSpeakerLines`, `IRelayCommand BrowseSpeakerLinesForNodeCommand`.
- Produces on `MainWindowViewModel`: `Func<SpeakerLineBrowserViewModel, Task>? ShowSpeakerLineBrowser`, `Func<Task<ScanDirtyChoice>>? ConfirmBrowseWithUnsavedChanges`, `bool CanBrowseSpeakerLines`, `IAsyncRelayCommand BrowseSpeakerLinesCommand`, `Task OpenSpeakerLineBrowserAsync(string? initialSpeakerGuid = null)`.

- [ ] **Step 1: Add the context command + event on `ConversationViewModel`**

In `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs`, add near the other node context commands (e.g. beside `BeginConnectCmd`/`DeleteNodeCmd`):

```csharp
/// Raised with a node's speaker GUID when the user asks to browse that
/// character's lines. MainWindowViewModel subscribes and opens the browser
/// (it owns the project/provider and the dirty guard).
public event Action<string>? RequestBrowseSpeakerLines;

[RelayCommand]
private void BrowseSpeakerLinesForNode(NodeViewModel? node)
{
    if (node is not null && !string.IsNullOrWhiteSpace(node.SpeakerGuid))
        RequestBrowseSpeakerLines?.Invoke(node.SpeakerGuid);
}
```

- [ ] **Step 2: Add gate, open-method, command, and delegates on `MainWindowViewModel`**

In `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`, add next to `CanFindInProject`/`FindInProject` (around line 266 / 2372):

```csharp
/// Same requirements as Find in Project: an open project, a loaded provider, and a
/// game folder (the browser scans every vanilla conversation on disk).
public bool CanBrowseSpeakerLines =>
    _project is not null
    && _provider is not null
    && !string.IsNullOrEmpty(_currentGameDirectory);

/// The View constructs and shows the browser window (and kicks off ScanAsync).
public Func<SpeakerLineBrowserViewModel, Task>? ShowSpeakerLineBrowser { get; set; }

/// Browser-specific three-way dirty guard (sibling of ConfirmScanWithUnsavedChanges;
/// browser copy differs — the scan still includes the open conversation's live text).
/// Null in unit tests → a dirty project is treated as Cancel, never a silent open.
public Func<Task<ScanDirtyChoice>>? ConfirmBrowseWithUnsavedChanges { get; set; }

[RelayCommand(CanExecute = nameof(CanBrowseSpeakerLines))]
private Task BrowseSpeakerLines() => OpenSpeakerLineBrowserAsync(null);

/// Opens the Speaker Line Browser, pre-selecting <paramref name="initialSpeakerGuid"/>
/// when supplied (the canvas context action). Runs the dirty guard first; declining to
/// save still opens, cancelling does not. Reuses NavigateToFoundNode for jump-to-node.
public async Task OpenSpeakerLineBrowserAsync(string? initialSpeakerGuid = null)
{
    if (_project is null || _provider is null) return;

    if (IsModified)
    {
        var choice = ConfirmBrowseWithUnsavedChanges is null
            ? ScanDirtyChoice.Cancel
            : await ConfirmBrowseWithUnsavedChanges();
        if (choice == ScanDirtyChoice.Cancel) return;
        if (choice == ScanDirtyChoice.SaveAndScan) SaveProject();
    }

    var vm = new SpeakerLineBrowserViewModel(
        _project, _provider, _provider.Language,
        () => Canvas.Nodes.Count > 0
            ? (Canvas.ConversationName, Canvas.BuildSnapshot())
            : (null, (ConversationEditSnapshot?)null),
        initialSpeakerGuid);
    vm.RequestNavigate += NavigateToFoundNode;

    if (ShowSpeakerLineBrowser is not null)
        await ShowSpeakerLineBrowser(vm);
}
```

- [ ] **Step 3: Subscribe to the Canvas context event**

In the `MainWindowViewModel` constructor, alongside the other `Canvas.*` subscriptions (near the `Canvas.ConnectModeChanged += ...` block around line 380), add:

```csharp
Canvas.RequestBrowseSpeakerLines += guid => _ = OpenSpeakerLineBrowserAsync(guid);
```

- [ ] **Step 4: Notify the command's CanExecute at the same sites as Find in Project**

Add `BrowseSpeakerLinesCommand.NotifyCanExecuteChanged();` immediately after each existing
`FindInProjectCommand.NotifyCanExecuteChanged();` line (there are several — project
open/save/close, save-as, new-project, and game-folder load). Use a repo search to find them:

Run: `git grep -n "FindInProjectCommand.NotifyCanExecuteChanged" DialogEditor.ViewModels`

For each of the ~6 hits, add the sibling line directly below it. (This keeps the menu item's
enabled state in lockstep with Find in Project, which shares the exact same gate.)

- [ ] **Step 5: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/MainWindowViewModelSpeakerLinesTests.cs`:

```csharp
using System.Reflection;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// The Speaker Line Browser open path on MainWindowViewModel: gate + browser-specific
/// three-way dirty guard, mirroring MainWindowViewModelTextTagTests.
public class MainWindowViewModelSpeakerLinesTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _projectPath;

    public MainWindowViewModelSpeakerLinesTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_sl_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _projectPath = Path.Combine(Path.GetTempPath(), $"sl_{Guid.NewGuid():N}.dialogproject");
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { if (File.Exists(_projectPath)) File.Delete(_projectPath); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object value)
    {
        var fi = typeof(MainWindowViewModel).GetField(field,
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        fi.SetValue(vm, value);
    }

    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("SetProject",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    /// An open project + a stub provider, so the open path passes its project/provider gate.
    private MainWindowViewModel OpenProject(bool withPath = true)
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("T"));
        Inject(vm, "_provider", new FakeGameDataProvider("poe2", "en"));
        if (withPath) Inject(vm, "_projectPath", _projectPath);
        return vm;
    }

    [Fact]
    public async Task NoProject_DoesNotOpen()
    {
        var vm = MakeVm();
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.Null(shown);
    }

    [Fact]
    public async Task CleanProject_Opens_WithoutConsultingGuard()
    {
        var vm = OpenProject();
        var consulted = false;
        vm.ConfirmBrowseWithUnsavedChanges = () => { consulted = true; return Task.FromResult(ScanDirtyChoice.Cancel); };
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.NotNull(shown);
        Assert.False(consulted);
    }

    [Fact]
    public async Task Dirty_Cancel_DoesNotOpen_NorSave()
    {
        var vm = OpenProject();
        vm.IsModified = true;
        vm.ConfirmBrowseWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.Cancel);
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.Null(shown);
        Assert.True(vm.IsModified);
        Assert.False(File.Exists(_projectPath));
    }

    [Fact]
    public async Task Dirty_ScanSavedOnly_Opens_WithoutSaving()
    {
        var vm = OpenProject();
        vm.IsModified = true;
        vm.ConfirmBrowseWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.ScanSavedOnly);
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.NotNull(shown);
        Assert.True(vm.IsModified);
        Assert.False(File.Exists(_projectPath));
    }

    [Fact]
    public async Task Dirty_SaveAndScan_SavesThenOpens()
    {
        var vm = OpenProject();
        vm.IsModified = true;
        vm.ConfirmBrowseWithUnsavedChanges = () => Task.FromResult(ScanDirtyChoice.SaveAndScan);
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.NotNull(shown);
        Assert.False(vm.IsModified);              // SaveProject flipped the flag
        Assert.True(File.Exists(_projectPath));
    }

    [Fact]
    public async Task Dirty_NoGuardWired_DoesNotOpen()
    {
        var vm = OpenProject();
        vm.IsModified = true;                     // no ConfirmBrowseWithUnsavedChanges set
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        await vm.OpenSpeakerLineBrowserAsync();

        Assert.Null(shown);
    }

    [Fact]
    public async Task InitialSpeaker_IsForwarded_FromContextEvent()
    {
        var vm = OpenProject();
        SpeakerLineBrowserViewModel? shown = null;
        vm.ShowSpeakerLineBrowser = b => { shown = b; return Task.CompletedTask; };

        // Simulate the canvas context action for a node whose speaker is Bao.
        await vm.OpenSpeakerLineBrowserAsync("9c5f12c9-e93d-4952-9f1a-726c9498f8fb");

        Assert.NotNull(shown);   // opened; the pre-selection is exercised by the VM's own tests
    }
}
```

- [ ] **Step 6: Run the tests to verify they fail, then pass**

Run: `dotnet test DialogEditor.Tests --filter FullyQualifiedName~MainWindowViewModelSpeakerLinesTests`
Expected first run: FAIL (members not present until Steps 1–4 compiled). After Steps 1–4 are in place: PASS (all 7). If red, fix the implementation, not the tests.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs \
        DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs \
        DialogEditor.Tests/ViewModels/MainWindowViewModelSpeakerLinesTests.cs
git commit -m "feat(speaker-lines): MainWindowViewModel open path + canvas context command"
```

---

## Task 4: Window, menu, context menu, dialog copy, strings

**Files:**
- Create: `DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml`
- Create: `DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (menu item)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (delegates + browser seam)
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml` (context-menu item)
- Modify: `DialogEditor.Avalonia/Views/SaveBeforeScanDialog.axaml` + `.axaml.cs` (optional copy override)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (new keys)
- Test: `DialogEditor.Tests/Views/SpeakerLineBrowserWindowTests.cs`

**Interfaces:**
- Consumes: `SpeakerLineBrowserViewModel` (Task 2), its `Rows`/`Speakers`/`SelectedSpeaker`/`OnlyMyLines`/`IsBusy`/`StatusText`/`RefreshCommand`/`CancelScanCommand`/`NavigateTo`; `MainWindowViewModel.ShowSpeakerLineBrowser`/`ConfirmBrowseWithUnsavedChanges`/`BrowseSpeakerLinesCommand`; `ConversationViewModel.BrowseSpeakerLinesForNodeCommand`; `Loc.Get`.
- Produces: the runnable window + wired entry points; a smoke test constructing the window.

- [ ] **Step 1: Add all resource strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, add (adjust surrounding formatting to match the file; keep translator-note style consistent):

```xml
<!-- Speaker Line Browser -->
<x:String x:Key="Menu_BrowseSpeakerLines">Browse Speaker Lines…</x:String>
<x:String x:Key="ToolTip_BrowseSpeakerLines">Open a read-only window listing every line the chosen character speaks across the whole game, with your project's edits applied — for checking that new lines match the character's established voice.</x:String>
<x:String x:Key="Menu_Canvas_BrowseSpeakerLinesForNode">Show all lines by this speaker</x:String>
<x:String x:Key="ToolTip_Canvas_BrowseSpeakerLinesForNode">Open the Speaker Line Browser pre-filtered to this node's speaker.</x:String>

<x:String x:Key="SpeakerLines_Title">Speaker Lines</x:String>
<x:String x:Key="SpeakerLines_CharacterLabel">Character:</x:String>
<x:String x:Key="ToolTip_SpeakerLines_Character">Choose whose lines to list. The count in parentheses is how many lines that character speaks across the whole game plus your edits.</x:String>
<x:String x:Key="SpeakerLines_OnlyMyLines">Only my lines</x:String>
<x:String x:Key="ToolTip_SpeakerLines_OnlyMyLines">Hide unchanged game lines, showing only lines your project edits or adds.</x:String>
<x:String x:Key="AutomationName_SpeakerLines_OnlyMyLines">Only my lines</x:String>
<x:String x:Key="SpeakerLines_Refresh">Refresh</x:String>
<x:String x:Key="ToolTip_SpeakerLines_Refresh">Re-scan the game and your project to pick up edits made since this window opened.</x:String>
<x:String x:Key="AutomationName_SpeakerLines_Refresh">Refresh scan</x:String>
<x:String x:Key="SpeakerLines_Cancel">Cancel</x:String>
<x:String x:Key="ToolTip_SpeakerLines_Cancel">Stop the scan in progress.</x:String>
<x:String x:Key="AutomationName_SpeakerLines_Cancel">Cancel scan</x:String>
<x:String x:Key="AutomationName_SpeakerLines_Character">Character</x:String>
<x:String x:Key="SpeakerLines_Scanning">Scanning every conversation…</x:String>
<x:String x:Key="SpeakerLines_Cancelled">Scan cancelled.</x:String>
<x:String x:Key="SpeakerLines_NoLines">No lines found for this character.</x:String>
<x:String x:Key="SpeakerLines_LineCount_One">{0} line</x:String>
<x:String x:Key="SpeakerLines_LineCount_Other">{0} lines</x:String>
<x:String x:Key="SpeakerLines_Col_Conversation">Conversation</x:String>
<x:String x:Key="SpeakerLines_Col_Node">Node</x:String>
<x:String x:Key="SpeakerLines_Col_Origin">Origin</x:String>
<x:String x:Key="SpeakerLines_Col_Line">Line</x:String>
<x:String x:Key="SpeakerLines_Origin_Vanilla">Vanilla</x:String>
<x:String x:Key="SpeakerLines_Origin_Edited">Edited</x:String>
<x:String x:Key="SpeakerLines_Origin_New">New</x:String>
<x:String x:Key="SpeakerLines_Variant_Female">Female</x:String>

<!-- Browser-specific save-before-browse copy (reuses SaveBeforeScanDialog) -->
<x:String x:Key="SaveBeforeBrowse_Message">This project has unsaved changes. Save first so line origins and other conversations reflect your latest work? Your currently-open conversation's unsaved text is included either way — saving only affects how lines are tagged and the other conversations you've edited.</x:String>
<x:String x:Key="SaveBeforeBrowse_SaveAndBrowse">Save and browse</x:String>
<x:String x:Key="ToolTip_SaveBeforeBrowse_SaveAndBrowse">Save the project, then open the browser with accurate line origins.</x:String>
<x:String x:Key="SaveBeforeBrowse_BrowseAnyway">Browse without saving</x:String>
<x:String x:Key="ToolTip_SaveBeforeBrowse_BrowseAnyway">Open the browser now. Your open conversation's edits still show, but their origin tags and other unsaved conversations reflect the last save.</x:String>
```

- [ ] **Step 2: Add optional copy override to `SaveBeforeScanDialog`**

In `DialogEditor.Avalonia/Views/SaveBeforeScanDialog.axaml`, give the message TextBlock a name so code-behind can override it:

```xml
    <TextBlock x:Name="MessageText" Text="{DynamicResource SaveBeforeScan_Message}"
               Foreground="{DynamicResource Brush.Text.Primary}" FontSize="{DynamicResource FontSize.Body}"
               TextWrapping="Wrap"/>
```

In `DialogEditor.Avalonia/Views/SaveBeforeScanDialog.axaml.cs`, add an optional constructor
that overrides the message and the two action-button labels via resource keys (Cancel is
generic and unchanged):

```csharp
using DialogEditor.ViewModels.Resources;
// ...

public SaveBeforeScanDialog() { /* existing body */ }

/// Overload for callers that need scan-specific copy (e.g. the Speaker Line Browser).
/// Null keys keep the default XAML resources (the Validate Text Tags wording).
public SaveBeforeScanDialog(string? messageKey, string? saveButtonKey, string? proceedButtonKey)
    : this()
{
    if (messageKey is not null)       MessageText.Text          = Loc.Get(messageKey);
    if (saveButtonKey is not null)    SaveAndScanButton.Content = Loc.Get(saveButtonKey);
    if (proceedButtonKey is not null) ScanSavedOnlyButton.Content = Loc.Get(proceedButtonKey);
}
```

- [ ] **Step 3: Create the window XAML**

Create `DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:views="clr-namespace:DialogEditor.Avalonia.Views"
        x:Class="DialogEditor.Avalonia.Views.SpeakerLineBrowserWindow"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Title="{DynamicResource SpeakerLines_Title}"
        Width="820" Height="620" MinWidth="560" MinHeight="360"
        Background="{DynamicResource Brush.Surface.Panel}">
  <DockPanel Margin="12">

    <!-- Controls row -->
    <Grid DockPanel.Dock="Top" ColumnDefinitions="Auto,*,Auto,Auto" Margin="0,0,0,8">
      <StackPanel Grid.Column="0" Orientation="Horizontal" Spacing="6" VerticalAlignment="Center">
        <TextBlock Text="{DynamicResource SpeakerLines_CharacterLabel}"
                   VerticalAlignment="Center"
                   Foreground="{DynamicResource Brush.Text.Primary}"
                   FontSize="{DynamicResource FontSize.Body}"/>
        <ComboBox x:Name="SpeakerPicker" MinWidth="240"
                  ItemsSource="{Binding Speakers}"
                  SelectedItem="{Binding SelectedSpeaker}"
                  AutomationProperties.Name="{DynamicResource AutomationName_SpeakerLines_Character}"
                  ToolTip.Tip="{DynamicResource ToolTip_SpeakerLines_Character}"
                  AutomationProperties.HelpText="{DynamicResource ToolTip_SpeakerLines_Character}"/>
      </StackPanel>

      <CheckBox Grid.Column="2" Content="{DynamicResource SpeakerLines_OnlyMyLines}"
                IsChecked="{Binding OnlyMyLines}" Margin="8,0"
                VerticalAlignment="Center"
                AutomationProperties.Name="{DynamicResource AutomationName_SpeakerLines_OnlyMyLines}"
                ToolTip.Tip="{DynamicResource ToolTip_SpeakerLines_OnlyMyLines}"
                AutomationProperties.HelpText="{DynamicResource ToolTip_SpeakerLines_OnlyMyLines}"/>

      <Button Grid.Column="3" Content="{DynamicResource SpeakerLines_Refresh}"
              Command="{Binding RefreshCommand}"
              AutomationProperties.Name="{DynamicResource AutomationName_SpeakerLines_Refresh}"
              ToolTip.Tip="{DynamicResource ToolTip_SpeakerLines_Refresh}"
              AutomationProperties.HelpText="{DynamicResource ToolTip_SpeakerLines_Refresh}"/>
    </Grid>

    <!-- Status + focus hint -->
    <views:FocusHintBar x:Name="HintBar" DockPanel.Dock="Bottom"/>
    <TextBlock DockPanel.Dock="Bottom" Text="{Binding StatusText}" Margin="2,6,2,2"
               Foreground="{DynamicResource Brush.Text.Secondary}"
               FontSize="{DynamicResource FontSize.Body}"/>

    <!-- Results + loading overlay share the same cell -->
    <Panel>
      <ListBox x:Name="ResultsList" ItemsSource="{Binding Rows}"
               KeyDown="ResultsList_KeyDown">
        <ListBox.ItemTemplate>
          <DataTemplate>
            <Grid ColumnDefinitions="Auto,Auto,Auto,*" Margin="0,2" DoubleTapped="OnRowActivated">
              <TextBlock Grid.Column="0" Text="{Binding OriginLabel}" Width="70"
                         Foreground="{DynamicResource Brush.Text.Secondary}"
                         FontSize="{DynamicResource FontSize.Small}"/>
              <TextBlock Grid.Column="1" Text="{Binding Row.ConversationName}" Width="200"
                         TextTrimming="CharacterEllipsis"
                         Foreground="{DynamicResource Brush.Text.Primary}"
                         FontSize="{DynamicResource FontSize.Small}"/>
              <TextBlock Grid.Column="2" Text="{Binding NodeLabel}" Width="90"
                         Foreground="{DynamicResource Brush.Text.Secondary}"
                         FontSize="{DynamicResource FontSize.Small}"/>
              <TextBlock Grid.Column="3" Text="{Binding Row.LineText}" TextWrapping="Wrap"
                         Foreground="{DynamicResource Brush.Text.Primary}"
                         FontSize="{DynamicResource FontSize.Body}"/>
            </Grid>
          </DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>

      <Border IsVisible="{Binding IsBusy}"
              Background="{DynamicResource Brush.Surface.Panel}"
              HorizontalAlignment="Stretch" VerticalAlignment="Stretch">
        <StackPanel HorizontalAlignment="Center" VerticalAlignment="Center" Spacing="12">
          <ProgressBar IsIndeterminate="True" Width="220" Height="6"/>
          <TextBlock Text="{Binding StatusText}" HorizontalAlignment="Center"
                     Foreground="{DynamicResource Brush.Text.Primary}"
                     FontSize="{DynamicResource FontSize.Body}"/>
          <Button Content="{DynamicResource SpeakerLines_Cancel}"
                  HorizontalAlignment="Center"
                  Command="{Binding CancelScanCommand}"
                  AutomationProperties.Name="{DynamicResource AutomationName_SpeakerLines_Cancel}"
                  ToolTip.Tip="{DynamicResource ToolTip_SpeakerLines_Cancel}"
                  AutomationProperties.HelpText="{DynamicResource ToolTip_SpeakerLines_Cancel}"/>
        </StackPanel>
      </Border>
    </Panel>

  </DockPanel>
</Window>
```

- [ ] **Step 4: Create the window code-behind**

Create `DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml.cs`:

```csharp
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

/// <summary>
/// Non-modal results window for SpeakerLineBrowserViewModel. Rows are wrapped in a
/// display record that pre-resolves the localized origin badge and "conversation node"
/// label (mirrors FindInProjectWindow's language-label approach — cheaper than a
/// globally-registered converter for one window's columns). The window kicks off the
/// off-thread scan on construction and re-projects Rows whenever the VM raises the change.
/// </summary>
public partial class SpeakerLineBrowserWindow : Window
{
    private readonly SpeakerLineBrowserViewModel? _vm;

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public SpeakerLineBrowserWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public SpeakerLineBrowserWindow(SpeakerLineBrowserViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += Vm_PropertyChanged;
        RefreshResults();
        _ = vm.ScanAsync();   // starts the off-thread whole-game scan
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.CancelScanCommand.Execute(null);   // stop any scan still in flight
        }
    }

    private void Vm_PropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpeakerLineBrowserViewModel.Rows))
            RefreshResults();
    }

    private void RefreshResults()
    {
        if (_vm is null) return;
        ResultsList.ItemsSource = _vm.Rows.Select(r => new DisplayRow(
            r,
            Loc.Get(r.Origin switch
            {
                LineOrigin.New    => "SpeakerLines_Origin_New",
                LineOrigin.Edited => "SpeakerLines_Origin_Edited",
                _                 => "SpeakerLines_Origin_Vanilla",
            }),
            NodeLabelFor(r))).ToList();
    }

    private static string NodeLabelFor(SpeakerLineRow r) =>
        r.Variant == LineVariant.Female
            ? $"{Loc.Format("SpeakerLines_Col_Node")} {r.NodeId} · {Loc.Get("SpeakerLines_Variant_Female")}"
            : $"{Loc.Format("SpeakerLines_Col_Node")} {r.NodeId}";

    private void OnRowActivated(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if ((sender as Control)?.DataContext is DisplayRow display)
            _vm.NavigateTo(display.Row);
    }

    private void ResultsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Key != Key.Enter) return;
        if (ResultsList.SelectedItem is DisplayRow display)
        {
            _vm.NavigateTo(display.Row);
            e.Handled = true;
        }
    }

    private sealed record DisplayRow(SpeakerLineRow Row, string OriginLabel, string NodeLabel);
}
```

Note: `Loc.Format("SpeakerLines_Col_Node")` with no args returns the raw "Node" string.
If `Loc.Format` requires args, use `Loc.Get("SpeakerLines_Col_Node")` instead — check the
existing `Loc` API and match it.

- [ ] **Step 5: Wire the window + browser seam in `MainWindow.axaml.cs`**

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`, next to the existing
`vm.ShowFindInProject = ...` (around line 204) add:

```csharp
vm.ShowSpeakerLineBrowser = async browserVm =>
{
    var win = new SpeakerLineBrowserWindow(browserVm);
    win.Show(this);          // non-modal, owned — stays open while the writer browses
    await Task.CompletedTask;
};
```

And next to the existing `vm.ConfirmScanWithUnsavedChanges = ...` (around line 84) add the
browser-specific guard using the new `SaveBeforeScanDialog` overload:

```csharp
vm.ConfirmBrowseWithUnsavedChanges = () =>
    new SaveBeforeScanDialog(
        messageKey:        "SaveBeforeBrowse_Message",
        saveButtonKey:     "SaveBeforeBrowse_SaveAndBrowse",
        proceedButtonKey:  "SaveBeforeBrowse_BrowseAnyway").ShowDialogAsync(this);
```

- [ ] **Step 6: Add the menu item**

In `DialogEditor.Avalonia/Views/MainWindow.axaml`, directly after the Find in Project
`MenuItem` (the block at lines 139–143), add:

```xml
                        <MenuItem Header="{DynamicResource Menu_BrowseSpeakerLines}"
                                  Command="{Binding BrowseSpeakerLinesCommand}"
                                  ToolTip.Tip="{DynamicResource ToolTip_BrowseSpeakerLines}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_BrowseSpeakerLines}"/>
```

- [ ] **Step 7: Add the node context-menu item**

In `DialogEditor.Avalonia/Views/ConversationView.axaml`, inside the per-node
`<ContextMenu>` (after the `Menu_ConnectToNode` item, around line 181), add:

```xml
                                    <MenuItem Header="{DynamicResource Menu_Canvas_BrowseSpeakerLinesForNode}"
                                              Command="{Binding $parent[UserControl].DataContext.BrowseSpeakerLinesForNodeCommand}"
                                              CommandParameter="{Binding}"
                                              ToolTip.Tip="{DynamicResource ToolTip_Canvas_BrowseSpeakerLinesForNode}"
                                              AutomationProperties.HelpText="{DynamicResource ToolTip_Canvas_BrowseSpeakerLinesForNode}"/>
```

- [ ] **Step 8: Write the window smoke test**

Create `DialogEditor.Tests/Views/SpeakerLineBrowserWindowTests.cs` (mirrors
`FindInProjectWindowTests`; construct the VM with an injected scan stub so no IO happens):

```csharp
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Views;

public class SpeakerLineBrowserWindowTests
{
    public SpeakerLineBrowserWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Window_Constructs_WithRows()
    {
        var provider = new FakeGameDataProvider("poe2", "en");
        var rows = new[]
        {
            new SpeakerLineRow("g1", "c1", 1, LineVariant.Default, "hello", LineOrigin.Vanilla),
        };
        var vm = new SpeakerLineBrowserViewModel(
            DialogProject.Empty("P"), provider, "en", () => (null, null), null,
            scanner: (_, _, _) => rows);

        var window = new SpeakerLineBrowserWindow(vm);

        Assert.NotNull(window);
        Assert.Same(vm, window.DataContext);
    }
}
```

Check the existing `FindInProjectWindowTests.cs` for the exact test-attribute (`[AvaloniaFact]`
vs `[Fact]`) and headless setup, and match it.

- [ ] **Step 9: Run the full test project + build the app**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — including `AutomationNameTests`, `AutomationHelpTextTests`,
`NoStrayHexTests`, `NoNamedColourForegroundTests`, `NoStaticStringResourceTests`,
`NoStaticFontSizeResourceTests`, `FakeWatermarkTests`, `HitTargetSizeTests`. If any
structural enforcer fails, fix the offending XAML (e.g. a missing `AutomationProperties.Name`
on the picker, a stray literal colour/fontsize) — do not weaken the test.

Run: `dotnet build DialogEditor.Avalonia`
Expected: build succeeds (confirms the AXAML compiles and both new entry points bind).

- [ ] **Step 10: Commit**

```bash
git add DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml \
        DialogEditor.Avalonia/Views/SpeakerLineBrowserWindow.axaml.cs \
        DialogEditor.Avalonia/Views/MainWindow.axaml \
        DialogEditor.Avalonia/Views/MainWindow.axaml.cs \
        DialogEditor.Avalonia/Views/ConversationView.axaml \
        DialogEditor.Avalonia/Views/SaveBeforeScanDialog.axaml \
        DialogEditor.Avalonia/Views/SaveBeforeScanDialog.axaml.cs \
        DialogEditor.Avalonia/Resources/Strings.axaml \
        DialogEditor.Tests/Views/SpeakerLineBrowserWindowTests.cs
git commit -m "feat(speaker-lines): browser window, menu + context entry points, dirty-guard copy"
```

---

## Task 5: Live verification + Gaps.md

**Files:**
- Modify: `Gaps.md` (mark the Speaker line browser item implemented)

- [ ] **Step 1: Drive the app end-to-end**

Use the `running-the-app` skill to launch the editor against a real PoE2 game folder and an
open project, then verify:
1. `Edit ▸ Browse Speaker Lines…` is enabled only with a project + game folder open.
2. Opening it shows the loading animation, then a populated character picker with counts.
3. Selecting a character lists full lines; the Vanilla/Edited/New badges are correct; "Only my
   lines" hides Vanilla rows.
4. Double-clicking a row jumps to that node (switching conversation, honouring the dirty guard).
5. The canvas node context-menu **"Show all lines by this speaker"** opens the window pre-filtered.
6. With unsaved changes, opening shows the browser-specific save prompt; "Browse without saving"
   proceeds, "Save and browse" saves first.
7. Starting a scan on a large folder and clicking **Cancel** aborts promptly with a "cancelled" status.

Capture a screenshot of the populated window.

- [ ] **Step 2: Update `Gaps.md`**

In the **Smaller Writer/UX Backlog** section, replace the Speaker line browser bullet with a
`✓ Implemented (2026-07-13)` entry summarising: whole-game + edits scan
(`SpeakerLineScanner`), character picker with counts, full-line rows with Vanilla/Edited/New
origin badge + "only my lines" filter, jump-to-node, two entry points (Edit menu + canvas
context action), off-thread cancellable scan, and the browser-specific save-before-browse
guard. Cite the spec path.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark speaker line browser implemented"
```

---

## Self-Review

**Spec coverage:**
- Whole-game + edits scan → Task 1 (`SpeakerLineScanner`, union of enumerated + patched names, live snapshot for open conversation). ✓
- Full-line rows, female-as-own-row, empty skip → Task 1 tests + impl. ✓
- Origin badge (Vanilla/Edited/New) → Task 1 origin classification; Task 4 textual badge. ✓
- "Only my lines" filter → Task 2 `OnlyMyLines`; Task 4 checkbox. ✓
- Character picker with counts, name-sorted → Task 2 `Speakers`/`SpeakerPickerItem`. ✓
- Two entry points → Task 3 (command + context event) + Task 4 (menu + context menu). ✓
- Jump-to-node via reused `NavigateToFoundNode` → Task 3 `OpenSpeakerLineBrowserAsync`. ✓
- Off-thread scan + visible loading animation + Cancel → Task 2 `ScanAsync`/`CancelScanCommand`; Task 4 overlay + ProgressBar. ✓
- Dirty-project three-way guard with browser-specific crystal-clear copy → Task 3 seam + Task 4 `SaveBeforeScanDialog` overload + strings. ✓
- Primary-language only → Task 1 (`primaryLanguage`, no per-language loop). ✓
- Localisation / tooltips / automation / window icon / focus hint → Task 4 strings + XAML. ✓
- Live verification → Task 5. ✓

**Placeholder scan:** Task 1 Step 6 explicitly removes the one deliberate throwaway line in a test; no other TBD/TODO/"add error handling" placeholders — all code shown in full.

**Type consistency:** `SpeakerLineRow`/`LineVariant`/`LineOrigin` (Task 1) are used unchanged in Tasks 2/4. `SpeakerLineScanner.Scan` signature matches the VM's injected `_scan` delegate `(string?, ConversationEditSnapshot?, CancellationToken) → IReadOnlyList<SpeakerLineRow>`. `SpeakerLineBrowserViewModel` members named in Task 2's Produces block match their use in Task 3 (`OpenSpeakerLineBrowserAsync`) and Task 4 (window bindings): `Speakers`, `SelectedSpeaker`, `OnlyMyLines`, `IsBusy`, `StatusText`, `Rows`, `RefreshCommand`, `CancelScanCommand`, `RequestNavigate`, `NavigateTo`, `ScanAsync`. `ConfirmBrowseWithUnsavedChanges`/`ShowSpeakerLineBrowser`/`BrowseSpeakerLinesCommand`/`CanBrowseSpeakerLines` consistent across Tasks 3–4. `SaveBeforeScanDialog(messageKey, saveButtonKey, proceedButtonKey)` overload defined in Task 4 Step 2 and called in Step 5 with matching argument names.

**Deviations from spec (intentional, noted):** the scanner returns `IReadOnlyList<SpeakerLineRow>` rather than a `SpeakerLineScanResult` wrapper — the picker list is derived in the VM (the layer that legitimately depends on `SpeakerNameService`), keeping the scanner pure and global-state-free. Spec updated to match.
