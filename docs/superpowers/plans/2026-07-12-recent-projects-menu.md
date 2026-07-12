# Recent Projects Menu Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a File ▸ Recent Projects submenu (MRU, max 10) that reopens recently opened/created/saved-as projects, with a keep-until-clicked policy for missing files.

**Architecture:** MRU list persisted in `AppSettings` (same `settings.json`), mutated only through three static helpers. `MainWindowViewModel` records at the three sites that already set `LastProjectPath`, exposes the list, and hosts open/clear commands routed through the existing `GuardDirtyThen` + `LoadProjectAsync` funnel. A small `RecentProjectMissingDialog` (modelled on `ForceDeleteDialog`) backs the missing-file remove-offer via a testable delegate. The submenu is rebuilt in thin code-behind on `SubmenuOpened`.

**Tech Stack:** C# / .NET 8, Avalonia, CommunityToolkit.Mvvm (`[RelayCommand]`), xUnit.

## Global Constraints

- **Localisation:** no user-visible string hard-coded in XAML or C#. Every label/tooltip/status/dialog string is a key in `DialogEditor.Avalonia/Resources/Strings.axaml`, referenced via `{DynamicResource}` (XAML) or `Loc.Get`/`Loc.Format` (C#). Enforced by `NoHardcodedUiStringsTests` and `NoStaticStringResourceTests`.
- **Tooltips:** every new interactive control carries a detailed `ToolTip.Tip` and a mirrored `AutomationProperties.HelpText`. OK/Cancel-style confirmation buttons are the only exemption.
- **Window icon:** every `<Window>` sets `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- **Error handling (production code):** every caught exception logs via `AppLog.Error`/`AppLog.Warn` (except `OperationCanceledException`). No bare `catch {}` in production.
- **UI Automation:** controls stay discoverable by UIA Name; menu items get theirs from the localised `Header`. Don't suppress automation peers.
- **TDD:** red → green → refactor. Never write implementation before a failing test exists.
- **Tests run serially** (`DialogEditor.Tests`): `AppSettings`/`Loc` are global state. Isolate `AppSettings` via `AppSettings.SettingsPathOverride = <temp path>` in a ctor and reset to `null` in `Dispose`.
- **Path comparison:** recent-project paths compare case-insensitively (`StringComparison.OrdinalIgnoreCase`) — Windows paths.

---

### Task 1: AppSettings storage + MRU helpers

**Files:**
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs`
- Test: `DialogEditor.Tests/ViewModels/AppSettingsRecentProjectsTests.cs` (create)

