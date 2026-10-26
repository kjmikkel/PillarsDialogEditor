using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Reading mode (#234): the editor mode on the main view model and switching it behind the
/// unsaved-changes guard. Spec: docs/superpowers/specs/2026-10-26-reading-mode-design.md
public class MainWindowViewModelReadingModeTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowViewModelReadingModeTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_reading_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"reading_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    // ── Helpers ───────────────────────────────────────────────────────────

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static Task InvokeLoadProjectAsync(MainWindowViewModel vm, string path)
    {
        var mi = typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (Task)mi.Invoke(vm, [path, false])!;
    }

    private async Task<MainWindowViewModel> OpenVm()
    {
        var path = Path.Combine(_tempDir, "p.dialogproject");
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("p"));
        var vm = MakeVm();
        await InvokeLoadProjectAsync(vm, path);
        return vm;
    }

    private static NodeViewModel MakeNode(int id) =>
        new(new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [],
            [], [], "Conversation", "None"), null);

    // ── Initial mode ──────────────────────────────────────────────────────

    [Fact]
    public void NoSavedMode_StartsInEditing()
    {
        Assert.Equal(EditorMode.Editing, MakeVm().Mode);
    }

    [Fact]
    public void SavedReading_StartsInReading()
    {
        AppSettings.Mode = EditorMode.Reading;
        Assert.True(MakeVm().IsReadingMode);
    }

    // ── Switching ─────────────────────────────────────────────────────────

    [Fact]
    public async Task ReadingMode_WithAProjectOpen_MakesTheCanvasReadOnly()
    {
        var vm = await OpenVm();
        Assert.True(vm.Canvas.IsEditable);

        await vm.SwitchModeAsync(EditorMode.Reading);
        Assert.False(vm.Canvas.IsEditable);

        await vm.SwitchModeAsync(EditorMode.Editing);
        Assert.True(vm.Canvas.IsEditable);
    }

    [Fact]
    public async Task Switch_SavesTheModeForNextLaunch()
    {
        var vm = MakeVm();
        await vm.SwitchModeAsync(EditorMode.Reading);
        Assert.Equal(EditorMode.Reading, AppSettings.Mode);
    }

    [Fact]
    public async Task Switch_ToReading_WithUnsavedChanges_AsksAndStaysOnCancel()
    {
        var vm = await OpenVm();
        vm.IsModified = true;
        vm.UnsavedChangesRequested += vm.CancelPendingNavigation;

        Assert.False(await vm.SwitchModeAsync(EditorMode.Reading));
        Assert.Equal(EditorMode.Editing, vm.Mode);
    }

    [Fact]
    public async Task Switch_ToReading_AfterDiscard_KeepsTheProjectOpen()
    {
        var vm = await OpenVm();
        vm.IsModified = true;
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));

        Assert.True(vm.IsReadingMode);
        Assert.True(vm.IsProjectOpen);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task Switch_ToReading_WhenClean_DoesNotAsk()
    {
        var vm = await OpenVm();
        var asked = false;
        vm.UnsavedChangesRequested += () => asked = true;

        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));
        Assert.False(asked);
    }

    [Fact]
    public async Task Switch_KeepsTheOpenConversationAndSelectedNode()
    {
        var vm = await OpenVm();
        vm.Canvas.AddNode(MakeNode(7), new LayoutPoint(0, 0));
        vm.CurrentConversationName = "some_conv";
        vm.IsModified = false;
        var selected = vm.Canvas.Nodes.First(n => n.NodeId == 7);
        vm.Canvas.SelectedNode = selected;

        await vm.SwitchModeAsync(EditorMode.Reading);
        await vm.SwitchModeAsync(EditorMode.Editing);

        Assert.Equal("some_conv", vm.CurrentConversationName);
        Assert.Equal(7, vm.Canvas.SelectedNode?.NodeId);
    }

    [Fact]
    public async Task Detail_FollowsTheMode()
    {
        var vm = MakeVm();
        Assert.False(vm.Detail.IsReadingMode);

        await vm.SwitchModeAsync(EditorMode.Reading);
        Assert.True(vm.Detail.IsReadingMode);

        await vm.SwitchModeAsync(EditorMode.Editing);
        Assert.False(vm.Detail.IsReadingMode);
    }

    [Fact]
    public async Task ToggleReadingModeCommand_SwitchesToTheOtherMode()
    {
        var vm = MakeVm();

        await vm.ToggleReadingModeCommand.ExecuteAsync(null);
        Assert.Equal(EditorMode.Reading, vm.Mode);

        await vm.ToggleReadingModeCommand.ExecuteAsync(null);
        Assert.Equal(EditorMode.Editing, vm.Mode);
    }
}
