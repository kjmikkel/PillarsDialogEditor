# VO Preview and WAV→WEM Encoding Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add in-dialog audio preview to `VoImportDialog` and implement the deferred WAV→WEM encoding pipeline with Low/Medium/High quality presets.

**Architecture:** Feature 1 (preview) injects the existing shared `VoAudioPlayer` into `VoImportDialog` and extends it to handle `.wav` files without vgmstream. Feature 2 (encoding) adds a `WemQuality` enum to `IVoImporter.cs`, bundles a minimal `template.wproj` Wwise project, implements `EncodeWavToWemAsync` in `VoImporter`, and adds a quality radio-button row to the dialog.

**Tech Stack:** C# 12, .NET 8, Avalonia 11, NAudio (already a dependency), `System.IO.Compression`, xUnit.

## Global Constraints

- No user-visible string hard-coded inline — all in `Strings.axaml` via `{DynamicResource}`.
- Every new interactive control must have `ToolTip.Tip` and `AutomationProperties.HelpText`. Icon-only buttons also need `AutomationProperties.Name`.
- Every caught exception (except `OperationCanceledException`) must be logged via `AppLog.Error(...)` before any status update.
- Tests live in `DialogEditor.Tests`, mirroring project structure.
- Run `dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false` (serial — global state race in `AppSettings`/`Loc`).
- TDD: failing test before implementation for every non-trivial method.

## File Map

| File | Change |
|------|--------|
| `DialogEditor.Avalonia/Audio/VoAudioPlayer.cs` | Add `.wav` short-circuit in `Play()` |
| `DialogEditor.Tests/Audio/VoAudioPlayerTests.cs` | **Create** — `.wav` playback test |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | Add preview + quality string keys |
| `DialogEditor.Avalonia/Views/VoImportDialog.axaml` | Add play buttons + quality row |
| `DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs` | Add player, `PlayingSlot` enum, quality field |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Pass `audioPlayer` to dialog constructor |
| `DialogEditor.ViewModels/Services/IVoImporter.cs` | Add `WemQuality` enum, update records |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Pass `selection.Quality` in `ImportVo` |
| `DialogEditor.Avalonia/Assets/Wwise/template.wproj` | **Create** — bundled Wwise project template |
| `DialogEditor.Avalonia/Audio/VoImporter.cs` | Implement encoding pipeline |
| `DialogEditor.Tests/Audio/VoImporterTests.cs` | **Create** — `GenerateWsourcesXml` tests |

`<AvaloniaResource Include="Assets\**"/>` in the .csproj already covers `Assets/Wwise/template.wproj` — no .csproj edit needed.

---

### Task 1: VoAudioPlayer `.wav` short-circuit + test

**Files:**
- Modify: `DialogEditor.Avalonia/Audio/VoAudioPlayer.cs`
- Create: `DialogEditor.Tests/Audio/VoAudioPlayerTests.cs`

**Interfaces:**
- Produces: `VoAudioPlayer.Play(string path)` now handles `.wav` without vgmstream — consumed by Task 2.

- [ ] **Step 1: Write the failing test**

Create `DialogEditor.Tests/Audio/VoAudioPlayerTests.cs`:

```csharp
using DialogEditor.Avalonia.Audio;

namespace DialogEditor.Tests.Audio;

public class VoAudioPlayerTests : IDisposable
{
    private readonly string _wavPath;

    public VoAudioPlayerTests()
    {
        // Write a minimal valid PCM WAV (44-byte RIFF header, 0 audio frames).
        _wavPath = Path.Combine(Path.GetTempPath(), $"votest_{Guid.NewGuid():N}.wav");
        WriteMinimalWav(_wavPath);
    }

    public void Dispose()
    {
        try { File.Delete(_wavPath); } catch { }
    }

    // Before the fix, Play() on a .wav file always attempts to spawn vgmstream-cli.exe.
    // When vgmstream is absent (IsAvailable == false), the Process.Start inside Task.Run
    // throws Win32Exception, which is caught and logged — so the synchronous call doesn't
    // throw, but PlaybackStopped never fires because the decode "failed".
    //
    // After the fix, .wav files skip vgmstream entirely and go directly to NAudio.
    // NAudio can initialise AudioFileReader on a zero-frame WAV without throwing.
    // PlaybackStopped fires almost immediately (empty file = instant playback end).
    //
    // The test distinguishes the two cases: only after the fix does PlaybackStopped fire
    // for a .wav when vgmstream is absent.
    [Fact]
    public async Task Play_WavFile_FiresPlaybackStopped_WhenVgmstreamAbsent()
    {
        var player = new VoAudioPlayer();
        if (player.IsAvailable)
            return; // vgmstream is present in this env — test only meaningful without it

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackStopped += () => tcs.TrySetResult(true);

        player.Play(_wavPath);

        // Give the async pipeline up to 5 s to complete.
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task;
        Assert.True(completed, "PlaybackStopped did not fire — .wav may still be routed through vgmstream");
    }

    private static void WriteMinimalWav(string path)
    {
        // 44-byte RIFF/PCM header, 0 data bytes, 16-bit stereo 44100 Hz
        using var fs = File.OpenWrite(path);
        using var w  = new BinaryWriter(fs);
        w.Write("RIFF"u8); w.Write(36);          // chunk size (header only, 0 data)
        w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16);           // subchunk1 size
        w.Write((short)1);                         // PCM
        w.Write((short)2);                         // 2 channels
        w.Write(44100);                            // sample rate
        w.Write(176400);                           // byte rate
        w.Write((short)4);                         // block align
        w.Write((short)16);                        // bits per sample
        w.Write("data"u8); w.Write(0);             // 0 bytes of audio data
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```
dotnet test DialogEditor.Tests --filter "VoAudioPlayerTests" -- xunit.parallelizeTestCollections=false
```

Expected: test is skipped (if vgmstream present) or FAILS because `PlaybackStopped` never fires (the old code routes `.wav` through vgmstream which isn't installed).

- [ ] **Step 3: Add `.wav` short-circuit to `VoAudioPlayer.Play()`**

In `DialogEditor.Avalonia/Audio/VoAudioPlayer.cs`, replace the existing `Play` method (lines 34–89) with:

```csharp
public void Play(string path)
{
    StopAndCleanup();        // increments _generation, cleans up previous
    _manualStop = false;
    var gen = ++_generation; // this play's identity token

    if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
    {
        // .wav: no decode step needed — open directly with NAudio on the calling UI thread.
        try
        {
            _reader = new AudioFileReader(path);
            _output = new WaveOutEvent();
            _output.PlaybackStopped += OnNaturalPlaybackStopped;
            _output.Init(_reader);
            _output.Play();
            // _tempFile stays null — no temp file to clean up for direct WAV playback.
        }
        catch (Exception ex)
        {
            AppLog.Error("VoAudioPlayer: NAudio failed to start WAV", ex);
        }
        return;
    }

    _ = Task.Run(async () =>
    {
        var tempFile = Path.Combine(Path.GetTempPath(), $"vo_{Guid.NewGuid():N}.wav");
        try
        {
            var psi = new ProcessStartInfo(ToolPath, $"-o \"{tempFile}\" \"{path}\"")
            {
                CreateNoWindow  = true,
                UseShellExecute = false,
            };
            using var proc = Process.Start(psi)!;
            await proc.WaitForExitAsync();

            if (proc.ExitCode != 0)
            {
                AppLog.Warn($"vgmstream-cli exited {proc.ExitCode} for: {path}");
                TryDeleteTemp(tempFile);
                return;
            }

            if (gen != _generation) { TryDeleteTemp(tempFile); return; }

            Dispatcher.UIThread.Post(() =>
            {
                if (gen != _generation) { TryDeleteTemp(tempFile); return; }

                try
                {
                    _reader = new AudioFileReader(tempFile);
                    _output = new WaveOutEvent();
                    _output.PlaybackStopped += OnNaturalPlaybackStopped;
                    _output.Init(_reader);
                    _tempFile = tempFile;
                    _output.Play();
                }
                catch (Exception ex)
                {
                    AppLog.Error("VoAudioPlayer: NAudio failed to start", ex);
                    TryDeleteTemp(tempFile);
                }
            });
        }
        catch (Exception ex)
        {
            AppLog.Error($"VoAudioPlayer.Play failed for: {path}", ex);
            TryDeleteTemp(tempFile);
        }
    });
}
```

Also rename the parameter from `wemPath` to `path` in the `IVoAudioPlayer` interface (`DialogEditor.ViewModels/Services/IVoAudioPlayer.cs`) and the `NullVoAudioPlayer` stub:

```csharp
void Play(string path);
```

And in `NullVoAudioPlayer`:
```csharp
public void Play(string path) { }
```

- [ ] **Step 4: Run test again**

```
dotnet test DialogEditor.Tests --filter "VoAudioPlayerTests" -- xunit.parallelizeTestCollections=false
```

Expected: PASS (or skip if vgmstream is present in the test environment).

- [ ] **Step 5: Run full suite to check regressions**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: same pass count as before.

- [ ] **Step 6: Commit**

```
git add DialogEditor.Avalonia/Audio/VoAudioPlayer.cs
git add DialogEditor.ViewModels/Services/IVoAudioPlayer.cs
git add DialogEditor.Tests/Audio/VoAudioPlayerTests.cs
git commit -m "feat(vo-preview): VoAudioPlayer plays .wav directly without vgmstream"
```

---

### Task 2: Preview buttons in `VoImportDialog`

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia/Views/VoImportDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`

**Interfaces:**
- Consumes: `IVoAudioPlayer` from Task 1.
- Produces: `VoImportDialog(IVoImporter, VoImportPaths, IVoAudioPlayer)` constructor — the three-arg form used by Task 5.

No automated test for the dialog UI (existing precedent). Manual verification checklist covers it.

- [ ] **Step 1: Add preview string keys to `Strings.axaml`**

Open `DialogEditor.Avalonia/Resources/Strings.axaml`. Find the `VoImport_` block (around line 1135). Add immediately after `ToolTip_VoImportDownloadWwise`:

```xml
    <sys:String x:Key="ToolTip_VoPreviewPlay_Primary">Play the selected primary voice-over file.</sys:String>
    <sys:String x:Key="ToolTip_VoPreviewPlay_Fem">Play the selected female voice-over file.</sys:String>
    <sys:String x:Key="ToolTip_VoPreviewStop">Stop playback.</sys:String>
    <sys:String x:Key="AutomationName_VoPreviewPlay_Primary">Play primary voice-over</sys:String>
    <sys:String x:Key="AutomationName_VoPreviewPlay_Fem">Play female voice-over</sys:String>
    <sys:String x:Key="AutomationName_VoPreviewStop">Stop voice-over preview</sys:String>
```

- [ ] **Step 2: Add play buttons to `VoImportDialog.axaml`**

Replace the two slot `<Grid>` elements. The column layout changes from `"120,*,Auto,Auto"` to `"120,*,Auto,Auto,Auto"` (new Auto column for the play button, inserted before Browse):

