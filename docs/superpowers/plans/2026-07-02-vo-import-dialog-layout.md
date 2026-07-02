# VO Import Dialog Layout Consolidation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace `VoImportDialog`'s two stacked sections (Current voice-over / Import) with a single Current → New grid, one row per variant, per `docs/superpowers/specs/2026-07-02-vo-import-dialog-layout-design.md`.

**Architecture:** View-layer-only change. `VoImportDialog.axaml` is restructured into a three-column grid (`label | Current | New`); the constructor in `VoImportDialog.axaml.cs` collapses the Current column and narrows the window when no current file exists on disk. All play/browse/clear/quality/OK logic, `IVoImporter`, `VoImportPaths`, `IVoAudioPlayer`, `NodeDetailViewModel`, and `BatchVoImportDialog` are untouched.

**Tech Stack:** Avalonia 11 XAML, C# code-behind, `DynamicResource` string localisation, xUnit test suite (runs serially — do not parallelise).

## Global Constraints

- No user-visible text hard-coded in XAML or C# — every string via `Strings.axaml` keys (`DynamicResource` in XAML, `Loc.Get(...)` in C#).
- Every interactive control carries `ToolTip.Tip`; icon-only buttons also carry `AutomationProperties.Name`. Exception: plain OK/Cancel buttons.
- Every `<Window>` carries `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"` (already present — do not remove).
- Caught exceptions must be logged via `AppLog.Error/Warn`; `OperationCanceledException` swallowed silently. (No new catch blocks are expected in this plan.)
- TDD applies to non-trivial logic. This change is XAML layout plus constructor view-wiring with no ViewModel surface; the existing `NodeDetailViewModel` import tests must stay green, and no new unit tests are required. Verification is build + full test suite + the manual checklist in Task 2.
- `CHANGELOG.md` is frozen — do not touch it.
- Embed design reasoning as comments in the .axaml/.cs files themselves, not only in the spec.

---

### Task 1: Rework `VoImportDialog` into the Current → New grid

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (VO import block, currently lines ~1151–1188)
- Modify: `DialogEditor.Avalonia/Views/VoImportDialog.axaml` (full-body restructure)
- Modify: `DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs` (constructor + one `using`)

**Interfaces:**
- Consumes: `VoImportPaths` (`PrimaryDestinationPath: string`, `FemDestinationPath: string?`), `IVoImporter.IsWwiseAvailable`, `IVoAudioPlayer` — all unchanged.
- Produces: same public surface as today — `VoImportDialog(IVoImporter, VoImportPaths, IVoAudioPlayer)` and `Result: VoImportDialogResult?`. Callers (`MainWindow.axaml.cs`) need no changes. New XAML control names consumed by the code-behind: `CurrentColumn` (ColumnDefinition), `CurrentHeader`, `NewHeader`, `CurrentPrimaryCell`, `FemRowLabel`, `CurrentFemCell`, `FemSourceCell`. Removed names: `CurrentVoSection`, `CurrentFemRow`, `FemSourceRow`.

- [ ] **Step 1: Update `Strings.axaml`**

In the `<!-- ── VO import ── -->` block, **delete** these two lines (the section headers no longer exist):

```xml
    <sys:String x:Key="VoImport_CurrentSection">Current voice-over</sys:String>
    <sys:String x:Key="VoImport_ImportSection">Import voice-over</sys:String>
```

and **add** these five keys at the end of the same block (after `AutomationName_VoCurrentPlay_Fem`):

```xml
    <sys:String x:Key="VoImport_ColCurrent">Current</sys:String>
    <sys:String x:Key="VoImport_ColNew">New</sys:String>
    <!-- Shown in the Current column when the variant has a resolvable path but no file on disk yet -->
    <sys:String x:Key="VoImport_NoCurrentFile">—</sys:String>
    <sys:String x:Key="AutomationName_VoImportClear_Primary">Clear chosen primary file</sys:String>
    <sys:String x:Key="AutomationName_VoImportClear_Fem">Clear chosen female file</sys:String>
```

