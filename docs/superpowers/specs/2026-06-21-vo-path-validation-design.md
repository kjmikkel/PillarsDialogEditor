# VO Path Validation — Design Spec

**Date:** 2026-06-21  
**Scope:** PoE2 only. PoE1 audio is in Unity asset archives; deferred per research.  
**No audio playback** — existence check only. Playback is a separate, future concern.

---

## Background

PoE2 story VO files are loose `.wem` files on disk at a deterministic path:

```
{GameRoot}\PillarsOfEternityII_Data\StreamingAssets\Audio\Windows\Voices\English(US)\
    {chatterPrefix}\{conversationName}_{nodeId:0000}.wem
```

`ChatterPrefix` comes from `speakers.gamedatabundle → Components[0].ChatterPrefix`.  
`ExternalVO` on a `TalkNode` overrides the `{chatterPrefix}/{convName}_{nodeId}` portion; the stored value never includes `.wem`.  
Both `HasVO` and `ExternalVO` are already persisted in `ConversationNode` and round-tripped by the serializer.

**Which nodes are checked:** only nodes where `HasVO == true` OR `ExternalVO` is non-empty.  
Script nodes are never voiced; all other categories (NPC, Player, Narrator) may be.

---

## Feature Overview

Two tiers, both PoE2-only (silently absent for PoE1 and when no game folder is open):

1. **Per-node status** — instant indicator in the node detail panel when a node is selected
2. **Validation window** — batch scan of the whole open conversation, opened from **Test → Validate Voice-Over…**

---

## §1 — Parser & ChatterPrefixService

### Parser change

`Poe2SpeakerNameParser` gains a second public method:

```csharp
public static IReadOnlyDictionary<string, string> ParseChatterPrefixes(string json)
```

Returns `Dictionary<string, string>` keyed by speaker GUID (case-insensitive), value = `ChatterPrefix` string. Reads `Components[0].ChatterPrefix` from each `SpeakerGameData` entry. Skips entries where `ChatterPrefix` is null or empty. `ParseChatterPrefixesFile(string path)` is the file-reading wrapper.

JSON structure being parsed (confirmed against GOG install):
```json
{
  "GameDataObjects": [
    {
      "$type": "Game.GameData.SpeakerGameData, ...",
      "DebugName": "SPK_Companion_Eder",
      "ID": "9c5f12c9-...",
      "Components": [
        {
          "$type": "Game.GameData.SpeakerComponent, ...",
          "Gender": "Male",
          "ChatterPrefix": "eder",
          ...
        }
      ]
    }
  ]
}
```

### ChatterPrefixService

New static class in `DialogEditor.ViewModels.Services`, mirrors `SpeakerNameService`:

```csharp
public static class ChatterPrefixService
{
    public static void Register(IReadOnlyDictionary<string, string> prefixes);
    public static string? GetPrefix(string speakerGuid);  // null if unknown
    public static void Clear();
}
```

