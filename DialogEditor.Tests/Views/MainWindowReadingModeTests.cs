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
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Views;

/// Issue 234 (Task 8): the main window in reading mode. Spec section 3 "Menus":
/// editing items are not in the menu at all, the kept ones are, and shortcuts that edit do nothing.
public class MainWindowReadingModeTests : IDisposable
{
    private readonly string _settingsPath;
    private readonly string _tempDir;

    public MainWindowReadingModeTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwrm_settings_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
        _tempDir = Path.Combine(Path.GetTempPath(), $"mwrm_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_tempDir, recursive: true); } catch (Exception) { /* best-effort */ }
    }

    // The menu headers come from the app's XAML resources, not the stubbed Loc.
    private static string R(string key) => (string)Application.Current!.FindResource(key)!;

    private static (MainWindow window, MainWindowViewModel vm) ShowInReadingMode()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        vm.Mode = EditorMode.Reading;
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, vm);
    }

    private static MenuItem FindItem(MainWindow window, string headerKey)
    {
        var header = R(headerKey);
        return window.GetLogicalDescendants().OfType<MenuItem>().First(m => Equals(m.Header, header));
    }

    // A keyboard activation through the real menu: open the View submenu, focus the item and
    // press Enter. Unlike RaiseEvent(ClickEvent) this toggles a checkable item before the Click
    // event fires, which is what a real click does.
    private static void RealClick(MainWindow window, MenuItem item)
    {
        ((MenuItem)item.Parent!).IsSubMenuOpen = true;
        Dispatcher.UIThread.RunJobs();
        Assert.True(item.Focus());
        window.KeyPress(Key.Enter, RawInputModifiers.None, PhysicalKey.Enter, null);
    }

    private static ConversationNode PlainNode() =>
        new(1, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None");

    [AvaloniaTheory]
    [InlineData("Menu_NewProject")]
    [InlineData("Menu_SaveProject")]
    [InlineData("Menu_SaveProjectAs")]
    [InlineData("Menu_MergeProjects")]
    [InlineData("PatchManager_Title")]
    [InlineData("Menu_ImportConversation")]
    [InlineData("Menu_ExportConversation")]
    [InlineData("Menu_ExportForTranslation")]
    [InlineData("Menu_ImportTranslation")]
    [InlineData("Menu_ExportModBundle")]
    [InlineData("Button_Undo")]
    [InlineData("Button_Redo")]
    [InlineData("Menu_FindReplace")]
    [InlineData("Menu_BatchReplace")]
    [InlineData("Menu_FindInProject")]
    [InlineData("Menu_FlowAnalytics")]
    [InlineData("Menu_CompareVersions")]
    [InlineData("Menu_History")]
    [InlineData("Menu_Attribution")]
    [InlineData("Menu_Branches")]
    [InlineData("Button_TestPatch")]
    [InlineData("TestMode_Restore")]
    [InlineData("Button_RestoreBackup")]
    [InlineData("Menu_ValidateVO")]
    [InlineData("Menu_BatchImportVo")]
    [InlineData("Menu_BatchImportVoAll")]
    [InlineData("Menu_ValidateTextTags")]
    [InlineData("Menu_CreateSample")]
    [InlineData("Menu_OpenWalkthrough")]
    [InlineData("Menu_TagReference")]
    [InlineData("Menu_ExportUiStrings")]
    [InlineData("Menu_ImportUiStrings")]
    public void HiddenItem_IsNotVisible_InReadingMode(string headerKey)
    {
        var (window, _) = ShowInReadingMode();
        try { Assert.False(FindItem(window, headerKey).IsVisible); }
        finally { window.Close(); }
    }

    [AvaloniaTheory]
    [InlineData("Button_OpenFolder")]
    [InlineData("Menu_OpenProject")]
    [InlineData("Menu_RecentProjects")]
    [InlineData("Menu_CloseProject")]
    [InlineData("Menu_Settings")]
    [InlineData("Menu_BrowseSpeakerLines")]
    [InlineData("Menu_RepDispositionBalance")]
    [InlineData("Menu_View_Conversations")]
    [InlineData("Menu_View_Details")]
    [InlineData("Menu_View_ConditionSearch")]
    [InlineData("Menu_View_PrivacyMode")]
    [InlineData("Menu_View_ShowMinimap")]
    [InlineData("Menu_View_ResetLayout")]
    [InlineData("Menu_View_ReadingMode")]
    [InlineData("Menu_StartGuidedTour")]
    [InlineData("Menu_Changelog")]
    [InlineData("Menu_About")]
    public void KeptItem_IsVisible_InReadingMode(string headerKey)
    {
        var (window, _) = ShowInReadingMode();
        try { Assert.True(FindItem(window, headerKey).IsVisible); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void TestMenu_IsNotVisible_InReadingMode()
    {
        var (window, _) = ShowInReadingMode();
        try { Assert.False(FindItem(window, "Menu_Test").IsVisible); }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadingModeMenuItem_IsChecked_InReadingMode_AndSwitchesWhenClicked()
    {
        var (window, vm) = ShowInReadingMode();
        try
        {
            var item = FindItem(window, "Menu_View_ReadingMode");
            Assert.True(item.IsChecked);

            item.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(vm.IsEditingMode);
            Assert.False(item.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ReadingModeMenuItem_StaysInStep_WhenTheSwitchIsCancelled()
    {
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        try
        {
            var item = FindItem(window, "Menu_View_ReadingMode");
            Assert.False(item.IsChecked);
            vm.Canvas.Load(new Conversation("c", [PlainNode()], new StringTable([new StringEntry(1, "hi", "")])));
            vm.CurrentConversationName = "c";
            vm.IsModified = true;
            // Replace the window's modal dialog handler with a Cancel answer.
            typeof(MainWindowViewModel).GetField(nameof(MainWindowViewModel.UnsavedChangesRequested),
                    BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, null);
            vm.UnsavedChangesRequested += vm.CancelPendingNavigation;

            RealClick(window, item);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsEditingMode);
            Assert.Equal(vm.IsReadingMode, item.IsChecked);

            // The binding must survive: a later successful switch checks the item.
            vm.IsModified = false;
            RealClick(window, item);
            Dispatcher.UIThread.RunJobs();
            Assert.True(vm.IsReadingMode, $"mode={vm.Mode} status={vm.StatusText} mod={vm.IsModified} checked={item.IsChecked}");
            Assert.True(item.IsChecked);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void ConflictsBanner_IsHidden_InReadingMode()
    {
        var (window, vm) = ShowInReadingMode();
        try
        {
            var banner = window.GetLogicalDescendants().OfType<Button>()
                .First(b => Equals(b.Content, R("MainWindow_ResolveConflictsButton")));
            // A pending resolution would show it in editing mode; reading mode must still hide it.
            vm.HasPendingConflictResolution = true;
            Dispatcher.UIThread.RunJobs();
            Assert.False(banner.IsEffectivelyVisible);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Delete_OnASelectedNode_DoesNothing_InReadingMode()
    {
        var (window, vm) = ShowInReadingMode();
        try
        {
            var view = new ConversationView { DataContext = vm.Canvas };
            window.GetVisualDescendants().OfType<Panel>().First().Children.Add(view);
            vm.Canvas.Load(new Conversation("c", [PlainNode()], new StringTable([new StringEntry(1, "hi", "")])));
            vm.Canvas.IsEditable = true;   // would let Delete through if only the editable flag guarded it
            vm.Canvas.SelectedNode = vm.Canvas.Nodes[0];
            Dispatcher.UIThread.RunJobs();
            Assert.True(view.FindControl<Control>("Editor")!.Focus());

            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

            Assert.Single(vm.Canvas.Nodes);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void Delete_InTheConversationFilter_StillEditsTheText()
    {
        var (window, _) = ShowInReadingMode();
        try
        {
            var box = new TextBox { Text = "abc" };
            window.GetVisualDescendants().OfType<Panel>().First().Children.Add(box);
            Assert.True(box.Focus());
            box.CaretIndex = 0;

            window.KeyPress(Key.Delete, RawInputModifiers.None, PhysicalKey.Delete, null);

            Assert.Equal("bc", box.Text);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void CtrlN_DoesNotStartANewProject_InReadingMode()
    {
        var (window, vm) = ShowInReadingMode();
        try
        {
            var picker = new StubFilePicker();
            typeof(MainWindowViewModel).GetField("_filePicker", BindingFlags.NonPublic | BindingFlags.Instance)!
                .SetValue(vm, picker);

            window.KeyPress(Key.N, RawInputModifiers.Control, PhysicalKey.N, "n");
            Dispatcher.UIThread.RunJobs();

            Assert.Empty(picker.Titles);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public async Task CtrlW_ClosesTheProject_InReadingMode()
    {
        var (window, vm) = ShowInReadingMode();
        try
        {
            var path = Path.Combine(_tempDir, "p.dialogproject");
            DialogProjectSerializer.SaveToFile(path, DialogProject.Empty("p"));
            await (Task)typeof(MainWindowViewModel)
                .GetMethod("LoadProjectAsync", BindingFlags.NonPublic | BindingFlags.Instance)!
                .Invoke(vm, [path, false])!;
            Assert.True(vm.IsProjectOpen);

            window.KeyPress(Key.W, RawInputModifiers.Control, PhysicalKey.W, "w");
            Dispatcher.UIThread.RunJobs();

            Assert.False(vm.IsProjectOpen);
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void HalfTypedDetailEdit_IsCommitted_BeforeSwitchingToReading()
    {
        // Node Details text boxes commit on LostFocus, so switching to reading must blur the
        // focused one first or the unsaved-changes guard would not see the half-typed edit.
        var window = new MainWindow();
        var vm = (MainWindowViewModel)window.DataContext!;
        window.Show();
        try
        {
            var detail = new NodeDetailView { DataContext = vm.Detail };
            window.GetVisualDescendants().OfType<Panel>().First().Children.Add(detail);
            vm.Canvas.Load(new Conversation("c", [PlainNode()], new StringTable([new StringEntry(1, "hi", "")])));
            vm.CurrentConversationName = "c";
            vm.Canvas.SelectedNode = vm.Canvas.Nodes[0];
            Dispatcher.UIThread.RunJobs();
            Assert.False(vm.IsModified);

            var box = detail.FindControl<TextBox>("DefaultTextBox")!;
            Assert.True(box.Focus());
            window.KeyTextInput("!");
            Assert.Equal("hi", vm.Canvas.Nodes[0].DefaultText);   // still only in the text box

            // Drop the window's own handler (it opens the real modal dialog) and answer Cancel instead.
            typeof(MainWindowViewModel).GetField(nameof(MainWindowViewModel.UnsavedChangesRequested),
                    BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(vm, null);
            bool? modifiedWhenAsked = null;
            vm.UnsavedChangesRequested += () => { modifiedWhenAsked = vm.IsModified; vm.CancelPendingNavigation(); };
            FindItem(window, "Menu_View_ReadingMode").RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
            Dispatcher.UIThread.RunJobs();

            Assert.True(modifiedWhenAsked);
        }
        finally { window.Close(); }
    }
}
