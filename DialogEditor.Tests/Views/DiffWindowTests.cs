using System.Collections.Generic;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

public class DiffWindowTests : IDisposable
{
    private readonly List<string> _tempFiles = [];

    public DiffWindowTests() => Loc.Configure(new StubStringProvider());

    public void Dispose()
    {
        foreach (var f in _tempFiles)
            try { File.Delete(f); } catch { /* best-effort */ }
    }

    private string WriteTempProject(DialogProject project)
    {
        var path = Path.Combine(Path.GetTempPath(), $"diffwin_{Guid.NewGuid():N}.dialogproject");
        _tempFiles.Add(path);
        File.WriteAllText(path, DialogProjectSerializer.Serialize(project));
        return path;
    }

    private static NodeEditSnapshot Node(int id) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false, [], [], []);

    /// <summary>
    /// Builds a FakeGit that returns refContent for "show" and empty OK for branch/log.
    /// </summary>
    private static FakeGit MakeFakeGit(string projectDir, string? refContent,
        string branchOutput = "main\n")
        => new(args =>
        {
            if (args is ["rev-parse", "--show-toplevel"])
                return new GitResult(0, projectDir + "\n", "");

            if (args.Length == 2 && args[0] == "show")
            {
                if (refContent is null)
                    return new GitResult(128, "", "fatal: bad ref");
                return new GitResult(0, refContent, "");
            }

            if (args.Length >= 1 && args[0] == "branch")
                return new GitResult(0, branchOutput, "");

            if (args.Length >= 1 && args[0] == "log")
                return new GitResult(0, "", "");

            return new GitResult(0, "", "");
        });

    [AvaloniaFact]
    public void ChangedList_ShowsOneItem_WhenOneConversationDiffers()
    {
        // Disk project has node 1 + node 2; git ref has only node 1
        var diskProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1), Node(2)], [], []));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;

        var refProject = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [Node(1)], [], []));
        var refJson = DialogProjectSerializer.Serialize(refProject);

        var git = MakeFakeGit(dir, refContent: refJson);
        var vm  = new DiffViewModel(git, new StubDispatcher(), path);

        var window = new DiffWindow(vm);
        window.Show();

        Assert.Equal(1, window.FindControl<ListBox>("ChangedList")!.ItemCount);
    }

    // ── Detail panel helpers ──────────────────────────────────────────────────

    private static ConversationPatch PatchWithText(
        string convName,
        IReadOnlyList<(int Id, string Text)> nodes)
    {
        var snapNodes = nodes.Select(n => Node(n.Id)).ToList();
        var txList    = nodes.Select(n => new NodeTranslation(n.Id, n.Text, "")).ToList();
        return new ConversationPatch(
            convName, ConversationPatch.CurrentSchemaVersion,
            snapNodes, [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>>
                { ["en"] = txList }
        };
    }

    private static ConversationPatch PatchMultiLang(
        string convName, IReadOnlyList<int> nodeIds,
        IReadOnlyDictionary<string, IReadOnlyList<(int Id, string Text)>> byLang)
    {
        var snapNodes = nodeIds.Select(Node).ToList();
        var translations = byLang.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<NodeTranslation>)kv.Value
                .Select(t => new NodeTranslation(t.Id, t.Text, "")).ToList());
        return new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, snapNodes, [], [])
        {
            Translations = translations
        };
    }

    // ── Detail panel tests ────────────────────────────────────────────────────

    [AvaloniaFact]
    public void DetailPanel_Hidden_WhenNoNodeSelected()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));

        // Disk (left): node 1 with text "old text"
        var diskProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "old text")]));

        // Git ref (right): node 1 with different text → Changed
        var refProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "new text")]));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");

        // Select the conversation so the canvas is built, but do NOT select any node
        vm.Selected = vm.Changes.Single(c => c.Name == convName);

        var window = new DiffWindow(vm);
        window.Show();

        Assert.False(window.FindControl<Border>("DetailPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public void DetailPanel_Visible_AfterSelectingChangedNode()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));

        // Disk (left): node 1 with text "old text"
        var diskProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "old text")]));

        // Git ref (right): node 1 with different text → Changed
        var refProject = DialogProject.Empty("p").WithPatch(
            PatchWithText(convName, [(1, "new text")]));

        var path = WriteTempProject(diskProject);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refProject));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");

        // Select the conversation → canvas is built
        vm.Selected = vm.Changes.Single(c => c.Name == convName);

        var window = new DiffWindow(vm);
        window.Show();

        // Select the changed node on the canvas → SelectedNodeDetail should be set
        var changedNode = vm.DiffCanvas!.Nodes.First(n => n.NodeId == 1);
        vm.DiffCanvas.SelectedNode = changedNode;

        Assert.True(window.FindControl<Border>("DetailPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public void DetailPanel_ShowsOneSectionPerChangedLanguage()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));

        var disk = DialogProject.Empty("p").WithPatch(PatchMultiLang(convName, [1],
            new Dictionary<string, IReadOnlyList<(int, string)>>
            { ["en"] = [(1, "old en")], ["fr"] = [(1, "vieux")] }));
        var refp = DialogProject.Empty("p").WithPatch(PatchMultiLang(convName, [1],
            new Dictionary<string, IReadOnlyList<(int, string)>>
            { ["en"] = [(1, "new en")], ["fr"] = [(1, "neuf")] }));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));

        var vm     = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        var window = new DiffWindow(vm);
        window.Show();

        vm.Selected = vm.Changes.Single(c => c.Name == convName);
        vm.DiffCanvas!.SelectedNode = vm.DiffCanvas.Nodes.First(n => n.NodeId == 1);

        Assert.Equal(2, window.FindControl<ItemsControl>("SectionsList")!.ItemCount);
    }

    private static NodeEditSnapshot NodeWithLink(int id, int toId) =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", "", false, false,
            [new LinkEditSnapshot(id, toId, 1f, "", false)], [], []);

    [AvaloniaFact]
    public void DanglingPanel_Hidden_WhenApplyLeavesNoDanglingLinks()
    {
        // working copy (left) = [1]; ref (right) adds node 9, no deletions.
        var disk = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1)], [], []));
        var refp = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1), Node(9)], [], []));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path);

        var window = new DiffWindow(vm);
        window.Show();

        foreach (var g in vm.Groups) g.IsAllSelected = true;
        vm.ApplyCommand.Execute(null);

        Assert.Empty(vm.DanglingLinks);
        Assert.False(window.FindControl<Expander>("DanglingPanel")!.IsVisible);
    }

    [AvaloniaFact]
    public void DanglingPanel_VisibleWithRows_AfterApplyLeavesDanglingLinks()
    {
        // ref adds node 5 (links to 8) AND deletes node 8 → bringing in both dangles.
        var disk = DialogProject.Empty("p");
        var refp = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion,
                [NodeWithLink(5, 8)], [8], []));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path);

        var window = new DiffWindow(vm);
        window.Show();

        foreach (var g in vm.Groups) g.IsAllSelected = true;
        vm.ApplyCommand.Execute(null);

        Assert.NotEmpty(vm.DanglingLinks);
        Assert.True(window.FindControl<Expander>("DanglingPanel")!.IsVisible);
        Assert.Equal(vm.DanglingLinks.Count,
                     window.FindControl<ItemsControl>("DanglingList")!.ItemCount);
    }

    [AvaloniaFact]
    public void AutoPullCheckbox_DefaultsChecked_AndBindsToViewModel()
    {
        var disk = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1), Node(2)], [], []));
        var refp = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1)], [], []));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path);

        var window = new DiffWindow(vm);
        window.Show();

        var cb = window.FindControl<CheckBox>("AutoPullCheck")!;
        Assert.True(cb.IsChecked);

        vm.AutoPullDependencies = false;
        Assert.False(cb.IsChecked);
    }

    [AvaloniaFact]
    public void HintBar_UpdatesText_WhenFocusMovesToControlWithHelpText()
    {
        var disk   = DialogProject.Empty("p");
        var path   = WriteTempProject(disk);
        var dir    = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git    = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(disk));
        var vm     = new DiffViewModel(git, new StubDispatcher(), path);
        var window = new DiffWindow(vm);
        window.Show();

        var picker      = window.FindControl<ComboBox>("LeftPicker")!;
        var expectedHint = AutomationProperties.GetHelpText(picker);
        Assert.False(string.IsNullOrEmpty(expectedHint));

        picker.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent        = InputElement.GotFocusEvent,
            NavigationMethod   = NavigationMethod.Tab,
        });

        Assert.Equal(expectedHint, window.FindControl<FocusHintBar>("HintBar")!.Text);
    }

    private sealed class FakeGit(Func<string[], GitResult> handler) : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => handler(args);
    }
}
