# Git Conflict Female-Variant Text Detail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show both the Default and Female text variants — each as a labelled, word-level-highlighted mine-vs-theirs row — in the git merge-conflict resolution dialog, fixing the female-only and both-differ display gaps.

**Architecture:** A `TranslationEdit` conflict carries the Default text in `MineValue`/`TheirsValue` and adds init-only `MineFemaleValue`/`TheirsFemaleValue` for the female variant. The analyzer populates all four; the ViewModel exposes a `HasFemaleRow` flag; the window renders a second labelled diff block per side, visible only when female text exists. The merge logic is untouched — it already replaces the whole `NodeTranslation`.

**Tech Stack:** C#, .NET, Avalonia (headless tests), CommunityToolkit.Mvvm, xUnit. Strings live in `DialogEditor.Avalonia/Resources/Strings.axaml` and resolve via the `Loc` provider.

> **Test command note:** `DialogEditor.Tests` runs serially by design (AppSettings/Loc global-state race). Run filtered tests with `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~<name>"`.

---

## File Structure

- `DialogEditor.Patch/GitConflict/MergeConflict.cs` — add `MineFemaleValue`/`TheirsFemaleValue` init properties.
- `DialogEditor.Patch/GitConflict/GitMergeAnalyzer.cs` — populate Default + female values; delete `DisplayText`.
- `DialogEditor.ViewModels/ViewModels/GitConflictResolutionViewModel.cs` — expose female values + `HasFemaleRow` on `ConflictRowViewModel`.
- `DialogEditor.Avalonia/Resources/Strings.axaml` — two caption strings.
- `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml` — second labelled diff block per side.
- `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs` — render female diff inlines.
- `Gaps.md` — remove the resolved cosmetic-limitation bullet.
- Tests: `GitMergeAnalyzerTests`, `GitConflictResolutionViewModelTests`, `GitConflictResolutionWindowTests`.

---

## Task 1: Model — female-variant properties on MergeConflict

**Files:**
- Modify: `DialogEditor.Patch/GitConflict/MergeConflict.cs`

This task adds the data carriers. No test of its own — it is exercised by Task 2's analyzer tests (a bare record property has no behaviour to test in isolation).

- [ ] **Step 1: Add the init-only properties**

In `MergeConflict.cs`, inside the record body (alongside the existing `DeletedMarker` const), add:

```csharp
    /// Female-variant text for a TranslationEdit conflict (mine side).
    /// Empty for every other conflict kind. Display-only: the merge replaces
    /// the whole NodeTranslation regardless of which sub-field differs.
    public string MineFemaleValue { get; init; } = "";

    /// Female-variant text for a TranslationEdit conflict (theirs side). See MineFemaleValue.
    public string TheirsFemaleValue { get; init; } = "";
```

- [ ] **Step 2: Build to verify it compiles**

