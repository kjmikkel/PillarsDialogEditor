using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.GameData;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// <summary>
/// Issue 85, part A (docs/superpowers/specs/2026-10-13-conversations-dock-a-visual-design.md):
/// the Conversations dock's rows held 10 px text in 32 px rows, folder names were the faintest
/// text in the dock, and long names ran off the edge with no ellipsis.
/// </summary>
public class GameBrowserLayoutTests
{
    public GameBrowserLayoutTests() => Loc.Configure(new StubStringProvider());

    private const string LongName = "00_bs_poi_poko_kohara_desert_with_a_very_long_conversation_name";

    private static ConversationItemViewModel MakeItem(string name, bool isNew = false) =>
        new(new ConversationFile(name, "companions", $@"companions\{name}.conversation",
                                 $@"companions\{name}.stringtable"), isNew);

    private sealed record Dock(Window Window, GameBrowserView View, GameBrowserViewModel Vm,
                               ConversationFolderViewModel Folder, ConversationItemViewModel Short,
                               ConversationItemViewModel Long);

    private static void WithDock(double scale, Action<Dock> check)
    {
        var shortItem = MakeItem("companion_eder_hub");
        var longItem  = MakeItem(LongName);
        var folder = new ConversationFolderViewModel("companions", [shortItem, longItem], isExpanded: true);
        var vm = new GameBrowserViewModel(new StubDispatcher());
        vm.Folders.Add(folder);
        var view = new GameBrowserView { DataContext = vm };
        // As narrow as the dock opens, so the long name can't fit.
        var window = new Window { Content = view, Width = 260, Height = 600 };
        try
        {
            new FontScaleApplier().Apply(scale);
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            check(new Dock(window, view, vm, folder, shortItem, longItem));
        }
        finally
        {
            window.Close();
            new FontScaleApplier().Apply(1.0);
        }
    }

    private static TreeViewItem Row(Dock d, object item)
    {
        var tree = d.View.GetVisualDescendants().OfType<TreeView>().Single();
        return Assert.IsAssignableFrom<TreeViewItem>(tree.TreeContainerFromItem(item));
    }

    private static TextBlock Label(TreeViewItem row) =>
        row.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_HeaderPresenter")
           .GetVisualDescendants().OfType<TextBlock>().First();

    // The row's own header strip, excluding its expanded children.
    private static Control HeaderStrip(TreeViewItem row) =>
        row.GetVisualDescendants().OfType<Control>().First(c => c.Name == "PART_LayoutRoot");

    private static double Token(string key) =>
        (double)global::Avalonia.Application.Current!.FindResource(key)!;

    [AvaloniaTheory]
    [InlineData(1.0)]
    [InlineData(2.0)]
    public void Labels_UseTheBodyFontSize(double scale)
        => WithDock(scale, d =>
        {
            Assert.Equal(Token("FontSize.Body"), Label(Row(d, d.Folder)).FontSize);
            Assert.Equal(Token("FontSize.Body"), Label(Row(d, d.Short)).FontSize);
        });

    [AvaloniaFact]
    public void Rows_AreSizedByTheirText_AtNormalScale()
        => WithDock(1.0, d =>
        {
            // At least the WCAG 2.5.8 target size, without Fluent's 32 px floor around 12 px text.
            foreach (var item in new object[] { d.Folder, d.Short })
            {
                var h = HeaderStrip(Row(d, item)).Bounds.Height;
                Assert.InRange(h, 24, 30);
            }
        });

    [AvaloniaFact]
    public void Rows_GrowWithTheText_AtDoubleScale()
        => WithDock(2.0, d =>
        {
            foreach (var item in new object[] { d.Folder, d.Short })
            {
                var row = Row(d, item);
                var label = Label(row);
                Assert.True(label.Bounds.Height <= HeaderStrip(row).Bounds.Height,
                    $"label {label.Bounds.Height} taller than its row {HeaderStrip(row).Bounds.Height}");
                Assert.True(label.DesiredSize.Height <= label.Bounds.Height + 0.5, "label clipped vertically");
            }
        });

    [AvaloniaFact]
    public void LongNames_TrimWithAnEllipsis_AndShowTheFullNameAsATooltip()
        => WithDock(1.0, d =>
        {
            var label = Label(Row(d, d.Long));
            Assert.Equal(TextTrimming.CharacterEllipsis, label.TextTrimming);
            label.Measure(global::Avalonia.Size.Infinity);
            Assert.True(label.DesiredSize.Width > label.Bounds.Width + 1,
                $"the long name fits ({label.DesiredSize.Width} <= {label.Bounds.Width}): the row is not width-bound");
            Assert.Equal(d.Long.DisplayName, ToolTip.GetTip(label));
        });

    [AvaloniaFact]
    public void Tree_DoesNotScrollSideways()
        => WithDock(1.0, d =>
        {
            var tree = d.View.GetVisualDescendants().OfType<TreeView>().Single();
            Assert.Equal(global::Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
                         ScrollViewer.GetHorizontalScrollBarVisibility(tree));
        });

    [AvaloniaFact]
    public void FolderLabels_AreStrongText()
        => WithDock(1.0, d =>
        {
            var label = Label(Row(d, d.Folder));
            var primary = ((ISolidColorBrush)global::Avalonia.Application.Current!.FindResource("Brush.Text.Primary")!).Color;
            Assert.Equal(primary, ((ISolidColorBrush)label.Foreground!).Color);
            Assert.Equal(FontWeight.SemiBold, label.FontWeight);
        });

