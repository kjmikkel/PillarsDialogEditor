# VO Audio Playback — Design Spec

**Date:** 2026-06-22  
**Scope:** PoE2 only. PoE1 audio is in Unity asset archives; deferred per research.  
**Depends on:** VO path validation feature (already shipped — `ChatterPrefixService`, `VoPathResolver`, `VoCheckResult`, per-node status indicator).

---

## Background

PoE2 story VO files are loose `.wem` files (Wwise Vorbis, idCodec=4) at a deterministic
path already resolved by `VoPathResolver`. The path validation feature confirmed existence;
this feature plays them.

**Target audiences:**

- **Explorers** (audience #1) — game fans revisiting dialogue they liked; no assumed
  technical literacy. Playback must work with zero setup.
- **Modders** (audience #2) — at minimum technically capable, but also benefit from
  frictionless audio preview.

Because audience #1 cannot be expected to install third-party tools, `vgmstream-cli` is
**bundled** with the editor rather than requiring the user to provide it.

---

## Decoder & playback pipeline

**vgmstream-cli** (MIT/ISC) is bundled as `tools/vgmstream-cli.exe` next to the editor
executable. Attribution: `THIRD_PARTY_LICENSES.md`.

On Play:

1. Shell out: `vgmstream-cli.exe -o {tempFile}.wav {input}.wem` where `tempFile` is a
   uniquely-named file in `Path.GetTempPath()`.
2. Wait for the process to exit (background thread — UI stays live).
3. Open the WAV via NAudio `AudioFileReader`, play with `WaveOutEvent`.
4. On `PlaybackStopped` (natural end or manual stop): delete the temp file, fire
   `IVoAudioPlayer.PlaybackStopped`, reset internal state.

**Error handling:**

| Situation | Behaviour |
|-----------|-----------|
| `vgmstream-cli.exe` absent | `IsAvailable = false`; buttons hidden; no error shown |
| Process exits non-zero (corrupt `.wem`) | `AudioFileReader` throws; caught; `AppLog.Warn`; `PlaybackStopped` fired so button resets |
| Any other IO/audio exception | Caught; `AppLog.Warn`; `PlaybackStopped` fired |

`OperationCanceledException` is swallowed silently per CLAUDE.md. Bare `catch {}` is not permitted.

**NAudio** is added as a NuGet dependency to `DialogEditor.Avalonia` only (Windows-specific
layer; acceptable because the `.wem` files themselves live under `Audio\Windows\`).

---

## Playback behaviour

- **Toggle:** clicking Play starts playback; clicking again (while the same file is playing)
  stops it. Button glyph flips ▶ ↔ ■.
- **Stop previous, play new:** only one file plays at a time. This applies both across nodes
  (navigating away stops current audio) and within a node (clicking ▶ F while ▶ M is
  playing stops M and starts F).
- **Node navigation:** `NotifyAllProxies()` calls `_player.Stop()` before refreshing the VO
  check, ensuring a clean state on every node switch.

---

## §1 — IVoAudioPlayer interface (`DialogEditor.ViewModels.Services`)

```csharp
public interface IVoAudioPlayer
{
    bool IsAvailable { get; }   // false if vgmstream-cli.exe not found
    event Action? PlaybackStopped;
    void Play(string wemPath);
    void Stop();
}
```

Lives in `DialogEditor.ViewModels.Services` so `NodeDetailViewModel` can depend on it
without a reference to Avalonia/NAudio. Injected as a singleton; the stub in tests
implements it with a no-op `Play` and a manually-fireable `PlaybackStopped` event.

---

## §2 — VoAudioPlayer implementation (`DialogEditor.Avalonia`)

```csharp
public sealed class VoAudioPlayer : IVoAudioPlayer, IDisposable
```

- Constructor checks `Path.Combine(AppContext.BaseDirectory, "tools", "vgmstream-cli.exe")`
  and sets `IsAvailable` accordingly.
- `Play(string wemPath)`: stops any current playback, runs the vgmstream process on a
  background thread, then hands off the temp WAV to NAudio.
- `Stop()`: calls `WaveOutEvent.Stop()`; `PlaybackStopped` fires via NAudio's own event.
- `PlaybackStopped`: deletes the temp file, fires the interface event.
- `Dispose()`: stops playback, disposes NAudio objects, deletes any remaining temp file.

Registered as a singleton in `App.axaml.cs`. Passed to `MainWindowViewModel` constructor,
which passes it to `NodeDetailViewModel`.

---

## §3 — VoCheckResult extension (`DialogEditor.ViewModels.Services`)

`VoCheckResult` gains two nullable path properties so `NodeDetailViewModel` does not need
to reconstruct paths:

```csharp
public record VoCheckResult(
    VoPresence Status,
    bool       FemaleVariantFound,
    string?    PrimaryWemPath,   // full path to {name}.wem, or null if NotApplicable/unknown speaker
    string?    FemWemPath);      // full path to {name}_fem.wem, or null if not found
```

`VoPathResolver.Check(...)` already computes both paths; storing them is zero extra work.

**Existing tests:** all current `VoCheckResult(...)` constructor calls gain `null, null`
for the two new parameters — mechanical, no logic change.

---

## §4 — NodeDetailViewModel changes (`DialogEditor.ViewModels`)

### New constructor parameter

`IVoAudioPlayer player` is added to the existing `NodeDetailViewModel` constructor
alongside its current parameters.

### Internal playing state

```csharp
private enum Playing { None, Primary, Female }
private Playing _currentlyPlaying = Playing.None;
```

### New properties

| Property | Type | Description |
|----------|------|-------------|
| `IsPlayingPrimary` | `bool` | `_currentlyPlaying == Playing.Primary` |
| `IsPlayingFem` | `bool` | `_currentlyPlaying == Playing.Female` |
| `CanPlayFem` | `bool` | `_voCheck?.FemaleVariantFound ?? false` |
| `CanPlayAudio` | `bool` | `_player.IsAvailable && VoStatusIsFound` |
| `PlayPrimaryGlyph` | `string` | `"■"` if `IsPlayingPrimary`, else `"▶"` |
| `PlayFemGlyph` | `string` | `"■"` if `IsPlayingFem`, else `"▶"` |
| `PlayPrimaryTooltip` | `string` | `Loc.Get("ToolTip_StopVO")` or `Loc.Get("ToolTip_PlayVO")` |
| `PlayFemTooltip` | `string` | `Loc.Get("ToolTip_StopFemVO")` or `Loc.Get("ToolTip_PlayFemVO")` |
| `PlayPrimaryCommand` | `IRelayCommand` | Toggle primary playback |
| `PlayFemCommand` | `IRelayCommand` | Toggle female playback |

### Command logic

```csharp
// PlayPrimaryCommand.Execute
if (_currentlyPlaying == Playing.Primary) { _player.Stop(); }
else { _player.Play(_voCheck!.PrimaryWemPath!); SetPlaying(Playing.Primary); }

// PlayFemCommand.Execute
if (_currentlyPlaying == Playing.Female) { _player.Stop(); }
else { _player.Play(_voCheck!.FemWemPath!); SetPlaying(Playing.Female); }
```

`SetPlaying(Playing p)` sets `_currentlyPlaying = p` and raises all dependent properties.

On `_player.PlaybackStopped`: call `SetPlaying(Playing.None)`.

### NotifyAllProxies changes

Before refreshing `_voCheck`, call `_player.Stop()` to clean up any in-progress playback.
After refreshing, raise all new properties alongside the existing VO ones.

---

## §5 — NodeDetailView.axaml changes

The existing ✓/✗ `StackPanel` grows play buttons at its trailing end:

```xml
<StackPanel Orientation="Horizontal" Margin="0,2,0,4"
            IsVisible="{Binding HasVoStatus}"
            ToolTip.Tip="{Binding VoStatusText}"
            AutomationProperties.HelpText="{Binding VoStatusText}">

    <!-- existing status glyph and text -->
    <TextBlock Text="{Binding VoStatusGlyph}" ... />
    <TextBlock Text="{Binding VoStatusText}"  ... />

    <!-- play buttons — only when file found and vgmstream available -->
    <Button Content="{Binding PlayPrimaryGlyph}"
            Command="{Binding PlayPrimaryCommand}"
            IsVisible="{Binding CanPlayAudio}"
            ToolTip.Tip="{Binding PlayPrimaryTooltip}"
            AutomationProperties.HelpText="{Binding PlayPrimaryTooltip}"
            Margin="8,0,0,0" Padding="6,2"/>

    <Button Content="{Binding PlayFemGlyph}"
            Command="{Binding PlayFemCommand}"
            IsVisible="{Binding CanPlayFem}"
            ToolTip.Tip="{Binding PlayFemTooltip}"
            AutomationProperties.HelpText="{Binding PlayFemTooltip}"
            Margin="4,0,0,0" Padding="6,2"/>
</StackPanel>
```

Tooltips bind to resolved string properties (`PlayPrimaryTooltip`, `PlayFemTooltip`) on
the ViewModel, which call `Loc.Get(...)` directly — consistent with how `VoStatusText`
already works. No converter needed.

---

## New localisation strings (`Strings.axaml`)

```xml
<!-- ── Voice-Over audio playback ───────────────────────────────────── -->
<x:String x:Key="ToolTip_PlayVO">Play voice-over</x:String>
<x:String x:Key="ToolTip_StopVO">Stop playback</x:String>
<x:String x:Key="ToolTip_PlayFemVO">Play female voice-over variant</x:String>
<x:String x:Key="ToolTip_StopFemVO">Stop playback (female variant)</x:String>
```

---

## Files to create / modify

| File | Change |
|------|--------|
| `DialogEditor.ViewModels/Services/IVoAudioPlayer.cs` | **Create** |
| `DialogEditor.ViewModels/Services/VoCheckResult.cs` | Extend: add `PrimaryWemPath`, `FemWemPath` |
| `DialogEditor.ViewModels/Services/VoPathResolver.cs` | Extend: populate new `VoCheckResult` fields |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Extend: inject player, add commands + properties |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Extend: accept + pass `IVoAudioPlayer` |
| `DialogEditor.Avalonia/Audio/VoAudioPlayer.cs` | **Create** |
| `DialogEditor.Avalonia/App.axaml.cs` | Extend: register singleton, pass to VM |
| `DialogEditor.Avalonia/Views/NodeDetailView.axaml` | Extend: add play buttons to VO status row |
| `DialogEditor.Avalonia.Shared/Resources/Strings.axaml` | Extend: 4 new strings |
| `DialogEditor.Tests/Services/VoPathResolverTests.cs` | Update: `VoCheckResult` constructor calls |
| `DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs` | **Create** (or extend): playback command tests |

---

## Testing strategy

TDD throughout. New tests in `DialogEditor.Tests`:

- `VoPathResolverTests` — extend: assert `PrimaryWemPath` and `FemWemPath` are populated correctly for each existing test case.
- `NodeDetailViewModelTests` — new or extended:
  - `PlayPrimaryCommand_WhenFileFound_CallsPlayerPlay`
  - `PlayPrimaryCommand_WhenAlreadyPlaying_CallsPlayerStop`
  - `PlayFemCommand_StopsPlayingPrimary_ThenStartsFemale`
  - `NotifyAllProxies_StopsCurrentPlayback`
  - `PlaybackStopped_ResetsPlayingState`
  - `CanPlayAudio_FalseWhenPlayerUnavailable`
  - `CanPlayFem_TrueOnlyWhenFemVariantFound`

`IVoAudioPlayer` stub: no-op `Play`, manual `PlaybackStopped` fire, configurable `IsAvailable`.

No automated tests for actual audio output — that requires a real vgmstream binary and real `.wem` files; verified manually.

---

## Out of scope

- Audio playback in the Validation Window (only missing files are listed there — nothing to play).
- Volume control or seek.
- PoE1 VO playback (Unity asset archives; deferred indefinitely).
- Female-variant playback gated on player character gender (informational only — user chooses which to play).
