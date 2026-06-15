# FontSize Token Foundation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Centralise every literal numeric `FontSize` value across the app into a
9-entry `FontSize.*` token layer in `Tokens.axaml`, with a `NoStrayHexTests`-style
enforcement test that fails the build if any `.axaml` file outside `Tokens.axaml`
contains a bare numeric `FontSize`. Zero visual change — every value maps 1:1 to a
token of the same value.

**Architecture:** Add 9 `<x:Double x:Key="FontSize.*">` resources to
`DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`. Add
`DialogEditor.Tests/Theming/FontSizeTokenTests.cs` (pins the 9 key→value mappings) and
`DialogEditor.Tests/Theming/NoStrayFontSizeTests.cs` (enforcement, mirrors
`NoStrayHexTests`). Migrate all 30 affected `.axaml` files in 6 batches of 5 files each,
rewriting `FontSize="N"` → `FontSize="{StaticResource FontSize.X}"` and
`<Setter Property="FontSize" Value="N"/>` → `<Setter Property="FontSize"
Value="{StaticResource FontSize.X}"/>` via a mechanical `sed` sweep, driving the
enforcement test from 349 offenders down to 0.

**Tech Stack:** Avalonia `ResourceDictionary` (`x:Double` resource type), xUnit +
`Avalonia.Headless.XUnit` (`[AvaloniaTheory]`/`[Fact]`), `sed` (Git Bash) for the
mechanical rewrite, `dotnet build`/`dotnet test`.

**Reference spec:** `docs/superpowers/specs/2026-06-14-fontsize-token-foundation-design.md`

---

## Value → Token Mapping (used throughout this plan)

| Value | Token              |
|-------|--------------------|
| 8     | `FontSize.Micro`   |
| 9     | `FontSize.Caption` |
| 10    | `FontSize.Small`   |
| 11    | `FontSize.Label`   |
| 12    | `FontSize.Body`    |
| 13    | `FontSize.Medium`  |
| 14    | `FontSize.Subtitle`|
| 18    | `FontSize.Title`   |
| 32    | `FontSize.Display` |

## Migration Script (used by Tasks 3–8)

Each migration task runs this exact `sed` sweep over its batch's file list. It rewrites
both syntactic patterns (inline `FontSize="N"` attribute and `Setter Property="FontSize"
Value="N"` on the same line) using the mapping table above. It is idempotent — running
it twice on an already-migrated file is a no-op.

```bash
for f in <FILES>; do
  sed -i -E \
    -e '/Property="FontSize"/ s/Value="8"/Value="{StaticResource FontSize.Micro}"/' \
    -e '/Property="FontSize"/ s/Value="9"/Value="{StaticResource FontSize.Caption}"/' \
    -e '/Property="FontSize"/ s/Value="10"/Value="{StaticResource FontSize.Small}"/' \
    -e '/Property="FontSize"/ s/Value="11"/Value="{StaticResource FontSize.Label}"/' \
    -e '/Property="FontSize"/ s/Value="12"/Value="{StaticResource FontSize.Body}"/' \
    -e '/Property="FontSize"/ s/Value="13"/Value="{StaticResource FontSize.Medium}"/' \
    -e '/Property="FontSize"/ s/Value="14"/Value="{StaticResource FontSize.Subtitle}"/' \
    -e '/Property="FontSize"/ s/Value="18"/Value="{StaticResource FontSize.Title}"/' \
    -e '/Property="FontSize"/ s/Value="32"/Value="{StaticResource FontSize.Display}"/' \
    -e 's/FontSize="8"/FontSize="{StaticResource FontSize.Micro}"/g' \
    -e 's/FontSize="9"/FontSize="{StaticResource FontSize.Caption}"/g' \
    -e 's/FontSize="10"/FontSize="{StaticResource FontSize.Small}"/g' \
    -e 's/FontSize="11"/FontSize="{StaticResource FontSize.Label}"/g' \
    -e 's/FontSize="12"/FontSize="{StaticResource FontSize.Body}"/g' \
    -e 's/FontSize="13"/FontSize="{StaticResource FontSize.Medium}"/g' \
    -e 's/FontSize="14"/FontSize="{StaticResource FontSize.Subtitle}"/g' \
    -e 's/FontSize="18"/FontSize="{StaticResource FontSize.Title}"/g' \
    -e 's/FontSize="32"/FontSize="{StaticResource FontSize.Display}"/g' \
    "$f"
done
```