    // Issue 85, part B: a nested game folder renders inside its parent, with its raw path as
    // the tooltip; a top-level conversation renders at the top level, after the folders.
    [AvaloniaFact]
    public void NestedFolders_RenderInsideTheirParent_WithTheRawPathAsTooltip()
    {
        var tree = ConversationTreeBuilder.Build(
        [
            new ConversationFile("eder", "companions", @"companions\eder.conversation", @"companions\eder.stringtable"),
            new ConversationFile("x", @"companions\poi", @"companions\poi\x.conversation", @"companions\poi\x.stringtable"),
            new ConversationFile("gather_your_party", "", "gather_your_party.conversation", "gather_your_party.stringtable"),
        ]);
        var vm = new GameBrowserViewModel(new StubDispatcher());
        foreach (var f in tree.Folders) vm.Folders.Add(f);
        foreach (var i in tree.RootItems) vm.RootItems.Add(i);
        vm.Folders[0].IsExpanded = true;

        var view = new GameBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 300, Height = 600 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            var treeView = view.GetVisualDescendants().OfType<TreeView>().Single();

            var companions = Assert.IsAssignableFrom<TreeViewItem>(treeView.TreeContainerFromItem(vm.Folders[0]));
            var poi = Assert.IsAssignableFrom<TreeViewItem>(companions.ContainerFromItem(vm.Folders[0].Subfolders[0]));
            var label = poi.GetVisualDescendants().OfType<TextBlock>().First();
            Assert.Equal("Poi", label.Text);
            Assert.Equal(@"companions\poi", ToolTip.GetTip(label));

            Assert.Equal(2, treeView.ItemCount);   // the Companions folder, then gather_your_party
        }
        finally { window.Close(); }
    }

    // Issue 85, part C: a conversation the project changed shows a dot (with a tooltip), and its
    // row reports the same as its accessible item status; its accessible name is unchanged.
    [AvaloniaFact]
    public void EditedConversation_ShowsTheDot_AndReportsItsStatus()
        => WithDock(1.0, d =>
        {
            d.Vm.SetProjectConversations(new HashSet<string> { d.Short.Name });
            Dispatcher.UIThread.RunJobs();

            TextBlock Dot(object item) => Row(d, item).GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Classes.Contains("editedMark"));

            Assert.True(Dot(d.Short).IsEffectivelyVisible);
            Assert.False(Dot(d.Long).IsEffectivelyVisible);
            Assert.Equal(global::Avalonia.Application.Current!.FindResource("Browser_EditedInProject"), ToolTip.GetTip(Dot(d.Short)));

            Assert.Equal(Loc.Get("Browser_EditedInProject"), global::Avalonia.Automation.AutomationProperties.GetItemStatus(Row(d, d.Short)));
            Assert.True(string.IsNullOrEmpty(global::Avalonia.Automation.AutomationProperties.GetItemStatus(Row(d, d.Long))));
            Assert.Equal(d.Short.Name, global::Avalonia.Automation.AutomationProperties.GetName(Row(d, d.Short)));
        });

    [AvaloniaFact]
    public void EditedDot_IsNotUnderTheScrollbar()
    {
        // Captures showed the dot half-hidden under the tree's overlay scrollbar, which shares the
        // rows' right edge. Enough rows to bring the scrollbar up, then the dot must clear it.
        var items = Enumerable.Range(0, 60).Select(i => MakeItem($"conversation_{i:00}")).ToList();
        var folder = new ConversationFolderViewModel("companions", items, isExpanded: true);
        var vm = new GameBrowserViewModel(new StubDispatcher());
        vm.Folders.Add(folder);
        vm.SetProjectConversations(new HashSet<string> { items[0].Name });
        var view = new GameBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 260, Height = 400 };
        try
        {
            window.Show();
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();
            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var bar = tree.GetVisualDescendants().OfType<global::Avalonia.Controls.Primitives.ScrollBar>()
                .Single(b => b.Orientation == Orientation.Vertical);
            Assert.True(bar.IsEffectivelyVisible, "the scrollbar should be showing");

            var row = Assert.IsAssignableFrom<TreeViewItem>(tree.TreeContainerFromItem(items[0]));
            var dot = row.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("editedMark"));
            var dotRight = global::Avalonia.VisualExtensions.TranslatePoint(dot, new global::Avalonia.Point(dot.Bounds.Width, 0), tree)!.Value.X;
            var barLeft  = global::Avalonia.VisualExtensions.TranslatePoint(bar, new global::Avalonia.Point(0, 0), tree)!.Value.X;
            Assert.True(dotRight <= barLeft, $"dot ends at {dotRight:F1}, scrollbar starts at {barLeft:F1}");
        }
        finally { window.Close(); }
    }

    [AvaloniaFact]
    public void OnlyProjectCheckbox_ShowsOnlyWithAProjectOpen()
        => WithDock(1.0, d =>
        {
            var box = d.View.GetVisualDescendants().OfType<CheckBox>().Single();
            Assert.False(box.IsEffectivelyVisible);

            d.Vm.SetProjectConversations(new HashSet<string>());
            Dispatcher.UIThread.RunJobs();
            Assert.True(box.IsEffectivelyVisible);
            Assert.NotNull(ToolTip.GetTip(box));

            box.IsChecked = true;
            Assert.True(d.Vm.OnlyProjectConversations);
        });

    [AvaloniaFact]
    public void Footer_UsesTheSmallFontSize()
        => WithDock(1.0, d =>
        {
            var footer = d.View.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == d.Vm.GameName && t.FindAncestorOfType<TreeView>() is null);
            Assert.Equal(Token("FontSize.Small"), footer.FontSize);
        });
}
