# VO Audio Playback Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let users play PoE2 `.wem` voice-over files directly in the node detail panel using bundled `vgmstream-cli` and NAudio.

**Architecture:** `VoPathResolver` already resolves the expected file paths; this feature stores those paths in `VoCheckResult`, exposes an `IVoAudioPlayer` interface in the ViewModels layer (no Avalonia dependency), and wires the concrete `VoAudioPlayer` (Avalonia layer, uses NAudio + `Dispatcher.UIThread`) into `NodeDetailViewModel` via a settable property. Toggle buttons (▶/■) appear in `NodeDetailView.axaml` whenever `vgmstream-cli.exe` is present and the `.wem` file is found.

**Tech Stack:** C#/.NET 9, Avalonia 11.3.14, NAudio 2.x (new), CommunityToolkit.Mvvm 8.2.2, `vgmstream-cli.exe` (ISC, bundled)

## Global Constraints

- No hard-coded user-visible strings — all copy goes in `DialogEditor.Avalonia/Resources/Strings.axaml`
- Every interactive control must have a `ToolTip.Tip` and matching `AutomationProperties.HelpText`
- Every caught exception must be logged via `AppLog.Error(...)` or `AppLog.Warn(...)` before or after any user-facing update; `OperationCanceledException` swallowed silently; bare `catch {}` forbidden
- TDD: write a failing test before writing implementation code
- Tests run serially — do NOT re-enable parallelisation
- `DialogEditor.ViewModels.csproj` has no Avalonia reference — ViewModel code must not use `Avalonia.Threading.Dispatcher`

---

## File Map

| File | Change |
|------|--------|
| `DialogEditor.ViewModels/Services/VoCheckResult.cs` | Add `PrimaryWemPath`, `FemWemPath` positional params |
| `DialogEditor.ViewModels/Services/VoPathResolver.cs` | Populate new fields |
| `DialogEditor.ViewModels/Services/IVoAudioPlayer.cs` | **Create** — interface + `NullVoAudioPlayer` |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Add `Player` property, commands, computed properties |
| `DialogEditor.Avalonia/Audio/VoAudioPlayer.cs` | **Create** — vgmstream shell-out + NAudio + UI-thread marshal |
| `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj` | Add NAudio NuGet + `tools/vgmstream-cli.exe` Content item |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Create player, set on `vm.Detail.Player`, dispose on `Closed` |
| `DialogEditor.Avalonia/Views/NodeDetailView.axaml` | Add ▶ M and ▶ F buttons to VO status row |
| `DialogEditor.Avalonia/Resources/Strings.axaml` | 4 new ToolTip strings |
| `DialogEditor.Tests/Services/VoPathResolverTests.cs` | Update existing ctor calls + add path assertions |
| `DialogEditor.Tests/ViewModels/NodeDetailViewModelPlaybackTests.cs` | **Create** — new test class |

---

## Task 1: Extend VoCheckResult + VoPathResolver

**Files:**
- Modify: `DialogEditor.ViewModels/Services/VoCheckResult.cs`
- Modify: `DialogEditor.ViewModels/Services/VoPathResolver.cs`
- Modify: `DialogEditor.Tests/Services/VoPathResolverTests.cs`