**Interfaces:**
- Consumes: nothing new.
- Produces:
  - `static IReadOnlyList<string> AppSettings.RecentProjects { get; }`
  - `static void AppSettings.AddRecentProject(string path)` — normalises to full path, removes any case-insensitive duplicate, inserts at front, truncates to 10, saves.
  - `static void AppSettings.RemoveRecentProject(string path)` — removes case-insensitive match, saves if changed.
  - `static void AppSettings.ClearRecentProjects()` — empties, saves if non-empty.

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/AppSettingsRecentProjectsTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class AppSettingsRecentProjectsTests : IDisposable
{
    private readonly string _path;

    public AppSettingsRecentProjectsTests()
    {
        _path = Path.GetTempFileName();
        AppSettings.SettingsPathOverride = _path;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_path); } catch { /* best-effort */ }
    }

    [Fact]
    public void RecentProjects_DefaultsToEmpty()
    {
        File.WriteAllText(_path, "{}");
        Assert.Empty(AppSettings.RecentProjects);
    }

    [Fact]
    public void Add_InsertsAtFront()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.AddRecentProject(@"C:\a\two.dialogproject");
        Assert.Equal(
            new[] { @"C:\a\two.dialogproject", @"C:\a\one.dialogproject" },
            AppSettings.RecentProjects);
    }

    [Fact]
    public void Add_Duplicate_DifferentCase_MovesToFrontNoDupe()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.AddRecentProject(@"C:\a\two.dialogproject");
        AppSettings.AddRecentProject(@"C:\A\ONE.DIALOGPROJECT");
        var list = AppSettings.RecentProjects;
        Assert.Equal(2, list.Count);
        Assert.Equal(@"C:\A\ONE.DIALOGPROJECT", list[0]);
        Assert.Equal(@"C:\a\two.dialogproject", list[1]);
    }

    [Fact]
    public void Add_CapsAtTen_EvictsOldest()
    {
        for (var i = 1; i <= 11; i++)
            AppSettings.AddRecentProject($@"C:\a\p{i}.dialogproject");
        var list = AppSettings.RecentProjects;
        Assert.Equal(10, list.Count);
        Assert.Equal(@"C:\a\p11.dialogproject", list[0]);   // newest
        Assert.Equal(@"C:\a\p2.dialogproject", list[9]);    // p1 evicted
        Assert.DoesNotContain(@"C:\a\p1.dialogproject", list);
    }

    [Fact]
    public void Remove_DeletesMatchingEntry()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.AddRecentProject(@"C:\a\two.dialogproject");
        AppSettings.RemoveRecentProject(@"C:\A\ONE.DIALOGPROJECT");
        Assert.Equal(new[] { @"C:\a\two.dialogproject" }, AppSettings.RecentProjects);
    }

    [Fact]
    public void Clear_EmptiesList()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.ClearRecentProjects();
        Assert.Empty(AppSettings.RecentProjects);
    }

    [Fact]
    public void RecentProjects_RoundTripsThroughFile()
    {
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");
        AppSettings.SettingsPathOverride = _path; // re-point (forces fresh Load)
        Assert.Contains(@"C:\a\one.dialogproject", AppSettings.RecentProjects);
    }
}
```

Note: `AddRecentProject` normalises via `Path.GetFullPath`. The test inputs are already rooted absolute paths, so `GetFullPath` returns them unchanged (only separators/`.`/`..` are collapsed) — the assertions above hold on Windows.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AppSettingsRecentProjects"`
Expected: FAIL — `AppSettings` has no `RecentProjects`/`AddRecentProject`/`RemoveRecentProject`/`ClearRecentProjects` (compile error).

- [ ] **Step 3: Add the backing field to `SettingsData`**

In `AppSettings.cs`, inside `private sealed class SettingsData`, after the `LastSeenVersion` property (line ~73):

```csharp
        // MRU list of recently opened/created/saved-as project file paths, newest
        // first, capped at MaxRecentProjects. Powers File ▸ Recent Projects.
        public List<string> RecentProjects { get; set; } = [];
```

- [ ] **Step 4: Add the public helpers**

In `AppSettings.cs`, after the `LastProjectPath` property (line ~152):

```csharp
    private const int MaxRecentProjects = 10;

    public static IReadOnlyList<string> RecentProjects => Load().RecentProjects;

    public static void AddRecentProject(string path)
    {
        var full = Path.GetFullPath(path);
        var s = Load();
        s.RecentProjects.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        s.RecentProjects.Insert(0, full);
        if (s.RecentProjects.Count > MaxRecentProjects)
            s.RecentProjects.RemoveRange(MaxRecentProjects, s.RecentProjects.Count - MaxRecentProjects);
        Save(s);
    }

    public static void RemoveRecentProject(string path)
    {
        var s = Load();
        if (s.RecentProjects.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase)) > 0)
            Save(s);
    }

    public static void ClearRecentProjects()
    {
        var s = Load();
        if (s.RecentProjects.Count == 0) return;
        s.RecentProjects.Clear();
        Save(s);
    }
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~AppSettingsRecentProjects"`
Expected: PASS (7 tests).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/Services/AppSettings.cs DialogEditor.Tests/ViewModels/AppSettingsRecentProjectsTests.cs
git commit -m "feat(recent-projects): AppSettings MRU storage + helpers"
```

---

### Task 2: Record recent projects + expose the list on the ViewModel

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (three recording sites + new property)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelRecentProjectsTests.cs` (create)