Note: `VoImport_Clear` (the ✕ glyph), `Button_Cancel`, and all other VO import keys already exist and are reused — do not duplicate them.

- [ ] **Step 2: Replace the body of `VoImportDialog.axaml`**

Replace the entire file content with:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.VoImportDialog"
        Title="{DynamicResource VoImport_DialogTitle}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="500"
        MinWidth="420"
        WindowStartupLocation="CenterOwner"
        CanResize="False"
        SizeToContent="Height">

    <!-- Layout (2026-07-02 spec): a single grid, one row per variant, columns reading
         left-to-right as a replacement — Current file → New file. This replaces the
         earlier action-grouped layout (a "Current voice-over" section above an "Import"
         section) which split one variant's state across two places. The Current column
         and the header row exist only when at least one current file is on disk at open
         time; the constructor collapses them (and keeps the 500px width) otherwise, so
         the common first-import case stays a simple one-column picker. Width is fixed
         per-open — files cannot appear mid-dialog. -->
    <StackPanel Margin="16">

        <TextBlock Text="{DynamicResource VoImport_ImportInstruction}"
                   FontStyle="Italic"
                   Foreground="{DynamicResource Brush.Text.Secondary}"
                   FontSize="{DynamicResource FontSize.Small}"
                   TextWrapping="Wrap" Margin="0,0,0,10"/>

        <Grid RowDefinitions="Auto,Auto,Auto" Margin="0,0,0,12">
            <Grid.ColumnDefinitions>
                <ColumnDefinition Width="68"/>
                <ColumnDefinition x:Name="CurrentColumn" Width="*"/>
                <ColumnDefinition Width="1.35*"/>
            </Grid.ColumnDefinitions>

            <!-- Header row — visible only when the Current column is (see constructor) -->
            <TextBlock x:Name="CurrentHeader" Grid.Row="0" Grid.Column="1"
                       Text="{DynamicResource VoImport_ColCurrent}"
                       FontWeight="SemiBold"
                       FontSize="{DynamicResource FontSize.Small}"
                       Foreground="{DynamicResource Brush.Text.Secondary}"
                       Margin="0,0,0,4"/>
            <TextBlock x:Name="NewHeader" Grid.Row="0" Grid.Column="2"
                       Text="{DynamicResource VoImport_ColNew}"
                       FontWeight="SemiBold"
                       FontSize="{DynamicResource FontSize.Small}"
                       Foreground="{DynamicResource Brush.Text.Secondary}"
                       Margin="12,0,0,4"/>

            <!-- ── Primary row ── -->
            <TextBlock Grid.Row="1" Grid.Column="0"
                       Text="{DynamicResource VoImport_PrimaryLabel}"
                       VerticalAlignment="Center" Margin="0,0,0,8"/>

            <Grid x:Name="CurrentPrimaryCell" Grid.Row="1" Grid.Column="1"
                  ColumnDefinitions="Auto,*" Margin="0,0,0,8">
                <Button    Grid.Column="0" x:Name="PlayCurrentPrimaryButton"
                           Content="▶"
                           Click="PlayCurrentPrimary_Click"
                           ToolTip.Tip="{DynamicResource ToolTip_VoCurrentPlay_Primary}"
                           AutomationProperties.Name="{DynamicResource AutomationName_VoCurrentPlay_Primary}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_VoCurrentPlay_Primary}"
                           Margin="0,0,6,0" Padding="6,2"/>
                <TextBlock Grid.Column="1" x:Name="CurrentPrimaryLabel"
                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                           Foreground="{DynamicResource Brush.Text.Secondary}"/>
            </Grid>

            <Grid Grid.Row="1" Grid.Column="2"
                  ColumnDefinitions="Auto,*,Auto,Auto" Margin="12,0,0,8">
                <Button    Grid.Column="0" x:Name="PlaySourcePrimaryButton"
                           Content="▶"
                           Click="PlaySourcePrimary_Click"
                           IsVisible="False"
                           ToolTip.Tip="{DynamicResource ToolTip_VoPreviewPlay_Primary}"
                           AutomationProperties.Name="{DynamicResource AutomationName_VoPreviewPlay_Primary}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_VoPreviewPlay_Primary}"
                           Margin="0,0,6,0" Padding="6,2"/>
                <TextBlock Grid.Column="1" x:Name="PrimarySourceLabel"
                           Text="{DynamicResource VoImport_NoFileChosen}"
                           Foreground="{DynamicResource Brush.Text.Secondary}"
                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                <Button    Grid.Column="2" Content="{DynamicResource VoImport_Browse}"
                           Click="BrowsePrimary_Click"
                           ToolTip.Tip="{DynamicResource ToolTip_VoImportBrowse_Primary}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportBrowse_Primary}"
                           Margin="6,0,4,0"/>
                <Button    Grid.Column="3" x:Name="ClearPrimaryButton"
                           Content="{DynamicResource VoImport_Clear}"
                           Click="ClearPrimary_Click"
                           IsVisible="False"
                           ToolTip.Tip="{DynamicResource ToolTip_VoImportClear_Primary}"
                           AutomationProperties.Name="{DynamicResource AutomationName_VoImportClear_Primary}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportClear_Primary}"/>
            </Grid>

            <!-- ── Female row — all three cells hidden when the node has no female text ── -->
            <TextBlock x:Name="FemRowLabel" Grid.Row="2" Grid.Column="0"
                       Text="{DynamicResource VoImport_FemaleLabel}"
                       IsVisible="False"
                       VerticalAlignment="Center"/>

            <Grid x:Name="CurrentFemCell" Grid.Row="2" Grid.Column="1"
                  ColumnDefinitions="Auto,*" IsVisible="False">
                <Button    Grid.Column="0" x:Name="PlayCurrentFemButton"
                           Content="▶"
                           Click="PlayCurrentFem_Click"
                           ToolTip.Tip="{DynamicResource ToolTip_VoCurrentPlay_Fem}"
                           AutomationProperties.Name="{DynamicResource AutomationName_VoCurrentPlay_Fem}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_VoCurrentPlay_Fem}"
                           Margin="0,0,6,0" Padding="6,2"/>
                <TextBlock Grid.Column="1" x:Name="CurrentFemLabel"
                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis"
                           Foreground="{DynamicResource Brush.Text.Secondary}"/>
            </Grid>

            <Grid x:Name="FemSourceCell" Grid.Row="2" Grid.Column="2"
                  ColumnDefinitions="Auto,*,Auto,Auto" IsVisible="False" Margin="12,0,0,0">
                <Button    Grid.Column="0" x:Name="PlaySourceFemButton"
                           Content="▶"
                           Click="PlaySourceFem_Click"
                           IsVisible="False"
                           ToolTip.Tip="{DynamicResource ToolTip_VoPreviewPlay_Fem}"
                           AutomationProperties.Name="{DynamicResource AutomationName_VoPreviewPlay_Fem}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_VoPreviewPlay_Fem}"
                           Margin="0,0,6,0" Padding="6,2"/>
                <TextBlock Grid.Column="1" x:Name="FemSourceLabel"
                           Text="{DynamicResource VoImport_NoFileChosen}"
                           Foreground="{DynamicResource Brush.Text.Secondary}"
                           VerticalAlignment="Center" TextTrimming="CharacterEllipsis"/>
                <Button    Grid.Column="2" Content="{DynamicResource VoImport_Browse}"
                           Click="BrowseFem_Click"
                           ToolTip.Tip="{DynamicResource ToolTip_VoImportBrowse_Fem}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportBrowse_Fem}"
                           Margin="6,0,4,0"/>
                <Button    Grid.Column="3" x:Name="ClearFemButton"
                           Content="{DynamicResource VoImport_Clear}"
                           Click="ClearFem_Click"
                           IsVisible="False"
                           ToolTip.Tip="{DynamicResource ToolTip_VoImportClear_Fem}"
                           AutomationProperties.Name="{DynamicResource AutomationName_VoImportClear_Fem}"
                           AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportClear_Fem}"/>
            </Grid>
        </Grid>

        <!-- Encoding quality — only visible when a .wav source file is selected -->
        <Grid ColumnDefinitions="68,*" Margin="0,0,0,12"
              x:Name="QualityPanel" IsVisible="False">
            <TextBlock Grid.Column="0" Text="{DynamicResource VoImport_QualityLabel}"
                       VerticalAlignment="Center"/>
            <StackPanel Grid.Column="1" Orientation="Horizontal" Spacing="16">
                <RadioButton x:Name="QualityLow"
                             Content="{DynamicResource VoImport_Quality_Low}"
                             GroupName="WemQuality"
                             Checked="QualityLow_Checked"
                             ToolTip.Tip="{DynamicResource ToolTip_VoQuality}"
                             AutomationProperties.HelpText="{DynamicResource ToolTip_VoQuality}"/>
                <RadioButton x:Name="QualityMedium"
                             Content="{DynamicResource VoImport_Quality_Medium}"
                             GroupName="WemQuality"
                             IsChecked="True"
                             Checked="QualityMedium_Checked"
                             ToolTip.Tip="{DynamicResource ToolTip_VoQuality}"
                             AutomationProperties.HelpText="{DynamicResource ToolTip_VoQuality}"/>
                <RadioButton x:Name="QualityHigh"
                             Content="{DynamicResource VoImport_Quality_High}"
                             GroupName="WemQuality"
                             Checked="QualityHigh_Checked"
                             ToolTip.Tip="{DynamicResource ToolTip_VoQuality}"
                             AutomationProperties.HelpText="{DynamicResource ToolTip_VoQuality}"/>
            </StackPanel>
        </Grid>

        <!-- Wwise warning — only visible when a .wav is selected and Wwise is absent -->
        <StackPanel x:Name="WwiseWarningPanel" IsVisible="False" Margin="0,0,0,12">
            <TextBlock Text="{DynamicResource VoImport_NoWwise}"
                       Foreground="{DynamicResource Brush.Severity.Warning}"
                       FontSize="{DynamicResource FontSize.Small}"/>
            <Button    x:Name="DownloadWwiseButton"
                       Content="{DynamicResource VoImport_DownloadWwise}"
                       Click="DownloadWwise_Click"
                       HorizontalAlignment="Left"
                       ToolTip.Tip="{DynamicResource ToolTip_VoImportDownloadWwise}"
                       AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportDownloadWwise}"
                       Margin="0,4,0,0"/>
        </StackPanel>

        <!-- OK / Cancel -->
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button Content="{DynamicResource Button_Cancel}" Click="Cancel_Click"/>
            <Button x:Name="OkButton"
                    Content="{DynamicResource VoImport_ImportButton}"
                    Click="Ok_Click" IsDefault="True" IsEnabled="False"/>
        </StackPanel>

    </StackPanel>