**Interfaces:**
- Produces: `VoCheckResult` with `PrimaryWemPath` and `FemWemPath` (consumed by Task 2's ViewModel)

---

- [ ] **Step 1: Write the failing tests**

Add these new test methods to the end of `VoPathResolverTests` (before the closing `}`).
Note: they won't compile until Step 3 adds the new record fields.

```csharp
// ── PrimaryWemPath and FemWemPath ─────────────────────────────────────

[Fact]
public void Check_KnownSpeaker_PrimaryWemPathContainsExpectedFilename()
{
    var dir = Path.Combine(_voRoot, "eder");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

    var result = VoPathResolver.Check(
        "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

    Assert.NotNull(result!.PrimaryWemPath);
    Assert.True(result.PrimaryWemPath!.EndsWith("test_conv_0001.wem",
        StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void Check_NoFemVariant_FemWemPathIsNull()
{
    var dir = Path.Combine(_voRoot, "eder");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

    var result = VoPathResolver.Check(
        "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

    Assert.Null(result!.FemWemPath);
}

[Fact]
public void Check_FemVariantExists_FemWemPathSet()
{
    var dir = Path.Combine(_voRoot, "eder");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"),     "");
    File.WriteAllText(Path.Combine(dir, "test_conv_0001_fem.wem"), "");

    var result = VoPathResolver.Check(
        "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

    Assert.NotNull(result!.FemWemPath);
    Assert.True(result.FemWemPath!.EndsWith("test_conv_0001_fem.wem",
        StringComparison.OrdinalIgnoreCase));
}

[Fact]
public void Check_NotApplicable_BothPathsNull()
{
    var result = VoPathResolver.Check("any-guid", false, "", 1, "conv", _gameRoot, "poe2");
    Assert.Null(result!.PrimaryWemPath);
    Assert.Null(result.FemWemPath);
}

[Fact]
public void Check_UnknownSpeaker_PrimaryWemPathIsNull()
{
    // Unknown speaker → we cannot resolve the folder, so the path stays null
    var result = VoPathResolver.Check("unknown-guid", true, "", 1, "conv", _gameRoot, "poe2");
    Assert.Null(result!.PrimaryWemPath);
    Assert.Null(result.FemWemPath);
}

[Fact]
public void Check_PrimaryFileMissing_PrimaryWemPathStillSet()
{
    // Even when the file doesn't exist, PrimaryWemPath holds the expected location —
    // the player needs it to attempt playback (and fail gracefully).
    var result = VoPathResolver.Check(
        "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

    Assert.Equal(VoPresence.Missing, result!.Status);
    Assert.NotNull(result.PrimaryWemPath);
}

[Fact]
public void Check_ExternalVO_PrimaryWemPathSet()
{
    var dir = Path.Combine(_voRoot, "eder");
    Directory.CreateDirectory(dir);
    File.WriteAllText(Path.Combine(dir, "00_cv_test_0153.wem"), "");

    var result = VoPathResolver.Check(
        "unknown-guid", false, "eder/00_cv_test_0153", 999, "anything", _gameRoot, "poe2");

    Assert.NotNull(result!.PrimaryWemPath);
    Assert.True(result.PrimaryWemPath!.EndsWith("00_cv_test_0153.wem",
        StringComparison.OrdinalIgnoreCase));
}
```

- [ ] **Step 2: Run tests — verify compile failure**

```
dotnet test DialogEditor.Tests --no-build 2>&1 | head -20
```

Expected: compile error mentioning `'VoCheckResult' does not contain a definition for 'PrimaryWemPath'`

- [ ] **Step 3: Extend VoCheckResult**

Replace the record declaration in `DialogEditor.ViewModels/Services/VoCheckResult.cs`:

```csharp
/// <summary>
/// Result of a VO path check for a single dialog node.
/// </summary>
/// <param name="Status">Whether the primary .wem file was found.</param>
/// <param name="FemaleVariantFound">
/// True when a <c>_fem.wem</c> companion file also exists alongside the primary file.
/// Informational only — does not affect <see cref="Status"/>.
/// </param>
/// <param name="PrimaryWemPath">
/// Full path to the primary <c>.wem</c> file, or <c>null</c> when the path cannot be
/// resolved (NotApplicable nodes, or nodes with an unknown speaker GUID).
/// </param>
/// <param name="FemWemPath">
/// Full path to the <c>_fem.wem</c> companion file, or <c>null</c> if it does not exist.
/// </param>
public record VoCheckResult(
    VoPresence Status,
    bool       FemaleVariantFound,
    string?    PrimaryWemPath,
    string?    FemWemPath);
```

- [ ] **Step 4: Fix existing VoCheckResult constructor calls in VoPathResolver**

In `VoPathResolver.cs`, make three changes:

**Change 1** — NotApplicable return (line ~47):
```csharp
// Before:
return new VoCheckResult(VoPresence.NotApplicable, false);
// After:
return new VoCheckResult(VoPresence.NotApplicable, false, null, null);
```

**Change 2** — Unknown speaker return (line ~69):
```csharp
// Before:
return new VoCheckResult(VoPresence.Missing, false);
// After:
return new VoCheckResult(VoPresence.Missing, false, null, null);
```

**Change 3** — Main return (lines ~78-84). Replace entirely:
```csharp
var primary   = basePath + ".wem";
var fem       = basePath + "_fem.wem";
var femExists = File.Exists(fem);
return new VoCheckResult(
    File.Exists(primary) ? VoPresence.Found : VoPresence.Missing,
    femExists,
    primary,            // always set when speaker is known; file may or may not exist
    femExists ? fem : null);
```

- [ ] **Step 5: Run tests — verify all pass**

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all existing tests plus the 7 new ones pass.

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/Services/VoCheckResult.cs
git add DialogEditor.ViewModels/Services/VoPathResolver.cs
git add DialogEditor.Tests/Services/VoPathResolverTests.cs
git commit -m "feat(vo): extend VoCheckResult with PrimaryWemPath and FemWemPath"
```

---

## Task 2: IVoAudioPlayer + NodeDetailViewModel Playback

**Files:**
- Create: `DialogEditor.ViewModels/Services/IVoAudioPlayer.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Create: `DialogEditor.Tests/ViewModels/NodeDetailViewModelPlaybackTests.cs`

**Interfaces:**
- Consumes: `VoCheckResult.PrimaryWemPath`, `VoCheckResult.FemWemPath` (from Task 1)
- Produces: `IVoAudioPlayer`, `NullVoAudioPlayer`, `NodeDetailViewModel.Player` settable property, `PlayPrimaryCommand`, `PlayFemCommand`, `IsPlayingPrimary`, `IsPlayingFem`, `CanPlayAudio`, `CanPlayFem`, `PlayPrimaryGlyph`, `PlayFemGlyph`, `PlayPrimaryTooltip`, `PlayFemTooltip` (consumed by Task 3's XAML and `VoAudioPlayer`)

---

- [ ] **Step 1: Create IVoAudioPlayer + NullVoAudioPlayer**

Create `DialogEditor.ViewModels/Services/IVoAudioPlayer.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Plays a single .wem voice-over file at a time.
/// Implemented by VoAudioPlayer (Avalonia layer) and NullVoAudioPlayer (no-op default).
/// </summary>
public interface IVoAudioPlayer
{
    /// False when vgmstream-cli.exe is absent — hides play buttons in the UI.
    bool IsAvailable { get; }

    /// Fired when playback ends naturally (NOT when Stop() is called explicitly).
    /// Always raised on the UI thread.
    event Action? PlaybackStopped;

    void Play(string wemPath);

    /// Stops any current playback. Does NOT fire PlaybackStopped.
    void Stop();
}

/// No-op player used as the default in NodeDetailViewModel so existing tests
/// that do not set up a player continue to compile and pass.
public sealed class NullVoAudioPlayer : IVoAudioPlayer
{
    public static readonly NullVoAudioPlayer Instance = new();
    private NullVoAudioPlayer() { }

    public bool IsAvailable => false;
    public event Action? PlaybackStopped;
    public void Play(string wemPath) { }
    public void Stop() { }
}
```

- [ ] **Step 2: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/NodeDetailViewModelPlaybackTests.cs`:

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelPlaybackTests : IDisposable
{
    private readonly NodeDetailViewModel _vm = new();
    private readonly StubVoAudioPlayer   _stub;
    private readonly string              _gameRoot;
    private readonly string              _voRoot;

    public NodeDetailViewModelPlaybackTests()
    {
        Loc.Configure(new StubStringProvider());
        _stub     = new StubVoAudioPlayer();
        _gameRoot = Path.Combine(Path.GetTempPath(), $"VoPlayTest_{Guid.NewGuid():N}");
        _voRoot   = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Directory.CreateDirectory(_voRoot);

        _vm.Player       = _stub;
        _vm.GameRoot     = _gameRoot;
        _vm.ActiveGameId = "poe2";
    }

    public void Dispose()
    {
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    // Plants real .wem stubs under _voRoot and loads the node so VoPathResolver
    // resolves it as Found. Uses ExternalVO to bypass ChatterPrefixService.
    private void PlantAndLoad(bool withFem = false)
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "testline_0001.wem"), "");
        if (withFem)
            File.WriteAllText(Path.Combine(dir, "testline_0001_fem.wem"), "");

        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "",
            ExternalVO: "eder/testline_0001", HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(1, "Test line", "")));
    }

    // ── CanPlayAudio / CanPlayFem ─────────────────────────────────────────

    [Fact]
    public void CanPlayAudio_TrueWhenPlayerAvailableAndFileFound()
    {
        PlantAndLoad();
        Assert.True(_vm.CanPlayAudio);
    }

    [Fact]
    public void CanPlayAudio_FalseWhenPlayerUnavailable()
    {
        _stub.IsAvailable = false;
        PlantAndLoad();
        Assert.False(_vm.CanPlayAudio);
    }

    [Fact]
    public void CanPlayFem_FalseWhenNoFemVariant()
    {
        PlantAndLoad(withFem: false);
        Assert.False(_vm.CanPlayFem);
    }

    [Fact]
    public void CanPlayFem_TrueWhenFemVariantExists()
    {
        PlantAndLoad(withFem: true);
        Assert.True(_vm.CanPlayFem);
    }

    // ── PlayPrimaryCommand ────────────────────────────────────────────────

    [Fact]
    public void PlayPrimaryCommand_CallsPlayerPlay()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.Equal(1, _stub.PlayCallCount);
        Assert.True(_stub.LastPlayPath?.EndsWith(".wem", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void PlayPrimaryCommand_SetsIsPlayingPrimaryTrue()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.True(_vm.IsPlayingPrimary);
        Assert.False(_vm.IsPlayingFem);
    }

    [Fact]
    public void PlayPrimaryCommand_GlyphChangesToStop()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.Equal("■", _vm.PlayPrimaryGlyph);
    }

    [Fact]
    public void PlayPrimaryCommand_WhenAlreadyPlayingPrimary_StopsAndResetsState()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null); // start
        _vm.PlayPrimaryCommand.Execute(null); // toggle off
        Assert.False(_vm.IsPlayingPrimary);
        Assert.Equal("▶", _vm.PlayPrimaryGlyph);
        Assert.Equal(1, _stub.PlayCallCount); // only played once
    }

    // ── PlayFemCommand ────────────────────────────────────────────────────

    [Fact]
    public void PlayFemCommand_SetsIsPlayingFemTrue()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayFemCommand.Execute(null);
        Assert.True(_vm.IsPlayingFem);
        Assert.False(_vm.IsPlayingPrimary);
    }

    [Fact]
    public void PlayFemCommand_WhilePrimaryPlaying_SwitchesToFem()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.True(_vm.IsPlayingPrimary);

        _vm.PlayFemCommand.Execute(null); // switch mid-play
        Assert.False(_vm.IsPlayingPrimary);
        Assert.True(_vm.IsPlayingFem);
        Assert.Equal(2, _stub.PlayCallCount); // two plays: primary then fem
    }

    [Fact]
    public void PlayFemCommand_WhenAlreadyPlayingFem_Stops()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayFemCommand.Execute(null); // start fem
        _vm.PlayFemCommand.Execute(null); // toggle off
        Assert.False(_vm.IsPlayingFem);
        Assert.Equal(1, _stub.PlayCallCount);
    }

    // ── PlaybackStopped (natural end) ────────────────────────────────────

    [Fact]
    public void NaturalPlaybackStopped_ResetsIsPlayingPrimary()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        _stub.FirePlaybackStopped(); // simulate track ending naturally
        Assert.False(_vm.IsPlayingPrimary);
        Assert.Equal("▶", _vm.PlayPrimaryGlyph);
    }

    [Fact]
    public void NaturalPlaybackStopped_ResetsIsPlayingFem()
    {
        PlantAndLoad(withFem: true);
        _vm.PlayFemCommand.Execute(null);
        _stub.FirePlaybackStopped();
        Assert.False(_vm.IsPlayingFem);
        Assert.Equal("▶", _vm.PlayFemGlyph);
    }

    // ── Node navigation stops playback ───────────────────────────────────

    [Fact]
    public void LoadNewNode_StopsCurrentPlayback()
    {
        PlantAndLoad();
        _vm.PlayPrimaryCommand.Execute(null);
        Assert.True(_vm.IsPlayingPrimary);

        PlantAndLoad(); // loading any node calls NotifyAllProxies which stops player
        Assert.False(_vm.IsPlayingPrimary);
    }

    // ── Stub ─────────────────────────────────────────────────────────────

    private sealed class StubVoAudioPlayer : IVoAudioPlayer
    {
        public bool IsAvailable { get; set; } = true;
        public event Action? PlaybackStopped;
        public string? LastPlayPath { get; private set; }
        public int PlayCallCount { get; private set; }

        public void Play(string wemPath) { LastPlayPath = wemPath; PlayCallCount++; }

        // Explicit stop — does NOT fire PlaybackStopped (by design, matching VoAudioPlayer).
        public void Stop() { }

        // Simulates natural track end for tests.
        public void FirePlaybackStopped() => PlaybackStopped?.Invoke();
    }
}
```

- [ ] **Step 3: Run tests — verify they fail**

```
dotnet test DialogEditor.Tests --filter "NodeDetailViewModelPlaybackTests" -v quiet
```

Expected: compile error — `'NodeDetailViewModel' does not contain a definition for 'Player'`

- [ ] **Step 4: Extend NodeDetailViewModel**

Add the following to `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`.

**A) After the `private VoCheckResult? _voCheck;` field declaration (around line 138), add:**

```csharp
// ── Audio playback ────────────────────────────────────────────────────
private enum Playing { None, Primary, Female }
private Playing _currentlyPlaying = Playing.None;
private IVoAudioPlayer _player = NullVoAudioPlayer.Instance;

public IVoAudioPlayer Player
{
    get => _player;
    set
    {
        _player.PlaybackStopped -= OnPlaybackStopped;
        _player = value;
        _player.PlaybackStopped += OnPlaybackStopped;
        OnPropertyChanged(nameof(CanPlayAudio));
    }
}

public bool IsPlayingPrimary => _currentlyPlaying == Playing.Primary;
public bool IsPlayingFem     => _currentlyPlaying == Playing.Female;
public bool CanPlayAudio     => _player.IsAvailable && VoStatusIsFound;
public bool CanPlayFem       => CanPlayAudio && (_voCheck?.FemaleVariantFound ?? false);

public string PlayPrimaryGlyph   => _currentlyPlaying == Playing.Primary ? "■" : "▶";
public string PlayFemGlyph       => _currentlyPlaying == Playing.Female  ? "■" : "▶";
public string PlayPrimaryTooltip => _currentlyPlaying == Playing.Primary
    ? Loc.Get("ToolTip_StopVO") : Loc.Get("ToolTip_PlayVO");
public string PlayFemTooltip => _currentlyPlaying == Playing.Female
    ? Loc.Get("ToolTip_StopFemVO") : Loc.Get("ToolTip_PlayFemVO");

[RelayCommand]
private void PlayPrimary()
{
    if (_currentlyPlaying == Playing.Primary)
    {
        _player.Stop();
        SetPlaying(Playing.None);
    }
    else
    {
        _player.Stop();
        _player.Play(_voCheck!.PrimaryWemPath!);
        SetPlaying(Playing.Primary);
    }
}

[RelayCommand]
private void PlayFem()
{
    if (_currentlyPlaying == Playing.Female)
    {
        _player.Stop();
        SetPlaying(Playing.None);
    }
    else
    {
        _player.Stop();
        _player.Play(_voCheck!.FemWemPath!);
        SetPlaying(Playing.Female);
    }
}

private void SetPlaying(Playing p)
{
    _currentlyPlaying = p;
    OnPropertyChanged(nameof(IsPlayingPrimary));
    OnPropertyChanged(nameof(IsPlayingFem));
    OnPropertyChanged(nameof(PlayPrimaryGlyph));
    OnPropertyChanged(nameof(PlayFemGlyph));
    OnPropertyChanged(nameof(PlayPrimaryTooltip));
    OnPropertyChanged(nameof(PlayFemTooltip));
}

// Called when a track ends naturally — Stop() does NOT trigger this.
private void OnPlaybackStopped() => SetPlaying(Playing.None);
```

**B) In `NotifyAllProxies()`, add two lines at the very top of the method (before `OnPropertyChanged(nameof(DefaultText))`), to clean up playback when the node changes:**

