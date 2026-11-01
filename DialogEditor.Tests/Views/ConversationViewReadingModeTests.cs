using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Controls;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.ViewModels;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// Issue 234 (Task 10): the conversation canvas in reading mode with a project open. Spec section 3
/// "Canvas": the node menu keeps only "Show all lines by this speaker", the empty-space menu is not
/// shown, double-click adds nothing, nodes cannot be dragged, and the start-node and note buttons go.
public class ConversationViewReadingModeTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public ConversationViewReadingModeTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"cvrm_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"cvrm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    private static object? R(string key) => Application.Current!.FindResource(key);

    // A project is open (so IsEditable would be true in Editing mode), then the mode is switched.
    private async Task<(Window window, ConversationView view, MainWindowViewModel vm, NodeViewModel root)>
        ShowAsync(EditorMode mode)
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        var path = Path.Combine(_tempDir, "p.dialogproject");
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("p"));
        await (Task)typeof(MainWindowViewModel)
            .GetMethod("LoadProjectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
            .Invoke(vm, [path, false])!;
        Assert.True(await vm.SwitchModeAsync(mode));

        var root = CanvasNavigationServiceTests.MakeNode(1, 100, 100); // no node 0: the canvas has no start node yet
        root.OnSelected = n => vm.Canvas.SelectedNode = n;
        vm.Canvas.Nodes.Add(root);
        vm.Canvas.ShowBatchVoImport = () => Task.CompletedTask;
        // The node menu's Import voice-over item (and the separator above it) show for a node with VO status.
        vm.Canvas.Detail = vm.Detail;
        typeof(NodeDetailViewModel).GetField("_voCheck", BindingFlags.NonPublic | BindingFlags.Instance)!
            .SetValue(vm.Detail, new VoCheckResult(VoPresence.Missing, false, null, null));
        Assert.True(vm.Detail.HasVoStatus);

        var view = new ConversationView { DataContext = vm.Canvas };
        var window = new Window { Content = view, Width = 900, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view, vm, root);
    }

    private static Nodify.Node FirstNode(ConversationView view) =>
        view.GetVisualDescendants().OfType<Nodify.Node>().First();

    private static List<MenuItem> OpenAndVisibleItems(ContextMenu menu, Control target)
    {
        menu.Open(target);
        Dispatcher.UIThread.RunJobs();
        return menu.Items.OfType<MenuItem>().Where(i => i.IsVisible).ToList();
    }

    [AvaloniaFact]
    public async Task NodeContextMenu_OffersOnlyShowAllLinesBySpeaker()
    {
        var (window, view, _, _) = await ShowAsync(EditorMode.Reading);
        try
        {
            var node = FirstNode(view);
            var visible = OpenAndVisibleItems(node.ContextMenu!, node);

            Assert.Equal([R("Menu_Canvas_BrowseSpeakerLinesForNode")], visible.Select(i => i.Header).ToArray());
            // A separator left over from a hidden item would be a stray line in the menu.
            Assert.DoesNotContain(node.ContextMenu!.Items.OfType<Separator>(), s => s.IsVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NodeContextMenu_ShowAllLines_StaysEnabled()
    {
        var (window, view, vm, root) = await ShowAsync(EditorMode.Reading);
        try
        {
            var node = FirstNode(view);
            OpenAndVisibleItems(node.ContextMenu!, node);
            var item = node.ContextMenu!.Items.OfType<MenuItem>()
                .First(i => Equals(i.Header, R("Menu_Canvas_BrowseSpeakerLinesForNode")));
            Assert.NotNull(item.Command);
            Assert.True(vm.Canvas.BrowseSpeakerLinesForNodeCommand.CanExecute(root));
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NodeContextMenu_InEditingMode_StillOffersTheEditingItems()
    {
        var (window, view, _, _) = await ShowAsync(EditorMode.Editing);
        try
        {
            var node = FirstNode(view);
            var headers = OpenAndVisibleItems(node.ContextMenu!, node).Select(i => i.Header).ToList();

            Assert.Contains(R("Menu_DeleteNode"), headers);
            Assert.Contains(R("Menu_AddConnectedNode"), headers);
            Assert.Contains(R("Menu_ConnectToNode"), headers);
        }
        finally { window.Close(); }
    }

    // A real right-click on empty canvas: the menu opens when the button is released.
    private static void RightClick(Window window, Point spot)
    {
        window.MouseDown(spot, MouseButton.Right);
        window.MouseUp(spot, MouseButton.Right);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task EmptySpaceContextMenu_IsNotShown()
    {
        var (window, view, _, _) = await ShowAsync(EditorMode.Reading);
        try
        {
            var editor = view.FindControl<CanvasEditor>("Editor")!;
            RightClick(window, editor.TranslatePoint(new Point(700, 400), window)!.Value);

            Assert.False(editor.ContextMenu!.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task EmptySpaceContextMenu_InEditingMode_Opens()
    {
        var (window, view, _, _) = await ShowAsync(EditorMode.Editing);
        try
        {
            var editor = view.FindControl<CanvasEditor>("Editor")!;
            RightClick(window, editor.TranslatePoint(new Point(700, 400), window)!.Value);

            Assert.True(editor.ContextMenu!.IsOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DoubleClickOnEmptySpace_AddsNoNode()
    {
        var (window, view, vm, _) = await ShowAsync(EditorMode.Reading);
        try
        {
            var editor = view.FindControl<CanvasEditor>("Editor")!;
            var before = vm.Canvas.Nodes.Count;
            var spot = editor.TranslatePoint(new Point(700, 400), window)!.Value;

            DoubleClick(window, spot);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before, vm.Canvas.Nodes.Count);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task DoubleClickOnEmptySpace_InEditingMode_AddsANode()
    {
        var (window, view, vm, _) = await ShowAsync(EditorMode.Editing);
        try
        {
            var editor = view.FindControl<CanvasEditor>("Editor")!;
            var before = vm.Canvas.Nodes.Count;
            var spot = editor.TranslatePoint(new Point(700, 400), window)!.Value;

            DoubleClick(window, spot);
            Dispatcher.UIThread.RunJobs();

            Assert.Equal(before + 1, vm.Canvas.Nodes.Count);
        }
        finally { window.Close(); }
    }

    private static void DoubleClick(Window window, Point spot)
    {
        for (var i = 0; i < 2; i++)
        {
            window.MouseDown(spot, MouseButton.Left);
            window.MouseUp(spot, MouseButton.Left);
        }
        Dispatcher.UIThread.RunJobs();
    }

    private static void Drag(Window window, Control container)
    {
        var start = container.TranslatePoint(new Point(container.Bounds.Width / 2, 10), window)
                    ?? throw new InvalidOperationException($"container not in the window tree ({container.Bounds})");
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(start + new Point(80, 60));
        window.MouseMove(start + new Point(160, 120));
        window.MouseUp(start + new Point(160, 120), MouseButton.Left);
        Dispatcher.UIThread.RunJobs();
    }

    [AvaloniaFact]
    public async Task DraggingANode_DoesNotMoveIt_OrMarkTheProjectModified()
    {
        var (window, view, vm, root) = await ShowAsync(EditorMode.Reading);
        try
        {
            vm.Canvas.IsModified = false;
            var container = view.GetVisualDescendants().OfType<Nodify.ItemContainer>().First();
            var before = root.Location;

            Drag(window, container);

            Assert.Equal(before, root.Location);
            Assert.False(vm.Canvas.IsModified);
        }
        finally { window.Close(); }
    }

    // A synthetic drag cannot finish in the headless host (Nodify's drag state throws on release),
    // so the editing side checks the switch that gates dragging.
    [AvaloniaTheory]
    [InlineData(EditorMode.Reading, false)]
    [InlineData(EditorMode.Editing, true)]
    public async Task NodesAreDraggable_OnlyInEditingMode(EditorMode mode, bool draggable)
    {
        var (window, view, _, _) = await ShowAsync(mode);
        try
        {
            var container = view.GetVisualDescendants().OfType<Nodify.ItemContainer>().First();
            Assert.Equal(draggable, container.IsDraggable);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChooseStartNode_AndAddNote_AreHidden()
    {
        var (window, view, vm, _) = await ShowAsync(EditorMode.Reading);
        try
        {
            // The no-start-node warning is up and the node could be made the start node.
            vm.Canvas.CanChooseStartNode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.Canvas.ShowStartNodeWarning);
            var buttons = view.GetLogicalDescendants().OfType<Button>().ToList();
            foreach (var key in new[] { "Canvas_ChooseStartNode", "Label_AddAnnotation" })
                Assert.False(buttons.Single(b => Equals(b.Content, R(key))).IsEffectivelyVisible, key);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task ChooseStartNode_AndAddNote_AreShown_InEditingMode()
    {
        var (window, view, vm, _) = await ShowAsync(EditorMode.Editing);
        try
        {
            vm.Canvas.CanChooseStartNode = true;
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.Canvas.ShowStartNodeWarning);
            var buttons = view.GetLogicalDescendants().OfType<Button>().ToList();
            foreach (var key in new[] { "Canvas_ChooseStartNode", "Label_AddAnnotation" })
                Assert.True(buttons.Single(b => Equals(b.Content, R(key))).IsEffectivelyVisible, key);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task NodeContextMenu_DoesNotOfferMakeStartNode_InReadingMode()
    {
        var (window, view, vm, _) = await ShowAsync(EditorMode.Reading);
        try
        {
            vm.Canvas.CanChooseStartNode = true;   // a node that could become the start node, were this editing
            var node = FirstNode(view);
            var headers = OpenAndVisibleItems(node.ContextMenu!, node).Select(i => i.Header).ToList();

            Assert.DoesNotContain(R("Menu_MakeStartNode"), headers);
        }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData(EditorMode.Reading, false)]
    [InlineData(EditorMode.Editing, true)]
    public async Task ConnectionContextMenu_OffersDelete_OnlyInEditingMode(EditorMode mode, bool offered)
    {
        var (window, view, vm, root) = await ShowAsync(mode);
        try
        {
            var child = CanvasNavigationServiceTests.MakeNode(2, 500, 100);
            vm.Canvas.Nodes.Add(child);
            vm.Canvas.Connections.Add(CanvasNavigationServiceTests.Connect(root, child));
            Dispatcher.UIThread.RunJobs();

            var connection = view.GetVisualDescendants().OfType<Nodify.Connection>().First();
            var headers = OpenAndVisibleItems(connection.ContextMenu!, connection).Select(i => i.Header).ToList();

            Assert.Equal(offered, headers.Contains(R("Menu_DeleteConnection")));
            Assert.Equal(offered, headers.Contains(R("Menu_MoveLinkEarlier")));
            Assert.Equal(offered, headers.Contains(R("Menu_MoveLinkLater")));
        }
        finally { window.Close(); }
    }
}