</Window>
```

Changes worth noting against the old file: the two section headers, the `Separator`, and the `CurrentVoSection`/`CurrentFemRow`/`FemSourceRow` containers are gone; the label column narrows 80 → 68; the New-cell contents are the old source-slot controls with unchanged names and handlers; the Cancel button now uses the existing `Button_Cancel` key instead of hard-coded `Content="Cancel"`; both ✕ buttons gain `AutomationProperties.Name`.

- [ ] **Step 3: Update the code-behind constructor**

In `DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs`:

Add the `using` for `GridLength` at the top of the file:

```csharp
using Avalonia;
```

Then replace the section of the parameterised constructor from `// ── Current voice-over section ──` down to (and including) the `FemSourceRow.IsVisible = true;` block with:

```csharp
        // ── Current → New grid state (2026-07-02 layout spec) ──
        // The Current column exists only when a current file is on disk at open
        // time; visibility and window width are fixed per-open because files
        // cannot appear while the dialog is up.
        var primaryExists = File.Exists(paths.PrimaryDestinationPath);
        var femExists     = paths.FemDestinationPath is not null
                         && File.Exists(paths.FemDestinationPath);
        var hasCurrent    = primaryExists || femExists;

        if (hasCurrent)
        {
            // Three columns need more room than the collapsed 500px default.
            Width = 640;
            CurrentPrimaryLabel.Text = primaryExists
                ? Path.GetFileName(paths.PrimaryDestinationPath)
                : Loc.Get("VoImport_NoCurrentFile");
            PlayCurrentPrimaryButton.IsVisible = primaryExists;
        }
        else
        {
            CurrentColumn.Width          = new GridLength(0);
            CurrentHeader.IsVisible      = false;
            NewHeader.IsVisible          = false;
            CurrentPrimaryCell.IsVisible = false;
        }

        // Female row visible only when the node has female text.
        if (paths.FemDestinationPath is not null)
        {
            FemRowLabel.IsVisible   = true;
            FemSourceCell.IsVisible = true;
            if (hasCurrent)
            {
                CurrentFemCell.IsVisible = true;
                CurrentFemLabel.Text = femExists
                    ? Path.GetFileName(paths.FemDestinationPath)
                    : Loc.Get("VoImport_NoCurrentFile");
                PlayCurrentFemButton.IsVisible = femExists;
            }
        }
```

