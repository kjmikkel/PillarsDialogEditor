# Listed/Collapsible Dangling-Link Panel Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the diff window's count-only dangling-link warning with a collapsible panel that lists each dangling link.

**Architecture:** The detection and model already exist (`NodeLinkAnalyzer` → `DiffViewModel.DanglingLinks`). Add a display projection (`DanglingLinkDescriptions`) populated alongside it in `Apply()`, and render it in a collapsible `Expander` docked above the apply bar. Read-only; collapsed by default; hidden when there are none.

**Tech Stack:** C# 12 / .NET 8, CommunityToolkit.Mvvm, Avalonia 11.3.14, xUnit + `Avalonia.Headless.XUnit`.

**Spec:** `docs/superpowers/specs/2026-06-04-dangling-link-panel-design.md`

---

## File Structure

- **Modify** `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` — add `DanglingLinkDescriptions`, populate in `Apply()`.
- **Modify** `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs` — assert the projection.
- **Modify** `DialogEditor.Avalonia/Resources/Strings.axaml` — `Diff_DanglingRow`, `ToolTip_Diff_DanglingPanel`.
- **Modify** `DialogEditor.Avalonia/Views/DiffWindow.axaml` — the `Expander` panel; remove the apply-bar count line.
- **Modify** `DialogEditor.Tests/Views/DiffWindowTests.cs` — headless hidden/visible tests.
- **Modify** `Gaps.md` — mark the panel follow-up done.

All four tasks are independent and build green.

---

### Task 1: `DanglingLinkDescriptions` projection in `DiffViewModel`

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`

The display strings are built in the ViewModel (localized, testable). `StubStringProvider` returns the key verbatim, so each description equals `"Diff_DanglingRow"` in tests — enough to confirm the format call is wired. The existing `MakeDanglingScenario()` fixture and the `Apply_PopulatesDanglingWarning_WhenSelectionLeavesADanglingLink` test already produce dangling links to assert against.

- [ ] **Step 1: Write the failing test**

Add to `DiffViewModelApplyTests` (next to `Apply_PopulatesDanglingWarning_WhenSelectionLeavesADanglingLink`):

```csharp
    [Fact]
    public void Apply_PopulatesDanglingLinkDescriptions_MatchingDanglingLinks()
    {
        var vm = MakeDanglingScenario();
        foreach (var g in vm.Groups) g.IsAllSelected = true;
        vm.CommitApply = _ => { };

        vm.ApplyCommand.Execute(null);

        Assert.NotEmpty(vm.DanglingLinkDescriptions);
        Assert.Equal(vm.DanglingLinks.Count, vm.DanglingLinkDescriptions.Count);
        // StubStringProvider returns the key verbatim, so every row is the format key.
        Assert.All(vm.DanglingLinkDescriptions, d => Assert.Equal("Diff_DanglingRow", d));
    }
```

- [ ] **Step 2: Run, confirm fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffViewModelApplyTests"
```
Expected: compile error — `DanglingLinkDescriptions` does not exist.

- [ ] **Step 3: Add the property**

In `DiffViewModel.cs`, directly below the existing `DanglingLinks` property:

```csharp
    public ObservableCollection<DanglingLink> DanglingLinks { get; } = [];

    public ObservableCollection<string> DanglingLinkDescriptions { get; } = [];
```

(The first line already exists — add the second line after it.)

- [ ] **Step 4: Populate it in `Apply()`**

Replace the existing dangling-population block in `Apply()`:

```csharp
        DanglingLinks.Clear();
        foreach (var d in NodeLinkAnalyzer.Analyze(result))
            DanglingLinks.Add(d);
```

with:

```csharp
        DanglingLinks.Clear();
        DanglingLinkDescriptions.Clear();
        foreach (var d in NodeLinkAnalyzer.Analyze(result))
        {
            DanglingLinks.Add(d);
            DanglingLinkDescriptions.Add(
                Loc.Format("Diff_DanglingRow", d.Conversation, d.FromNode, d.ToNode));
        }
```

- [ ] **Step 5: Run, confirm pass**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffViewModelApplyTests"
```
Expected: all `DiffViewModelApplyTests` pass (existing + 1 new).

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Tests/ViewModels/DiffViewModelApplyTests.cs
git commit -m "feat: DiffViewModel exposes per-link dangling descriptions for display"
```

---

### Task 2: Localized strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