```csharp
// Stop without firing PlaybackStopped; reset state explicitly.
_player.Stop();
_currentlyPlaying = Playing.None;
```

**C) In `NotifyAllProxies()`, append these eight lines immediately after the existing `OnPropertyChanged(nameof(VoStatusIsFound));` line:**

```csharp
OnPropertyChanged(nameof(CanPlayAudio));
OnPropertyChanged(nameof(CanPlayFem));
OnPropertyChanged(nameof(IsPlayingPrimary));
OnPropertyChanged(nameof(IsPlayingFem));
OnPropertyChanged(nameof(PlayPrimaryGlyph));
OnPropertyChanged(nameof(PlayFemGlyph));
OnPropertyChanged(nameof(PlayPrimaryTooltip));
OnPropertyChanged(nameof(PlayFemTooltip));
```

- [ ] **Step 5: Run tests — verify all pass**

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all tests pass, including the 11 new playback tests.

- [ ] **Step 6: Commit**

```
git add DialogEditor.ViewModels/Services/IVoAudioPlayer.cs
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
git add DialogEditor.Tests/ViewModels/NodeDetailViewModelPlaybackTests.cs
git commit -m "feat(vo): add IVoAudioPlayer, NullVoAudioPlayer, and playback commands to NodeDetailViewModel"
```

