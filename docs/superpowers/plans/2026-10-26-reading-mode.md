# Reading Mode Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a read-only Reading mode to the editor's main window: a first-launch mode
chooser, a View › Reading mode toggle and a toolbar badge that switch modes, and a reading
mode that hides editing tools and can never write a game or project file (GitHub #234).

**Architecture:** One `EditorMode` value lives in `AppSettings` and on
`MainWindowViewModel.Mode`. Reading mode drives the existing read-only switch
(`ConversationViewModel.IsEditable`, which becomes "project open **and** editing") and adds
`IsEditingMode` to every editing command's `CanExecute`. Views hide editing controls by
binding `IsVisible` to `IsEditingMode`. One allow-list (`ReadingModeShortcuts`) gates the
main-window and canvas key handlers. Project-open prompts that would write wait until the
user switches to editing.

**Tech Stack:** C# / .NET 10, Avalonia 11.3, CommunityToolkit.Mvvm, xUnit + Avalonia.Headless.

**Spec:** `docs/superpowers/specs/2026-10-26-reading-mode-design.md`. Mockups are in
`docs/superpowers/specs/2026-10-26-reading-mode-mockups/`.

## Global Constraints

- Strict red/green TDD: every task's first step is a failing test (`CLAUDE.md`).
- No inline user-visible text. Every string goes in
  `DialogEditor.Avalonia/Resources/Strings.axaml`, and VM-side text goes through `Loc`.
- Every new interactive control has a detailed `ToolTip.Tip` and a matching
  `AutomationProperties.HelpText`, plus an `AutomationProperties.Name` where the content
  isn't text.
- New windows: `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"` and `WindowSizing`
  (see `EveryWindowIsSizedTests`).
- Every caught exception is logged via `AppLog.Error`/`AppLog.Warn`. No bare `catch { }`.
- Headless test command: `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~<Class>"`.
  A bare `dotnet test` launches the GUI. Stop any running editor first, because it locks
  the build output.
- Default when no mode is saved: **`EditorMode.Editing`** on the view model, so every
  existing test keeps its current behaviour. Only the first-launch path shows the chooser.
- Commit after each task, with a message ending in the
  `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>` line. Reference `#234`.
- Run `trail check` before every push.

## Review Focus

1. **A half-typed edit in Node Details when the user switches to reading.** Expected: the
   edit counts as unsaved and the Unsaved Changes dialog appears. Test in Task 2.
2. **Typing in a text box (Conversations filter, node search) while in reading mode.**
   Expected: keys the shortcut gate blocks (Delete, Ctrl+Z, letters) still reach the text
   box. The gate must skip the shortcut, not swallow the key. Test in Task 8.
3. **A stale autosave sidecar next to a project opened in reading mode.** Expected: it is
   not deleted, because deleting is a write. Test in Task 4.
4. **Relaunching in reading mode with a Git-conflicted last project.** Expected: no modal.
   The local side opens read-only and the conflict is offered on switching to editing.
   Test in Task 4.
5. **Switching modes while a guided tour is showing.** Expected: the running tour is
   dismissed, and the new mode's tour starts only if it hasn't been seen. Test in Task 6.

---

### Task 1: `EditorMode` and its settings

**Files:**
- Create: `DialogEditor.ViewModels/Services/EditorMode.cs`
- Modify: `DialogEditor.ViewModels/Services/AppSettings.cs` (settings record ~L40-100; `Load()` fresh-install branch ~L164-171; static accessors ~L355-370)
- Test: `DialogEditor.Tests/Services/AppSettingsEditorModeTests.cs`

**Interfaces:**
- Produces: `public enum EditorMode { Reading, Editing }` in `DialogEditor.ViewModels.Services`, marked `[JsonConverter(typeof(JsonStringEnumConverter<EditorMode>))]` so `settings.json` stays hand-readable.
- Produces: `AppSettings.Mode` (`EditorMode?`, static get/set; `null` = never chosen) and `AppSettings.ReaderTourSeen` (`bool`, static get/set).

- [x] **Step 1: Write the failing tests** (use `AppSettings.SettingsPathOverride` to a temp file, as `MainWindowViewModelCloseProjectTests` does)

```csharp
[Fact] public void FreshInstall_HasNoMode() => Assert.Null(AppSettings.Mode);           // no settings file
[Fact] public void Mode_RoundTrips_AsAString()
{
    AppSettings.Mode = EditorMode.Reading;
    Assert.Equal(EditorMode.Reading, AppSettings.Mode);
    Assert.Contains("\"Reading\"", File.ReadAllText(_settingsPath));
}
[Fact] public void FreshInstall_HasNotSeenTheReaderTour() => Assert.False(AppSettings.ReaderTourSeen);
[Fact] public void ExistingSettingsFile_WithoutTheKey_TreatsTheReaderTourAsSeen()
{
    File.WriteAllText(_settingsPath, "{}");
    Assert.True(AppSettings.ReaderTourSeen);
}
```

- [x] **Step 2: Run them and confirm they fail.** Expected: compile errors, because `EditorMode`, `Mode` and `ReaderTourSeen` don't exist yet.
- [x] **Step 3: Implement.** Add `public EditorMode? Mode { get; set; }` to the settings record, and `public bool ReaderTourSeen { get; set; } = true;` with the same "existing installs default true, fresh install false" comment pattern `GuidedTourSeen` uses. Add `ReaderTourSeen = false` to both fresh-install branches of `Load()`. Add the static accessors.
- [x] **Step 4: Run the tests.** Expected: PASS.
- [x] **Step 5: Commit**: `feat(reading-mode): EditorMode setting (#234)`.

---

### Task 2: Mode on the main view model, and switching

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (`SetProject` ~L349, which sets `Canvas.IsEditable`; the unsaved guard ~L191-240)
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs` (next to `IsEditable`/`IsReadOnly` ~L147)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelReadingModeTests.cs`

**Interfaces:**
- Consumes: `EditorMode`, `AppSettings.Mode` (Task 1).
- Produces on `MainWindowViewModel`:
  - `[ObservableProperty] EditorMode _mode`, initialised from `AppSettings.Mode ?? EditorMode.Editing`
  - `public bool IsReadingMode`, `public bool IsEditingMode` (both notify when `Mode` changes)
  - `public Task<bool> SwitchModeAsync(EditorMode target)` returns false when the user picks *Stay Here*
  - `ToggleReadingModeCommand` (async, no `CanExecute`) switches to the other mode
  - `private Task OnEnteredEditingModeAsync()` is a hook that Task 4 fills (it returns `Task.CompletedTask` until then)
- Produces on `NodeDetailViewModel`: `[ObservableProperty] bool _isReadingMode`, set by the main VM.

- [x] **Step 1: Write the failing tests** (build the VM with `MakeVm()` as in `MainWindowViewModelCloseProjectTests`, and load a project via the same reflection helper)

```csharp
[Fact] public void NoSavedMode_StartsInEditing() { Assert.Equal(EditorMode.Editing, MakeVm().Mode); }
[Fact] public void SavedReading_StartsInReading() { AppSettings.Mode = EditorMode.Reading; Assert.True(MakeVm().IsReadingMode); }
[Fact] public async Task ReadingMode_WithAProjectOpen_MakesTheCanvasReadOnly()
{   // project open + editing → IsEditable true; after SwitchModeAsync(Reading) → false; back → true
}
[Fact] public async Task Switch_SavesTheModeForNextLaunch()
{   await vm.SwitchModeAsync(EditorMode.Reading); Assert.Equal(EditorMode.Reading, AppSettings.Mode); }
[Fact] public async Task Switch_ToReading_WithUnsavedChanges_AsksAndStaysOnCancel()
{   // dirty project; subscribe UnsavedChangesRequested → call vm.CancelPendingNavigation() (the existing Stay Here path)
    Assert.False(await vm.SwitchModeAsync(EditorMode.Reading));
    Assert.Equal(EditorMode.Editing, vm.Mode);
}
[Fact] public async Task Switch_ToReading_AfterDiscard_KeepsTheProjectOpen()
{   // UnsavedChangesRequested → vm.DiscardAndProceed(); then
    Assert.True(vm.IsReadingMode); Assert.True(vm.IsProjectOpen); Assert.False(vm.IsModified);
}
[Fact] public async Task Switch_ToReading_WhenClean_DoesNotAsk() { /* UnsavedChangesRequested never raised */ }
[Fact] public async Task Switch_KeepsTheOpenConversationAndSelectedNode() { /* same CurrentConversationName and Canvas.SelectedNode id after the switch */ }
[Fact] public async Task Switch_CommitsAHalfTypedDetailEdit_SoTheDialogSeesIt() // Review Focus 1
{   // set Detail's DefaultText binding source the way the TextBox would on commit, without saving;
    // SwitchModeAsync(Reading) must raise UnsavedChangesRequested
}
[Fact] public async Task Detail_FollowsTheMode() { /* vm.Detail.IsReadingMode tracks vm.IsReadingMode */ }
```

Use the name of the existing "Stay Here" method on the guard. Look it up next to
`SaveAndProceed`/`DiscardAndProceed`. If Node Details commits text on focus loss rather
than on every keystroke, the view has to flush the focused text box before calling
`SwitchModeAsync`. In that case, move the last test to the View test in Task 8 and assert
it there.

- [x] **Step 2: Run them and confirm they fail.**
- [x] **Step 3: Implement.** Replace `Canvas.IsEditable = project is not null;` with a private `UpdateEditable()` that sets `Canvas.IsEditable = _project is not null && IsEditingMode` and `Detail.IsReadingMode = IsReadingMode`, and call it from `SetProject` and `OnModeChanged`. `SwitchModeAsync`: if the target equals `Mode`, return true. If the target is Reading, `await EnsureNoUnsavedEditsAsync()` and return false when it does. Then set `Mode`, write `AppSettings.Mode`, and when the target is Editing, `await OnEnteredEditingModeAsync()`.
- [x] **Step 4: Run the tests.** Expected: PASS. Also run `MainWindowViewModel*Tests` to confirm nothing else changed.
- [x] **Step 5: Commit**: `feat(reading-mode): mode on the main view model (#234)`.

---

### Task 3: Editing commands are off in reading mode

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (the `[RelayCommand]` attributes and their `Can*` predicates)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelReadingModeCommandTests.cs`

**Interfaces:**
- Consumes: `IsEditingMode` (Task 2).

- [x] **Step 1: Write the failing theory.** Put a project and a game folder in place so each command would otherwise be enabled where its gate allows. Then switch to reading and assert, by reflection over the generated command property:

```csharp
// Off in reading mode
[Theory]
[InlineData("NewProjectCommand")] [InlineData("NewConversationCommand")] [InlineData("ImportConversationCommand")]
[InlineData("ExportConversationsCommand")] [InlineData("SaveProjectCommand")] [InlineData("SaveProjectAsCommand")]
[InlineData("UndoApplyCommand")] [InlineData("MergeProjectsCommand")] [InlineData("ExportForTranslationCommand")]
[InlineData("ImportTranslationCommand")] [InlineData("CreateSampleProjectCommand")] [InlineData("OpenWalkthroughCommand")]
[InlineData("TagReferenceCommand")] [InlineData("RestoreBackupCommand")] [InlineData("SaveCommand")]
[InlineData("TestPatchCommand")] [InlineData("RestoreConversationCommand")] [InlineData("FindInProjectCommand")]
[InlineData("BatchReplaceCommand")] [InlineData("BatchImportVoAllCommand")] [InlineData("ResolveConflictsCommand")]
public void EditingCommand_CannotRun_InReadingMode(string name) => Assert.False(Cmd(vm, name).CanExecute(null));

// Still on
[Theory]
[InlineData("OpenProjectCommand")] [InlineData("OpenRecentProjectCommand")] [InlineData("ClearRecentProjectsCommand")]
[InlineData("CloseProjectCommand")] [InlineData("OpenFolderCommand")] [InlineData("StartGuidedTourCommand")]
[InlineData("ChangelogCommand")] [InlineData("AboutCommand")] [InlineData("ToggleReadingModeCommand")]
public void ReadingCommand_StillRuns_InReadingMode(string name) => Assert.True(Cmd(vm, name).CanExecute(null));

[Fact] public void SwitchingBack_ReenablesTheEditingCommands() { /* SaveProjectCommand.CanExecute true again after switching to Editing with a dirty project */ }
```

`ShowRepDispositionBalanceWindowCommand` and `BrowseSpeakerLinesCommand` are covered in
Task 5. For a command whose own gate needs state the fixture can't set up (for example
`ResolveConflictsCommand` needs a pending conflict), set up that state, or assert only the
false case.

- [x] **Step 2: Run the theory and confirm it fails.**
- [x] **Step 3: Implement.** Give commands that have no `CanExecute` one with `CanExecute = nameof(IsEditingMode)`, and add `&& IsEditingMode` to the existing `Can*` predicates of the commands in the first list. In `OnModeChanged`, call `NotifyCanExecuteChanged()` on every one of them.
- [x] **Step 4: Run the tests.** Expected: PASS.
- [x] **Step 5: Commit**: `feat(reading-mode): editing commands off in reading mode (#234)`.

---

### Task 4: Opening a project in reading mode writes nothing

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (`LoadProjectAsync` ~L1063, `LoadConflictedProjectAsync` ~L1140, `OnEnteredEditingModeAsync` from Task 2)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelReadingModeOpenTests.cs`
- Modify: `Strings.axaml`: add `Status_ProjectOpenedReadOnlyDeferred` = "Opened '{0}' for reading. It has unsaved autosave work or merge conflicts; switch to editing to deal with them."

**Interfaces:**
- Consumes: `IsReadingMode`, `OnEnteredEditingModeAsync` (Task 2).

- [x] **Step 1: Write the failing tests.** Build sidecars with `AutosaveRecovery`'s helpers, as `MainWindowViewModelAutosaveTests` does.

```csharp
[Fact] public async Task Reading_NewerAutosave_OpensTheSavedFile_WithoutAsking()
{   // ConfirmRestoreAutosave set to a delegate that fails the test if called
    // project opens (IsProjectOpen), sidecar still exists, status = Status_ProjectOpenedReadOnlyDeferred
}
[Fact] public async Task Reading_StaleAutosave_IsNotDeleted() { /* Review Focus 3 */ }
[Fact] public async Task Reading_ConflictedProject_OpensTheLocalSide_WithoutTheModal()   // Review Focus 4
{   // ShowGitConflictResolution set to a delegate that fails if called; IsProjectOpen; HasPendingConflictResolution
}
[Fact] public async Task SwitchingToEditing_OffersTheDeferredAutosave() { /* ConfirmRestoreAutosave called exactly once */ }
[Fact] public async Task SwitchingToEditing_OffersTheDeferredConflict() { /* ShowGitConflictResolution called exactly once */ }
[Fact] public async Task Reading_StartupReopen_DefersToo() { /* ReopenLastProjectOnStartup with a newer sidecar: no prompt */ }
[Fact] public async Task Reading_Open_WritesNothingInTheProjectFolder()
{   // snapshot names + last-write times of every file in the project folder before and after open; equal
}
```

- [x] **Step 2: Run them and confirm they fail.**
- [x] **Step 3: Implement.** In `LoadProjectAsync`, when `IsReadingMode`, skip the whole autosave branch (no `TryDelete`, no prompt) and remember `_deferredOpenPath = path` when the state was `Newer`. In `LoadConflictedProjectAsync`, when `IsReadingMode` and there are conflicts, `FinishLoad(mine, path)` and set the pending-conflict state, as the `offerDeferred` branch does, without showing the modal. In `OnEnteredEditingModeAsync`: if `_deferredOpenPath` is set, clear it and `await LoadProjectAsync(path, offerDeferred: false)`. Else, if `HasPendingConflictResolution`, `await ResolveConflictsCommand.ExecuteAsync(null)`.
- [x] **Step 4: Audit the rest of the open path.** Read `FinishLoad`, `SetProject` and anything they call, for writes to the project folder or game folder (format upgrade, the Git attribution loader, lock files). Writes to `AppSettings` (`LastProjectPath`, recent projects) are allowed. Gate any other write on `IsEditingMode`, and extend the last test if one turns up. Record what was checked in the commit message body.
- [x] **Step 5: Run the tests.** Expected: PASS. Also run `MainWindowViewModelAutosaveTests`.
- [x] **Step 6: Commit**: `feat(reading-mode): opening a project for reading writes nothing (#234)`.

---

### Task 5: Speaker Lines and Rep & Disposition Balance without a project

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (`CanShowRepDispositionBalance` ~L331, `CanBrowseSpeakerLines` ~L2921, `ShowRepDispositionBalanceWindow` ~L2907, `OpenSpeakerLineBrowserAsync` ~L2940)
- Modify: `DialogEditor.ViewModels/ViewModels/RepDispositionBalanceViewModel.cs`
- Modify: `DialogEditor.Avalonia/Views/RepDispositionBalanceWindow.axaml` (the source picker)
- Test: `DialogEditor.Tests/ViewModels/ReadingWithoutProjectTests.cs`

**Interfaces:**
- Produces: `RepDispositionBalanceViewModel(DialogProject project, IGameDataProvider provider, Func<…> getOpenConversation, bool hasProject = true)` plus `public bool HasProject`. With `hasProject: false`, `Source` starts as `BalanceSource.OnDiskPlusChanges` (with an empty project that means "on disk"), and the window hides the source picker (`IsVisible="{Binding HasProject}"`).

- [x] **Step 1: Write the failing tests** (game folder loaded via `FakeGameDataProvider`, no project)

```csharp
[Fact] public void Balance_CanOpen_WithOnlyAGameFolder() => Assert.True(vm.ShowRepDispositionBalanceWindowCommand.CanExecute(null));
[Fact] public void SpeakerLines_CanOpen_WithOnlyAGameFolder() => Assert.True(vm.BrowseSpeakerLinesCommand.CanExecute(null));
[Fact] public void Neither_CanOpen_WithoutAGameFolder() { /* both false */ }
[Fact] public async Task Balance_WithoutAProject_ScansTheGameOnDisk()
{   // capture the VM handed to ShowRepDispositionBalance: HasProject false, Source OnDiskPlusChanges;
    // run its scan (Scope.All); rows come from FakeGameDataProvider's conversations
}
[Fact] public async Task SpeakerLines_WithoutAProject_ListsGameLines() { /* captured VM's scan returns rows */ }
[Fact] public void Both_StillOpen_InReadingMode() { /* switch to Reading with and without a project: CanExecute true */ }
```

- [x] **Step 2: Run them and confirm they fail.**
- [x] **Step 3: Implement.** Drop `_project is not null` from both gates. Pass `_project ?? DialogProject.Empty(string.Empty)` to both view models, and `hasProject: _project is not null` to the balance VM. Notify both commands from `OnFolderLoaded` (wherever `_currentGameDirectory`/`_provider` are set) as well as from `SetProject`.
- [x] **Step 4: Run the tests.** Expected: PASS. Also run the existing `RepDispositionBalance*` and `SpeakerLine*` tests.
- [x] **Step 5: Commit**: `feat(reading-mode): speaker lines and balance without a project (#234)`.

---

### Task 6: A guided tour per mode

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/GuidedTourViewModel.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (`StartGuidedTour` ~L2108, `SwitchModeAsync`)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (`OnOpened` ~L902)
- Modify: `DialogEditor.Avalonia/Docking/TourTargetResolver.cs` if it needs the new `ModeBadge` key
- Modify: `Strings.axaml`: add `Tour_Reader_Step1_Text` … `Tour_Reader_Step4_Text`, with the copy from spec §6
- Test: `DialogEditor.Tests/ViewModels/GuidedTourModeTests.cs`

**Interfaces:**
- Produces: `GuidedTourViewModel.ReaderSteps` = `BrowserPanel`, `CanvasView`, `DetailPanel`, `ModeBadge` (with the four keys above), the constructor `GuidedTourViewModel(IReadOnlyList<GuidedTourStep> editingSteps, IReadOnlyList<GuidedTourStep> readerSteps)`, `void Start(EditorMode mode)` (it marks that mode's seen flag), and `static bool HasSeen(EditorMode mode)`. `Start()` with no argument is removed. Update its callers.

- [x] **Step 1: Write the failing tests**

```csharp
[Fact] public void Start_Reading_RunsTheReaderSteps_AndMarksItSeen() { /* CurrentStep.TargetName == "BrowserPanel", step count 4, last is "ModeBadge"; AppSettings.ReaderTourSeen */ }
[Fact] public void Start_Editing_RunsTheEditingSteps_AndMarksItSeen() { /* AppSettings.GuidedTourSeen */ }
[Fact] public void StartGuidedTourCommand_StartsTheCurrentModesTour() { /* main VM in Reading → reader steps */ }
[Fact] public async Task FirstSwitchToAMode_StartsItsTour_Once() { /* fresh settings: switch to Editing starts the editing tour; switch back and forth again: no restart */ }
[Fact] public async Task Switching_DismissesARunningTour() { /* Review Focus 5: Tour.IsVisible false after the switch when the new mode's tour was already seen */ }
```

- [x] **Step 2: Run them and confirm they fail.**
- [x] **Step 3: Implement.** `OnOpened` calls `if (!GuidedTourViewModel.HasSeen(vm.Mode)) vm.Tour.Start(vm.Mode);`. `SwitchModeAsync` dismisses a visible tour, then starts the new mode's tour if it hasn't been seen. Add the `ModeBadge` key to `TourTargetResolver`, resolving to the badge from Task 9 (`x:Name="ModeBadge"`).
- [x] **Step 4: Run the tests.** Expected: PASS. Also run the existing `GuidedTour*` tests and update them for the new `Start` signature.
- [x] **Step 5: Commit**: `feat(reading-mode): a guided tour per mode (#234)`.

---

### Task 7: First-launch mode chooser

**Files:**
- Create: `DialogEditor.ViewModels/ViewModels/ModeChooserViewModel.cs`
- Create: `DialogEditor.Avalonia/Views/ModeChooserWindow.axaml` and `.axaml.cs`
- Modify: `DialogEditor.Avalonia/App.axaml.cs` (the startup branch ~L78-93)
- Modify: `Strings.axaml`: add the keys below
- Test: `DialogEditor.Tests/ViewModels/ModeChooserViewModelTests.cs`, `DialogEditor.Tests/Views/ModeChooserWindowTests.cs`

**Interfaces:**
- Produces: `ModeChooserViewModel` with `ChooseReadingCommand`, `ChooseEditingCommand`, `EditorMode Result` (default `Reading`), `event Action? CloseRequested`, and `void OnClosedWithoutChoice()` (saves `Reading`).
- Produces: `App` startup order: theme onboarding (if not seen), then `ModeChooserWindow` (if `AppSettings.Mode is null`), then `MainWindow`.

Strings, with the copy taken from spec §2:
`ModeChooser_Title`, `ModeChooser_Heading` ("How do you want to use the editor?"), `ModeChooser_Lead`,
`ModeChooser_Reading_Title`, `ModeChooser_Reading_Body`, `ModeChooser_Reading_Bullet1..3`,
`ModeChooser_Reading_Button` ("Start reading"), `ToolTip_ModeChooser_Reading`, and the six
matching `ModeChooser_Editing_*` keys plus `ToolTip_ModeChooser_Editing`, and
`ModeChooser_Footer` ("This window only appears once. Closing it starts Reading mode. You
can switch modes later from View › Reading mode.").

- [x] **Step 1: Write the failing tests**

```csharp
// VM
[Fact] public void ChooseReading_SavesReading_AndCloses()
[Fact] public void ChooseEditing_SavesEditing_AndCloses()
[Fact] public void ClosingWithoutAChoice_SavesReading()
// Headless window
[AvaloniaFact] public void Window_HasBothPanes_SideBySide()      // Reading pane's Bounds.X < Editing pane's Bounds.X, same Y
[AvaloniaFact] public void Window_StacksThePanes_WhenNarrow()      // at 200 % text size (FontScale 2.0) the window fits the screen-width cap and the panes stack (Reading above Editing)
[AvaloniaFact] public void Escape_ClosesAndSavesReading()
[AvaloniaFact] public void Buttons_HaveTooltipsAndAutomationNames()
```

Also add `ModeChooserWindow` to the window list in `EveryWindowIsSizedTests` and in the
window-icon test, if one enumerates windows.

- [x] **Step 2: Run them and confirm they fail.**
- [x] **Step 3: Implement** the view model and the window. Lay out the window as in `mode-chooser.html` "Side by side": a `Grid` with two star columns that becomes one column below a width threshold (a `Bounds` width check in code-behind, or a container query if one is in use elsewhere in the repo). Closing without a choice calls `OnClosedWithoutChoice()`. Wire the chooser into `App.axaml.cs` after onboarding.
- [x] **Step 4: Run the tests.** Expected: PASS.
- [x] **Step 5: Commit**: `feat(reading-mode): first-launch mode chooser (#234)`.

---

### Task 8: Main window menus, welcome screen and shortcuts

**Files:**
- Create: `DialogEditor.Avalonia/Input/ReadingModeShortcuts.cs`
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (menus ~L27-350, conflicts banner ~L470-500)
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (`OnKeyDownTunnel` ~L520-640)
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs` (key handler ~L140-175)
- Modify: `Strings.axaml`: add `Menu_View_ReadingMode` ("Reading mode") and `ToolTip_Menu_View_ReadingMode` (explains read-only and switching)
- Test: `DialogEditor.Tests/Views/MainWindowReadingModeTests.cs`, `DialogEditor.Tests/Input/ReadingModeShortcutsTests.cs`

**Interfaces:**
- Produces: `public static bool ReadingModeShortcuts.IsAllowed(Key key, KeyModifiers modifiers)`. True for: unmodified arrows, PageUp, PageDown, Home, Enter, Escape, Apps; F6 and Shift+F6; Shift+F10; Ctrl+Shift+O; Ctrl+O; Ctrl+W; Ctrl+OemComma. False for everything else.

- [x] **Step 1: Write the failing tests**

```csharp
// ReadingModeShortcutsTests: a theory over the allowed list (true), and over
// Ctrl+N, Ctrl+S, Ctrl+Shift+S, Ctrl+Z, Ctrl+Y, Delete, F5, Shift+F5, F7, Ctrl+Shift+B,
// Ctrl+F, Ctrl+H, Ctrl+Shift+H, Ctrl+Shift+F, Ctrl+L, Ctrl+Right (false).

// MainWindowReadingModeTests (headless, new MainWindow(), find MenuItems by Header == resolved resource string)
[AvaloniaTheory] // every key in the spec §3 "Hidden" column
[InlineData("Menu_NewProject")] … [InlineData("Menu_ImportUiStrings")]
public void HiddenItem_IsNotVisible_InReadingMode(string headerKey)
[AvaloniaTheory] // every key in the "Kept" column, including Menu_View_ReadingMode
public void KeptItem_IsVisible_InReadingMode(string headerKey)
[AvaloniaFact] public void TestMenu_IsNotVisible_InReadingMode()
[AvaloniaFact] public void ReadingModeMenuItem_IsChecked_InReadingMode_AndSwitchesWhenClicked()
[AvaloniaFact] public void ConflictsBanner_IsHidden_InReadingMode()
[AvaloniaFact] public void Delete_OnASelectedNode_DoesNothing_InReadingMode()          // node still there
[AvaloniaFact] public void Delete_InTheConversationFilter_StillEditsTheText()          // Review Focus 2: e.Handled false, the TextBox text shrinks
[AvaloniaFact] public void CtrlN_DoesNotStartANewProject_InReadingMode()
[AvaloniaFact] public void CtrlW_ClosesTheProject_InReadingMode()
```

- [x] **Step 2: Run them and confirm they fail.**
- [x] **Step 3: Implement.** Bind each hidden item's `IsVisible` to `IsEditingMode`. The Close Project item keeps its existing `IsProjectOpen`-based state. Add the checkable `Menu_View_ReadingMode` item (`ToggleType="CheckBox"`, `IsChecked="{Binding IsReadingMode, Mode=OneWay}"`, `Command="{Binding ToggleReadingModeCommand}"`). Hide separators that would end up doubled or trailing. At the top of `OnKeyDownTunnel` and the canvas key handler, add `if (vm.IsReadingMode && !ReadingModeShortcuts.IsAllowed(e.Key, e.KeyModifiers)) return;`, which leaves `e.Handled` false so text boxes still receive the key. The welcome screen's tour button already calls `StartGuidedTourCommand` and needs no change.
- [x] **Step 4: Run the tests.** Expected: PASS. Also run `MainWindowPaneCycleTests`, `MainWindowTabOrderTests` and `ConversationViewKeyboardTests`.
- [x] **Step 5: Commit**: `feat(reading-mode): menus and shortcuts in reading mode (#234)`.

---

### Task 9: Mode badge

**Files:**
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml` (the right-docked toolbar `StackPanel`, after `HelpToggle`, ~L383-398)
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`: add `Brush.ModeBadge.Reading.Background` → `Palette.Green.600` and `Brush.ModeBadge.Editing.Background` → `Palette.Burnt.600`. Foreground is `Brush.Text.OnAccent`. Both pairs already pass contrast as Button.Confirm and Button.Caution, so reusing them keeps every palette valid.
- Modify: `DialogEditor.Tests/Theming/PaletteContrastTests.cs` (two new pairs labelled `ModeBadge.*`)
- Modify: `Strings.axaml`: `ModeBadge_Reading` ("Reading"), `ModeBadge_Editing` ("Editing"), `ModeBadge_ReadingGlyph` ("📖"), `ModeBadge_EditingGlyph` ("✎"), `AutomationName_ModeBadge_Reading` ("Reading mode: nothing can be changed"), `AutomationName_ModeBadge_Editing` ("Editing mode: changes can be made"), and `ToolTip_ModeBadge_Reading` / `ToolTip_ModeBadge_Editing`, each saying what the mode means and that clicking switches
- Test: `DialogEditor.Tests/Views/ModeBadgeTests.cs`

**Interfaces:**
- Produces: a `Button x:Name="ModeBadge"` (Task 6's tour target) with `Command="{Binding ToggleReadingModeCommand}"`.

- [x] **Step 1: Write the failing tests**

```csharp
[AvaloniaFact] public void Badge_SaysReading_InReadingMode()   // visible text, AutomationProperties.Name == AutomationName_ModeBadge_Reading, Background == Brush.ModeBadge.Reading.Background
[AvaloniaFact] public void Badge_SaysEditing_InEditingMode()
[AvaloniaFact] public void Badge_IsTheLastToolbarControl()     // right of HelpToggle
[AvaloniaFact] public void ClickingTheBadge_Switches()         // Command executes → Mode flips
[AvaloniaFact] public void Badge_HasATooltip_AndIsAFocusableTabStop()
```

- [x] **Step 2: Run them and confirm they fail.**
- [x] **Step 3: Implement** the badge as one `Button` whose content (glyph + word), background, `ToolTip.Tip` and `AutomationProperties.Name` switch on `IsReadingMode`. Use two styles keyed on a class toggled by `Classes.reading="{Binding IsReadingMode}"`, or bindings with a bool-to-resource converter if one already exists. Add the contrast pairs.
- [x] **Step 4: Run the tests.** Expected: PASS. Also run `PaletteContrastTests`, `PaletteGoldenTests`, `TokenRegistryTests` and `MainWindowTabOrderTests`.
- [x] **Step 5: Commit**: `feat(reading-mode): mode badge (#234)`.

---

### Task 10: Canvas and Node Details in reading mode

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml` (the "Choose start node" and "Add note" buttons ~L112-215, the node and empty-space context menus)
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs` (`Editor_DoubleTapped` ~L442)
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml` (add/delete link ~L148-260, the dictionary buttons, the token and spelling warning Borders, the VO alias/reuse controls, and the Edit Scripts/Edit Conditions buttons ~L630-655)
- Modify: `DialogEditor.Avalonia/Docking/EditorDockFactory.cs` (`CreateLayout` ~L56) and the `BuildDock` call site in `MainWindow.axaml.cs`
- Test: `DialogEditor.Tests/Views/ConversationViewReadingModeTests.cs`, `DialogEditor.Tests/Views/NodeDetailViewReadingModeTests.cs`, `DialogEditor.Tests/Docking/EditorDockFactoryReadingModeTests.cs`

**Interfaces:**
- Consumes: `ConversationViewModel.IsEditable`, and `NodeDetailViewModel.IsReadingMode` (Task 2).
- Produces: `EditorDockFactory.CreateLayout(EditorMode mode)`. With `Reading`, the default layout puts the Condition search tool in `HiddenDockables`, so View › Condition search brings it back. The parameterless override calls it with `Editing`. `BuildDock` uses the mode overload only when there's no saved layout. A saved layout is always respected (spec, "Left to the builder").

These controls are already **disabled** when `IsEditable` is false (no project). In
reading mode they must be **hidden**. Bind their `IsVisible` to `!IsReadingMode` and
leave the existing `IsEnabled` bindings alone. The canvas has no `IsReadingMode` of its
own: reading mode with no project already makes `IsEditable` false, and that is enough
for the canvas. If a test shows that a project-open reading canvas still offers an editing
route, add a `ConversationViewModel.IsReadingMode` set from `UpdateEditable()` (Task 2).

- [x] **Step 1: Write the failing tests** (headless, a project open, `vm.SwitchModeAsync(Reading)`)

```csharp
[AvaloniaFact] public void NodeContextMenu_OffersOnlyShowAllLinesBySpeaker()
[AvaloniaFact] public void EmptySpaceContextMenu_IsNotShown()
[AvaloniaFact] public void DoubleClickOnEmptySpace_AddsNoNode()
[AvaloniaFact] public void DraggingANode_DoesNotMoveIt_OrMarkTheProjectModified()
[AvaloniaFact] public void ChooseStartNode_AndAddNote_AreHidden()
[AvaloniaFact] public void Details_ShowTextSpeakerAndSummaries_ReadOnly()   // DefaultTextBox.IsReadOnly, ConditionSummary/ScriptSummary visible
[AvaloniaFact] public void Details_HideEveryEditingControl()                // add/delete link, Edit Conditions/Scripts, dictionary buttons, warnings, VO alias/reuse: IsVisible false
[AvaloniaFact] public void Details_ShowEverythingAgain_InEditingMode()
// EditorDockFactoryReadingModeTests (factory built as in DockLayoutPinnedPaneTests.NewFactory)
[Fact] public void DefaultReadingLayout_HidesConditionSearch_ButCanRestoreIt()  // in root.HiddenDockables; RestoreDockable("ConditionSearch") brings it back to RightPane
[Fact] public void DefaultEditingLayout_IsUnchanged()                         // ConditionSearch in RightPane.VisibleDockables
```

- [x] **Step 2: Run them and confirm they fail.**
- [x] **Step 3: Implement** the bindings, and the early return in `Editor_DoubleTapped` (it already checks `IsEditable`, so confirm the test reaches that check).
- [x] **Step 4: Run the tests.** Expected: PASS. Also run `NodeDetailViewTextWarningsTests` and `SpellingContextMenuTests`.
- [x] **Step 5: Commit**: `feat(reading-mode): canvas and node details in reading mode (#234)`.

---

### Task 11: Audit of kept windows, GUI verification and docs

**Files:**
- Read: `SettingsWindow`, `ChangelogWindow`, `AboutWindow`, `SpeakerLineBrowserWindow`, `RepDispositionBalanceWindow`, `ConditionSearchView`, `GameBrowserView` (`.axaml` + `.axaml.cs` + their VMs)
- Test: `DialogEditor.Tests/Views/ReadingModeKeptWindowsTests.cs` (only if the audit finds a write route)
- Modify: `docs/superpowers/specs/2026-10-26-reading-mode-design.md` (only if the code now differs), and this plan's checkboxes plus a `## Handoff — <date>` section
- Modify: `docs/audience.md`. The "When the Reader and the Modder conflict" section changes from "until that exists" to describe reading mode.

- [x] **Step 1: Audit.** For each kept window, list every action that writes outside the app's own settings and log folder: a game file, a project file, or an export. Expected findings: none in Settings (it only writes app settings). In the Speaker Line Browser, its jump-to-node navigates and doesn't edit. Hide any write route found behind `IsEditingMode`, with a failing test first.
- [x] **Step 2: Run the full headless suite.** `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. Expected: all pass.
- [x] **Step 3: GUI verification** with the `running-the-app` skill against a canonical fixture copy, with settings backed up and restored, and the editor stopped before the restore:
  1. Fresh settings: theme onboarding, then the chooser side by side. Close it, and confirm the main window opens in reading mode, the Reader tour starts, and the badge reads "Reading".
  2. Open a conversation and walk a branch with the arrow keys. Check that Test is missing, that Ctrl+S and Delete do nothing, and that the filter box still takes typing.
  3. Click the badge. Editing mode opens, the editing tour starts, and the menus are back.
  4. Make an edit, then View › Reading mode. The Unsaved Changes dialog appears. *Stay Here* keeps editing; *Discard* switches with the project still open.
  5. Open Rep & Disposition Balance and Browse Speaker Lines with only a game folder open.
  6. At 200 % text size, the chooser stacks.
  Screenshot each step and look at every PNG.
- [x] **Step 4: Docs and learnings.** Update `docs/audience.md` and tick this plan's boxes. Use `trail note` for anything that took digging, such as the deferred-prompt flow and why the shortcut gate returns without marking the key handled. Append the handoff section.
- [x] **Step 5: Commit and open the PR**: `docs(reading-mode): audience, plan and handoff (#234)`. Run `trail check`, push, then `gh pr create` with "Closes #234". Bind the PR and turn on Auto-fix.

## Handoff — 2026-10-26

**Done.** All 11 tasks are implemented and each was reviewed. After that came a final whole-branch review (Opus) and three residual fix rounds. PR [#330](https://github.com/kjmikkel/PillarsDialogEditor/pull/330) ("Closes #234") is open on `feat/234-reading-mode`, with Auto-fix on. The full headless suite passes: 4825 passed, 0 failed.

**Changed beyond the plan.** These are behaviour changes; the PR description lists them.
- Autosave serialises a folded copy, so `_project` stays the last-saved state.
- Discard reverts the open conversation before it continues.
- `FinishLoad` reloads the open conversation when the project is swapped.
- In Editing, cancelling the deferred conflict merge closes the project, and so does answering "Later" to the deferred autosave offer.
- `SaveProject` refuses to write over a file whose git conflict is still pending.
- Resolve Conflicts refuses while there are unsaved edits.
- The test-mode overlay's End Test and Restore button also works in Reading.
The three `.trail/learnings/2026-10-26-*` notes explain why.

**Remaining.**
1. Done 2026-10-27. A GUI smoke run in the real app passed all five checks:
   - no "Viewing only … New Project" line in Reading;
   - the female-text placeholder reads "(same as default)";
   - no "Only my lines" control without a project;
   - the Rep & Disposition hints drop the source picker;
   - after a badge switch the status bar keeps no stale hover text, Discard after an autosave restores the saved text with no dirty marker, and Stay Here keeps the edit.
   The run found one more stale "selected source" hint, on the Analyze button. That is now fixed.
2. Watch PR #330 CI and review, then merge.

**Follow-ups filed, not in this PR.** #325 (type-name UIA names), #326 (merge and export ignore unsaved edits), #327 (New Project and Merge Projects leave a stale canvas), #328 (switching to a project-created conversation after the prompt fails), #329 (Apply from diff, Undo apply and Merge Projects can write over a pending conflict), and a plural comment on #264.

**Do not redo.** Any of Tasks 1–11, the final review, or the residual fixes R1–R7.