**Interfaces:**
- Consumes: `AppSettings.AddRecentProject`, `AppSettings.RecentProjects` (Task 1).
- Produces: `public IReadOnlyList<string> MainWindowViewModel.RecentProjects { get; }` — reads through to `AppSettings.RecentProjects`; a change notification is raised after each mutation.

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/MainWindowViewModelRecentProjectsTests.cs`:

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelRecentProjectsTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _projectPath;

    public MainWindowViewModelRecentProjectsTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_rp_{Guid.NewGuid():N}.json");
        _projectPath  = Path.Combine(Path.GetTempPath(), $"proj_{Guid.NewGuid():N}.dialogproject");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { File.Delete(_settingsPath); } catch { /* best-effort */ }
        try { File.Delete(_projectPath);  } catch { /* best-effort */ }
    }

    private static MainWindowViewModel NewProject(string savePath) =>
        new(new StubDispatcher(), new StubFolderPicker(),
            new StubFilePicker(saveResult: savePath));

    [Fact]
    public void NewProject_RecordsPathInRecents()
    {
        var vm = NewProject(_projectPath);
        vm.NewProjectCommand.Execute(null);
        Assert.Contains(Path.GetFullPath(_projectPath), vm.RecentProjects);
    }

    [Fact]
    public void CloseProject_DoesNotRemoveFromRecents()
    {
        var vm = NewProject(_projectPath);
        vm.NewProjectCommand.Execute(null);
        Assert.Contains(Path.GetFullPath(_projectPath), vm.RecentProjects);

        vm.CloseProjectCommand.Execute(null);

        Assert.Null(AppSettings.LastProjectPath);                            // close cleared auto-reopen
        Assert.Contains(Path.GetFullPath(_projectPath), vm.RecentProjects);  // history kept
    }
}
```

Rationale: after `NewProjectCommand`, `CurrentConversationName` is still null, so `GuardDirtyThen` runs the action immediately (no dialog plumbing needed). `DoNewProject` writes a real file to `_projectPath` and records it. `CloseProject` clears `LastProjectPath` but must leave recents intact.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MainWindowViewModelRecentProjects"`
Expected: FAIL — `vm.RecentProjects` does not exist (compile error), and no recording happens.

- [ ] **Step 3: Add the `RecentProjects` property**

In `MainWindowViewModel.cs`, near the other project-state members (e.g. just above `NewProject` command, ~line 553):

```csharp
    /// MRU list of recently opened/created/saved-as projects (newest first) for the
    /// File ▸ Recent Projects submenu. Reads through to AppSettings; the submenu is
    /// rebuilt on open, and this raises change notification after each mutation.
    public IReadOnlyList<string> RecentProjects => AppSettings.RecentProjects;
```

- [ ] **Step 4: Record at the three sites**

In `DoNewProject` (after `AppSettings.LastProjectPath = path;`, ~line 573):

```csharp
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        OnPropertyChanged(nameof(RecentProjects));
```

In `FinishLoad` (after `AppSettings.LastProjectPath = path;`, ~line 813):

```csharp
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        OnPropertyChanged(nameof(RecentProjects));
```

In the Save-As method (after `AppSettings.LastProjectPath = path;`, ~line 1145):

```csharp
        AppSettings.LastProjectPath = path;
        AppSettings.AddRecentProject(path);
        OnPropertyChanged(nameof(RecentProjects));
```

