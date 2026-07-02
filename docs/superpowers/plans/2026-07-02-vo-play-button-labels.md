# VO Play Button Variant Labels Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the detail panel's two VO play buttons visually distinguishable by adding a localised variant letter to each button face (`▶ M` / `▶ F`, `■` while playing), per `docs/superpowers/specs/2026-07-02-vo-play-button-labels-design.md`.

**Architecture:** The `NodeDetailViewModel` properties `PlayPrimaryGlyph`/`PlayFemGlyph` are renamed to `PlayPrimaryLabel`/`PlayFemLabel` and now compose glyph + `Loc.Get(...)` letter, keeping the button face's single source of truth in the ViewModel where it is unit-testable. The XAML bindings rename; nothing else in the view changes.

**Tech Stack:** Avalonia 11, CommunityToolkit.Mvvm ViewModel, `Loc.Get` string localisation, xUnit (suite runs serially — do not parallelise).

## Global Constraints

- No user-visible text hard-coded in XAML or C# — the M/F letters are `Strings.axaml` keys read via `Loc.Get(...)`.
- Strict red/green TDD: the new failing tests are written and observed failing before any implementation change.
- Tooltips/AutomationProperties on the buttons are already present and must not be removed.
- `CHANGELOG.md` is frozen — do not touch it.

---

### Task 1: Variant letters on the detail-panel play buttons

**Files:**
- Modify: `DialogEditor.Tests/ViewModels/NodeDetailViewModelPlaybackTests.cs` (new tests + 4 renamed assertions at lines 112, 122, 197, 207)
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs:270-271` (property rename + composition) and `:318-319` (`OnPropertyChanged` names)
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml:220,226` (binding renames)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (two new keys in the VO import block)

**Interfaces:**
- Consumes: existing `Loc.Get(string key)` (in `DialogEditor.ViewModels.Resources`, already imported by the ViewModel), existing `Playing` enum and `_currentlyPlaying` field.
- Produces: `public string PlayPrimaryLabel` and `public string PlayFemLabel` on `NodeDetailViewModel` — bound by `NodeDetailView.axaml`. The old `PlayPrimaryGlyph`/`PlayFemGlyph` names cease to exist.

- [ ] **Step 1: Write the failing tests**

In `DialogEditor.Tests/ViewModels/NodeDetailViewModelPlaybackTests.cs`, add after the `PlayFemCommand_WhenAlreadyPlayingFem_Stops` test (ends line 186):

```csharp
    // ── Variant letter labels (2026-07-02 spec) ──────────────────────────
    // The two play buttons are otherwise identical; each face carries a
    // localised variant letter so users can tell primary (M) from female (F).

    [Fact]
    public void PlayPrimaryLabel_Idle_ShowsPlayGlyphWithMaleLetter()
    {
        PlantAndLoad(withFem: true);
        Assert.Equal("▶ M", _vm.PlayPrimaryLabel);
        Assert.Equal("▶ F", _vm.PlayFemLabel);
    }

    [Fact]
    public void PlayPrimaryLabel_WhilePlayingPrimary_ShowsStopGlyph_FemUnaffected()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.Equal("■ M", _vm.PlayPrimaryLabel);
        Assert.Equal("▶ F", _vm.PlayFemLabel);
    }

    [Fact]
    public void PlayFemLabel_WhilePlayingFem_ShowsStopGlyph_PrimaryUnaffected()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayFemCommand.Execute(null);
        Assert.Equal("■ F", _vm.PlayFemLabel);
        Assert.Equal("▶ M", _vm.PlayPrimaryLabel);
    }
```

Also rename the four existing assertions that use the old property names — the expected values gain the letter:

| Line | Old | New |
|------|-----|-----|
| 112 | `Assert.Equal("■", _vm.PlayPrimaryGlyph);` | `Assert.Equal("■ M", _vm.PlayPrimaryLabel);` |
| 122 | `Assert.Equal("▶", _vm.PlayPrimaryGlyph);` | `Assert.Equal("▶ M", _vm.PlayPrimaryLabel);` |
| 197 | `Assert.Equal("▶", _vm.PlayPrimaryGlyph);` | `Assert.Equal("▶ M", _vm.PlayPrimaryLabel);` |
| 207 | `Assert.Equal("▶", _vm.PlayFemGlyph);` | `Assert.Equal("▶ F", _vm.PlayFemLabel);` |

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NodeDetailViewModelPlaybackTests"`
Expected: **build failure** — `error CS1061: 'NodeDetailViewModel' does not contain a definition for 'PlayPrimaryLabel'`. A compile-error red is the correct red for a rename.

- [ ] **Step 3: Implement**

`DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` — replace lines 270–271:

```csharp
    // Button faces carry a localised variant letter (M/F) because the two play
    // buttons sit side by side and are otherwise identical (2026-07-02 spec).
    public string PlayPrimaryLabel => (_currentlyPlaying == Playing.Primary ? "■ " : "▶ ")
                                      + Loc.Get("VoPlay_MaleLetter");
    public string PlayFemLabel     => (_currentlyPlaying == Playing.Female  ? "■ " : "▶ ")
                                      + Loc.Get("VoPlay_FemaleLetter");
```

and in `SetPlaying` (lines 318–319) rename the notifications:

```csharp
        OnPropertyChanged(nameof(PlayPrimaryLabel));
        OnPropertyChanged(nameof(PlayFemLabel));
```

`DialogEditor.Avalonia/Views/NodeDetailView.axaml` — the two `Content` bindings:

```xml
                    <Button Content="{Binding PlayPrimaryLabel}"
```
(line 220, was `PlayPrimaryGlyph`) and

```xml
                    <Button Content="{Binding PlayFemLabel}"
```
(line 226, was `PlayFemGlyph`). All other attributes unchanged.

`DialogEditor.Avalonia/Resources/Strings.axaml` — add at the end of the `<!-- ── VO import ── -->` block (after `AutomationName_VoImportClear_Fem`):

```xml
    <!-- Variant letters shown inside the detail-panel play buttons (▶ M / ▶ F) -->
    <sys:String x:Key="VoPlay_MaleLetter">M</sys:String>
    <sys:String x:Key="VoPlay_FemaleLetter">F</sys:String>
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NodeDetailViewModelPlaybackTests"`
Expected: PASS, 0 failed.

Then confirm no stale references to the old names anywhere:

```
grep -rn "PlayPrimaryGlyph\|PlayFemGlyph" --include="*.cs" --include="*.axaml" .
```
Expected: no matches.

- [ ] **Step 5: Run the full suite**

Run: `dotnet test --nologo`
Expected: all tests pass (serial run — no parallelisation flags).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs "DialogEditor.Avalonia/Views/NodeDetailView.axaml" "DialogEditor.Avalonia/Resources/Strings.axaml" DialogEditor.Tests/ViewModels/NodeDetailViewModelPlaybackTests.cs
git commit -m "feat(vo): variant letters on detail-panel play buttons (M/F)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [ ] **Step 7: Visual check**

Run `dotnet run --project DialogEditor.Avalonia`, select a node with VO (with female variant if available): the status row shows `▶ M` (and `▶ F`), the playing one flips to `■`. This is a quick sanity look, not the full import-dialog checklist.