Replace `<FILES>` with the batch's file list (one quoted path per line, backslash
continuation), as given in each task below.

---

### Task 1: RED — enforcement test for stray FontSize literals

**Files:**
- Create: `DialogEditor.Tests/Theming/NoStrayFontSizeTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Text.RegularExpressions;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// The FontSize-token contract enforcer. Bare numeric FontSize values may only appear
/// in Tokens.axaml (where the FontSize.* tokens are defined); every view, control theme,
/// and style must bind {StaticResource FontSize.*} instead. See
/// docs/superpowers/specs/2026-06-14-fontsize-token-foundation-design.md.
/// </summary>
public class NoStrayFontSizeTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // Skip build output and other branches' working copies under .worktrees/ (gitignored,
    // but Directory.EnumerateFiles doesn't honour .gitignore).
    private static bool IsExcluded(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}");

    // Inline attribute: FontSize="12"
    private static readonly Regex InlineFontSize = new(@"FontSize\s*=\s*""[0-9]", RegexOptions.Compiled);

    // Style setter: <Setter Property="FontSize" ... Value="12"/> — both fragments appear
    // on the same line for every existing setter, so a per-line check on each suffices.
    private static readonly Regex SetterFontSizeProperty = new(@"Property\s*=\s*""FontSize""", RegexOptions.Compiled);
    private static readonly Regex SetterNumericValue = new(@"Value\s*=\s*""[0-9]", RegexOptions.Compiled);

    [Fact]
    public void NoFontSizeLiteralsOutsideTokens()
    {
        var root = SolutionRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (IsExcluded(file)) continue;
            if (Path.GetFileName(file).Equals("Tokens.axaml", StringComparison.OrdinalIgnoreCase)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
            {
                var line = lines[i];
                var isViolation = InlineFontSize.IsMatch(line)
                    || (SetterFontSizeProperty.IsMatch(line) && SetterNumericValue.IsMatch(line));
                if (isViolation)
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {line.Trim()}");
            }
        }
        Assert.True(offenders.Count == 0,
            "Bare numeric FontSize values are only allowed in Tokens.axaml; bind FontSize.* tokens instead. Offenders:\n"
            + string.Join("\n", offenders));
    }
}
```

- [ ] **Step 2: Run test to verify it fails with 349 offenders**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayFontSizeTests"`

Expected: FAIL. The assertion message lists 349 offender lines across 30 files.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Theming/NoStrayFontSizeTests.cs
git commit -m "test(theming): add NoStrayFontSizeTests enforcing FontSize token usage (RED)"
```

---

### Task 2: GREEN — add FontSize.* tokens and pin their values

**Files:**
- Create: `DialogEditor.Tests/Theming/FontSizeTokenTests.cs`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`

- [ ] **Step 1: Write the failing test**

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

public class FontSizeTokenTests
{
    private static double Size(string key)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, Application.Current!.ActualThemeVariant, out var v),
            $"FontSize key '{key}' is not defined");
        return Assert.IsType<double>(v);
    }

    [AvaloniaTheory]
    [InlineData("FontSize.Micro", 8)]
    [InlineData("FontSize.Caption", 9)]
    [InlineData("FontSize.Small", 10)]
    [InlineData("FontSize.Label", 11)]
    [InlineData("FontSize.Body", 12)]
    [InlineData("FontSize.Medium", 13)]
    [InlineData("FontSize.Subtitle", 14)]
    [InlineData("FontSize.Title", 18)]
    [InlineData("FontSize.Display", 32)]
    public void TokenResolvesToExpectedValue(string key, double expected)
        => Assert.Equal(expected, Size(key));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~FontSizeTokenTests"`

Expected: FAIL for all 9 cases with "FontSize key '...' is not defined".

- [ ] **Step 3: Add the 9 FontSize.\* tokens to Tokens.axaml**

In `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`, insert immediately after the
opening `<ResourceDictionary ...>` tag (line 2) and before the existing `<!-- TOKENS —
semantic brush layer ... -->` comment:

```xml

    <!-- Font sizes — semantic type-scale layer (mirrors the Brush.* token layer below).
         Every view binds these keys; no bare FontSize="N" literal may appear outside
         this file. 1:1 mapping of every size value in use today — pure renaming, no
         visual change. See docs/superpowers/specs/2026-06-14-fontsize-token-foundation-design.md. -->
    <x:Double x:Key="FontSize.Micro">8</x:Double>
    <x:Double x:Key="FontSize.Caption">9</x:Double>
    <x:Double x:Key="FontSize.Small">10</x:Double>
    <x:Double x:Key="FontSize.Label">11</x:Double>
    <x:Double x:Key="FontSize.Body">12</x:Double>
    <x:Double x:Key="FontSize.Medium">13</x:Double>
    <x:Double x:Key="FontSize.Subtitle">14</x:Double>
    <x:Double x:Key="FontSize.Title">18</x:Double>
    <x:Double x:Key="FontSize.Display">32</x:Double>
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~FontSizeTokenTests"`

Expected: PASS, all 9 cases.

- [ ] **Step 5: Confirm NoStrayFontSizeTests is unchanged (still 349 offenders)**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayFontSizeTests"`

Expected: FAIL, still 349 offenders (Tokens.axaml is excluded from the scan, so adding
tokens there doesn't change the count).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Tests/Theming/FontSizeTokenTests.cs DialogEditor.Avalonia.Shared/Resources/Tokens.axaml
git commit -m "feat(theming): add FontSize.* token layer to Tokens.axaml"
```

---

### Task 3: Migrate batch A — LegendWindow + small files (89 offenders)

**Files (all `Modify`):**
- `DialogEditor.Avalonia/Views/LegendWindow.axaml` (82 occurrences)
- `DialogEditor.Avalonia.Shared/ThemePickerView.axaml` (1)
- `DialogEditor.Avalonia.Shared/FocusHintBar.axaml` (1)
- `DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml` (2)
- `DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml` (3)

- [ ] **Step 1: Run the migration script**

```bash
for f in \
  "DialogEditor.Avalonia/Views/LegendWindow.axaml" \
  "DialogEditor.Avalonia.Shared/ThemePickerView.axaml" \
  "DialogEditor.Avalonia.Shared/FocusHintBar.axaml" \
  "DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml" \
  "DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml"
do
  sed -i -E \
    -e '/Property="FontSize"/ s/Value="8"/Value="{StaticResource FontSize.Micro}"/' \
    -e '/Property="FontSize"/ s/Value="9"/Value="{StaticResource FontSize.Caption}"/' \
    -e '/Property="FontSize"/ s/Value="10"/Value="{StaticResource FontSize.Small}"/' \
    -e '/Property="FontSize"/ s/Value="11"/Value="{StaticResource FontSize.Label}"/' \
    -e '/Property="FontSize"/ s/Value="12"/Value="{StaticResource FontSize.Body}"/' \
    -e '/Property="FontSize"/ s/Value="13"/Value="{StaticResource FontSize.Medium}"/' \
    -e '/Property="FontSize"/ s/Value="14"/Value="{StaticResource FontSize.Subtitle}"/' \
    -e '/Property="FontSize"/ s/Value="18"/Value="{StaticResource FontSize.Title}"/' \
    -e '/Property="FontSize"/ s/Value="32"/Value="{StaticResource FontSize.Display}"/' \
    -e 's/FontSize="8"/FontSize="{StaticResource FontSize.Micro}"/g' \
    -e 's/FontSize="9"/FontSize="{StaticResource FontSize.Caption}"/g' \
    -e 's/FontSize="10"/FontSize="{StaticResource FontSize.Small}"/g' \
    -e 's/FontSize="11"/FontSize="{StaticResource FontSize.Label}"/g' \
    -e 's/FontSize="12"/FontSize="{StaticResource FontSize.Body}"/g' \
    -e 's/FontSize="13"/FontSize="{StaticResource FontSize.Medium}"/g' \
    -e 's/FontSize="14"/FontSize="{StaticResource FontSize.Subtitle}"/g' \
    -e 's/FontSize="18"/FontSize="{StaticResource FontSize.Title}"/g' \
    -e 's/FontSize="32"/FontSize="{StaticResource FontSize.Display}"/g' \
    "$f"
done
```

- [ ] **Step 2: Build to catch resource-resolution errors**

Run: `dotnet build DialogEditor.slnx`

Expected: Build succeeds (0 errors). A typo like `FontSize.Bdoy` would fail XAML
compilation here.

- [ ] **Step 3: Verify offender count dropped to 260**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayFontSizeTests"`

Expected: FAIL, with exactly 260 offenders remaining (349 − 89).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/LegendWindow.axaml DialogEditor.Avalonia.Shared/ThemePickerView.axaml DialogEditor.Avalonia.Shared/FocusHintBar.axaml DialogEditor.Avalonia/Views/LanguageCodeDialog.axaml DialogEditor.Avalonia/Views/ForceDeleteDialog.axaml
git commit -m "refactor(theming): migrate LegendWindow and small dialogs to FontSize.* tokens (batch 1/6)"
```

---

### Task 4: Migrate batch B — DiffWindow + small dialogs (43 offenders)

**Files (all `Modify`):**
- `DialogEditor.Avalonia/Views/DiffWindow.axaml` (28)
- `DialogEditor.Avalonia/Views/TestModeOverlay.axaml` (3)
- `DialogEditor.Avalonia/Views/ChangelogWindow.axaml` (4)
- `DialogEditor.Avalonia/Views/BranchNameDialog.axaml` (4)
- `DialogEditor.Avalonia/Views/ConversationNameDialog.axaml` (4)

- [ ] **Step 1: Run the migration script**

```bash
for f in \
  "DialogEditor.Avalonia/Views/DiffWindow.axaml" \
  "DialogEditor.Avalonia/Views/TestModeOverlay.axaml" \
  "DialogEditor.Avalonia/Views/ChangelogWindow.axaml" \
  "DialogEditor.Avalonia/Views/BranchNameDialog.axaml" \
  "DialogEditor.Avalonia/Views/ConversationNameDialog.axaml"
do
  sed -i -E \
    -e '/Property="FontSize"/ s/Value="8"/Value="{StaticResource FontSize.Micro}"/' \
    -e '/Property="FontSize"/ s/Value="9"/Value="{StaticResource FontSize.Caption}"/' \
    -e '/Property="FontSize"/ s/Value="10"/Value="{StaticResource FontSize.Small}"/' \
    -e '/Property="FontSize"/ s/Value="11"/Value="{StaticResource FontSize.Label}"/' \
    -e '/Property="FontSize"/ s/Value="12"/Value="{StaticResource FontSize.Body}"/' \
    -e '/Property="FontSize"/ s/Value="13"/Value="{StaticResource FontSize.Medium}"/' \
    -e '/Property="FontSize"/ s/Value="14"/Value="{StaticResource FontSize.Subtitle}"/' \
    -e '/Property="FontSize"/ s/Value="18"/Value="{StaticResource FontSize.Title}"/' \
    -e '/Property="FontSize"/ s/Value="32"/Value="{StaticResource FontSize.Display}"/' \
    -e 's/FontSize="8"/FontSize="{StaticResource FontSize.Micro}"/g' \
    -e 's/FontSize="9"/FontSize="{StaticResource FontSize.Caption}"/g' \
    -e 's/FontSize="10"/FontSize="{StaticResource FontSize.Small}"/g' \
    -e 's/FontSize="11"/FontSize="{StaticResource FontSize.Label}"/g' \
    -e 's/FontSize="12"/FontSize="{StaticResource FontSize.Body}"/g' \
    -e 's/FontSize="13"/FontSize="{StaticResource FontSize.Medium}"/g' \
    -e 's/FontSize="14"/FontSize="{StaticResource FontSize.Subtitle}"/g' \
    -e 's/FontSize="18"/FontSize="{StaticResource FontSize.Title}"/g' \
    -e 's/FontSize="32"/FontSize="{StaticResource FontSize.Display}"/g' \
    "$f"
done
```

- [ ] **Step 2: Build to catch resource-resolution errors**

Run: `dotnet build DialogEditor.slnx`

Expected: Build succeeds (0 errors).

- [ ] **Step 3: Verify offender count dropped to 217**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayFontSizeTests"`

Expected: FAIL, with exactly 217 offenders remaining (260 − 43).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/DiffWindow.axaml DialogEditor.Avalonia/Views/TestModeOverlay.axaml DialogEditor.Avalonia/Views/ChangelogWindow.axaml DialogEditor.Avalonia/Views/BranchNameDialog.axaml DialogEditor.Avalonia/Views/ConversationNameDialog.axaml
git commit -m "refactor(theming): migrate DiffWindow and small dialogs to FontSize.* tokens (batch 2/6)"
```

---

### Task 5: Migrate batch C — NodeDetailView + conflict/find dialogs (53 offenders)

**Files (all `Modify`):**
- `DialogEditor.Avalonia/Views/NodeDetailView.axaml` (22)
- `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml` (16)
- `DialogEditor.Avalonia/Views/FindReplaceWindow.axaml` (7)
- `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml` (4)
- `DialogEditor.Avalonia/Views/UnsavedChangesDialog.axaml` (4)

- [ ] **Step 1: Run the migration script**

```bash
for f in \
  "DialogEditor.Avalonia/Views/NodeDetailView.axaml" \
  "DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml" \
  "DialogEditor.Avalonia/Views/FindReplaceWindow.axaml" \
  "DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml" \
  "DialogEditor.Avalonia/Views/UnsavedChangesDialog.axaml"
do
  sed -i -E \
    -e '/Property="FontSize"/ s/Value="8"/Value="{StaticResource FontSize.Micro}"/' \
    -e '/Property="FontSize"/ s/Value="9"/Value="{StaticResource FontSize.Caption}"/' \
    -e '/Property="FontSize"/ s/Value="10"/Value="{StaticResource FontSize.Small}"/' \
    -e '/Property="FontSize"/ s/Value="11"/Value="{StaticResource FontSize.Label}"/' \
    -e '/Property="FontSize"/ s/Value="12"/Value="{StaticResource FontSize.Body}"/' \
    -e '/Property="FontSize"/ s/Value="13"/Value="{StaticResource FontSize.Medium}"/' \
    -e '/Property="FontSize"/ s/Value="14"/Value="{StaticResource FontSize.Subtitle}"/' \
    -e '/Property="FontSize"/ s/Value="18"/Value="{StaticResource FontSize.Title}"/' \
    -e '/Property="FontSize"/ s/Value="32"/Value="{StaticResource FontSize.Display}"/' \
    -e 's/FontSize="8"/FontSize="{StaticResource FontSize.Micro}"/g' \
    -e 's/FontSize="9"/FontSize="{StaticResource FontSize.Caption}"/g' \
    -e 's/FontSize="10"/FontSize="{StaticResource FontSize.Small}"/g' \
    -e 's/FontSize="11"/FontSize="{StaticResource FontSize.Label}"/g' \
    -e 's/FontSize="12"/FontSize="{StaticResource FontSize.Body}"/g' \
    -e 's/FontSize="13"/FontSize="{StaticResource FontSize.Medium}"/g' \
    -e 's/FontSize="14"/FontSize="{StaticResource FontSize.Subtitle}"/g' \
    -e 's/FontSize="18"/FontSize="{StaticResource FontSize.Title}"/g' \
    -e 's/FontSize="32"/FontSize="{StaticResource FontSize.Display}"/g' \
    "$f"
done
```

- [ ] **Step 2: Build to catch resource-resolution errors**

Run: `dotnet build DialogEditor.slnx`

Expected: Build succeeds (0 errors).

- [ ] **Step 3: Verify offender count dropped to 164**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayFontSizeTests"`

Expected: FAIL, with exactly 164 offenders remaining (217 − 53).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/NodeDetailView.axaml DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml DialogEditor.Avalonia/Views/FindReplaceWindow.axaml DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml DialogEditor.Avalonia/Views/UnsavedChangesDialog.axaml
git commit -m "refactor(theming): migrate NodeDetailView and conflict/find dialogs to FontSize.* tokens (batch 3/6)"
```

---

### Task 6: Migrate batch D — batch-replace/condition editors + misc (53 offenders)

**Files (all `Modify`):**
- `DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml` (19)
- `DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml` (19)
- `DialogEditor.Avalonia/Views/GameBrowserView.axaml` (5)
- `DialogEditor.Avalonia/Views/SettingsWindow.axaml` (3)
- `DialogEditor.Avalonia/Views/HistoryWindow.axaml` (7)

- [ ] **Step 1: Run the migration script**

```bash
for f in \
  "DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml" \
  "DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml" \
  "DialogEditor.Avalonia/Views/GameBrowserView.axaml" \
  "DialogEditor.Avalonia/Views/SettingsWindow.axaml" \
  "DialogEditor.Avalonia/Views/HistoryWindow.axaml"
do
  sed -i -E \
    -e '/Property="FontSize"/ s/Value="8"/Value="{StaticResource FontSize.Micro}"/' \
    -e '/Property="FontSize"/ s/Value="9"/Value="{StaticResource FontSize.Caption}"/' \
    -e '/Property="FontSize"/ s/Value="10"/Value="{StaticResource FontSize.Small}"/' \
    -e '/Property="FontSize"/ s/Value="11"/Value="{StaticResource FontSize.Label}"/' \
    -e '/Property="FontSize"/ s/Value="12"/Value="{StaticResource FontSize.Body}"/' \
    -e '/Property="FontSize"/ s/Value="13"/Value="{StaticResource FontSize.Medium}"/' \
    -e '/Property="FontSize"/ s/Value="14"/Value="{StaticResource FontSize.Subtitle}"/' \
    -e '/Property="FontSize"/ s/Value="18"/Value="{StaticResource FontSize.Title}"/' \
    -e '/Property="FontSize"/ s/Value="32"/Value="{StaticResource FontSize.Display}"/' \
    -e 's/FontSize="8"/FontSize="{StaticResource FontSize.Micro}"/g' \
    -e 's/FontSize="9"/FontSize="{StaticResource FontSize.Caption}"/g' \
    -e 's/FontSize="10"/FontSize="{StaticResource FontSize.Small}"/g' \
    -e 's/FontSize="11"/FontSize="{StaticResource FontSize.Label}"/g' \
    -e 's/FontSize="12"/FontSize="{StaticResource FontSize.Body}"/g' \
    -e 's/FontSize="13"/FontSize="{StaticResource FontSize.Medium}"/g' \
    -e 's/FontSize="14"/FontSize="{StaticResource FontSize.Subtitle}"/g' \
    -e 's/FontSize="18"/FontSize="{StaticResource FontSize.Title}"/g' \
    -e 's/FontSize="32"/FontSize="{StaticResource FontSize.Display}"/g' \
    "$f"
done
```

- [ ] **Step 2: Build to catch resource-resolution errors**

Run: `dotnet build DialogEditor.slnx`

Expected: Build succeeds (0 errors).

- [ ] **Step 3: Verify offender count dropped to 111**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayFontSizeTests"`

Expected: FAIL, with exactly 111 offenders remaining (164 − 53).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml DialogEditor.Avalonia/Views/GameBrowserView.axaml DialogEditor.Avalonia/Views/SettingsWindow.axaml DialogEditor.Avalonia/Views/HistoryWindow.axaml
git commit -m "refactor(theming): migrate batch-replace/condition editors and misc views to FontSize.* tokens (batch 4/6)"
```

---

### Task 7: Migrate batch E — patch manager + diff help/blame/commit/about (57 offenders)

**Files (all `Modify`):**
- `DialogEditor.Avalonia.Shared/PatchManagerView.axaml` (16)
- `DialogEditor.Avalonia/Views/DiffHelpWindow.axaml` (14)
- `DialogEditor.Avalonia/Views/BlameWindow.axaml` (14)
- `DialogEditor.Avalonia/Views/CommitConsentDialog.axaml` (6)
- `DialogEditor.Avalonia/Views/AboutWindow.axaml` (7)

- [ ] **Step 1: Run the migration script**

```bash
for f in \
  "DialogEditor.Avalonia.Shared/PatchManagerView.axaml" \
  "DialogEditor.Avalonia/Views/DiffHelpWindow.axaml" \
  "DialogEditor.Avalonia/Views/BlameWindow.axaml" \
  "DialogEditor.Avalonia/Views/CommitConsentDialog.axaml" \
  "DialogEditor.Avalonia/Views/AboutWindow.axaml"
do
  sed -i -E \
    -e '/Property="FontSize"/ s/Value="8"/Value="{StaticResource FontSize.Micro}"/' \
    -e '/Property="FontSize"/ s/Value="9"/Value="{StaticResource FontSize.Caption}"/' \
    -e '/Property="FontSize"/ s/Value="10"/Value="{StaticResource FontSize.Small}"/' \
    -e '/Property="FontSize"/ s/Value="11"/Value="{StaticResource FontSize.Label}"/' \
    -e '/Property="FontSize"/ s/Value="12"/Value="{StaticResource FontSize.Body}"/' \
    -e '/Property="FontSize"/ s/Value="13"/Value="{StaticResource FontSize.Medium}"/' \
    -e '/Property="FontSize"/ s/Value="14"/Value="{StaticResource FontSize.Subtitle}"/' \
    -e '/Property="FontSize"/ s/Value="18"/Value="{StaticResource FontSize.Title}"/' \
    -e '/Property="FontSize"/ s/Value="32"/Value="{StaticResource FontSize.Display}"/' \
    -e 's/FontSize="8"/FontSize="{StaticResource FontSize.Micro}"/g' \
    -e 's/FontSize="9"/FontSize="{StaticResource FontSize.Caption}"/g' \
    -e 's/FontSize="10"/FontSize="{StaticResource FontSize.Small}"/g' \
    -e 's/FontSize="11"/FontSize="{StaticResource FontSize.Label}"/g' \
    -e 's/FontSize="12"/FontSize="{StaticResource FontSize.Body}"/g' \
    -e 's/FontSize="13"/FontSize="{StaticResource FontSize.Medium}"/g' \
    -e 's/FontSize="14"/FontSize="{StaticResource FontSize.Subtitle}"/g' \
    -e 's/FontSize="18"/FontSize="{StaticResource FontSize.Title}"/g' \
    -e 's/FontSize="32"/FontSize="{StaticResource FontSize.Display}"/g' \
    "$f"
done
```

- [ ] **Step 2: Build to catch resource-resolution errors**

Run: `dotnet build DialogEditor.slnx`

Expected: Build succeeds (0 errors).

- [ ] **Step 3: Verify offender count dropped to 54**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayFontSizeTests"`

Expected: FAIL, with exactly 54 offenders remaining (111 − 57).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia.Shared/PatchManagerView.axaml DialogEditor.Avalonia/Views/DiffHelpWindow.axaml DialogEditor.Avalonia/Views/BlameWindow.axaml DialogEditor.Avalonia/Views/CommitConsentDialog.axaml DialogEditor.Avalonia/Views/AboutWindow.axaml
git commit -m "refactor(theming): migrate PatchManagerView, DiffHelpWindow, BlameWindow, CommitConsentDialog, AboutWindow to FontSize.* tokens (batch 5/6)"
```

---

### Task 8: Migrate batch F — script editor, main window, remaining views (54 offenders) → GREEN

**Files (all `Modify`):**
- `DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml` (15)
- `DialogEditor.Avalonia/Views/MainWindow.axaml` (11)
- `DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml` (11)
- `DialogEditor.Avalonia/Views/ConversationView.axaml` (7)
- `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml` (10)

- [ ] **Step 1: Run the migration script**

```bash
for f in \
  "DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml" \
  "DialogEditor.Avalonia/Views/MainWindow.axaml" \
  "DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml" \
  "DialogEditor.Avalonia/Views/ConversationView.axaml" \
  "DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml"
do
  sed -i -E \
    -e '/Property="FontSize"/ s/Value="8"/Value="{StaticResource FontSize.Micro}"/' \
    -e '/Property="FontSize"/ s/Value="9"/Value="{StaticResource FontSize.Caption}"/' \
    -e '/Property="FontSize"/ s/Value="10"/Value="{StaticResource FontSize.Small}"/' \
    -e '/Property="FontSize"/ s/Value="11"/Value="{StaticResource FontSize.Label}"/' \
    -e '/Property="FontSize"/ s/Value="12"/Value="{StaticResource FontSize.Body}"/' \
    -e '/Property="FontSize"/ s/Value="13"/Value="{StaticResource FontSize.Medium}"/' \
    -e '/Property="FontSize"/ s/Value="14"/Value="{StaticResource FontSize.Subtitle}"/' \
    -e '/Property="FontSize"/ s/Value="18"/Value="{StaticResource FontSize.Title}"/' \
    -e '/Property="FontSize"/ s/Value="32"/Value="{StaticResource FontSize.Display}"/' \
    -e 's/FontSize="8"/FontSize="{StaticResource FontSize.Micro}"/g' \
    -e 's/FontSize="9"/FontSize="{StaticResource FontSize.Caption}"/g' \
    -e 's/FontSize="10"/FontSize="{StaticResource FontSize.Small}"/g' \
    -e 's/FontSize="11"/FontSize="{StaticResource FontSize.Label}"/g' \
    -e 's/FontSize="12"/FontSize="{StaticResource FontSize.Body}"/g' \
    -e 's/FontSize="13"/FontSize="{StaticResource FontSize.Medium}"/g' \
    -e 's/FontSize="14"/FontSize="{StaticResource FontSize.Subtitle}"/g' \
    -e 's/FontSize="18"/FontSize="{StaticResource FontSize.Title}"/g' \
    -e 's/FontSize="32"/FontSize="{StaticResource FontSize.Display}"/g' \
    "$f"
done
```

- [ ] **Step 2: Build to catch resource-resolution errors**

Run: `dotnet build DialogEditor.slnx`

Expected: Build succeeds (0 errors).

- [ ] **Step 3: Verify NoStrayFontSizeTests is now GREEN (0 offenders)**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj --filter "FullyQualifiedName~NoStrayFontSizeTests"`

Expected: PASS, 0 offenders (54 − 54 = 0).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml
git commit -m "refactor(theming): migrate ScriptEditorWindow, MainWindow, and remaining views to FontSize.* tokens (batch 6/6, GREEN)"
```

---

### Task 9: Final verification and Gaps.md update

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test DialogEditor.Tests/DialogEditor.Tests.csproj`

Expected: PASS, 1374 tests (1364 existing + 1 `NoFontSizeLiteralsOutsideTokens` + 9
`FontSizeTokenTests.TokenResolvesToExpectedValue` theory cases), 0 failures.

- [ ] **Step 2: Update Gaps.md item 6**

In `Gaps.md`, replace item 6 (lines 289–294):

```markdown
6. **Tiny fixed font sizes, no text scaling.** ~127 instances of 9–11px fonts across 19
   views; `NodeDetailView` group headers are **FontSize 8**. There is no UI-scale setting,
   and fixed-size windows (`SettingsWindow` is `CanResize="False" Height="220"`) will clip
   under OS text scaling. Opportunity: move font sizes into `Tokens.axaml` as semantic
   tokens (`FontSize.Caption`, `FontSize.Body`, …) — the Layer 0 token infrastructure and
   its enforcement-test pattern already exist — then add a scale factor in Settings.
```

with:

```markdown
6. **No UI-scale setting; fixed-size windows will clip under OS text scaling.**
   ✅ Part A IMPLEMENTED (2026-06-14): all 349 literal `FontSize` values across 30
   `.axaml` files now bind a 9-entry `FontSize.*` token layer in `Tokens.axaml`
   (`NoStrayFontSizeTests` enforces this — see
   `docs/superpowers/specs/2026-06-14-fontsize-token-foundation-design.md`). Remaining
   (part B): there is still no UI-scale-factor setting, and fixed-size windows
   (`SettingsWindow` is `CanResize="False" Height="220"`) will clip under OS text
   scaling. Opportunity: add a scale factor in Settings that multiplies the
   `FontSize.*` tokens, and make fixed-height windows resizable or auto-sizing.
```

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark a11y item 6 part A (FontSize token foundation) implemented"
```

---

## Self-Review Notes

- **Spec coverage:** Token definitions (Task 2), enforcement test (Task 1),
  `FontSizeTokenTests` pinning (Task 2), full migration of all 30 files / 349
  occurrences (Tasks 3–8), final full-suite verification and Gaps.md split (Task 9). All
  spec sections covered.
- **`.worktrees/` exclusion:** `NoStrayFontSizeTests` excludes `.worktrees/` (gitignored,
  but `Directory.EnumerateFiles` doesn't honour `.gitignore`) — without this, the
  unrelated `focus-hint-bar` worktree's un-migrated `.axaml` copies would inflate the RED
  count and prevent the test from ever reaching 0.
- **Idempotency:** the migration script is safe to re-run if a batch is interrupted
  partway through.
- **Batch totals:** 89 + 43 + 53 + 53 + 57 + 54 = 349, across 5+5+5+5+5+5 = 30 files —
  matches the spec's full migration surface exactly.