Leave `DoCloseProject` (~line 597) untouched — close must not record or remove.

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MainWindowViewModelRecentProjects"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelRecentProjectsTests.cs
git commit -m "feat(recent-projects): record on open/new/save-as, expose RecentProjects"
```

---

### Task 3: Open-recent and clear commands + missing-file delegate

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs`
- Modify: `DialogEditor.ViewModels/Resources/*` — add `Status_RecentProjectMissing` string (see note below)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelRecentProjectsTests.cs` (extend)

**Interfaces:**
- Consumes: `AppSettings.RemoveRecentProject`/`ClearRecentProjects` (Task 1); existing private `LoadProject(string)` (funnel) and `GuardDirtyThen(Action)`.
- Produces:
  - `public Func<string, Task<bool>>? ConfirmRemoveMissingProject { get; set; }` — view supplies; returns true to remove the dead entry.
  - `OpenRecentProjectCommand` (`IRelayCommand<string>`) — existing file → load funnel; missing file → warn + status + confirm delegate.
  - `ClearRecentProjectsCommand` — empties the list.

Note on the status string: `MainWindowViewModel` resolves UI strings via `Loc.Format`, which reads the merged Avalonia dictionary at runtime. Add the key to `DialogEditor.Avalonia/Resources/Strings.axaml` (done in Task 5) — but because tests use `StubStringProvider` (returns the key or a formatted echo), the VM tests here do **not** depend on the real resource. Assert on state, not on the exact status text.

- [ ] **Step 1: Write the failing tests** (append to the Task 2 test class)

```csharp
    [Fact]
    public void OpenRecent_MissingFile_ConfirmYes_RemovesEntryAndAsks()
    {
        var vm = NewProject(_projectPath);
        var ghost = @"Z:\nope\ghost.dialogproject";
        AppSettings.AddRecentProject(ghost);
        var asked = new List<string>();
        vm.ConfirmRemoveMissingProject = p => { asked.Add(p); return Task.FromResult(true); };

        vm.OpenRecentProjectCommand.Execute(ghost);

        Assert.Equal(new[] { Path.GetFullPath(ghost) }, asked);
        Assert.DoesNotContain(Path.GetFullPath(ghost), vm.RecentProjects);
    }

    [Fact]
    public void OpenRecent_MissingFile_ConfirmNo_KeepsEntry()
    {
        var vm = NewProject(_projectPath);
        var ghost = @"Z:\nope\ghost.dialogproject";
        AppSettings.AddRecentProject(ghost);
        vm.ConfirmRemoveMissingProject = _ => Task.FromResult(false);

        vm.OpenRecentProjectCommand.Execute(ghost);

        Assert.Contains(Path.GetFullPath(ghost), vm.RecentProjects);
    }

    [Fact]
    public void OpenRecent_ExistingFile_OpensProject()
    {
        var vm = NewProject(_projectPath);
        vm.NewProjectCommand.Execute(null);   // writes a real .dialogproject at _projectPath
        vm.CloseProjectCommand.Execute(null); // no project open now

        vm.OpenRecentProjectCommand.Execute(Path.GetFullPath(_projectPath));

        Assert.NotNull(vm.CurrentProjectName); // funnel loaded the project
    }

    [Fact]
    public void ClearRecent_EmptiesList()
    {
        var vm = NewProject(_projectPath);
        AppSettings.AddRecentProject(@"C:\a\one.dialogproject");

        vm.ClearRecentProjectsCommand.Execute(null);

        Assert.Empty(vm.RecentProjects);
    }
```

`AddRecentProject(ghost)` normalises to `Path.GetFullPath(ghost)`; the assertions compare against that. The missing-file handler runs synchronously to completion because the stub delegate returns an already-completed `Task`.

- [ ] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MainWindowViewModelRecentProjects"`
Expected: FAIL — `ConfirmRemoveMissingProject`, `OpenRecentProjectCommand`, `ClearRecentProjectsCommand` don't exist (compile error).

- [ ] **Step 3: Add the delegate and commands**

In `MainWindowViewModel.cs`, near the `OpenProject` command (~line 579). First the delegate, placed with the other view-supplied `Func` delegates:

```csharp
    /// View-supplied confirm for a recent entry whose file is missing: returns true to
    /// remove it from the list, false to keep it. Null in headless/tests.
    public Func<string, Task<bool>>? ConfirmRemoveMissingProject { get; set; }
```

Then the commands:

```csharp
    [RelayCommand]
    private void OpenRecentProject(string? path)
    {
        if (string.IsNullOrEmpty(path)) return;
        if (!File.Exists(path))
        {
            _ = HandleMissingRecentProjectAsync(path);
            return;
        }
        GuardDirtyThen(() => LoadProject(path));
    }

    private async Task HandleMissingRecentProjectAsync(string path)
    {
        AppLog.Warn($"Recent project not found: {path}");
        StatusText = Loc.Format("Status_RecentProjectMissing", path);
        if (ConfirmRemoveMissingProject is null) return;
        if (await ConfirmRemoveMissingProject(path))
        {
            AppSettings.RemoveRecentProject(path);
            OnPropertyChanged(nameof(RecentProjects));
        }
    }

    [RelayCommand]
    private void ClearRecentProjects()
    {
        AppSettings.ClearRecentProjects();
        OnPropertyChanged(nameof(RecentProjects));
    }
```

`LoadProject` (private, ~line 616) is the same funnel File ▸ Open uses (`LoadProjectAsync(path, offerDeferred: false)`), so unsaved-changes guarding, git-conflict handling, and the autosave-restore offer all apply. `File`/`Path` and `Loc`/`AppLog` are already imported in this file.

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~MainWindowViewModelRecentProjects"`
Expected: PASS (6 tests total in the class).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelRecentProjectsTests.cs
git commit -m "feat(recent-projects): open-recent + clear commands, missing-file delegate"
```

---

### Task 4: RecentProjectMissingDialog + its strings

**Files:**
- Create: `DialogEditor.Avalonia/Views/RecentProjectMissingDialog.axaml`
- Create: `DialogEditor.Avalonia/Views/RecentProjectMissingDialog.axaml.cs`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Test: `DialogEditor.Tests/Views/RecentProjectMissingDialogTests.cs` (create)

**Interfaces:**
- Consumes: nothing from prior tasks (standalone view).
- Produces: `RecentProjectMissingDialog(string message)` with `Task<bool> ShowDialogAsync(Window owner)` returning true = remove, false = keep. This is what Task 5 wires to `ConfirmRemoveMissingProject`.

- [ ] **Step 1: Add the resource strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, add a new commented section (place near the other dialog strings, e.g. after the ForceDelete keys around line 1025):

```xml
    <!-- ─── Recent projects: missing-file dialog ──────────────────────── -->
    <sys:String x:Key="RecentMissing_Title">Project Not Found</sys:String>
    <!-- {0} = full path -->
    <sys:String x:Key="RecentMissing_Message" xml:space="preserve">This project could not be found:

{0}

Remove it from the recent list?</sys:String>
    <sys:String x:Key="RecentMissing_Remove">Remove</sys:String>
    <sys:String x:Key="RecentMissing_Keep">Keep</sys:String>
    <sys:String x:Key="ToolTip_RecentMissing_Remove">Remove this entry from the recent projects list. The project file itself is not deleted.</sys:String>
    <sys:String x:Key="ToolTip_RecentMissing_Keep">Leave the entry in the list — the file may become available again (a reconnected drive, or a different git branch).</sys:String>
    <!-- {0} = full path -->
    <sys:String x:Key="Status_RecentProjectMissing">Project not found: {0}</sys:String>
```

- [ ] **Step 2: Write the failing test**

Create `DialogEditor.Tests/Views/RecentProjectMissingDialogTests.cs`:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class RecentProjectMissingDialogTests
{
    public RecentProjectMissingDialogTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void MessageText_IsSetFromCtorArgument()
    {
        var dlg = new RecentProjectMissingDialog("could not be found: X");
        var msg = dlg.FindControl<TextBlock>("MessageText")!;
        Assert.Equal("could not be found: X", msg.Text);
    }

    [AvaloniaFact]
    public void DefaultResult_IsKeep()
    {
        var dlg = new RecentProjectMissingDialog("x");
        Assert.False(dlg.Result); // false = keep, until the user clicks Remove
    }
}
```

