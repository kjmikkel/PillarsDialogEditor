using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
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

    private static ConversationNode PlainNode(int id, params int[] to) =>
        new(id, false, SpeakerCategory.Npc, "spk", "lst", [.. to.Select(t => new NodeLink(id, t, []))],
            [], [], "Conversation", "None");

    /// A saved project with one new conversation "fresh" (node 1 -> node 2) open on the canvas.
    private async Task<MainWindowViewModel> OpenFreshConversation()
    {
        var provider = new FakeGameDataProvider("poe2", "en");
        var created = ConversationSnapshotBuilder.Build(new Conversation("fresh",
            [PlainNode(1, 2), PlainNode(2)],
            new StringTable([new StringEntry(1, "first", ""), new StringEntry(2, "second", "")])));
        var patch = DiffEngine.Diff("fresh", new ConversationEditSnapshot([]), created, "en");
        var path = Path.Combine(_tempDir, "fresh.dialogproject");
        DialogProjectSerializer.SaveToFile(path,
            DialogProject.Empty("Fresh").WithNewConversation("fresh").WithPatch(patch));

        var vm = MakeVm();
        typeof(MainWindowViewModel)
            .GetField("_provider", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .SetValue(vm, provider);
        await InvokeLoadProjectAsync(vm, path);
        typeof(MainWindowViewModel)
            .GetMethod("OnConversationSelected", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [provider.BuildNewConversationFile("fresh")]);
        return vm;
    }

    [Fact]
    public async Task Switch_ToReading_AfterDiscard_DropsTheEditButKeepsConversationAndSelection()
    {
        var vm = await OpenFreshConversation();
        vm.Canvas.SelectedNode = vm.Canvas.Nodes.Single(n => n.NodeId == 2);
        vm.Canvas.AddNode(MakeNode(3), new LayoutPoint(0, 0));   // a real, unsaved edit
        Assert.True(vm.IsModified);
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));

        Assert.True(vm.IsReadingMode);
        Assert.True(vm.IsProjectOpen);
        Assert.False(vm.IsModified);
        Assert.Equal("fresh", vm.CurrentConversationName);
        Assert.DoesNotContain(vm.Canvas.Nodes, n => n.NodeId == 3);
        Assert.Equal(2, vm.Canvas.SelectedNode?.NodeId);
    }

    [Fact]
    public async Task Switch_ToReading_AfterDiscard_ProjectOnlyEdit_ReloadsTheProject()
    {
        // Metadata-only dirt, no conversation open: nothing on screen to lose, so the project
        // is re-read from its saved file.
        var vm = await OpenVm();
        vm.Detail.AddWordToProjectDictionary!("zzword");
        Assert.True(vm.IsModified);
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        Assert.True(await vm.SwitchModeAsync(EditorMode.Reading));

        Assert.True(vm.IsReadingMode);
        Assert.True(vm.IsProjectOpen);
        Assert.False(vm.IsModified);
        Assert.DoesNotContain("zzword", vm.ProjectWords ?? []);
    }

    [Fact]
    public async Task Switch_ToReading_WhenDiscardCannotBeReverted_StaysInEditingAndSaysWhy()
    {
        var vm = await OpenVm();
        vm.IsModified = true;
        File.Delete(vm.ProjectPath!);   // nothing left to re-read
        vm.UnsavedChangesRequested += vm.DiscardAndProceed;

        Assert.False(await vm.SwitchModeAsync(EditorMode.Reading));

        Assert.Equal(EditorMode.Editing, vm.Mode);
        Assert.Equal("Status_ModeSwitchRefused", vm.StatusText);
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