Run: `dotnet build DialogEditor.Patch`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Patch/GitConflict/MergeConflict.cs
git commit -m "feat: add female-variant value carriers to MergeConflict"
```

---

## Task 2: Analyzer — populate Default + female values

**Files:**
- Modify: `DialogEditor.Patch/GitConflict/GitMergeAnalyzer.cs:94-100` and delete `DisplayText` at `:169-172`
- Test: `DialogEditor.Tests/Patch/GitConflict/GitMergeAnalyzerTests.cs`

- [ ] **Step 1: Rewrite the female test and add a both-differ test**

In `GitMergeAnalyzerTests.cs`, replace the existing `TranslationFemaleTextDiffers_FallsBackToFemaleTextForDisplay` test (around `:198-208`) with these two tests:

```csharp
    [Fact]
    public void TranslationFemaleOnlyDiffers_DefaultEqual_FemaleCarriesDiff()
    {
        var mine   = ProjectWithTranslation(4, "en", "Hello", "HelloF");
        var theirs = ProjectWithTranslation(4, "en", "Hello", "HelloFemale");

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal(MergeConflictKind.TranslationEdit, c.Kind);
        Assert.Equal("Hello",       c.MineValue);          // shared Default
        Assert.Equal("Hello",       c.TheirsValue);
        Assert.Equal("HelloF",      c.MineFemaleValue);
        Assert.Equal("HelloFemale", c.TheirsFemaleValue);
    }

    [Fact]
    public void TranslationBothVariantsDiffer_AllFourValuesCarried()
    {
        var mine   = ProjectWithTranslation(4, "en", "Hi friend",   "Hi friendF");
        var theirs = ProjectWithTranslation(4, "en", "Hi traveler", "Hi travelerF");

        var c = Assert.Single(GitMergeAnalyzer.Analyze(mine, theirs));
        Assert.Equal("Hi friend",    c.MineValue);
        Assert.Equal("Hi traveler",  c.TheirsValue);
        Assert.Equal("Hi friendF",   c.MineFemaleValue);
        Assert.Equal("Hi travelerF", c.TheirsFemaleValue);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TranslationFemaleOnlyDiffers|FullyQualifiedName~TranslationBothVariantsDiffer"`
Expected: FAIL — `MineValue` is currently the collapsed `DisplayText` (e.g. `"HelloF"`), not `"Hello"`; `MineFemaleValue` is empty.

- [ ] **Step 3: Update the analyzer to populate all four values**

In `GitMergeAnalyzer.cs`, replace the translation-edit `granular.Add(...)` block (`:97-99`):

```csharp
            if (theirTr.TryGetValue(key, out var theirT) && !mineT.Equals(theirT))
                granular.Add(new MergeConflict(
                    MergeConflictKind.TranslationEdit, conv, key.NodeId, key.Lang,
                    mineT.DefaultText, theirT.DefaultText)
                {
                    MineFemaleValue   = mineT.FemaleText,
                    TheirsFemaleValue = theirT.FemaleText,
                });
```

Then delete the now-unused `DisplayText` helper and its comment (`:169-172`):

```csharp
    // Show the field that actually differs: DefaultText when it differs, else the
    // FemaleText (so a female-only difference is still visible in the dialog).
    private static string DisplayText(NodeTranslation self, NodeTranslation other)
        => self.DefaultText != other.DefaultText ? self.DefaultText : self.FemaleText;
```

- [ ] **Step 4: Run the full analyzer test class to verify pass + no regressions**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~GitMergeAnalyzerTests"`
Expected: PASS. `TranslationTextDiffers_IsTranslationEditConflict` still passes (`MineValue`=`"Hello friend"`, female values empty).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/GitConflict/GitMergeAnalyzer.cs DialogEditor.Tests/Patch/GitConflict/GitMergeAnalyzerTests.cs
git commit -m "feat: analyzer carries Default + female text separately for translation conflicts"
```

---

## Task 3: ViewModel — expose female values and HasFemaleRow

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/GitConflictResolutionViewModel.cs`
- Test: `DialogEditor.Tests/ViewModels/GitConflictResolutionViewModelTests.cs`

- [ ] **Step 1: Write the failing tests**

Add to `GitConflictResolutionViewModelTests.cs`. First add a translation-case helper near `FieldEditCase()`:

```csharp
    private static (DialogProject Mine, DialogProject Theirs, IReadOnlyList<MergeConflict> Conflicts) TranslationCase(
        string mineDefault, string theirsDefault, string mineFemale, string theirsFemale)
    {
        DialogProject P(string def, string fem)
        {
            var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                {
                    ["en"] = [new NodeTranslation(4, def, fem)],
                },
            };
            return DialogProject.Empty("p").WithPatch(patch);
        }

        var mine   = P(mineDefault, mineFemale);
        var theirs = P(theirsDefault, theirsFemale);
        return (mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }
```

Then the tests:

```csharp
    [Fact]
    public void TranslationRow_WithFemaleText_HasFemaleRowAndValues()
    {
        var (m, t, c) = TranslationCase("Hello", "Hello", "HelloF", "HelloFemale");
        var vm = new GitConflictResolutionViewModel(m, t, c);
        var row = vm.Conflicts[0];

        Assert.True(row.HasFemaleRow);
        Assert.Equal("HelloF",      row.MineFemaleValue);
        Assert.Equal("HelloFemale", row.TheirsFemaleValue);
    }

    [Fact]
    public void TranslationRow_NoFemaleText_NoFemaleRow()
    {
        var (m, t, c) = TranslationCase("Hello friend", "Hello traveler", "", "");
        var vm = new GitConflictResolutionViewModel(m, t, c);

        Assert.False(vm.Conflicts[0].HasFemaleRow);
    }

    [Fact]
    public void FieldEditRow_HasNoFemaleRow()
    {
        var (m, t, c) = FieldEditCase();
        var vm = new GitConflictResolutionViewModel(m, t, c);

        Assert.False(vm.Conflicts[0].HasFemaleRow);
    }
```

Add `using DialogEditor.Core.Models;` to the file's usings if not already present (needed for `NodeTranslation`).

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TranslationRow_WithFemaleText|FullyQualifiedName~TranslationRow_NoFemaleText|FullyQualifiedName~FieldEditRow_HasNoFemaleRow"`
Expected: FAIL — `ConflictRowViewModel` has no `HasFemaleRow`/`MineFemaleValue`/`TheirsFemaleValue` members (compile error).

- [ ] **Step 3: Add the members to ConflictRowViewModel**

In `GitConflictResolutionViewModel.cs`, after the existing `MineValue`/`TheirsValue` properties (`:20-21`), add:

```csharp
    public string MineFemaleValue   => Conflict.MineFemaleValue;
    public string TheirsFemaleValue => Conflict.TheirsFemaleValue;

    public bool HasFemaleRow =>
        Kind == MergeConflictKind.TranslationEdit
        && (!string.IsNullOrEmpty(MineFemaleValue) || !string.IsNullOrEmpty(TheirsFemaleValue));
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~GitConflictResolutionViewModelTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/GitConflictResolutionViewModel.cs DialogEditor.Tests/ViewModels/GitConflictResolutionViewModelTests.cs
git commit -m "feat: expose female-variant values and HasFemaleRow on conflict row"
```

---

## Task 4: Localization — caption strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

No standalone test — captions are consumed by Task 5's window. The StubStringProvider used in tests returns keys, so window tests don't depend on these literals.

- [ ] **Step 1: Add the caption strings**

In `Strings.axaml`, after `GitConflict_RowTitleTranslation` (`:709`), add:

```xml
    <!-- Per-variant captions in the conflict detail body -->
    <sys:String x:Key="GitConflict_DefaultTextLabel">Default text</sys:String>
    <sys:String x:Key="GitConflict_FemaleTextLabel">Female text</sys:String>
```

- [ ] **Step 2: Build to verify the resource dictionary parses**

Run: `dotnet build DialogEditor.Avalonia`
Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: add Default/Female text caption strings for conflict dialog"
```

---

## Task 5: View — render the female-variant diff block

**Files:**
- Modify: `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs`
- Test: `DialogEditor.Tests/Views/GitConflictResolutionWindowTests.cs`

- [ ] **Step 1: Write the failing tests**

In `GitConflictResolutionWindowTests.cs`, add a helper that produces a female-only translation conflict, then two tests. Place after `MakeTranslationVm()`:

```csharp
    private static GitConflictResolutionViewModel MakeFemaleTranslationVm()
    {
        static DialogProject P(string female)
        {
            var patch = new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [])
            {
                Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                {
                    ["en"] = [new NodeTranslation(4, "Hello", female)],   // Default equal both sides
                },
            };
            return DialogProject.Empty("p").WithPatch(patch);
        }

        var mine   = P("Hello friend");
        var theirs = P("Hello traveler");
        return new GitConflictResolutionViewModel(mine, theirs, GitMergeAnalyzer.Analyze(mine, theirs));
    }

    [AvaloniaFact]
    public void FemaleTranslationConflict_RendersVisibleFemaleInlines()
    {
        var vm     = MakeFemaleTranslationVm();
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        var femaleText = window.FindControl<TextBlock>("MineFemaleDiffText")!;
        Assert.True(femaleText.IsVisible);
        Assert.True(femaleText.Inlines!.Count > 1);   // common + mine-only spans
    }

    [AvaloniaFact]
    public void FieldEditConflict_HidesFemaleBlock()
    {
        var vm     = MakeVm();   // field edit, no female text
        var window = new GitConflictResolutionWindow(vm);
        window.Show();

        Assert.False(window.FindControl<TextBlock>("MineFemaleDiffText")!.IsVisible);
    }
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FemaleTranslationConflict_RendersVisibleFemaleInlines|FullyQualifiedName~FieldEditConflict_HidesFemaleBlock"`
Expected: FAIL — `MineFemaleDiffText` control does not exist (`FindControl` returns null → NRE).

