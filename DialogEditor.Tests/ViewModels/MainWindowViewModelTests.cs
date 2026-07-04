using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Import;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.GitConflict;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelTests : IDisposable
{
    private readonly List<string> _importTempFiles = [];
    private readonly string _settingsPath;

    public MainWindowViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
        // Isolate AppSettings so the VM constructor doesn't auto-load the machine's real
        // last game folder — which reads thousands of game files from disk and makes these
        // tests slow and dependent on local state (see project_flaky_test_appsettings).
        // A fresh, non-existent settings file means LastGameDirectory is null, so the
        // constructor's startup LoadDirectory(...) is skipped.
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        foreach (var f in _importTempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort cleanup */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    /// <summary>Injects a project into the VM via the private SetProject method using reflection.</summary>
    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
    }

    /// <summary>Injects a provider into the VM's private _provider field via reflection.</summary>
    private static void InjectProvider(MainWindowViewModel vm, IGameDataProvider provider)
    {
        var fi = typeof(MainWindowViewModel)
            .GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, provider);
    }

    [Fact]
    public void CreateSampleProject_DisabledUntilGameLoaded()
    {
        var vm = MakeVm();   // AppSettings is isolated in the fixture, so no game is auto-loaded
        Assert.False(vm.CreateSampleProjectCommand.CanExecute(null));

        InjectProvider(vm, new DialogEditor.Tests.Helpers.FakeGameDataProvider("poe1", "en"));
        Assert.True(vm.CreateSampleProjectCommand.CanExecute(null));
    }

    [Fact]
    public void OpenWalkthrough_TriesBundledFileThenUrl()
    {
        var vm = MakeVm();
        IReadOnlyList<string>? offered = null;
        vm.WalkthroughOpener = candidates => { offered = candidates; return true; };

        vm.OpenWalkthroughCommand.Execute(null);

        Assert.NotNull(offered);
        Assert.Equal(2, offered!.Count);
        Assert.EndsWith("walkthrough.md", offered[0]);   // bundled file first
        Assert.StartsWith("http", offered[1]);           // URL fallback second
    }

    private static DialogProject MakeProjectWithTranslations()
    {
        var patch = new ConversationPatch("test_conv", 2, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
            {
                ["en"] = [new NodeTranslation(1, "Hello", "")],
            },
        };
        return DialogProject.Empty("TestProject").WithPatch(patch);
    }

    private static void InvokeLoadProject(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("LoadProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [path]);
    }

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("LoadProjectAsync", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;   // offerDeferred: false (explicit-open semantics)
    }

    private string TempProjectPath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"conf_{Guid.NewGuid():N}.dialogproject");
        _importTempFiles.Add(path);
        return path;
    }

    // A DialogProject with one conversation whose node 4 sets DefaultText to `to`.
    private static DialogProject GreetingProject(string to)
    {
        var mod = new NodeModification(
            4, new Dictionary<string, FieldChange> { ["DefaultText"] = new FieldChange("orig", to) }, [], []);
        return DialogProject.Empty("ConfProj").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [], [mod]));
    }

    // Wraps two serialized projects in git conflict markers — the reconstructed
    // sides deserialize back to `mine` / `theirs`.
    private static string ConflictedBlob(DialogProject mine, DialogProject theirs)
        => "<<<<<<< HEAD\n" + DialogProjectSerializer.Serialize(mine)
         + "\n=======\n"    + DialogProjectSerializer.Serialize(theirs)
         + "\n>>>>>>> branch\n";

    // ── Git conflict detection on open ────────────────────────────────────

    [Fact]
    public void LoadProject_GitConflictUnparseable_ShowsGuidanceAndDoesNotOpen()
    {
        var conflicted =
            "{\n" +
            "<<<<<<< HEAD\n" +
            "  this is not valid json\n" +
            "=======\n" +
            "  neither is this\n" +
            ">>>>>>> feature\n" +
            "}\n";
        var path = Path.Combine(Path.GetTempPath(), $"conf_{Guid.NewGuid():N}.dialogproject");
        File.WriteAllText(path, conflicted);
        _importTempFiles.Add(path);

        var vm = MakeVm();
        InvokeLoadProject(vm, path);

        Assert.Equal("Status_ProjectGitConflictUnparseable", vm.StatusText);
        Assert.False(vm.IsProjectOpen);
    }

    [Fact]
    public async Task LoadProject_GitConflictResolved_OpensDirtyAndSavePersistsMerged()
    {
        var path = TempProjectPath();
        File.WriteAllText(path, ConflictedBlob(GreetingProject("friend"), GreetingProject("traveler")));

        var vm = MakeVm();
        vm.ShowGitConflictResolution = res =>
        {
            res.Conflicts[0].Choice = MergeSide.Theirs;   // take "traveler"
            res.ApplyCommand.Execute(null);
            return Task.FromResult(res.Result);
        };

        await InvokeLoadProjectAsync(vm, path);

        Assert.True(vm.IsProjectOpen);
        Assert.True(vm.IsModified);
        Assert.True(vm.SaveProjectCommand.CanExecute(null));   // I1: saveable with no conversation open

        vm.SaveProjectCommand.Execute(null);

        var reloaded = DialogProjectSerializer.LoadFromFile(path);
        var mod = reloaded.Patches["greeting"].ModifiedNodes.Single(m => m.NodeId == 4);
        Assert.Equal("traveler", mod.FieldChanges["DefaultText"].To);
    }

    [Fact]
    public async Task LoadProject_MarkersButIdenticalSides_OpensWithoutShowingDialog()
    {
        var path = TempProjectPath();
        File.WriteAllText(path, ConflictedBlob(GreetingProject("same"), GreetingProject("same")));

        var vm = MakeVm();
        var dialogShown = false;
        vm.ShowGitConflictResolution = _ => { dialogShown = true; return Task.FromResult<DialogProject?>(null); };

        await InvokeLoadProjectAsync(vm, path);

        Assert.False(dialogShown);
        Assert.True(vm.IsProjectOpen);
    }

    [Fact]
    public async Task LoadProject_GitConflictCancelled_DoesNotOpen()
    {
        var path = TempProjectPath();
        File.WriteAllText(path, ConflictedBlob(GreetingProject("friend"), GreetingProject("traveler")));

        var vm = MakeVm();
        vm.ShowGitConflictResolution = _ => Task.FromResult<DialogProject?>(null);   // user cancels

        await InvokeLoadProjectAsync(vm, path);

        Assert.False(vm.IsProjectOpen);
        Assert.Equal("Status_ProjectGitConflictCancelled", vm.StatusText);
    }

    [Fact]
    public void ReopenLastProjectOnStartup_ConflictedProject_OffersResolvePromptNotModal()
    {
        // Startup must NOT slam up the resolution modal; it offers a "Resolve…" action.
        var saved = AppSettings.LastProjectPath;
        try
        {
            var path = TempProjectPath();
            File.WriteAllText(path, ConflictedBlob(GreetingProject("friend"), GreetingProject("traveler")));
            AppSettings.LastProjectPath = path;

            var vm = MakeVm();
            var shown = false;
            vm.ShowGitConflictResolution = _ => { shown = true; return Task.FromResult<DialogProject?>(null); };

            vm.ReopenLastProjectOnStartup();

            Assert.False(shown);                                       // no immediate modal
            Assert.True(vm.HasPendingConflictResolution);              // a Resolve… prompt is offered
            Assert.True(vm.ResolveConflictsCommand.CanExecute(null));
            Assert.False(vm.IsProjectOpen);                            // not opened until resolved
        }
        finally
        {
            AppSettings.LastProjectPath = saved;
        }
    }

    [Fact]
    public async Task ResolveConflictsCommand_ShowsDialogResolvesAndClearsPrompt()
    {
        var saved = AppSettings.LastProjectPath;
        try
        {
            var path = TempProjectPath();
            File.WriteAllText(path, ConflictedBlob(GreetingProject("friend"), GreetingProject("traveler")));
            AppSettings.LastProjectPath = path;

            var vm = MakeVm();
            vm.ShowGitConflictResolution = res =>
            {
                res.Conflicts[0].Choice = MergeSide.Theirs;
                res.ApplyCommand.Execute(null);
                return Task.FromResult(res.Result);
            };

            vm.ReopenLastProjectOnStartup();
            Assert.True(vm.HasPendingConflictResolution);

            await vm.ResolveConflictsCommand.ExecuteAsync(null);

            Assert.True(vm.IsProjectOpen);
            Assert.False(vm.HasPendingConflictResolution);   // prompt cleared after resolve
        }
        finally
        {
            AppSettings.LastProjectPath = saved;
        }
    }

    // ── WindowTitle ───────────────────────────────────────────────────────

    [Fact]
    public void WindowTitle_NoConversationOpen_ContainsAppTitle()
    {
        var vm = MakeVm();
        Assert.Equal(Loc.Get("App_Title"), vm.WindowTitle);
    }

    [Fact]
    public void WindowTitle_WithProjectName_AppendsProjectName()
    {
        var vm = MakeVm();
        vm.CurrentProjectName = "MyMod";
        Assert.Contains("MyMod", vm.WindowTitle);
    }

    [Fact]
    public void WindowTitle_WithOpenConversation_ShowsConversationName()
    {
        var vm = MakeVm();
        vm.CurrentConversationName = "intro";
        Assert.Contains("intro", vm.WindowTitle);
    }

    [Fact]
    public void WindowTitle_WhenDirtyAndConversationOpen_ShowsBullet()
    {
        var vm = MakeVm();
        vm.CurrentConversationName = "intro";
        vm.Canvas.AddNode(MakeNode(1), new(0, 0)); // makes canvas dirty
        Assert.StartsWith("●", vm.WindowTitle);
    }

    [Fact]
    public void WindowTitle_WhenDirtyButNoConversation_NoBullet()
    {
        var vm = MakeVm();
        // IsModified=true but CurrentConversationName is null
        vm.Canvas.AddNode(MakeNode(1), new(0, 0));
        Assert.DoesNotContain("●", vm.WindowTitle);
    }

    // ── IsBrowserFlyoutOpen ───────────────────────────────────────────────

    [Fact]
    public void IsBrowserFlyoutOpen_TrueWhenExpandedAndNotPinned()
    {
        var vm = MakeVm();
        vm.IsBrowserPinned   = false;
        vm.IsBrowserExpanded = true;
        Assert.True(vm.IsBrowserFlyoutOpen);
    }

    [Fact]
    public void IsBrowserFlyoutOpen_FalseWhenPinned()
    {
        var vm = MakeVm();
        vm.IsBrowserPinned   = true;
        vm.IsBrowserExpanded = true;
        Assert.False(vm.IsBrowserFlyoutOpen);
    }

    [Fact]
    public void IsBrowserFlyoutOpen_FalseWhenCollapsed()
    {
        var vm = MakeVm();
        vm.IsBrowserPinned   = false;
        vm.IsBrowserExpanded = false;
        Assert.False(vm.IsBrowserFlyoutOpen);
    }

    // ── GuardDirtyThen — clean state: runs immediately ────────────────────

    [Fact]
    public void GuardDirtyThen_WhenNotDirty_RunsActionImmediately()
    {
        var vm  = MakeVm();
        var ran = false;
        vm.GuardDirtyThen(() => ran = true);
        Assert.True(ran);
    }

    [Fact]
    public void GuardDirtyThen_WhenDirtyButNoConversation_RunsImmediately()
    {
        // Dirty canvas but no CurrentConversationName — guard should not fire
        var vm  = MakeVm();
        vm.Canvas.AddNode(MakeNode(1), new(0, 0));
        var ran = false;
        vm.GuardDirtyThen(() => ran = true);
        Assert.True(ran);
    }

    // ── GuardDirtyThen — dirty state: fires event instead ────────────────

    [Fact]
    public void GuardDirtyThen_WhenDirtyAndConversationOpen_FiresEvent()
    {
        var vm      = MakeVm();
        vm.CurrentConversationName = "intro";
        vm.Canvas.IsModified = true;
        vm.IsModified        = true;

        var eventFired = false;
        vm.UnsavedChangesRequested += () => eventFired = true;

        var ran = false;
        vm.GuardDirtyThen(() => ran = true);

        Assert.True(eventFired);
        Assert.False(ran);
    }

    // ── DiscardAndProceed ─────────────────────────────────────────────────

    [Fact]
    public void DiscardAndProceed_RunsPendingAction()
    {
        var vm = MakeVm();
        vm.CurrentConversationName = "intro";
        vm.Canvas.IsModified = true;
        vm.IsModified        = true;

        var ran = false;
        vm.GuardDirtyThen(() => ran = true);   // sets _pendingAction
        vm.DiscardAndProceed();

        Assert.True(ran);
    }

    // ── CancelPendingNavigation ───────────────────────────────────────────

    [Fact]
    public void CancelPendingNavigation_PreventsActionFromRunning()
    {
        var vm = MakeVm();
        vm.CurrentConversationName = "intro";
        vm.Canvas.IsModified = true;
        vm.IsModified        = true;

        var ran = false;
        vm.GuardDirtyThen(() => ran = true);   // sets _pendingAction
        vm.CancelPendingNavigation();
        vm.DiscardAndProceed();                // Proceed() — no pending action anymore

        Assert.False(ran);
    }

    // ── IsProjectOpen ─────────────────────────────────────────────────────

    [Fact]
    public void IsProjectOpen_FalseInitially()
    {
        var vm = MakeVm();
        Assert.False(vm.IsProjectOpen);
    }

    // ── ExportForTranslationCommand ───────────────────────────────────────

    [Fact]
    public async Task ExportForTranslation_WithProject_CallsExportService()
    {
        var tempFile = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        var vm = new MainWindowViewModel(
            new StubDispatcher(),
            new StubFolderPicker(),
            new StubFilePicker(saveResult: tempFile));

        InjectProject(vm, MakeProjectWithTranslations());
        vm.RequestLanguageCode = (_, _) => Task.FromResult<string?>("en");

        await vm.ExportForTranslationCommand.ExecuteAsync(null);

        Assert.True(File.Exists(tempFile), "Export service should have written a file at the stub path.");
        if (File.Exists(tempFile)) File.Delete(tempFile);
    }

    [Fact]
    public async Task ImportTranslation_WithProject_MarksProjectDirty()
    {
        // Write a minimal CSV that the import service can parse
        var tempCsv = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(tempCsv,
            "ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText\r\n" +
            "test_conv,1,,Hello,,Bonjour,\r\n");

        try
        {
            var vm = new MainWindowViewModel(
                new StubDispatcher(),
                new StubFolderPicker(),
                new StubFilePicker(openResult: tempCsv));

            InjectProject(vm, MakeProjectWithTranslations());
            vm.RequestLanguageCode = (_, _) => Task.FromResult<string?>("fr");

            await vm.ImportTranslationCommand.ExecuteAsync(null);

            Assert.True(vm.IsModified, "Importing a translation should mark the project as dirty.");
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    [Fact]
    public void ExportForTranslation_WithoutProject_CommandDisabled()
    {
        var vm = MakeVm();
        Assert.False(vm.ExportForTranslationCommand.CanExecute(null));
    }

    [Fact]
    public async Task ImportTranslation_LanguageCancelled_DoesNotMarkDirty()
    {
        var tempCsv = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".csv");
        File.WriteAllText(tempCsv,
            "ConversationName,NodeId,WriterComment,SourceDefaultText,SourceFemaleText,TranslatedDefaultText,TranslatedFemaleText\r\n" +
            "test_conv,1,,Hello,,Bonjour,\r\n");

        try
        {
            var vm = new MainWindowViewModel(
                new StubDispatcher(),
                new StubFolderPicker(),
                new StubFilePicker(openResult: tempCsv));

            InjectProject(vm, MakeProjectWithTranslations());
            vm.RequestLanguageCode = (_, _) => Task.FromResult<string?>(null); // user cancels

            await vm.ImportTranslationCommand.ExecuteAsync(null);

            Assert.False(vm.IsModified, "Cancelling language dialog should not mark the project dirty.");
        }
        finally
        {
            if (File.Exists(tempCsv)) File.Delete(tempCsv);
        }
    }

    // ── Import warnings ───────────────────────────────────────────────────

    private string WriteTempYarn(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), Path.ChangeExtension(Path.GetRandomFileName(), ".yarn"));
        File.WriteAllText(path, content);
        _importTempFiles.Add(path);
        return path;
    }

    private static (MainWindowViewModel Vm, StubProvider Provider) MakeImportableVm(string yarnPath)
    {
        var file     = new ConversationFile("stub_conv", "", "", "");
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));
        var vm = new MainWindowViewModel(
            new StubDispatcher(),
            new StubFolderPicker(),
            new StubFilePicker(openResult: yarnPath));
        InjectProvider(vm, provider);
        InjectProject(vm, DialogProject.Empty("TestProject"));
        return (vm, provider);
    }

    [Fact]
    public async Task ImportConversation_YarnWithSkippedConstructs_InvokesWarningCallback()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            <<if $x>>
            Npc: Hello.
            ===
            """);
        var (vm, _) = MakeImportableVm(path);

        IReadOnlyList<ImportWarning>? captured = null;
        vm.ShowImportWarnings = w => { captured = w; return Task.CompletedTask; };

        await vm.ImportConversationCommand.ExecuteAsync(null);

        Assert.NotNull(captured);
        Assert.Contains(captured!, w => w.Construct == "if");
    }

    [Fact]
    public async Task ImportConversation_YarnWithoutConstructs_DoesNotInvokeCallback()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Just dialogue.
            ===
            """);
        var (vm, _) = MakeImportableVm(path);

        var invoked = false;
        vm.ShowImportWarnings = w => { invoked = true; return Task.CompletedTask; };

        await vm.ImportConversationCommand.ExecuteAsync(null);

        Assert.False(invoked);
    }

    [Fact]
    public async Task ImportConversation_WithWarnings_UsesWarningStatusFormat()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            <<set $y = 1>>
            Npc: Hi.
            ===
            """);
        var (vm, _) = MakeImportableVm(path);
        vm.ShowImportWarnings = _ => Task.CompletedTask;

        await vm.ImportConversationCommand.ExecuteAsync(null);

        // StubStringProvider returns the key verbatim, so StatusText equals the chosen key.
        Assert.Equal("Status_ImportConversationAddedWithWarnings", vm.StatusText);
    }

    [Fact]
    public async Task ImportConversation_NoWarnings_UsesCleanStatusFormat()
    {
        var path = WriteTempYarn("""
            title: Start
            ---
            Npc: Hi.
            ===
            """);
        var (vm, _) = MakeImportableVm(path);

        await vm.ImportConversationCommand.ExecuteAsync(null);

        Assert.Equal("Status_ImportConversationAdded", vm.StatusText);
    }

    // ── EnsureNoUnsavedEditsAsync ─────────────────────────────────────────

    private static void SetModified(MainWindowViewModel vm, bool value)
    {
        vm.IsModified = value;
        // CurrentConversationName must be non-null for the dirty guard to engage.
        vm.CurrentConversationName = value ? "conv" : null;
    }

    [Fact]
    public async Task EnsureNoUnsavedEdits_Clean_ReturnsTrueImmediately()
    {
        var vm = MakeVm();
        Assert.True(await vm.EnsureNoUnsavedEditsAsync());
    }

    [Fact]
    public async Task EnsureNoUnsavedEdits_Dirty_Discard_ReturnsTrue()
    {
        var vm = MakeVm();
        SetModified(vm, true);

        var task = vm.EnsureNoUnsavedEditsAsync();      // pends on the dialog decision
        vm.DiscardAndProceed();                          // host's "Discard" path

        Assert.True(await task);
    }

    [Fact]
    public async Task EnsureNoUnsavedEdits_Dirty_Cancel_ReturnsFalse()
    {
        var vm = MakeVm();
        SetModified(vm, true);

        var task = vm.EnsureNoUnsavedEditsAsync();
        vm.CancelPendingNavigation();                    // host's "Cancel" path

        Assert.False(await task);
    }

    [Fact]
    public async Task EnsureNoUnsavedEdits_Dirty_Save_ReturnsTrue()
    {
        var vm = MakeVm();
        SetModified(vm, true);

        var task = vm.EnsureNoUnsavedEditsAsync();
        vm.SaveAndProceed();   // Save (no-ops without a provider/file in tests) then Proceed

        Assert.True(await task);
    }

    // ── ReloadCurrentProjectFromDisk ──────────────────────────────────────

    private static void SetProjectPath(MainWindowViewModel vm, string? path) =>
        typeof(MainWindowViewModel)
            .GetField("_projectPath", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, path);

    [Fact]
    public void Reload_FileExists_ReloadsProject()
    {
        var vm   = MakeVm();
        var path = TempProjectPath();
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("Reloaded"));
        SetProjectPath(vm, path);

        vm.ReloadCurrentProjectFromDisk();

        Assert.True(vm.IsProjectOpen);
        Assert.Equal("Reloaded", vm.CurrentProjectName);
    }

    [Fact]
    public void Reload_FileMissingOnBranch_ClosesProject()
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("Open"));
        SetProjectPath(vm, Path.Combine(Path.GetTempPath(), $"gone_{Guid.NewGuid():N}.dialogproject"));

        vm.ReloadCurrentProjectFromDisk();

        Assert.False(vm.IsProjectOpen);
        Assert.False(string.IsNullOrEmpty(vm.StatusText));
    }

    // ── DisplayStatusText / FocusHintText ───────────────────────────────────

    [Fact]
    public void DisplayStatusText_FallsBackToStatusText_WhenNoFocusHint()
    {
        var vm = MakeVm();
        vm.StatusText = "Saved";

        Assert.Equal("Saved", vm.DisplayStatusText);
    }

    [Fact]
    public void DisplayStatusText_PrefersFocusHintText_WhenSet()
    {
        var vm = MakeVm();
        vm.StatusText = "Saved";
        vm.FocusHintText = "Opens the settings dialog";

        Assert.Equal("Opens the settings dialog", vm.DisplayStatusText);
    }

    [Fact]
    public void DisplayStatusText_RevertsToStatusText_WhenFocusHintCleared()
    {
        var vm = MakeVm();
        vm.StatusText = "Saved";
        vm.FocusHintText = "Opens the settings dialog";

        vm.FocusHintText = "";

        Assert.Equal("Saved", vm.DisplayStatusText);
    }

    [Fact]
    public void DisplayStatusText_RaisesPropertyChanged_WhenEitherSourceChanges()
    {
        var vm = MakeVm();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);

        vm.FocusHintText = "Opens the settings dialog";
        Assert.Contains(nameof(MainWindowViewModel.DisplayStatusText), raised);

        raised.Clear();
        vm.StatusText = "Saved";
        Assert.Contains(nameof(MainWindowViewModel.DisplayStatusText), raised);
    }

    // ── ConnectMode StatusText ────────────────────────────────────────────

    private static void WireNode(ConversationViewModel canvas, NodeViewModel n)
    {
        n.OnSelected = node => canvas.SelectedNode = node;
        canvas.Nodes.Add(n);
    }

    [Fact]
    public void ConnectModeStarted_SetsStatusText()
    {
        var vm = MakeVm();
        vm.Canvas.IsEditable = true;
        var src = CanvasNavigationServiceTests.MakeNode(0);
        WireNode(vm.Canvas, src);

        vm.Canvas.BeginConnect(src);

        Assert.Equal("Status_ConnectMode_Started", vm.StatusText);
    }

    [Fact]
    public void ConnectModeConnected_SetsStatusText()
    {
        var vm = MakeVm();
        vm.Canvas.IsEditable = true;
        var src = CanvasNavigationServiceTests.MakeNode(0, 0, 0);
        var tgt = CanvasNavigationServiceTests.MakeNode(1, 400, 0);
        WireNode(vm.Canvas, src);
        WireNode(vm.Canvas, tgt);

        vm.Canvas.BeginConnect(src);
        vm.Canvas.SelectNode(tgt);
        vm.Canvas.TryConfirmConnection();

        Assert.Equal("Status_ConnectMode_Connected", vm.StatusText);
    }

    [Fact]
    public void ConnectModeCancelled_SetsStatusText()
    {
        var vm = MakeVm();
        vm.Canvas.IsEditable = true;
        var src = CanvasNavigationServiceTests.MakeNode(0);
        WireNode(vm.Canvas, src);

        vm.Canvas.BeginConnect(src);
        vm.Canvas.CancelConnect();

        Assert.Equal("Status_ConnectMode_Cancelled", vm.StatusText);
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static NodeViewModel MakeNode(int id)
    {
        var node = new DialogEditor.Core.Models.ConversationNode(
            NodeId: id, IsPlayerChoice: false,
            SpeakerCategory: DialogEditor.Core.Models.SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [],
            Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None");
        return new NodeViewModel(node, new DialogEditor.Core.Models.StringEntry(id, "text", ""));
    }

    // ── BatchImportVoAllCommand ───────────────────────────────────────────

    /// <summary>Sets a private string field (e.g. _projectPath, _activeGameId) via reflection.</summary>
    private static void SetPrivateField(MainWindowViewModel vm, string field, object? value)
    {
        var fi = typeof(MainWindowViewModel)
            .GetField(field, System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, value);
    }

    private static MainWindowViewModel MakeVoAllReadyVm(FakeGameDataProvider provider)
    {
        var vm = MakeVm();
        InjectProject(vm, DialogProject.Empty("P"));
        InjectProvider(vm, provider);
        SetPrivateField(vm, "_projectPath", Path.Combine(Path.GetTempPath(), "p.dialogproject"));
        SetPrivateField(vm, "_currentGameDirectory", Path.GetTempPath());
        SetPrivateField(vm, "_activeGameId", "poe2");
        return vm;
    }

    [Fact]
    public void BatchImportVoAll_DisabledWithoutProject()
    {
        var vm = MakeVm();
        Assert.False(vm.BatchImportVoAllCommand.CanExecute(null));
    }

    [Fact]
    public void BatchImportVoAll_DisabledWithoutPoe2GameFolder()
    {
        var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe1", "en"));
        SetPrivateField(vm, "_activeGameId", "poe1");
        Assert.False(vm.BatchImportVoAllCommand.CanExecute(null));
    }

    [Fact]
    public void BatchImportVoAll_DisabledWithoutSavedProjectPath()
    {
        var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe2", "en"));
        SetPrivateField(vm, "_projectPath", null);
        Assert.False(vm.BatchImportVoAllCommand.CanExecute(null));
    }

    [Fact]
    public void BatchImportVoAll_EnabledWithProjectAndPoe2Folder()
    {
        var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe2", "en"));
        Assert.True(vm.BatchImportVoAllCommand.CanExecute(null));
    }

    [Fact]
    public async Task BatchImportVoAll_EmptyScan_ReportsViaStatusBar_AndSkipsDialog()
    {
        // Project has no patches at all → scanner returns zero rows.
        var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe2", "en"));
        var dialogShown = false;
        vm.ShowBatchVoImportAll = _ => { dialogShown = true; return Task.CompletedTask; };

        await vm.BatchImportVoAllCommand.ExecuteAsync(null);

        Assert.False(dialogShown);
        Assert.Equal("Status_BatchImportVoAllEmpty", vm.StatusText);
    }

    [Fact]
    public async Task BatchImportVoAll_WithVoicedNodes_HandsRowsToDialogDelegate()
    {
        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
        try
        {
            var node = new ConversationNode(
                1, false, SpeakerCategory.Npc, "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "", [],
                [], [], "Conversation", "None",
                ActorDirection: "", Comments: "", ExternalVO: "",
                HasVO: true, HideSpeaker: false);
            var conv = new Conversation("conv", [node],
                new StringTable([new StringEntry(1, "line", "")]));
            var vm = MakeVoAllReadyVm(new FakeGameDataProvider("poe2", "en", conv));
            InjectProject(vm, DialogProject.Empty("P").WithPatch(
                new ConversationPatch("conv", ConversationPatch.CurrentSchemaVersion, [], [], [])));

            IReadOnlyList<BatchVoRowViewModel>? received = null;
            vm.ShowBatchVoImportAll = rows => { received = rows; return Task.CompletedTask; };

            await vm.BatchImportVoAllCommand.ExecuteAsync(null);

            Assert.NotNull(received);
            var row = Assert.Single(received!);
            Assert.Equal("conv", row.ConversationName);
            Assert.Equal(1, row.NodeId);
        }
        finally
        {
            ChatterPrefixService.Clear();
        }
    }
}
