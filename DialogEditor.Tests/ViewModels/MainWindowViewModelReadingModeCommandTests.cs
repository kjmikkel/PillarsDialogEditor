using System.Reflection;
using System.Windows.Input;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Reading mode (issue 234): every editing command is off while reading, so no menu item or
/// shortcut can reach one. Spec: docs/superpowers/specs/2026-10-26-reading-mode-design.md
public class MainWindowViewModelReadingModeCommandTests : IDisposable
{
    private const BindingFlags NonPublic = BindingFlags.NonPublic | BindingFlags.Instance;

    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowViewModelReadingModeCommandTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_readingcmd_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"readingcmd_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static void SetField(MainWindowViewModel vm, string name, object? value) =>
        typeof(MainWindowViewModel).GetField(name, NonPublic)!.SetValue(vm, value);

    private static ICommand Cmd(MainWindowViewModel vm, string name) =>
        (ICommand)typeof(MainWindowViewModel).GetProperty(name)!.GetValue(vm)!;

    /// A saved, modified project with a provider, a game folder, a pending undo-apply and a
    /// pending conflict resolution - every gated command is enabled in Editing mode.
    private async Task<MainWindowViewModel> OpenFullVm()
    {
        var path = Path.Combine(_tempDir, "p.dialogproject");
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("p"));
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        SetField(vm, "_provider", new FakeGameDataProvider("poe2", "en"));
        SetField(vm, "_currentGameDirectory", _tempDir);
        SetField(vm, "_activeGameId", "poe2");
        await (Task)typeof(MainWindowViewModel).GetMethod("LoadProjectAsync", NonPublic)!.Invoke(vm, [path, false])!;
        SetField(vm, "_currentFile", new ConversationFile("c", "", "", ""));
        SetField(vm, "_preApplyProject", DialogProject.Empty("p"));
        vm.HasPendingConflictResolution = true;
        vm.IsModified = true;
        return vm;
    }

    [Theory]
    [InlineData("NewProjectCommand")] [InlineData("NewConversationCommand")] [InlineData("ImportConversationCommand")]
    [InlineData("ExportConversationsCommand")] [InlineData("SaveProjectCommand")] [InlineData("SaveProjectAsCommand")]
    [InlineData("UndoApplyCommand")] [InlineData("MergeProjectsCommand")] [InlineData("ExportForTranslationCommand")]
    [InlineData("ImportTranslationCommand")] [InlineData("CreateSampleProjectCommand")] [InlineData("OpenWalkthroughCommand")]
    [InlineData("TagReferenceCommand")] [InlineData("RestoreBackupCommand")] [InlineData("SaveCommand")]
    [InlineData("TestPatchCommand")] [InlineData("RestoreConversationCommand")] [InlineData("FindInProjectCommand")]
    [InlineData("BatchReplaceCommand")] [InlineData("BatchImportVoAllCommand")] [InlineData("ResolveConflictsCommand")]
    public async Task EditingCommand_CannotRun_InReadingMode(string name)
    {
        var vm = await OpenFullVm();
        Assert.True(Cmd(vm, name).CanExecute(null), "precondition: enabled while editing");

        vm.Mode = EditorMode.Reading;

        Assert.False(Cmd(vm, name).CanExecute(null));
    }

    [Theory]
    [InlineData("OpenProjectCommand")] [InlineData("OpenRecentProjectCommand")] [InlineData("ClearRecentProjectsCommand")]
    [InlineData("CloseProjectCommand")] [InlineData("OpenFolderCommand")] [InlineData("StartGuidedTourCommand")]
    [InlineData("ChangelogCommand")] [InlineData("AboutCommand")] [InlineData("ToggleReadingModeCommand")]
    public async Task ReadingCommand_StillRuns_InReadingMode(string name)
    {
        var vm = await OpenFullVm();

        vm.Mode = EditorMode.Reading;

        Assert.True(Cmd(vm, name).CanExecute(null));
    }

    [Fact]
    public async Task SwitchingBack_ReenablesTheEditingCommands()
    {
        var vm = await OpenFullVm();
        vm.Mode = EditorMode.Reading;
        Assert.False(vm.SaveProjectCommand.CanExecute(null));

        vm.Mode = EditorMode.Editing;

        Assert.True(vm.SaveProjectCommand.CanExecute(null));
    }
}