Note: `[AvaloniaFact]` (from `Avalonia.Headless.XUnit`) is the headless-UI attribute used by the existing view tests (e.g. `CommitConsentDialogTests`, `DiffWindowTests`); the `Loc.Configure` ctor matches their setup so `{DynamicResource}` lookups resolve.

- [ ] **Step 3: Run the test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~RecentProjectMissingDialog"`
Expected: FAIL — `RecentProjectMissingDialog` does not exist (compile error).

- [ ] **Step 4: Create the view (XAML)**

`DialogEditor.Avalonia/Views/RecentProjectMissingDialog.axaml` (modelled on `ForceDeleteDialog.axaml`; the two buttons are a Remove/Keep confirmation pair — tooltips added anyway for clarity):

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.RecentProjectMissingDialog"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Title="{DynamicResource RecentMissing_Title}"
        Width="460" MinWidth="420" SizeToContent="Height"
        CanResize="True"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface.Panel}">
  <StackPanel Margin="20" Spacing="12">
    <TextBlock x:Name="MessageText"
               Foreground="{DynamicResource Brush.Text.Primary}" FontSize="{DynamicResource FontSize.Body}"
               TextWrapping="Wrap"/>
    <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="6">
      <Button x:Name="KeepButton"
              Content="{DynamicResource RecentMissing_Keep}"
              Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
              Padding="16,6" FontSize="{DynamicResource FontSize.Body}"
              IsCancel="True"
              IsDefault="True"
              ToolTip.Tip="{DynamicResource ToolTip_RecentMissing_Keep}"
              AutomationProperties.HelpText="{DynamicResource ToolTip_RecentMissing_Keep}"/>
      <Button x:Name="RemoveButton"
              Content="{DynamicResource RecentMissing_Remove}"
              Background="{DynamicResource Brush.Button.Destructive.Background}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
              Padding="16,6" FontSize="{DynamicResource FontSize.Body}"
              ToolTip.Tip="{DynamicResource ToolTip_RecentMissing_Remove}"
              AutomationProperties.HelpText="{DynamicResource ToolTip_RecentMissing_Remove}"/>
    </StackPanel>
  </StackPanel>
</Window>
```

- [ ] **Step 5: Create the code-behind**

`DialogEditor.Avalonia/Views/RecentProjectMissingDialog.axaml.cs` (mirrors `ForceDeleteDialog.axaml.cs`):

```csharp
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Views;

public partial class RecentProjectMissingDialog : Window
{
    /// True = remove the entry from the recent list; false = keep it.
    public bool Result { get; private set; }

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public RecentProjectMissingDialog() => InitializeComponent();

    /// <param name="message">The formatted message (path already interpolated by caller).</param>
    public RecentProjectMissingDialog(string message)
    {
        InitializeComponent();
        MessageText.Text = message;
        RemoveButton.Click += (_, _) => { Result = true;  Close(); };
        KeepButton.Click   += (_, _) => { Result = false; Close(); };
    }

