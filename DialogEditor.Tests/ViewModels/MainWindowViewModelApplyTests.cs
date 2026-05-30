using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelApplyTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public MainWindowViewModelApplyTests() => Loc.Configure(new StubStringProvider());

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm() =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());

    private static void Inject(MainWindowViewModel vm, string field, object? value)
    {
        var fi = typeof(MainWindowViewModel).GetField(field,
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        fi.SetValue(vm, value);
    }

    private static DialogProject? GetProject(MainWindowViewModel vm)
    {
        var fi = typeof(MainWindowViewModel).GetField("_project",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (DialogProject?)fi.GetValue(vm);
    }

    private MainWindowViewModel MakeLoadedVm(out string path)
    {
        var vm = MakeVm();
        path = Path.Combine(Path.GetTempPath(), $"apply_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        Inject(vm, "_project", DialogProject.Empty("ORIG"));
        Inject(vm, "_projectPath", path);
        return vm;
    }

    // ── Helpers for open-conversation state ───────────────────────────────

    /// A minimal node edit snapshot for use in patches.
    private static NodeEditSnapshot NodeFor(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None",
            "", "", "", false, false, [], [], []);

    /// Simulates opening a conversation named <paramref name="convName"/> in the VM:
    ///   - Injects a StubProvider that knows the conversation.
    ///   - Calls Canvas.Load() with a one-node conversation so BaseSnapshot becomes non-null.
    ///   - Reflection-sets _currentFile and _provider so SaveProject's re-fold branch fires.
    private static void OpenAConversation(MainWindowViewModel vm, string convName)
    {
        var file     = new ConversationFile(convName, "", "", "");
        var baseSnap = new ConversationEditSnapshot([NodeFor(99)]);
        var provider = new StubProvider(file, baseSnap);

        // Build a minimal Conversation so Canvas.Load() sets BaseSnapshot.
        var node = new ConversationNode(
            NodeId: 99, IsPlayerChoice: false,
            SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None");
        var conversation = new Conversation(convName, [node],
            new StringTable([new StringEntry(99, "hello", "")]));

        vm.Canvas.Load(conversation);   // sets Canvas.BaseSnapshot

        Inject(vm, "_currentFile", file);
        Inject(vm, "_provider",    provider);
    }

    // ── Existing tests ────────────────────────────────────────────────────

    [Fact]
    public async Task ApplyFromDiff_Dirty_AbortsWhenSaveDeclined()
    {
        var vm = MakeLoadedVm(out _);
        vm.IsModified = true;
        vm.ConfirmSaveBeforeApply = () => Task.FromResult(false);

        await vm.ApplyFromDiff(DialogProject.Empty("APPLIED"));

        Assert.Equal("ORIG", GetProject(vm)!.Name);   // not applied
    }

    [Fact]
    public async Task ApplyFromDiff_WritesAndClearsDirty()
    {
        var vm = MakeLoadedVm(out var path);

        await vm.ApplyFromDiff(DialogProject.Empty("APPLIED"));

        Assert.False(vm.IsModified);
        Assert.True(File.Exists(path));
        Assert.Equal("APPLIED", GetProject(vm)!.Name);
    }

    [Fact]
    public async Task UndoApply_RestoresPreApplyProject()
    {
        var vm = MakeLoadedVm(out _);
        var before = GetProject(vm)!;

        await vm.ApplyFromDiff(DialogProject.Empty("APPLIED"));
        vm.UndoApplyCommand.Execute(null);

        Assert.Equal(before.Name, GetProject(vm)!.Name);
    }

    // ── Regression: open-conversation re-fold must not overwrite applied patch ──

    [Fact]
    public async Task ApplyFromDiff_WithAConversationOpen_DoesNotLoseTheAppliedChange()
    {
        var vm = MakeLoadedVm(out var path);

        // Simulate an open conversation so SaveProject's re-fold branch is active.
        // Canvas.BaseSnapshot is set to a snapshot with node 99; _currentFile and
        // _provider are injected so _provider!.Language in DiffEngine.Diff won't NRE.
        OpenAConversation(vm, "greeting");

        // The applied project carries an "greeting" patch that adds node 42.
        var applied = DialogProject.Empty("ORIG").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [NodeFor(42)], [], []));

        await vm.ApplyFromDiff(applied);

        // The on-disk file must reflect the applied patch — node 42 must be present.
        // If the bug is present, the re-fold writes back the stale canvas (node 99 only)
        // and node 42 is lost.
        var onDisk = DialogProjectSerializer.Deserialize(File.ReadAllText(path));
        Assert.True(onDisk.Patches.TryGetValue("greeting", out var p));
        Assert.Contains(p!.AddedNodes, n => n.NodeId == 42);
    }

    [Fact]
    public async Task UndoApply_WithAConversationOpen_DoesNotLoseTheRestoredProject()
    {
        var vm = MakeLoadedVm(out var path);

        // Apply something first, then open a conversation, then undo.
        var applied = DialogProject.Empty("ORIG").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [NodeFor(42)], [], []));
        await vm.ApplyFromDiff(applied);

        // Now simulate an open conversation (stale canvas with node 99).
        OpenAConversation(vm, "greeting");

        // Undo: should restore the pre-apply project (ORIG with no patches).
        vm.UndoApplyCommand.Execute(null);

        // The pre-apply project had no patches, so the on-disk file must have none.
        var onDisk = DialogProjectSerializer.Deserialize(File.ReadAllText(path));
        Assert.False(onDisk.Patches.ContainsKey("greeting"),
            "After UndoApply the on-disk project should match the pre-apply state (no greeting patch).");
    }
}
