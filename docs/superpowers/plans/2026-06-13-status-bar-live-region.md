# Status Bar Live Region Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.
>
> **Resumability:** Tasks and steps are checked off (`- [x]`) as they complete. If
> the session is interrupted, the next worker should scan for the first unchecked
> `- [ ]` and resume from there — no other context is required.

**Goal:** Close Gaps.md Accessibility item 8 — make `MainWindow`'s status-bar
feedback (`MainWindowViewModel.StatusText`, the sole feedback channel for save/
error/operation results) audible to screen readers via a polite live region,
without re-announcing item 5's focus hints on every Tab/arrow-key move.

**Architecture:** Add a hidden, zero-size `TextBlock` named `StatusLiveRegion` to
`MainWindow`'s status bar, bound to `StatusText` (not `DisplayStatusText`) with
`AutomationProperties.LiveSetting="Polite"`. A headless probe (run during design)
confirmed `TextBlockAutomationPeer.GetName()` mirrors `Text` and automatically
raises a `PropertyChanged` notification when `Text` changes — no manual
`RaisePropertyChangedEvent` call is needed, this is pure declarative XAML.

**Tech Stack:** C#/.NET 8, Avalonia 11.3.14, CommunityToolkit.Mvvm, xUnit +
Avalonia.Headless.XUnit (`[AvaloniaFact]`).

**Design doc:** `docs/superpowers/specs/2026-06-13-status-bar-live-region-design.md`

---

### Task 1: Failing tests for the live-region element (RED)

**Files:**
- Create: `DialogEditor.Tests/Views/MainWindowStatusLiveRegionTests.cs`

- [ ] **Step 1: Create the test file**

```csharp
using Avalonia.Automation;
using Avalonia.Automation.Peers;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// <summary>
/// Gaps.md a11y item 8: StatusText (save/error/operation results) was never
/// announced to screen readers. A hidden "StatusLiveRegion" TextBlock, bound only
/// to StatusText and marked AutomationProperties.LiveSetting="Polite", announces
/// every StatusText change without re-announcing item 5's focus hints (which live
/// in DisplayStatusText, not StatusText, and are already announced by the normal
/// focus-description mechanism).
///
/// A headless probe (see design doc) confirmed TextBlockAutomationPeer.GetName()
/// mirrors Text and automatically raises a PropertyChanged notification when Text
/// changes — these tests assert on that peer-level behaviour directly.
/// </summary>
public class MainWindowStatusLiveRegionTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowStatusLiveRegionTests()
    {
        Loc.Configure(new StubStringProvider());
        // Fresh settings file so MainWindow's startup ReopenLastProjectOnStartup
        // (triggered by window.Show() -> OnOpened) finds no last project and is a
        // no-op — see project_flaky_test_appsettings.
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwslr_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [AvaloniaFact]
    public void StatusLiveRegion_IsPoliteLiveRegion_ExposedToAutomation()
    {
        var window = new MainWindow();
        window.Show();

        var liveRegion = window.FindControl<TextBlock>("StatusLiveRegion");
        Assert.NotNull(liveRegion);

        Assert.Equal(AutomationLiveSetting.Polite, AutomationProperties.GetLiveSetting(liveRegion!));

        var peer = ControlAutomationPeer.CreatePeerForElement(liveRegion!);
        Assert.True(peer.IsControlElement());
    }

    [AvaloniaFact]
    public void StatusLiveRegion_AnnouncesStatusTextChanges_NotFocusHintChanges()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();

        var liveRegion = window.FindControl<TextBlock>("StatusLiveRegion")!;
        var peer = ControlAutomationPeer.CreatePeerForElement(liveRegion);

        vm.StatusText = "Saved";
        Assert.Equal("Saved", peer.GetName());

        // A focus hint changes the visible DisplayStatusText, but must NOT change
        // the live region's announced name — that would duplicate the screen
        // reader's normal focus-description announcement.
        vm.FocusHintText = "Opens the settings dialog";
        Assert.Equal("Opens the settings dialog", vm.DisplayStatusText);
        Assert.Equal("Saved", peer.GetName());

        // A genuine status change while a focus hint is active still announces.
        vm.StatusText = "Project saved";
        Assert.Equal("Project saved", peer.GetName());
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail (RED)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowStatusLiveRegionTests"`

Expected: FAIL. `window.FindControl<TextBlock>("StatusLiveRegion")` returns `null`
because no element with that name exists yet — the first test fails on
`Assert.NotNull(liveRegion)`, and the second fails with a `NullReferenceException`
on `ControlAutomationPeer.CreatePeerForElement(liveRegion)`.

- [ ] **Step 3: Commit the failing tests**

```bash
git add DialogEditor.Tests/Views/MainWindowStatusLiveRegionTests.cs
git commit -m "test(a11y): MainWindow status bar announces StatusText via a polite live region"
```

---

