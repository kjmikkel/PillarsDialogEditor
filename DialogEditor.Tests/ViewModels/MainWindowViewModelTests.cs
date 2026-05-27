using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelTests
{
    public MainWindowViewModelTests() => Loc.Configure(new StubStringProvider());

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    /// <summary>Injects a project into the VM via the private SetProject method using reflection.</summary>
    private static void InjectProject(MainWindowViewModel vm, DialogProject project)
    {
        var mi = typeof(MainWindowViewModel)
            .GetMethod("SetProject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        mi.Invoke(vm, [project]);
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
}