    /// Shows modally over <paramref name="owner"/>; resolves to true if the user removed the entry.
    public async System.Threading.Tasks.Task<bool> ShowDialogAsync(Window owner)
    {
        await ShowDialog(owner);
        return Result;
    }
}
```

- [ ] **Step 6: Run the test to verify it passes**

Run: `dotnet test --nologo --filter "FullyQualifiedName~RecentProjectMissingDialog"`
Expected: PASS (2 tests).

- [ ] **Step 7: Run the localisation guards**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NoHardcodedUiStrings|FullyQualifiedName~NoStaticStringResource"`
Expected: PASS (new strings are `{DynamicResource}`; no literals introduced).

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.Avalonia/Views/RecentProjectMissingDialog.axaml DialogEditor.Avalonia/Views/RecentProjectMissingDialog.axaml.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Tests/Views/RecentProjectMissingDialogTests.cs
git commit -m "feat(recent-projects): missing-file confirm dialog + strings"
```

---

### Task 5: File-menu submenu wiring + strings + delegate hookup

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (submenu skeleton)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (SubmenuOpened rebuild + delegate wiring)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (menu strings)

**Interfaces:**
- Consumes: `vm.RecentProjects`, `vm.OpenRecentProjectCommand`, `vm.ClearRecentProjectsCommand`, `vm.ConfirmRemoveMissingProject` (Tasks 2–3); `RecentProjectMissingDialog` (Task 4).
- Produces: nothing downstream (terminal UI wiring).

This task is view wiring; verification is via the localisation/build guards plus the `running-the-app` GUI check (there is no headless test for menu population). Keep the logic that could carry bugs (MRU, missing-file) in the already-tested ViewModel — the code-behind only rebuilds items and forwards clicks.

- [ ] **Step 1: Add the menu strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, near `Menu_OpenProject` (line ~752):

```xml
    <sys:String x:Key="Menu_RecentProjects">Recent Projects</sys:String>
    <sys:String x:Key="ToolTip_RecentProjects">Reopen a project you've recently opened, created, or saved. Newest first; the list fills as you use the editor.</sys:String>
    <sys:String x:Key="Menu_RecentProjectsEmpty">(no recent projects)</sys:String>
    <sys:String x:Key="Menu_ClearRecentProjects">Clear Recently Opened</sys:String>
    <sys:String x:Key="ToolTip_ClearRecentProjects">Remove every entry from the Recent Projects list. Your project files are not deleted.</sys:String>
```

- [ ] **Step 2: Add the submenu skeleton to the File menu**

In `DialogEditor.Avalonia/Views/MainWindow.axaml`, immediately after the **Open Project** `MenuItem` (line ~47-49), insert:

```xml
                        <MenuItem x:Name="RecentProjectsMenu"
                                  Header="{DynamicResource Menu_RecentProjects}"
                                  ToolTip.Tip="{DynamicResource ToolTip_RecentProjects}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_RecentProjects}"/>
```

The submenu's children are built in code-behind on open (next step). Give the `MenuItem` the `x:Name` so the code-behind can find it.

- [ ] **Step 3: Wire the rebuild + confirm delegate in code-behind**

In `DialogEditor.Avalonia/Views/MainWindow.axaml.cs`, where the other `vm.*` delegates are wired (near line 92 / 138), add the confirm delegate:

```csharp
        vm.ConfirmRemoveMissingProject = path =>
            new RecentProjectMissingDialog(Loc.Format("RecentMissing_Message", path)).ShowDialogAsync(this);
```

Then wire the submenu rebuild. After the `RecentProjectsMenu` element exists (it's named in XAML), attach a `SubmenuOpened` handler that repopulates its `Items` from `vm.RecentProjects`:

```csharp
        RecentProjectsMenu.SubmenuOpened += (_, _) => RebuildRecentProjectsMenu(vm);