---

## Task 3: VoAudioPlayer, vgmstream binary, and View wiring

**Files:**
- Create: `DialogEditor.Avalonia/Audio/VoAudioPlayer.cs`
- Modify: `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Binary: `DialogEditor.Avalonia/tools/vgmstream-cli.exe` (place manually)

**Interfaces:**
- Consumes: `IVoAudioPlayer` (Task 2), `NodeDetailViewModel.Player` settable property (Task 2), `VoCheckResult.PrimaryWemPath/FemWemPath` (Task 1)
- Produces: `VoAudioPlayer` (registered by MainWindow), UI play buttons bound to `PlayPrimaryCommand` / `PlayFemCommand`

**Note:** No automated tests for actual audio output — requires a real binary and `.wem` files; verified manually. The test suite from Task 2 already verifies all ViewModel logic with a stub.

---

- [ ] **Step 1: Add NAudio NuGet to DialogEditor.Avalonia.csproj**

In `DialogEditor.Avalonia/DialogEditor.Avalonia.csproj`, add `NAudio` to the first `<ItemGroup>` that has `PackageReference` elements:

```xml
<PackageReference Include="NAudio" Version="2.2.1" />
```

Also add a `Content` entry for the vgmstream binary in the last `<ItemGroup>` (after the CHANGELOG entry):

```xml
<!-- Bundle vgmstream-cli for PoE2 .wem audio preview. Attribution: THIRD_PARTY_LICENSES.md -->
<Content Include="tools\vgmstream-cli.exe">
  <CopyToOutputDirectory>PreserveNewest</CopyToOutputDirectory>