- [ ] **Step 3: Add the female caption + block to the XAML (both sides)**

In `GitConflictResolutionWindow.axaml`, replace the Mine-side `StackPanel` content (`:45-54`) with:

```xml
                    <StackPanel Spacing="8">
                        <TextBlock Text="{StaticResource GitConflictWindow_MineHeader}"
                                   Foreground="#9cc4ff" FontSize="12" FontWeight="Bold"/>
                        <TextBlock Text="{StaticResource GitConflict_DefaultTextLabel}"
                                   Foreground="#888" FontSize="10"
                                   IsVisible="{Binding HasFemaleRow}"/>
                        <TextBlock x:Name="MineDiffText"
                                   FontSize="12" TextWrapping="Wrap"/>
                        <TextBlock Text="{StaticResource GitConflict_FemaleTextLabel}"
                                   Foreground="#888" FontSize="10"
                                   IsVisible="{Binding HasFemaleRow}"/>
                        <TextBlock x:Name="MineFemaleDiffText"
                                   FontSize="12" TextWrapping="Wrap"
                                   IsVisible="{Binding HasFemaleRow}"/>
                        <RadioButton x:Name="MineRadio" GroupName="Choice"
                                     Content="{Binding MineLabel}"
                                     IsChecked="{Binding IsMineChosen, Mode=TwoWay}"
                                     ToolTip.Tip="{StaticResource GitConflictWindow_MineTooltip}"
                                     Foreground="#e8e8e8"/>
                    </StackPanel>
```