```xml
        <!-- Primary slot -->
        <Grid ColumnDefinitions="120,*,Auto,Auto,Auto" Margin="0,0,0,8">
            <TextBlock Grid.Column="0" Text="{DynamicResource VoImport_PrimaryLabel}"
                       VerticalAlignment="Center"/>
            <TextBlock Grid.Column="1" x:Name="PrimaryLabel"
                       Text="—" VerticalAlignment="Center"
                       TextTrimming="CharacterEllipsis"/>
            <Button    Grid.Column="2" x:Name="PlayPrimaryButton"
                       Content="▶"
                       Click="PlayPrimary_Click"
                       IsVisible="False"
                       ToolTip.Tip="{DynamicResource ToolTip_VoPreviewPlay_Primary}"
                       AutomationProperties.Name="{DynamicResource AutomationName_VoPreviewPlay_Primary}"
                       AutomationProperties.HelpText="{DynamicResource ToolTip_VoPreviewPlay_Primary}"
                       Margin="8,0,4,0" Padding="6,2"/>
            <Button    Grid.Column="3" x:Name="BrowsePrimaryButton"
                       Content="{DynamicResource VoImport_Browse}"
                       Click="BrowsePrimary_Click"
                       ToolTip.Tip="{DynamicResource ToolTip_VoImportBrowse_Primary}"
                       AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportBrowse_Primary}"
                       Margin="0,0,4,0"/>
            <Button    Grid.Column="4" x:Name="ClearPrimaryButton"
                       Content="{DynamicResource VoImport_Clear}"
                       Click="ClearPrimary_Click"
                       IsVisible="False"
                       ToolTip.Tip="{DynamicResource ToolTip_VoImportClear_Primary}"
                       AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportClear_Primary}"/>
        </Grid>

        <!-- Female (optional) slot -->
        <Grid ColumnDefinitions="120,*,Auto,Auto,Auto" Margin="0,0,0,16">
            <TextBlock Grid.Column="0" Text="{DynamicResource VoImport_FemaleLabel}"
                       VerticalAlignment="Center"/>
            <TextBlock Grid.Column="1" x:Name="FemLabel"
                       Text="—" VerticalAlignment="Center"
                       TextTrimming="CharacterEllipsis"/>
            <Button    Grid.Column="2" x:Name="PlayFemButton"
                       Content="▶"
                       Click="PlayFem_Click"
                       IsVisible="False"
                       ToolTip.Tip="{DynamicResource ToolTip_VoPreviewPlay_Fem}"
                       AutomationProperties.Name="{DynamicResource AutomationName_VoPreviewPlay_Fem}"
                       AutomationProperties.HelpText="{DynamicResource ToolTip_VoPreviewPlay_Fem}"
                       Margin="8,0,4,0" Padding="6,2"/>
            <Button    Grid.Column="3" x:Name="BrowseFemButton"
                       Content="{DynamicResource VoImport_Browse}"
                       Click="BrowseFem_Click"
                       ToolTip.Tip="{DynamicResource ToolTip_VoImportBrowse_Fem}"
                       AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportBrowse_Fem}"
                       Margin="0,0,4,0"/>
            <Button    Grid.Column="4" x:Name="ClearFemButton"
                       Content="{DynamicResource VoImport_Clear}"
                       Click="ClearFem_Click"
                       IsVisible="False"
                       ToolTip.Tip="{DynamicResource ToolTip_VoImportClear_Fem}"
                       AutomationProperties.HelpText="{DynamicResource ToolTip_VoImportClear_Fem}"/>
        </Grid>
```

- [ ] **Step 3: Update `VoImportDialog.axaml.cs`**

Replace the entire file with:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

public partial class VoImportDialog : Window
{
    private enum PlayingSlot { None, Primary, Fem }

    private readonly IVoImporter    _importer = null!;
    private readonly IVoAudioPlayer _player   = null!;

    private string?     _primarySource;
    private string?     _femSource;
    private PlayingSlot _playingSlot = PlayingSlot.None;

    /// Set to non-null when the user clicks OK.
    public VoImportDialogResult? Result { get; private set; }

    // Parameterless ctor so the XAML compiler can embed the type (avoids AVLN3001).
    public VoImportDialog() => InitializeComponent();

    public VoImportDialog(IVoImporter importer, VoImportPaths paths, IVoAudioPlayer player)
    {
        InitializeComponent();
        _importer = importer;
        _player   = player;

        _player.PlaybackStopped += OnPlaybackStopped;
        Closed += (_, _) =>
        {
            _player.PlaybackStopped -= OnPlaybackStopped;
            _player.Stop();
        };

        PrimaryLabel.Text = Path.GetFileName(paths.PrimaryDestinationPath);
        if (paths.FemDestinationPath is not null)
            FemLabel.Text = Path.GetFileName(paths.FemDestinationPath);
    }

    // ── Play buttons ─────────────────────────────────────────────────────

    private void PlayPrimary_Click(object? sender, RoutedEventArgs e)
    {
        if (_primarySource is null) return;
        if (_playingSlot == PlayingSlot.Primary)
        {
            _player.Stop();
            _playingSlot = PlayingSlot.None;
        }
        else
        {
            _player.Play(_primarySource);
            _playingSlot = PlayingSlot.Primary;
        }
        UpdatePlayGlyphs();
    }

    private void PlayFem_Click(object? sender, RoutedEventArgs e)
    {
        if (_femSource is null) return;
        if (_playingSlot == PlayingSlot.Fem)
        {
            _player.Stop();
            _playingSlot = PlayingSlot.None;
        }
        else
        {
            _player.Play(_femSource);
            _playingSlot = PlayingSlot.Fem;
        }
        UpdatePlayGlyphs();
    }