</Content>
```

- [ ] **Step 2: Place vgmstream-cli.exe**

Download the Windows CLI binary from https://github.com/vgmstream/vgmstream/releases (look for `vgmstream-win64-cli.zip`). Extract `vgmstream-cli.exe` and copy it to:

```
DialogEditor.Avalonia/tools/vgmstream-cli.exe
```

The `tools/` directory must be created if it does not exist. `.gitignore` does not exclude `*.exe`, so the binary will be tracked by git.

- [ ] **Step 3: Create VoAudioPlayer**

Create `DialogEditor.Avalonia/Audio/VoAudioPlayer.cs`:

```csharp
using System.Diagnostics;
using Avalonia.Threading;
using NAudio.Wave;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Audio;

/// <summary>
/// Plays a single PoE2 .wem voice-over file at a time by shelling out to
/// vgmstream-cli.exe (ISC, bundled in tools/) to decode it to a temp WAV,
/// then playing that WAV via NAudio. All public members are UI-thread-safe
/// because Play/Stop are called from commands on the UI thread.
///
/// PlaybackStopped is always raised on the UI thread so NodeDetailViewModel
/// (which has no Avalonia reference) can call SetPlaying() directly.
/// Stop() does NOT raise PlaybackStopped — only natural track completion does.
/// </summary>
public sealed class VoAudioPlayer : IVoAudioPlayer, IDisposable
{
    private static readonly string ToolPath =
        Path.Combine(AppContext.BaseDirectory, "tools", "vgmstream-cli.exe");