Then replace the Theirs-side `StackPanel` content (`:59-68`) with the mirror:

```xml
                    <StackPanel Spacing="8">
                        <TextBlock Text="{StaticResource GitConflictWindow_TheirsHeader}"
                                   Foreground="#ff9c9c" FontSize="12" FontWeight="Bold"/>
                        <TextBlock Text="{StaticResource GitConflict_DefaultTextLabel}"
                                   Foreground="#888" FontSize="10"
                                   IsVisible="{Binding HasFemaleRow}"/>
                        <TextBlock x:Name="TheirsDiffText"
                                   FontSize="12" TextWrapping="Wrap"/>
                        <TextBlock Text="{StaticResource GitConflict_FemaleTextLabel}"
                                   Foreground="#888" FontSize="10"
                                   IsVisible="{Binding HasFemaleRow}"/>
                        <TextBlock x:Name="TheirsFemaleDiffText"
                                   FontSize="12" TextWrapping="Wrap"
                                   IsVisible="{Binding HasFemaleRow}"/>
                        <RadioButton x:Name="TheirsRadio" GroupName="Choice"
                                     Content="{Binding TheirsLabel}"
                                     IsChecked="{Binding IsTheirsChosen, Mode=TwoWay}"
                                     ToolTip.Tip="{StaticResource GitConflictWindow_TheirsTooltip}"
                                     Foreground="#e8e8e8"/>
                    </StackPanel>
```

- [ ] **Step 4: Render the female inlines in code-behind**