### Task 2: Add the hidden live-region TextBlock (GREEN)

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml:332-346`

- [ ] **Step 1: Add the TextBlock to the status bar Grid**

In `MainWindow.axaml`, the status bar is:

```xml
        <!-- Status bar -->
        <Border Grid.Row="2" Background="{DynamicResource Brush.Surface.Card}" Padding="8,4">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Grid.Column="0" Text="{Binding DisplayStatusText}"
                           Foreground="{DynamicResource Brush.Text.Muted}" FontSize="11" VerticalAlignment="Center"/>
                <Button Grid.Column="1"
                        Content="{StaticResource MainWindow_ResolveConflictsButton}"
                        Command="{Binding ResolveConflictsCommand}"
                        IsVisible="{Binding HasPendingConflictResolution}"
                        ToolTip.Tip="{StaticResource MainWindow_ResolveConflictsTooltip}"
                        AutomationProperties.HelpText="{StaticResource MainWindow_ResolveConflictsTooltip}"
                        Background="{DynamicResource Brush.Button.Caution.Background}" Foreground="White" BorderThickness="0"
                        Padding="12,2" FontSize="11"/>
            </Grid>
        </Border>
```

Add a hidden `TextBlock` bound to `StatusText` (not `DisplayStatusText`) as the
first child of the `Grid`, immediately before the existing visible `TextBlock`:

```xml
        <!-- Status bar -->
        <Border Grid.Row="2" Background="{DynamicResource Brush.Surface.Card}" Padding="8,4">
            <Grid ColumnDefinitions="*,Auto">
                <!-- Hidden polite live region: announces StatusText (save/error/
                     operation results) to screen readers. Deliberately NOT bound to
                     DisplayStatusText — item 5's focus hints are already announced
                     by the normal focus-description mechanism, and re-announcing
                     them here on every Tab/arrow-key move would be duplicate
                     chatter. See Gaps.md a11y item 8. -->
                <TextBlock x:Name="StatusLiveRegion" Grid.Column="0" Text="{Binding StatusText}"
                           Width="0" Height="0" Opacity="0" IsHitTestVisible="False" ClipToBounds="True"
                           AutomationProperties.LiveSetting="Polite"/>
                <TextBlock Grid.Column="0" Text="{Binding DisplayStatusText}"
                           Foreground="{DynamicResource Brush.Text.Muted}" FontSize="11" VerticalAlignment="Center"/>
                <Button Grid.Column="1"
                        Content="{StaticResource MainWindow_ResolveConflictsButton}"
                        Command="{Binding ResolveConflictsCommand}"
                        IsVisible="{Binding HasPendingConflictResolution}"
                        ToolTip.Tip="{StaticResource MainWindow_ResolveConflictsTooltip}"
                        AutomationProperties.HelpText="{StaticResource MainWindow_ResolveConflictsTooltip}"
                        Background="{DynamicResource Brush.Button.Caution.Background}" Foreground="White" BorderThickness="0"
                        Padding="12,2" FontSize="11"/>
            </Grid>
        </Border>
```

- [ ] **Step 2: Run the new tests (GREEN)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowStatusLiveRegionTests"`

Expected: PASS (both tests from Task 1).

- [ ] **Step 3: Run the full suite to confirm no regressions**

Run: `dotnet test DialogEditor.Tests`

Expected: PASS, total test count = baseline (1332) + 2.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml
git commit -m "feat(a11y): announce status bar feedback via a polite live region"
```

---

## Task 3: Update Gaps.md and final verification

**Files:**
- Modify: `Gaps.md` (item 8)

- [ ] **Step 1: Mark item 8 as implemented**

Replace the item 8 entry (currently):

```
8. **Status bar feedback is never announced.** `StatusText` (`MainWindow.axaml`) is the
   sole feedback for many operations, rendered as 11px muted text, and screen readers
   won't announce changes. Avalonia supports `AutomationProperties.LiveSetting` — mark the
   status `TextBlock` as a polite live region so save/error results are spoken.
```

with:

```
8. **Status bar feedback is never announced. ✅ IMPLEMENTED (2026-06-13).** A
   hidden `StatusLiveRegion` TextBlock in `MainWindow`'s status bar, bound only to
   `StatusText` (not `DisplayStatusText`, so item 5's focus hints don't trigger
   duplicate announcements when tabbing around) and marked
   `AutomationProperties.LiveSetting="Polite"`, announces every operation result
   (save/error/project-opened/etc.) to screen readers. A headless probe confirmed
   `TextBlockAutomationPeer.GetName()` mirrors `Text` and automatically raises a
   `PropertyChanged` notification on change — purely declarative, no manual
   `RaisePropertyChangedEvent` call needed. Design:
   `docs/superpowers/specs/2026-06-13-status-bar-live-region-design.md`.
```

- [ ] **Step 2: Run the full suite one last time**

Run: `dotnet test DialogEditor.Tests`

Expected: PASS.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark a11y item 8 (status bar live region) implemented"
```