    private void OnPlaybackStopped()
    {
        _playingSlot = PlayingSlot.None;
        UpdatePlayGlyphs();
    }

    private void UpdatePlayGlyphs()
    {
        PlayPrimaryButton.Content = _playingSlot == PlayingSlot.Primary ? "■" : "▶";
        PlayFemButton.Content     = _playingSlot == PlayingSlot.Fem     ? "■" : "▶";

        PlayPrimaryButton.ToolTip = _playingSlot == PlayingSlot.Primary
            ? Loc.Get("ToolTip_VoPreviewStop")
            : Loc.Get("ToolTip_VoPreviewPlay_Primary");
        PlayFemButton.ToolTip = _playingSlot == PlayingSlot.Fem
            ? Loc.Get("ToolTip_VoPreviewStop")
            : Loc.Get("ToolTip_VoPreviewPlay_Fem");
    }

    // ── Browse / Clear ───────────────────────────────────────────────────

    private async void BrowsePrimary_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickVoFileAsync();
        if (path is null) return;
        _primarySource               = path;
        PrimaryLabel.Text            = Path.GetFileName(path);
        ClearPrimaryButton.IsVisible = true;
        PlayPrimaryButton.IsVisible  = true;
        UpdateWwiseWarning();
        UpdateOkButton();
    }

    private void ClearPrimary_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingSlot == PlayingSlot.Primary) { _player.Stop(); _playingSlot = PlayingSlot.None; }
        _primarySource               = null;
        PrimaryLabel.Text            = "—";
        ClearPrimaryButton.IsVisible = false;
        PlayPrimaryButton.IsVisible  = false;
        UpdatePlayGlyphs();
        UpdateWwiseWarning();
        UpdateOkButton();
    }

    private async void BrowseFem_Click(object? sender, RoutedEventArgs e)
    {
        var path = await PickVoFileAsync();
        if (path is null) return;
        _femSource            = path;
        FemLabel.Text         = Path.GetFileName(path);
        ClearFemButton.IsVisible = true;
        PlayFemButton.IsVisible  = true;
        UpdateWwiseWarning();
    }

    private void ClearFem_Click(object? sender, RoutedEventArgs e)
    {
        if (_playingSlot == PlayingSlot.Fem) { _player.Stop(); _playingSlot = PlayingSlot.None; }
        _femSource               = null;
        FemLabel.Text            = "—";
        ClearFemButton.IsVisible = false;
        PlayFemButton.IsVisible  = false;
        UpdatePlayGlyphs();
        UpdateWwiseWarning();
    }

    // ── OK / Cancel ──────────────────────────────────────────────────────

    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (_primarySource is null) return;
        Result = new VoImportDialogResult(_primarySource, _femSource);
        Close();
    }

    private void Cancel_Click(object? sender, RoutedEventArgs e) => Close();

    private void DownloadWwise_Click(object? sender, RoutedEventArgs e)
        => System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(
            "https://www.audiokinetic.com/en/products/wwise/") { UseShellExecute = true });

    // ── Helpers ──────────────────────────────────────────────────────────

    private async Task<string?> PickVoFileAsync()
    {
        var options = new FilePickerOpenOptions
        {
            Title          = Loc.Get("VoImport_PickerTitle"),
            AllowMultiple  = false,
            FileTypeFilter =
            [
                new FilePickerFileType(Loc.Get("VoImport_FileType_All"))
                    { Patterns = ["*.wem", "*.wav"] },
                new FilePickerFileType(Loc.Get("VoImport_FileType_Wem"))
                    { Patterns = ["*.wem"] },
                new FilePickerFileType(Loc.Get("VoImport_FileType_Wav"))
                    { Patterns = ["*.wav"] },
            ],
        };
        var files = await StorageProvider.OpenFilePickerAsync(options);
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    private void UpdateOkButton()
    {
        var isWav = _primarySource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true;
        OkButton.IsEnabled = _primarySource is not null && (!isWav || _importer.IsWwiseAvailable);
    }

    private void UpdateWwiseWarning()
    {
        var anyWav =
            (_primarySource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true) ||
            (_femSource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true);
        WwiseWarningPanel.IsVisible = anyWav && !_importer.IsWwiseAvailable;
    }
}
```

- [ ] **Step 4: Update `MainWindow.axaml.cs` — pass `audioPlayer` to dialog**

Find the `ShowImportDialog` delegate (around line 109):

```csharp
vm.Detail.ShowImportDialog = async paths =>
{
    var dialog = new VoImportDialog(voImporter, paths);
    await dialog.ShowDialog(this);
    return dialog.Result;
};
```

Replace with:

```csharp
vm.Detail.ShowImportDialog = async paths =>
{
    var dialog = new VoImportDialog(voImporter, paths, audioPlayer);
    await dialog.ShowDialog(this);
    return dialog.Result;
};
```

- [ ] **Step 5: Build**

```
dotnet build DialogEditor.Avalonia
```

Expected: 0 errors.

- [ ] **Step 6: Manual smoke test**

- Open project, select a node with a VO status row, click 🎤
- Pick a `.wem` file → ▶ button appears next to Browse
- Click ▶ → glyph changes to ■, audio plays
- Click ■ → glyph resets to ▶, audio stops
- Pick a `.wav` file → same behaviour (plays via NAudio directly)
- Close dialog while playing → audio stops

- [ ] **Step 7: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add DialogEditor.Avalonia/Views/VoImportDialog.axaml
git add DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m "feat(vo-preview): add in-dialog audio preview to VoImportDialog"
```

---

### Task 3: `WemQuality` enum + record changes + `NodeDetailViewModel`