    public bool IsAvailable { get; } = File.Exists(ToolPath);

    public event Action? PlaybackStopped;

    private WaveOutEvent?  _output;
    private AudioFileReader? _reader;
    private string? _tempFile;
    private bool _manualStop;
    // Incremented on every Play/Stop to cancel in-flight background work.
    private int _generation;

    public void Play(string wemPath)
    {
        StopAndCleanup();        // increments _generation, cleans up previous
        _manualStop = false;
        var gen = ++_generation; // this play's identity token

        _ = Task.Run(async () =>
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"vo_{Guid.NewGuid():N}.wav");
            try
            {
                var psi = new ProcessStartInfo(ToolPath, $"-o \"{tempFile}\" \"{wemPath}\"")
                {
                    CreateNoWindow  = true,
                    UseShellExecute = false,
                };
                using var proc = Process.Start(psi)!;
                await proc.WaitForExitAsync();

                if (proc.ExitCode != 0)
                {
                    AppLog.Warn($"vgmstream-cli exited {proc.ExitCode} for: {wemPath}");
                    TryDeleteTemp(tempFile);
                    return;
                }

                // Abort if a newer Play/Stop invalidated this request.
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
                        AppLog.Warn("VoAudioPlayer: NAudio failed to start", ex);
                        TryDeleteTemp(tempFile);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Warn($"VoAudioPlayer.Play failed for: {wemPath}", ex);
                TryDeleteTemp(tempFile);
            }
        });
    }

    public void Stop()
    {
        _manualStop = true;
        StopAndCleanup();
    }

    private void StopAndCleanup()
    {
        _generation++;          // invalidates any in-flight Task.Run
        _output?.Stop();
        Cleanup();
    }

    private void OnNaturalPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // NAudio calls this from its own thread; marshal to UI before notifying ViewModel.
        Dispatcher.UIThread.Post(() =>
        {
            Cleanup();
            if (!_manualStop)
                PlaybackStopped?.Invoke();
        });
    }

    private void Cleanup()
    {
        // Null before dispose so a concurrent second Cleanup call is a no-op.
        var output   = _output;   _output   = null;
        var reader   = _reader;   _reader   = null;
        var tempFile = _tempFile; _tempFile = null;
        output?.Dispose();
        reader?.Dispose();
        TryDeleteTemp(tempFile);
    }

    private static void TryDeleteTemp(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch { /* best-effort; file may still be open */ }
    }

    public void Dispose()
    {
        _manualStop = true;
        StopAndCleanup();
    }
}
```

- [ ] **Step 4: Wire VoAudioPlayer in MainWindow**

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`:

**A) Add the using at the top of the file:**
```csharp
using DialogEditor.Avalonia.Audio;
```

**B) After the `DataContext = new MainWindowViewModel(...)` block and after all `vm.xxx = ...` assignments (around line 94, just before the `if (!vm.IsBrowserExpanded)` check), add:**

```csharp
var audioPlayer = new VoAudioPlayer();
vm.Detail.Player = audioPlayer;
Closed += (_, _) => audioPlayer.Dispose();
```

- [ ] **Step 5: Add localisation strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, after the `VoStatus_Missing` line and before `VoValidation_Title`, add:

```xml
    <!-- ── Voice-Over audio playback ───────────────────────────────────── -->
    <sys:String x:Key="ToolTip_PlayVO">Play voice-over</sys:String>
    <sys:String x:Key="ToolTip_StopVO">Stop playback</sys:String>
    <sys:String x:Key="ToolTip_PlayFemVO">Play female voice-over variant</sys:String>
    <sys:String x:Key="ToolTip_StopFemVO">Stop playback (female variant)</sys:String>
```

- [ ] **Step 6: Add play buttons to NodeDetailView.axaml**

In `DialogEditor.Avalonia/Views/NodeDetailView.axaml`, the VO status `StackPanel` currently ends at `</StackPanel>` after the two `TextBlock` elements (around lines 207-219). Replace it with:

```xml
<!-- VO file status — PoE2 only; shown when HasVO or ExternalVO set -->
<StackPanel Orientation="Horizontal" Margin="0,2,0,4"
            IsVisible="{Binding HasVoStatus}"
            ToolTip.Tip="{Binding VoStatusText}"
            AutomationProperties.HelpText="{Binding VoStatusText}">
    <TextBlock Text="{Binding VoStatusGlyph}"
               Foreground="{Binding VoStatusIsFound, Converter={StaticResource BoolToVoStatusBrush}}"
               FontSize="{DynamicResource FontSize.Small}"
               VerticalAlignment="Center" Margin="0,0,4,0"/>
    <TextBlock Text="{Binding VoStatusText}"
               Foreground="{Binding VoStatusIsFound, Converter={StaticResource BoolToVoStatusBrush}}"
               FontSize="{DynamicResource FontSize.Small}"
               VerticalAlignment="Center"/>
    <!-- Play buttons — only when vgmstream-cli is present and the file is found -->
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

- [ ] **Step 7: Build to verify no errors**

```
dotnet build DialogEditor.Avalonia -c Release 2>&1 | tail -5
```

Expected: `Build succeeded` with 0 errors.

- [ ] **Step 8: Run the full test suite**

```
dotnet test DialogEditor.Tests -v quiet
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```
git add DialogEditor.Avalonia/Audio/VoAudioPlayer.cs
git add DialogEditor.Avalonia/DialogEditor.Avalonia.csproj
git add DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git add DialogEditor.Avalonia/Views/NodeDetailView.axaml
git add DialogEditor.Avalonia/Resources/Strings.axaml
git add DialogEditor.Avalonia/tools/vgmstream-cli.exe
git commit -m "feat(vo): add VoAudioPlayer, wire play buttons in NodeDetailView"
```

---

## Manual Verification Checklist

After all three tasks are committed, verify by hand:

- [ ] Open a PoE2 project with a node that has a known `.wem` path (ExternalVO or standard HasVO)
- [ ] ▶ button appears next to the VO status text; hovering shows "Play voice-over" tooltip
- [ ] Clicking ▶ plays audio; button changes to ■
- [ ] Clicking ■ stops audio; button reverts to ▶
- [ ] Navigating to a different node while playing stops audio cleanly
- [ ] When both primary and female variants exist, two buttons (▶ and ▶) appear; clicking the second while first plays stops first and starts second
- [ ] When `tools/vgmstream-cli.exe` is absent, play buttons do not appear
- [ ] When the `.wem` file is missing, play buttons do not appear (✗ status still shows)
- [ ] Corrupt `.wem` → vgmstream exits non-zero → `AppLog.Warn` written, button resets to ▶ after a moment