```

And add the method (private, in the same class):

```csharp
    private void RebuildRecentProjectsMenu(MainWindowViewModel vm)
    {
        RecentProjectsMenu.Items.Clear();
        var recents = vm.RecentProjects;

        if (recents.Count == 0)
        {
            RecentProjectsMenu.Items.Add(new MenuItem
            {
                Header = Loc.Get("Menu_RecentProjectsEmpty"),
                IsEnabled = false,
            });
            return;
        }

        foreach (var path in recents)
        {
            var item = new MenuItem
            {
                // Filename as the label (also the UIA Name); full path in the tooltip.
                Header  = Path.GetFileNameWithoutExtension(path),
                Command = vm.OpenRecentProjectCommand,
                CommandParameter = path,
            };
            ToolTip.SetTip(item, path);
            global::Avalonia.Automation.AutomationProperties.SetHelpText(item, path);
            RecentProjectsMenu.Items.Add(item);
        }

        RecentProjectsMenu.Items.Add(new Separator());
        var clear = new MenuItem
        {
            Header  = Loc.Get("Menu_ClearRecentProjects"),
            Command = vm.ClearRecentProjectsCommand,
        };
        ToolTip.SetTip(clear, Loc.Get("ToolTip_ClearRecentProjects"));
        global::Avalonia.Automation.AutomationProperties.SetHelpText(clear, Loc.Get("ToolTip_ClearRecentProjects"));
        RecentProjectsMenu.Items.Add(clear);
    }
```

Ensure the needed `using`s are present (`Avalonia.Controls`, `Avalonia.Controls.Primitives` for `Separator` if not already, `System.IO` for `Path`, and the `Loc` namespace `DialogEditor.ViewModels.Resources`). Add only those not already imported at the top of the file.

Note: two recent projects with the same filename in different folders show the same label — acceptable; the tooltip disambiguates by full path. (De-duplication by folder is out of scope.)

- [ ] **Step 4: Build the app project**

Run: `dotnet build DialogEditor.Avalonia --nologo -v q`
Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run the full guard + suite**

Run: `dotnet test --nologo -v q`
Expected: PASS — all tests green, including `NoHardcodedUiStringsTests`, `NoStaticStringResourceTests`, `AutomationHelpTextTests`.

- [ ] **Step 6: GUI verification (running-the-app skill)**

Launch the app; confirm: File ▸ Recent Projects shows "(no recent projects)" disabled when empty; open/create a couple of projects; reopen the menu and confirm they appear newest-first with full-path tooltips; click one to reopen; point one entry at a deleted file and confirm the missing-file dialog offers Remove/Keep; use Clear Recently Opened. Capture a screenshot of the populated submenu.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat(recent-projects): File menu submenu wiring + strings"
```

---

## Self-Review

**Spec coverage:**
- Submenu shape → Task 5 (skeleton + rebuild). ✓
- Storage / MRU helpers (cap 10, MRU, OrdinalIgnoreCase dedupe) → Task 1. ✓
- Recording at the three `LastProjectPath` sites, none on close → Task 2. ✓
- Open-recent through the `GuardDirtyThen`+`LoadProjectAsync` funnel → Task 3. ✓
- Missing-file: warn + status + keep-until-clicked remove-offer via delegate → Tasks 3 (logic) + 4 (dialog) + 5 (wiring). ✓
- Clear command, no confirmation → Task 3 (command) + 5 (menu item). ✓
- Empty-list disabled-with-hint submenu → Task 5 (`Menu_RecentProjectsEmpty`, `IsEnabled=false`). ✓
- All strings localised; tooltips + UIA names → Tasks 4/5 strings, `ToolTip.SetTip` + `AutomationProperties.SetHelpText`. ✓
- Testing (serial AppSettings isolation; recording; missing-file both branches; clear) → Tasks 1–4. ✓
- Out of scope (jump list, pinning, start page) → not planned. ✓

**Placeholder scan:** none — every code step carries complete code; every run step has an exact command + expected result.

**Type consistency:** `AddRecentProject`/`RemoveRecentProject`/`ClearRecentProjects`/`RecentProjects` consistent across Tasks 1–3; `ConfirmRemoveMissingProject` (`Func<string, Task<bool>>`) consistent between Task 3 (declaration) and Task 5 (wiring); `RecentProjectMissingDialog(string)` + `ShowDialogAsync` consistent between Task 4 (definition) and Task 5 (use); `OpenRecentProjectCommand`/`ClearRecentProjectsCommand` names match the `[RelayCommand]` method names `OpenRecentProject`/`ClearRecentProjects`.