**Files:**
- Modify: `DialogEditor.ViewModels/Services/IVoImporter.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`

**Interfaces:**
- Produces: `WemQuality { Low, Medium, High }` enum; updated `VoImportDialogResult(PrimarySourcePath, FemSourcePath?, Quality)` and `VoImportRequest(PrimaryDestinationPath, PrimarySourcePath, FemDestinationPath?, FemSourcePath?, Quality)` — consumed by Tasks 4 and 5.

No new tests needed — the existing `NodeDetailViewModelImportTests` uses `new VoImportDialogResult("source.wem", null)` which will pick up `Quality = WemQuality.Medium` as the default. The build step will catch any breaks.

- [ ] **Step 1: Add `WemQuality` enum to `IVoImporter.cs`**

Open `DialogEditor.ViewModels/Services/IVoImporter.cs`. Add the enum after the `NullVoImporter` class (before `VoImportRequest`):

```csharp
public enum WemQuality { Low, Medium, High }
```

- [ ] **Step 2: Add `Quality` to `VoImportDialogResult` and `VoImportRequest`**

Replace the two record declarations at the bottom of `IVoImporter.cs`:

```csharp
/// Passed to ShowImportDialog so the dialog knows where files will be saved.
public record VoImportPaths(string PrimaryDestinationPath, string? FemDestinationPath);

/// Returned by ShowImportDialog with the user's source-file selections.
public record VoImportDialogResult(
    string     PrimarySourcePath,
    string?    FemSourcePath,
    WemQuality Quality = WemQuality.Medium);
```

And update `VoImportRequest`:

```csharp
/// <param name="PrimaryDestinationPath">Expected .wem path inside _vo/ for the primary slot.</param>
/// <param name="PrimarySourcePath">.wav or .wem picked by the user for the primary slot.</param>
/// <param name="FemDestinationPath">Expected .wem path inside _vo/ for the female slot (null = not applicable).</param>
/// <param name="FemSourcePath">.wav or .wem picked by the user for the female slot (null = not provided).</param>
/// <param name="Quality">Encoding quality used when source is .wav. Ignored for .wem (already encoded).</param>
public record VoImportRequest(
    string     PrimaryDestinationPath,
    string     PrimarySourcePath,
    string?    FemDestinationPath,
    string?    FemSourcePath,
    WemQuality Quality = WemQuality.Medium);
```

- [ ] **Step 3: Update `NodeDetailViewModel.ImportVo` to pass `selection.Quality`**

In `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`, find the `ImportAsync` call (around line 215):

```csharp
        var result = await Importer.ImportAsync(
            new VoImportRequest(destPrimary, selection.PrimarySourcePath,
                                destFem, selection.FemSourcePath), default);
```

Replace with:

```csharp
        var result = await Importer.ImportAsync(
            new VoImportRequest(destPrimary, selection.PrimarySourcePath,
                                destFem, selection.FemSourcePath,
                                selection.Quality), default);
```

- [ ] **Step 4: Build both layers**

```
dotnet build DialogEditor.ViewModels
dotnet build DialogEditor.Avalonia
```

Expected: 0 errors in both.

- [ ] **Step 5: Run full test suite**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: same pass count as before. The existing `NodeDetailViewModelImportTests` uses the two-arg `VoImportDialogResult` constructor — the default `Quality = WemQuality.Medium` keeps those tests compiling and passing.

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/Services/IVoImporter.cs
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
git commit -m "feat(vo-encoding): add WemQuality enum, update VoImportRequest/Result records"
```

---

### Task 4: WAV→WEM encoding pipeline in `VoImporter` + `template.wproj` + tests

**Files:**
- Create: `DialogEditor.Avalonia/Assets/Wwise/template.wproj`
- Modify: `DialogEditor.Avalonia/Audio/VoImporter.cs`
- Create: `DialogEditor.Tests/Audio/VoImporterTests.cs`

**Interfaces:**
- Consumes: `WemQuality` from Task 3.
- Produces: `VoImporter.EncodeWavToWemAsync` (private); `VoImporter.GenerateWsourcesXml` (internal for tests); `VoImporter.ImportAsync` now honours `request.Quality`.

- [ ] **Step 1: Create the `template.wproj` placeholder**

Create `DialogEditor.Avalonia/Assets/Wwise/template.wproj`:

```xml
<?xml version="1.0" encoding="UTF-8"?>
<!--
  Minimal Wwise project template for WAV→WEM conversion.
  Contains three Vorbis conversion presets used by VoImporter.EncodeWavToWemAsync.

  VERIFICATION REQUIRED: This file must be validated against a real Wwise installation
  before the encoding path can ship. The schema version and property names are
  Wwise-version-dependent. Replace this placeholder with a template exported from
  Wwise Authoring (File > Save Project As, then strip all content except the
  ConversionSettings work unit with the three presets below).

  Preset mapping (VoImporter.GenerateWsourcesXml):
    WemQuality.Low    → "VorbisLow"    (Vorbis quality ≈ 0.3)
    WemQuality.Medium → "VorbisMedium" (Vorbis quality ≈ 0.6)
    WemQuality.High   → "VorbisHigh"   (Vorbis quality ≈ 0.9)