Everything below (play handlers, quality, browse/clear, OK/Cancel, helpers) is unchanged — the control names they reference (`PlayCurrentPrimaryButton`, `PrimarySourceLabel`, `ClearPrimaryButton`, etc.) all still exist in the new XAML.

- [ ] **Step 4: Build and check for orphaned keys**

Run from the repo root:

```
dotnet build
```

Expected: build succeeds with no `AVLN` XAML-compiler errors (an unknown `x:Name` referenced from code-behind would fail here).

Then confirm the removed keys are referenced nowhere:

```
grep -rn "VoImport_CurrentSection\|VoImport_ImportSection" --include="*.axaml" --include="*.cs" .
```

Expected: no matches.

- [ ] **Step 5: Run the full test suite**

```
dotnet test
```

Expected: all tests pass (the suite runs serially by configuration — do not add parallelisation flags). `NodeDetailViewModelImportTests` in particular must be green, proving the ViewModel surface is untouched.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml "DialogEditor.Avalonia/Views/VoImportDialog.axaml" "DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs"
git commit -m "refactor(vo): consolidate Import Voice-Over dialog into Current->New grid

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Manual verification of the reworked dialog

**Files:** none modified (fix-and-recommit in Task 1's files only if a check fails).

**Interfaces:** n/a — runs the built app.

- [ ] **Step 1: Launch the app against a project with VO files**

```
dotnet run --project DialogEditor.Avalonia
```

Open a saved project whose `_vo/` folder contains at least one imported file, select the corresponding node, and open Import Voice-Over via the 🎤 button.

- [ ] **Step 2: Walk the spec checklist**

- [ ] Node with existing primary + fem VO → three-column grid, Current/New headers visible, both Current ▶ buttons play, window opens ~640px wide
- [ ] Node with no VO on disk → Current column and headers absent, window 500px, dialog reads as a simple picker
- [ ] Node with fem path but only a primary file on disk → fem Current cell shows the muted "—" with no play button
- [ ] Node without female text → Female row absent entirely
- [ ] Pick a `.wav` → preview ▶ appears, quality radios appear; with Wwise absent the warning shows and Import disables
- [ ] ✕ clears the picked file, hides its preview ▶, and stops playback if that slot was playing
- [ ] All four ▶ buttons toggle ▶/■ and pre-empt each other through the shared player
- [ ] Import with a valid primary → dialog closes, VO status row refreshes; Cancel → no changes
- [ ] Tooltips present on every ▶, Browse…, ✕, quality radio, and the Download Wwise button

- [ ] **Step 3: Report results**

If every box ticks, the feature is done — no further commit needed. Any failure: fix in Task 1's three files, re-run `dotnet build` + `dotnet test`, re-verify the failed item, and commit the fix with a `fix(vo):` message.