Narrator GUID (`6a99a109-0000-0000-0000-000000000000`) is hardcoded to `"narrator"` inside `VoPathResolver` (not stored in the service — mirrors the game's hardcoded string in `Conversation.cs:342`).

### IGameDataProvider extension

```csharp
// Default implementation returns empty dict — PoE1 and stubs unaffected
public virtual IReadOnlyDictionary<string, string> LoadChatterPrefixes() => new Dictionary<string, string>();
```

`Poe2GameDataProvider` overrides it: reads `speakers.gamedatabundle` via `Poe2SpeakerNameParser.ParseChatterPrefixesFile`.

### Registration in MainWindowViewModel

In `LoadDirectory`, after the existing `SpeakerNameService.Register(...)`:

```csharp
ChatterPrefixService.Register(provider.LoadChatterPrefixes());
```

---

## §2 — VoPathResolver & VoCheckResult

### VoCheckResult (DialogEditor.ViewModels.Services)

```csharp
public enum VoPresence { NotApplicable, Found, Missing }

public record VoCheckResult(VoPresence Status, bool FemaleVariantFound);
```

`NotApplicable` — node has neither `HasVO` nor `ExternalVO`; nothing to check.  
`Found` / `Missing` — primary `.wem` exists or does not.  
`FemaleVariantFound` — `_fem.wem` exists alongside the primary file (informational only; does not affect `Status`).

Both types live in `DialogEditor.ViewModels.Services` (not Core): `VoPathResolver` depends on `ChatterPrefixService`, which is a ViewModels service, so Core would create a circular reference.

### VoPathResolver (DialogEditor.ViewModels.Services)

Static class. One public entry point taking only the fields it needs (no `ConversationNode` or `NodeViewModel` dependency — keeps it trivially unit-testable):

```csharp
public static VoCheckResult? Check(
    string speakerGuid,
    bool hasVO,
    string externalVO,
    int nodeId,
    string conversationName,
    string gameRoot,
    string activeGameId)
```

**Returns `null`** if `activeGameId != "poe2"` or `gameRoot` is empty — callers treat null as "not applicable, hide the status row".

**Returns `VoCheckResult(NotApplicable, false)`** if `!hasVO && string.IsNullOrEmpty(externalVO)`.

**Path construction:**

```csharp
const string NarratorGuid = "6a99a109-0000-0000-0000-000000000000";
var voRoot = Path.Combine(gameRoot,
    "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");

string basePath;
if (!string.IsNullOrEmpty(externalVO))
{
    // ExternalVO: "eder/00_cv_himuihi_0153"  (no .wem, may contain a subpath)
    basePath = Path.Combine(voRoot, externalVO);
}
else
{
    var chatterPrefix = string.Equals(speakerGuid, NarratorGuid,
                            StringComparison.OrdinalIgnoreCase)
                        ? "narrator"
                        : ChatterPrefixService.GetPrefix(speakerGuid);

    if (string.IsNullOrEmpty(chatterPrefix))
        return new VoCheckResult(VoPresence.Missing, false); // speaker unknown → treat as missing

    basePath = Path.Combine(voRoot,
        chatterPrefix.ToLowerInvariant(),
        $"{conversationName.ToLowerInvariant()}_{nodeId:0000}");
}

var primary = basePath + ".wem";
var fem     = basePath + "_fem.wem";
return new VoCheckResult(
    File.Exists(primary) ? VoPresence.Found : VoPresence.Missing,
    File.Exists(fem));
```

**ExternalVO path separator note:** `ExternalVO` values in shipped conversations use `/` (forward slash). `Path.Combine` on Windows normalises this correctly.

---

## §3 — Per-node status in NodeDetailViewModel + NodeDetailView

### NodeDetailViewModel changes

New settable property (set by `MainWindowViewModel.LoadDirectory`):

```csharp
public string GameRoot { get; set; } = string.Empty;
```

`ActiveGameId` already exists.

New computed property, re-evaluated inside `NotifyAllProxies()`:

```csharp
private VoCheckResult? _voCheck;

public bool   HasVoStatus      => _voCheck is { Status: not VoPresence.NotApplicable };
public string VoStatusGlyph    => _voCheck?.Status == VoPresence.Found ? "✓" : "✗";
public string VoStatusText     // localised — see strings below
public bool   VoStatusIsFound  => _voCheck?.Status == VoPresence.Found;
```

Inside `NotifyAllProxies()`:

```csharp
_voCheck = _node is null ? null
    : VoPathResolver.Check(
        _node.SpeakerGuid, _node.HasVO, _node.ExternalVO, _node.NodeId,
        Canvas?.ConversationName ?? "", GameRoot, ActiveGameId);
OnPropertyChanged(nameof(HasVoStatus));
OnPropertyChanged(nameof(VoStatusGlyph));
OnPropertyChanged(nameof(VoStatusText));
OnPropertyChanged(nameof(VoStatusIsFound));
```

`VoStatusText` cases:
- `Found`, no female: `Loc.Get("VoStatus_Found")`  → `"VO file found"`
- `Found`, female too: `Loc.Get("VoStatus_FoundWithFem")` → `"VO file found (+ female variant)"`
- `Missing`: `Loc.Get("VoStatus_Missing")` → `"VO file missing"`

### NodeDetailView.axaml changes

Immediately after the `ExternalVO` TextBox and the `HasVO` CheckBox, inside the **Voice** group:

```xml
<!-- VO path validation status (PoE2 only, shown when HasVO or ExternalVO set) -->
<StackPanel Orientation="Horizontal" Margin="0,2,0,4"
            IsVisible="{Binding HasVoStatus}">
    <TextBlock Text="{Binding VoStatusGlyph}"
               Foreground="{Binding VoStatusIsFound,
                   Converter={StaticResource BoolToVoStatusBrush}}"
               FontSize="{DynamicResource FontSize.Small}"
               VerticalAlignment="Center" Margin="0,0,4,0"/>
    <TextBlock Text="{Binding VoStatusText}"
               Foreground="{Binding VoStatusIsFound,
                   Converter={StaticResource BoolToVoStatusBrush}}"
               FontSize="{DynamicResource FontSize.Small}"
               VerticalAlignment="Center"/>
</StackPanel>
```

`BoolToVoStatusBrush` converter: `true` → `Brush.Severity.Success`, `false` → `Brush.Severity.Error`. These tokens already exist in `Tokens.axaml`. Add as a static resource alongside the other bool converters.

---

## §4 — VoValidationViewModel & VoValidationWindow

### VoValidationIssue (DialogEditor.ViewModels)

```csharp
public record VoValidationIssue(int NodeId, string TextPreview, bool IsMissing);
```

### VoValidationViewModel

Constructor: `(IReadOnlyList<ConversationNode> nodes, string conversationName, string gameRoot, string activeGameId)` where `ConversationNode` is the Core model (from `ConversationEditSnapshot.Nodes`).

Properties:
- `bool IsRunning` — `[ObservableProperty]`
- `ObservableCollection<VoValidationIssue> Results`
- `string SummaryText` — `[ObservableProperty]`, e.g. `"Checked 47 nodes — 3 missing"`
- `IRelayCommand CancelCommand` — enabled while `IsRunning`
- `IRelayCommand RunAgainCommand` — enabled while `!IsRunning`

`RunAsync()` flow:
1. Cancel any previous `CancellationTokenSource`, create new one.
2. Clear `Results`. Set `IsRunning = true`. Set `SummaryText = Loc.Get("VoValidation_Running")`.
3. Run a `Task.Run(...)` that iterates nodes, calls `VoPathResolver.Check(...)`, posts each result to `Dispatcher.UIThread` via a callback. Checks `CancellationToken` between iterations.
4. On completion (or cancellation): set `IsRunning = false`, set `SummaryText` to the final count string.

Text preview: first 60 characters of `DefaultText`, trimmed, with `"…"` appended if longer.

`CancelCommand` calls `_cts.Cancel()`.  
`RunAgainCommand` calls `RunAsync()`.

Only nodes where `HasVO || !string.IsNullOrEmpty(ExternalVO)` are included in the iteration; others are silently skipped (not counted in totals).

### VoValidationWindow

Non-modal `Window` — opened via `Show()` from `MainWindow.axaml.cs`. Title: `Loc.Get("VoValidation_Title")` → `"Voice-Over Validation"`. Carries the app icon per CLAUDE.md rule. Has a `FocusHintBar` (per accessibility pattern). Resizable with `MinWidth="420" MinHeight="280"`.

Layout (top to bottom):
1. **Summary row**: `SummaryText` TextBlock, left-aligned.
2. **Progress bar**: `IsIndeterminate="True"`, `IsVisible="{Binding IsRunning}"`.
3. **Button row**: Cancel button (`IsVisible="{Binding IsRunning}"`), Run Again button (`IsVisible="{Binding IsRunning, Converter={StaticResource InverseBoolToVis}}"`).
4. **Results list**: Scrollable `ItemsControl` over `Results`. Each row shows `NodeId`, truncated `TextPreview`, and a `"✗ Missing"` badge (only `IsMissing` nodes are added to `Results`). Found nodes are not listed — the summary count covers them.

If `Results` is empty after a completed scan, show a single `"All voice-over files found."` message instead of an empty list.

### Menu wiring (MainWindow.axaml)

In `Menu_Test`, after the existing `RestoreBackup` item:

```xml
<Separator/>
<MenuItem Header="{DynamicResource Menu_ValidateVO}"
          Click="ValidateVO_Click"
          IsEnabled="{Binding CanValidateVO}"
          ToolTip.Tip="{DynamicResource ToolTip_ValidateVO}"
          AutomationProperties.HelpText="{DynamicResource ToolTip_ValidateVO}"/>
```

`CanValidateVO` on `MainWindowViewModel`:
```csharp
public bool CanValidateVO => IsProjectOpen && _activeGameId == "poe2";
```

`ValidateVO_Click` in `MainWindow.axaml.cs`:
```csharp
private void ValidateVO_Click(object? sender, RoutedEventArgs e)
{
    var vm = ViewModel.CreateVoValidationViewModel();
    var win = new VoValidationWindow { DataContext = vm };
    win.Show(this);
    vm.RunAsync();
}
```

`MainWindowViewModel.CreateVoValidationViewModel()` builds the VM from the open conversation's nodes, `ConversationName`, `_currentGameDirectory`, and `_activeGameId`.

---

## New Localisation Strings

| Key | English |
|---|---|
| `VoStatus_Found` | `VO file found` |
| `VoStatus_FoundWithFem` | `VO file found (+ female variant)` |
| `VoStatus_Missing` | `VO file missing` |
| `VoValidation_Title` | `Voice-Over Validation` |
| `VoValidation_Running` | `Checking…` |
| `VoValidation_Summary` | `Checked {0} nodes — {1} missing` |
| `VoValidation_AllFound` | `All voice-over files found.` |
| `VoValidation_Cancelled` | `Cancelled. Checked {0} nodes — {1} missing so far.` |
| `Menu_ValidateVO` | `Validate Voice-Over…` |
| `ToolTip_ValidateVO` | `Scan the open conversation for nodes that are flagged as voiced (HasVO or ExternalVO set) but whose audio file could not be found on disk. Only available for Pillars of Eternity II with a game folder open.` |
| `Button_Cancel` | already exists |
| `Button_RunAgain` | `Run Again` |
| `VoValidation_NodeRow` | `Node {0}` |
| `VoValidation_MissingBadge` | `✗ Missing` |

---

## Token Usage

`Brush.Severity.Success` (VO found — green) and `Brush.Severity.Error` (VO missing — red) are already defined in `Tokens.axaml`. No new tokens required.

---

## Files to Create / Modify

| File | Change |
|---|---|
| `DialogEditor.ViewModels/Services/VoCheckResult.cs` | New: `VoPresence` enum + `VoCheckResult` record (ViewModels layer — depends on ChatterPrefixService) |
| `DialogEditor.Core/GameData/IGameDataProvider.cs` | Add default `LoadChatterPrefixes()` |
| `DialogEditor.Core/GameData/Poe2GameDataProvider.cs` | Implement `LoadChatterPrefixes()` |
| `DialogEditor.Core/Parsing/Poe2SpeakerNameParser.cs` | Add `ParseChatterPrefixes()` / `ParseChatterPrefixesFile()` |
| `DialogEditor.ViewModels/Services/ChatterPrefixService.cs` | New static service |
| `DialogEditor.ViewModels/Services/VoPathResolver.cs` | New static resolver |
| `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` | Add `GameRoot`, `_voCheck`, derived display props, refresh in `NotifyAllProxies` |
| `DialogEditor.ViewModels/ViewModels/VoValidationViewModel.cs` | New ViewModel |
| `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | Add `CanValidateVO`, `CreateVoValidationViewModel()`, wire `Detail.GameRoot`, register `ChatterPrefixService` |
| `DialogEditor.Avalonia/Views/NodeDetailView.axaml` | Add VO status row in Voice group |
| `DialogEditor.Avalonia/Views/VoValidationWindow.axaml` | New window |
| `DialogEditor.Avalonia/Views/VoValidationWindow.axaml.cs` | New code-behind |
| `DialogEditor.Avalonia/Views/MainWindow.axaml` | Add `Menu_ValidateVO` item |
| `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | Add `ValidateVO_Click` handler |
| `DialogEditor.Avalonia.Shared/Resources/Strings.axaml` | New localisation strings |
| `DialogEditor.Avalonia/Converters/BoolToVoStatusBrushConverter.cs` | New converter |
| `DialogEditor.Tests/…/VoPathResolverTests.cs` | New: unit tests for path construction + ExternalVO override |
| `DialogEditor.Tests/…/Poe2SpeakerNameParserTests.cs` | Extend: add ChatterPrefix parse tests |
| `DialogEditor.Tests/…/VoValidationViewModelTests.cs` | New: scan logic, cancellation, summary text |

---

## Out of Scope

- Audio playback (future, requires vgmstream-cli or ww2ogg — separate packaging decision)
- PoE1 VO validation (Unity asset archives; deferred per research)
- Female-variant validation as a separate issue category (female variant presence is informational only)
- Scanning across multiple conversations at once