-->
<WwiseDocument Type="WorkUnit" SchemaVersion="89">
  <ConversionSettings>
    <WorkUnit Name="Default Work Unit" ID="{A1000000-0000-0000-0000-000000000001}">
      <ChildrenList>
        <Conversion Name="VorbisLow" ID="{A1000000-0000-0000-0000-000000000002}">
          <PropertyList>
            <Property Name="AudioFormat" Type="int32">6</Property>
            <Property Name="SampleRate" Type="int32">0</Property>
            <Property Name="uQuality"   Type="int32">30</Property>
          </PropertyList>
        </Conversion>
        <Conversion Name="VorbisMedium" ID="{A1000000-0000-0000-0000-000000000003}">
          <PropertyList>
            <Property Name="AudioFormat" Type="int32">6</Property>
            <Property Name="SampleRate" Type="int32">0</Property>
            <Property Name="uQuality"   Type="int32">60</Property>
          </PropertyList>
        </Conversion>
        <Conversion Name="VorbisHigh" ID="{A1000000-0000-0000-0000-000000000004}">
          <PropertyList>
            <Property Name="AudioFormat" Type="int32">6</Property>
            <Property Name="SampleRate" Type="int32">0</Property>
            <Property Name="uQuality"   Type="int32">90</Property>
          </PropertyList>
        </Conversion>
      </ChildrenList>
    </WorkUnit>
  </ConversionSettings>
</WwiseDocument>
```

- [ ] **Step 2: Write failing tests for `GenerateWsourcesXml`**

Create `DialogEditor.Tests/Audio/VoImporterTests.cs`:

```csharp
using DialogEditor.Avalonia.Audio;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Audio;

public class VoImporterTests
{
    // GenerateWsourcesXml is internal — add [assembly: InternalsVisibleTo("DialogEditor.Tests")]
    // to DialogEditor.Avalonia if needed, OR make it public for testing.
    // (See Step 3 for the implementation which marks it internal.)

    [Theory]
    [InlineData(WemQuality.Low,    "VorbisLow")]
    [InlineData(WemQuality.Medium, "VorbisMedium")]
    [InlineData(WemQuality.High,   "VorbisHigh")]
    public void GenerateWsourcesXml_ContainsCorrectPresetName(
        WemQuality quality, string expectedPreset)
    {
        var xml = VoImporter.GenerateWsourcesXml(
            @"C:\sources\line_0001.wav",
            @"C:\dest\line_0001.wem",
            quality);

        Assert.Contains(expectedPreset, xml);
    }

    [Fact]
    public void GenerateWsourcesXml_ContainsSourceFileName()
    {
        var xml = VoImporter.GenerateWsourcesXml(
            @"C:\sources\line_0001.wav",
            @"C:\dest\line_0001.wem",
            WemQuality.Medium);

        Assert.Contains("line_0001.wav", xml);
    }

    [Fact]
    public void GenerateWsourcesXml_ContainsDestNameWithoutExtension()
    {
        var xml = VoImporter.GenerateWsourcesXml(
            @"C:\sources\line_0001.wav",
            @"C:\dest\line_0001.wem",
            WemQuality.Medium);

        Assert.Contains("line_0001", xml);
        Assert.DoesNotContain("line_0001.wem", xml); // destination is name-only, no extension
    }

    [Fact]
    public void GenerateWsourcesXml_UsesForwardSlashesInPaths()
    {
        var xml = VoImporter.GenerateWsourcesXml(
            @"C:\sources\sub\line_0001.wav",
            @"C:\dest\sub\line_0001.wem",
            WemQuality.Medium);

        // Wwise XML paths must use forward slashes
        Assert.DoesNotContain('\\', xml);
    }
}
```

- [ ] **Step 3: Run tests to confirm they fail**

```
dotnet test DialogEditor.Tests --filter "VoImporterTests" -- xunit.parallelizeTestCollections=false
```

Expected: compile error — `VoImporter.GenerateWsourcesXml` does not exist yet.

- [ ] **Step 4: Implement encoding in `VoImporter.cs`**

Replace `DialogEditor.Avalonia/Audio/VoImporter.cs` entirely:

```csharp
using Avalonia.Platform;
using Microsoft.Win32;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Audio;

/// <summary>
/// Copies .wem files directly or encodes .wav → .wem via Wwise CLI into the project's _vo/ folder.
///
/// Wwise detection order:
///   1. WWISEROOT environment variable → WwiseCLI.exe relative to it.
///   2. Registry HKLM\SOFTWARE\Audiokinetic\Wwise\ → newest installed version.
///   3. Common install-path fallback.
/// IsWwiseAvailable is cached at construction — restart the editor after installing Wwise.
/// </summary>
public sealed class VoImporter : IVoImporter
{
    private readonly string? _wwiseCliPath;

    // Cached path of the template.wproj extracted to %TEMP% on first encode.
    private static string? _cachedWprojPath;

    public bool IsWwiseAvailable => _wwiseCliPath is not null;

    public VoImporter()
    {
        _wwiseCliPath = DetectWwiseCli();
    }