In `GitConflictResolutionWindow.axaml.cs`, refactor `UpdateDiff` (`:54-86`) so the highlight logic is reusable, and drive the female blocks:

```csharp
    private void UpdateDiff(ConflictRowViewModel? row)
    {
        MineDiffText.Inlines   = BuildInlines(row, isMine: true,  female: false);
        TheirsDiffText.Inlines = BuildInlines(row, isMine: false, female: false);

        MineFemaleDiffText.Inlines   = BuildInlines(row, isMine: true,  female: true);
        TheirsFemaleDiffText.Inlines = BuildInlines(row, isMine: false, female: true);
    }

    // Build the highlighted inlines for one cell. Field/translation edits get
    // word-level highlighting; structural conflicts show the raw value.
    private static InlineCollection BuildInlines(ConflictRowViewModel? row, bool isMine, bool female)
    {
        var result = new InlineCollection();
        if (row is null)
            return result;

        var mineValue   = female ? row.MineFemaleValue   : row.MineValue;
        var theirsValue = female ? row.TheirsFemaleValue : row.TheirsValue;

        if (row.Kind is MergeConflictKind.FieldEdit or MergeConflictKind.TranslationEdit)
        {
            foreach (var span in TextDiff.Diff(mineValue, theirsValue))
            {
                switch (span.Kind)
                {
                    case DiffKind.Common:
                        result.Add(MakeRun(span.Text, CommonBrush));
                        break;
                    case DiffKind.MineOnly when isMine:
                        result.Add(MakeRun(span.Text, MineBrush));
                        break;
                    case DiffKind.TheirsOnly when !isMine:
                        result.Add(MakeRun(span.Text, TheirsBrush));
                        break;
                }
            }
        }
        else
        {
            result.Add(MakeRun(isMine ? mineValue : theirsValue, CommonBrush));
        }

        return result;
    }
```

Note: the female blocks' `IsVisible` is bound in XAML to `HasFemaleRow`, so the code-behind always populates their inlines but they only show when relevant.

- [ ] **Step 5: Run the window tests to verify they pass + no regressions**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~GitConflictResolutionWindowTests"`
Expected: PASS — including the existing `SelectedFieldEditConflict_BuildsHighlightedInlines`, `SelectedTranslationConflict_BuildsHighlightedInlines`, and apply tests.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs DialogEditor.Tests/Views/GitConflictResolutionWindowTests.cs
git commit -m "feat: render labelled Default/Female diff rows in conflict dialog"
```

---

## Task 6: Update Gaps.md and run the full suite

**Files:**
- Modify: `Gaps.md:19`

- [ ] **Step 1: Remove the resolved cosmetic-limitation bullet**

In `Gaps.md`, delete the bullet (`:19`):

```markdown
- A translation conflict where only `FemaleText` differs (same `DefaultText`) is detected and resolvable, but the resolution dialog shows the `FemaleText` rather than labelling it as the female-variant — a cosmetic display limitation.
```

If removing it leaves the "Minor known limitations:" intro with only the `ConversationLevel` bullet remaining, leave that intro and the surviving bullet intact (do not reword).

- [ ] **Step 2: Run the entire test suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — full green.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs: mark female-variant conflict display limitation resolved"
```

---

## Self-Review Notes

- **Spec coverage:** Model (Task 1), analyzer + `DisplayText` removal (Task 2), ViewModel `HasFemaleRow` (Task 3), localization (Task 4), view with both-side labelled rows + visibility (Task 5), docs (Task 6). Behaviour matrix cases all covered: female-only (Task 5 test), both-differ (Task 2 test), no-female (Task 3/5 tests), field-edit (Task 5 test).
- **Type consistency:** `MineFemaleValue`/`TheirsFemaleValue`/`HasFemaleRow` names identical across model, VM, tests, and XAML bindings. Control names `MineFemaleDiffText`/`TheirsFemaleDiffText` consistent between XAML and code-behind. `BuildInlines` signature consistent within Task 5.
- **No placeholders:** every code step shows full content.