Add the row format and the panel tooltip near the other `Diff_Dangling*` keys (search `Diff_DanglingWarning`). `Diff_DanglingWarning` (header) and `Diff_DanglingHeader` (intro) already exist and are reused.

- [ ] **Step 1: Add keys**

```xml
    <!-- {0} = conversation, {1} = source node id, {2} = removed target node id -->
    <sys:String x:Key="Diff_DanglingRow">{0}: node {1} → node {2} (target removed)</sys:String>
    <sys:String x:Key="ToolTip_Diff_DanglingPanel">Lists links whose destination node will not exist after bringing in these changes. They are allowed, but those links will lead nowhere in-game.</sys:String>
```
Use the proper arrow character (→), not `-&gt;`. Match the file's `<sys:String x:Key="...">` convention and indentation. If any key already exists, report instead of duplicating.

- [ ] **Step 2: Build**

```
dotnet build DialogEditor.Avalonia
```
Expected: `Build succeeded. 0 Error(s)`.

- [ ] **Step 3: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: strings for dangling-link row and panel tooltip"
```

---

### Task 3: The collapsible panel in `DiffWindow.axaml` + headless tests

**Files:**
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml`
- Modify: `DialogEditor.Tests/Views/DiffWindowTests.cs`

The panel is an `Expander` docked `Bottom`, placed in markup **after** the apply-bar `Border` so it sits above it. The redundant count `TextBlock` is removed from the apply bar. Headless tests drive `ApplyCommand` and assert visibility + row count.

- [ ] **Step 1: Write the failing headless tests**

Add to `DiffWindowTests` a link-carrying node helper and two tests. Add `using DialogEditor.Patch.Diff;` at the top if not present (for nothing new — the tests use only existing types; verify usings compile).

```csharp
    private static NodeEditSnapshot NodeWithLink(int id, int toId) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false,
            [new LinkEditSnapshot(id, toId, 1f, "", false)], [], []);

    [AvaloniaFact]
    public void DanglingPanel_Hidden_WhenApplyLeavesNoDanglingLinks()
    {
        // working copy (left) = [1]; ref (right) adds node 9, no deletions.
        var disk = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1)], [], []));
        var refp = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1), Node(9)], [], []));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path);

        var window = new DiffWindow(vm);
        window.Show();

        foreach (var g in vm.Groups) g.IsAllSelected = true;
        vm.ApplyCommand.Execute(null);

        Assert.Empty(vm.DanglingLinks);
        Assert.False(window.FindControl<Expander>("DanglingPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public void DanglingPanel_VisibleWithRows_AfterApplyLeavesDanglingLinks()
    {
        // ref adds node 5 (links to 8) AND deletes node 8 → bringing in both dangles.
        var disk = DialogProject.Empty("p");
        var refp = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [NodeWithLink(5, 8)], [8], []));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path);

        var window = new DiffWindow(vm);
        window.Show();

        foreach (var g in vm.Groups) g.IsAllSelected = true;
        vm.ApplyCommand.Execute(null);

        Assert.NotEmpty(vm.DanglingLinks);
        Assert.True(window.FindControl<Expander>("DanglingPanel")!.IsVisible);
        Assert.Equal(vm.DanglingLinks.Count,
                     window.FindControl<ItemsControl>("DanglingList")!.ItemCount);
    }
```

`Node`, `MakeFakeGit`, `WriteTempProject`, `DialogProject`, `ConversationPatch`, `DialogProjectSerializer`, `LinkEditSnapshot`, `Expander`, and `ItemsControl` are already available via the test file's existing usings and helpers (the file already imports `Avalonia.Controls`, `DialogEditor.Core.Models`, `DialogEditor.Patch`, `DialogEditor.Patch.Diff`). Add any that are genuinely missing.

- [ ] **Step 2: Run, confirm fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffWindowTests"
```
Expected: lookup failure — there is no control named `DanglingPanel`/`DanglingList` yet.

- [ ] **Step 3: Remove the redundant count line from the apply bar**

In `DiffWindow.axaml`, the apply bar's inner `StackPanel` currently contains the count `TextBlock` plus the hint. Remove the count `TextBlock` so only the hint remains. Change:

```xml
                <StackPanel>
                    <TextBlock Text="{Binding DanglingLinks.Count, StringFormat={StaticResource Diff_DanglingWarning}}"
                               Foreground="#e0a030" FontSize="10"
                               IsVisible="{Binding DanglingLinks.Count, Converter={StaticResource CountToVis}}"/>
                    <TextBlock Text="{StaticResource Diff_Hint}" Foreground="#888" FontSize="11" VerticalAlignment="Center"/>
                </StackPanel>