    public async Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct)
    {
        try
        {
            await ProcessSlotAsync(request.PrimarySourcePath,
                                   request.PrimaryDestinationPath,
                                   request.Quality, ct);

            if (request.FemSourcePath is not null && request.FemDestinationPath is not null)
                await ProcessSlotAsync(request.FemSourcePath,
                                       request.FemDestinationPath,
                                       request.Quality, ct);

            return new VoImportResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("VoImporter.ImportAsync failed", ex);
            return new VoImportResult(false, ex.Message);
        }
    }

    private async Task ProcessSlotAsync(
        string sourcePath, string destPath, WemQuality quality, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        if (sourcePath.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destPath, overwrite: true);
            return;
        }

        // .wav → .wem via Wwise CLI
        if (!IsWwiseAvailable)
            throw new InvalidOperationException(
                "Wwise not found. Install Wwise or use a pre-encoded .wem file.");

        await EncodeWavToWemAsync(sourcePath, destPath, quality, ct);
    }

    private async Task EncodeWavToWemAsync(
        string sourcePath, string destPath, WemQuality quality, CancellationToken ct)
    {
        var wprojPath = GetOrExtractTemplateWproj();
        var tempDir   = Path.Combine(Path.GetTempPath(), "PillarsDialogEditor",
                                     "wwise", $"encode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            // Write the .wsources file pointing at the source WAV.
            var wsourcesPath = Path.Combine(tempDir, "sources.wsources");
            await File.WriteAllTextAsync(wsourcesPath,
                GenerateWsourcesXml(sourcePath, destPath, quality), ct);

            // Invoke WwiseCLI to convert. The output .wem is written to:
            //   <wprojDir>\GeneratedSoundBanks\Windows\<destNameWithoutExtension>.wem
            // VERIFICATION: Confirm this output path against a real Wwise install.
            var psi = new System.Diagnostics.ProcessStartInfo(
                _wwiseCliPath!,
                $"\"{wprojPath}\" -Platform Windows -ConvertExternalSources \"{wsourcesPath}\"")
            {
                CreateNoWindow        = true,
                UseShellExecute       = false,
                RedirectStandardError = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException(
                    $"WwiseCLI exited {proc.ExitCode}: {err}");
            }

            // Locate and move the encoded .wem to the final destination.
            var wprojDir   = Path.GetDirectoryName(wprojPath)!;
            var outputName = Path.GetFileNameWithoutExtension(destPath) + ".wem";
            var outputWem  = Path.Combine(wprojDir, "GeneratedSoundBanks", "Windows", outputName);

            if (!File.Exists(outputWem))
                throw new FileNotFoundException(
                    $"WwiseCLI did not produce expected output at: {outputWem}");

            File.Move(outputWem, destPath, overwrite: true);
        }
        finally
        {
            // Best-effort cleanup of the per-encode temp directory.
            try { Directory.Delete(tempDir, recursive: true); } catch { }
        }
    }

    /// <summary>
    /// Generates a .wsources XML file content for a single WAV→WEM conversion.
    /// Pure (no I/O) — the primary unit-test target for this class.
    /// </summary>
    internal static string GenerateWsourcesXml(
        string sourcePath, string destPath, WemQuality quality)
    {
        var presetName = quality switch
        {
            WemQuality.Low  => "VorbisLow",
            WemQuality.High => "VorbisHigh",
            _               => "VorbisMedium",
        };

        // Wwise XML requires forward slashes.
        var sourceDir  = Path.GetDirectoryName(sourcePath)!.Replace('\\', '/');
        var sourceName = Path.GetFileName(sourcePath);
        var destName   = Path.GetFileNameWithoutExtension(destPath);

        return $"""
            <ExternalSourcesList SchemaVersion="1" Root="{sourceDir}">
              <Source Path="{sourceName}"
                      Destination="{destName}"
                      Conversion="{presetName}"/>
            </ExternalSourcesList>
            """;
    }

    private static string GetOrExtractTemplateWproj()
    {
        if (_cachedWprojPath is not null) return _cachedWprojPath;

        var destPath = Path.Combine(Path.GetTempPath(), "PillarsDialogEditor",
                                    "wwise", "template.wproj");
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        var uri    = new Uri("avares://DialogEditor.Avalonia/Assets/Wwise/template.wproj");
        using var stream = AssetLoader.Open(uri);
        using var fs     = File.Create(destPath);
        stream.CopyTo(fs);

        _cachedWprojPath = destPath;
        return destPath;
    }

    private static string? DetectWwiseCli()
    {
        // 1. WWISEROOT environment variable
        var wwiseRoot = Environment.GetEnvironmentVariable("WWISEROOT");
        if (!string.IsNullOrEmpty(wwiseRoot))
        {
            var candidate = Path.Combine(wwiseRoot, "Authoring", "x64", "Release", "bin", "WwiseCLI.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // 2. Registry: enumerate installed versions, pick the newest
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Audiokinetic\Wwise\");
            if (key is not null)
            {
                var versions = key.GetSubKeyNames()
                    .OrderByDescending(v => v)
                    .ToList();
                foreach (var version in versions)
                {
                    using var verKey = key.OpenSubKey(version);
                    var installDir = verKey?.GetValue("InstallDir") as string;
                    if (string.IsNullOrEmpty(installDir)) continue;
                    var candidate = Path.Combine(installDir,
                        "Authoring", "x64", "Release", "bin", "WwiseCLI.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"VoImporter: registry lookup failed: {ex.Message}");
        }

        // 3. Common install-path fallback
        var commonRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Audiokinetic");
        if (Directory.Exists(commonRoot))
        {
            foreach (var dir in Directory.GetDirectories(commonRoot, "Wwise*")
                                         .OrderByDescending(d => d))
            {
                var candidate = Path.Combine(dir, "Authoring", "x64", "Release", "bin", "WwiseCLI.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
```

- [ ] **Step 5: Expose `GenerateWsourcesXml` to the test project**

`GenerateWsourcesXml` is `internal`. Add an `InternalsVisibleTo` attribute so the test project can call it.

Create `DialogEditor.Avalonia/Properties/AssemblyInfo.cs` (if it doesn't already exist):

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DialogEditor.Tests")]
```

Check if the file already exists first:

```
Test-Path "DialogEditor.Avalonia/Properties/AssemblyInfo.cs"
```

If it exists, add only the `InternalsVisibleTo` line to the existing file rather than replacing it.

- [ ] **Step 6: Run `VoImporterTests` to confirm they pass**

```
dotnet test DialogEditor.Tests --filter "VoImporterTests" -- xunit.parallelizeTestCollections=false
```

Expected: all 5 tests PASS.

- [ ] **Step 7: Run full suite**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: same pass count as before + 5 new passing tests.

- [ ] **Step 8: Commit**

```
git add DialogEditor.Avalonia/Assets/Wwise/template.wproj
git add DialogEditor.Avalonia/Audio/VoImporter.cs
git add DialogEditor.Avalonia/Properties/AssemblyInfo.cs
git add DialogEditor.Tests/Audio/VoImporterTests.cs
git commit -m "feat(vo-encoding): implement WAV→WEM pipeline with bundled template.wproj"
```

---

### Task 5: Quality radio buttons in `VoImportDialog`

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia/Views/VoImportDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs`

**Interfaces:**
- Consumes: `WemQuality` enum from Task 3.
- Produces: `VoImportDialogResult.Quality` is set from user selection; `Ok_Click` passes it through.

- [ ] **Step 1: Add quality string keys to `Strings.axaml`**

In the `VoImport_` block (immediately after the preview strings added in Task 2), add:

```xml
    <sys:String x:Key="VoImport_QualityLabel">Quality (WAV only)</sys:String>
    <sys:String x:Key="VoImport_Quality_Low">Low</sys:String>
    <sys:String x:Key="VoImport_Quality_Medium">Medium</sys:String>
    <sys:String x:Key="VoImport_Quality_High">High</sys:String>
    <sys:String x:Key="ToolTip_VoQuality">Encoding quality for WAV files. Low uses less disk space; High preserves more detail. Has no effect on pre-encoded .wem files.</sys:String>
```

- [ ] **Step 2: Add quality row to `VoImportDialog.axaml`**

In `VoImportDialog.axaml`, add a new `<Grid>` between the Female slot `</Grid>` and `<!-- Wwise warning -->`:

```xml
        <!-- Encoding quality (WAV→WEM only; disabled when no .wav is selected) -->
        <Grid ColumnDefinitions="120,*" Margin="0,0,0,12">
            <TextBlock Grid.Column="0" Text="{DynamicResource VoImport_QualityLabel}"
                       VerticalAlignment="Center"/>
            <StackPanel Grid.Column="1" x:Name="QualityPanel"
                        Orientation="Horizontal" Spacing="16" IsEnabled="False">
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
```

- [ ] **Step 3: Add quality state and handlers to `VoImportDialog.axaml.cs`**

Add the `_quality` field after `_playingSlot`:

```csharp
    private WemQuality  _quality     = WemQuality.Medium;
```

Add three radio button handlers (add anywhere in the file, e.g. after `UpdatePlayGlyphs`):

```csharp
    private void QualityLow_Checked(object?    sender, RoutedEventArgs e) => _quality = WemQuality.Low;
    private void QualityMedium_Checked(object? sender, RoutedEventArgs e) => _quality = WemQuality.Medium;
    private void QualityHigh_Checked(object?   sender, RoutedEventArgs e) => _quality = WemQuality.High;
```

Update `Ok_Click` to include quality:

```csharp
    private void Ok_Click(object? sender, RoutedEventArgs e)
    {
        if (_primarySource is null) return;
        Result = new VoImportDialogResult(_primarySource, _femSource, _quality);
        Close();
    }
```

Update `UpdateWwiseWarning()` to also enable/disable the quality row:

```csharp
    private void UpdateWwiseWarning()
    {
        var anyWav =
            (_primarySource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true) ||
            (_femSource?.EndsWith(".wav", StringComparison.OrdinalIgnoreCase) == true);
        WwiseWarningPanel.IsVisible = anyWav && !_importer.IsWwiseAvailable;
        QualityPanel.IsEnabled      = anyWav;
    }
```

- [ ] **Step 4: Build**

```
dotnet build DialogEditor.Avalonia
```

Expected: 0 errors.

- [ ] **Step 5: Run full suite**

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: all tests pass.

- [ ] **Step 6: Manual smoke test**

- Open the import dialog and pick a `.wem` file → quality row is dimmed/disabled
- Pick a `.wav` file → quality row enables; switch to High; click OK
- Confirm `VoImportDialogResult.Quality == High` flows to `VoImporter.ImportAsync`
  (check AppLog for a WwiseCLI invocation attempt — it will fail without a real Wwise install but should log the command)

- [ ] **Step 7: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add DialogEditor.Avalonia/Views/VoImportDialog.axaml
git add DialogEditor.Avalonia/Views/VoImportDialog.axaml.cs
git commit -m "feat(vo-encoding): add quality preset UI to VoImportDialog"
```

---

## Full test suite gate

After all tasks complete:

```
dotnet test DialogEditor.Tests -- xunit.parallelizeTestCollections=false
```

Expected: all tests pass (no regressions).

---

## Manual Verification Checklist

- [ ] Pick a `.wav` in Primary slot → ▶ button appears; clicking it plays the file; clicking again (■) stops it
- [ ] Pick a `.wem` in Primary slot → ▶ button appears; clicking it plays via vgmstream (if available)
- [ ] Pick `.wav` in Primary, click ▶; then click ▶ on Fem slot → Primary stops, Fem starts
- [ ] Close dialog while audio is playing → playback stops
- [ ] With no `.wav` selected, quality row is disabled (dimmed)
- [ ] Pick a `.wav` → quality row enables; switch to High → OK enabled only if Wwise available
- [ ] With Wwise absent and `.wav` picked → OK disabled, warning visible, quality row enabled but moot
- [ ] Pick a `.wem` → quality setting irrelevant; import copies file directly
- [ ] F5 after import → `.wem` appears in game VO directory (end-to-end unchanged by this feature)