```

to:

```xml
                <StackPanel>
                    <TextBlock Text="{StaticResource Diff_Hint}" Foreground="#888" FontSize="11" VerticalAlignment="Center"/>
                </StackPanel>
```

- [ ] **Step 4: Insert the Expander panel above the apply bar**

In `DiffWindow.axaml`, immediately **after** the apply-bar `</Border>` (the one wrapping the `Diff_BringInButton`/`Diff_UndoBringIn` DockPanel) and **before** the `<!-- Main area: list + detail -->` comment / main `Grid`, insert:

```xml
        <!-- ── Dangling-link panel (above apply bar; visible after an apply leaves dangling links) ── -->
        <Expander DockPanel.Dock="Bottom"
                  x:Name="DanglingPanel"
                  Margin="0,6,0,0"
                  IsExpanded="False"
                  IsVisible="{Binding DanglingLinks.Count, Converter={StaticResource CountToVis}}"
                  ToolTip.Tip="{StaticResource ToolTip_Diff_DanglingPanel}">
            <Expander.Header>
                <TextBlock Text="{Binding DanglingLinks.Count, StringFormat={StaticResource Diff_DanglingWarning}}"
                           Foreground="#e0a030" FontSize="11"/>
            </Expander.Header>
            <StackPanel Margin="8,4,0,4" Spacing="2">
                <TextBlock Text="{StaticResource Diff_DanglingHeader}"
                           Foreground="#888" FontSize="11" TextWrapping="Wrap"/>
                <ItemsControl x:Name="DanglingList" ItemsSource="{Binding DanglingLinkDescriptions}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <TextBlock Text="{Binding}" Foreground="#ccc" FontSize="11" Margin="0,1,0,0"/>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </StackPanel>
        </Expander>
```

Note on docking: in a `DockPanel`, `Dock="Bottom"` siblings stack from the bottom up in document order. The status bar and apply bar are docked Bottom earlier in the markup, so placing this Expander after the apply bar makes it sit directly above the apply bar — exactly the intended position.

- [ ] **Step 5: Build**

```
dotnet build DialogEditor.Avalonia
```
Expected: `Build succeeded. 0 Error(s)`. Fix any missing-resource-key or unknown-control errors.

- [ ] **Step 6: Run the headless tests**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffWindowTests"
```
Expected: all `DiffWindowTests` pass (existing + 2 new).

- [ ] **Step 7: Commit**

```
git add DialogEditor.Avalonia/Views/DiffWindow.axaml DialogEditor.Tests/Views/DiffWindowTests.cs
git commit -m "feat: collapsible dangling-link panel listing each link above the apply bar"
```

---

### Task 4: Update Gaps.md and full verification

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Update Gaps.md**

In the **Selective apply** paragraph, find:

> Two follow-ups left intentionally: a fuller (listed/collapsible) dangling-link panel, and automatic dependency-pulling.

Replace with:

> The dangling-link warning is now a collapsible panel above the apply bar listing each dangling link (conversation, source node, removed target); read-only, collapsed by default, hidden when there are none. One follow-up remains intentionally: automatic dependency-pulling.

If the exact sentence is not found, search for "dependency-pulling" and report before editing.

- [ ] **Step 2: Full verification**

```
dotnet test DialogEditor.Tests
dotnet build
```
Expected: all tests pass; `Build succeeded. 0 Error(s)`. If anything fails, stop and report — do not commit.

- [ ] **Step 3: Commit**

```
git add Gaps.md
git commit -m "docs: record listed/collapsible dangling-link panel as implemented"
```

---

## Verification Checklist

1. `dotnet test DialogEditor.Tests` — all pass (suite runs serially by design).
2. `dotnet build` — 0 errors.
3. **Manual:** open the diff window with the working copy as one endpoint; tick changes that delete a node another brought-in node links to; click **Bring in** — an amber collapsible panel appears above the apply bar showing the count.
4. **Manual:** expand the panel — it lists one row per dangling link ("conversation: node X → node Y (target removed)") under the "Heads up…" intro line.
5. **Manual:** an apply that leaves no dangling links shows no panel.
6. **Manual:** the old single count line is gone from the apply bar (its info now lives in the panel header).
