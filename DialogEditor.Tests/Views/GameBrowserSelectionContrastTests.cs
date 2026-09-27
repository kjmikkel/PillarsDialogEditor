using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.GameData;
using DialogEditor.Tests.Helpers;
using DialogEditor.Tests.Theming;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// <summary>
/// Issue #83: in the Light theme the selected row of the Conversations browser tree drew its
/// label in the row's RESTING foreground (dark grey / dark green) on the dark-blue selection
/// highlight — effectively invisible. The selected label must instead take the token-layer
/// selection foreground, legible (WCAG AA, and the HC palette's stricter 7:1 gate) against the
/// selection background, in every palette and for every kind of row (folder, conversation,
/// new/unsaved conversation — each has a different resting colour).
///
/// Measured on the realised controls (the label TextBlock's effective Foreground vs the header
/// presenter's Background), not on token values, so a view that ignores the tokens still fails.
/// </summary>
public class GameBrowserSelectionContrastTests
{
    public GameBrowserSelectionContrastTests() => Loc.Configure(new StubStringProvider());

    public enum Row { Folder, Conversation, NewConversation }

    public static IEnumerable<object[]> Cases()
    {
        (string Theme, double Min)[] themes =
            [("Dark", 4.5), ("Light", 4.5), ("Colourblind", 4.5), ("HighContrast", 7.0)];
        foreach (var (theme, min) in themes)
            foreach (var row in Enum.GetValues<Row>())
                yield return [theme, row, min];
    }

    [AvaloniaTheory]
    [MemberData(nameof(Cases))]
    public void SelectedRowLabel_ContrastsWithSelectionHighlight(string theme, Row row, double min)
    {
        try
        {
            new ThemeApplier().Apply(theme);

            var existing = MakeItem("companion_eder_hub", isNew: false);
            var fresh    = MakeItem("my_new_conversation", isNew: true);
            var folder   = new ConversationFolderViewModel("companions", [existing, fresh], isExpanded: true);
            var vm = new GameBrowserViewModel(new StubDispatcher());
            vm.Folders.Add(folder);

            var view = new GameBrowserView { DataContext = vm };
            var window = new Window { Content = view, Width = 400, Height = 600 };
            window.Show();
            Dispatcher.UIThread.RunJobs();

            object target = row switch
            {
                Row.Folder       => folder,
                Row.Conversation => existing,
                _                => fresh,
            };
            vm.SelectedTreeItem = target;
            Dispatcher.UIThread.RunJobs();
            window.UpdateLayout();

            var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
            var container = Assert.IsAssignableFrom<TreeViewItem>(tree.TreeContainerFromItem(target));
            Assert.True(container.IsSelected, "the target row did not become selected");

            var header = container.GetVisualDescendants().OfType<ContentPresenter>()
                .First(p => p.Name == "PART_HeaderPresenter");
            var label = header.GetVisualDescendants().OfType<TextBlock>().First();

            var bg = Assert.IsAssignableFrom<ISolidColorBrush>(header.Background).Color;
            var fg = Assert.IsAssignableFrom<ISolidColorBrush>(label.Foreground).Color;
            var ratio = Wcag.ContrastRatio(fg, bg);

            Assert.True(ratio >= min,
                $"{theme} / selected {row}: label {fg} on highlight {bg} = {ratio:F2}:1 < {min:F1}:1");
        }
        finally { new ThemeApplier().Apply("Dark"); }
    }

    // Guards the other half of the fix: moving the label colours from local values into styles
    // must not flatten the resting colours, and the selection colour must follow the selection
    // (a row that is deselected, or a child of a selected folder, keeps its own resting colour).
    [AvaloniaFact]
    public void UnselectedRows_KeepTheirRestingColours()
    {
        var existing = MakeItem("companion_eder_hub", isNew: false);
        var fresh    = MakeItem("my_new_conversation", isNew: true);
        var folder   = new ConversationFolderViewModel("companions", [existing, fresh], isExpanded: true);
        var vm = new GameBrowserViewModel(new StubDispatcher());
        vm.Folders.Add(folder);

        var view = new GameBrowserView { DataContext = vm };
        var window = new Window { Content = view, Width = 400, Height = 600 };
        window.Show();
        Dispatcher.UIThread.RunJobs();

        // Select the folder, then move the selection to the existing conversation: the folder
        // must drop back to its resting colour, and the unselected new conversation (a child
        // of the previously selected folder) must never have taken the selection colour.
        vm.SelectedTreeItem = folder;
        Dispatcher.UIThread.RunJobs();
        vm.SelectedTreeItem = existing;
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();

        var tree = view.GetVisualDescendants().OfType<TreeView>().Single();
        Color LabelColour(object item)
        {
            var container = (TreeViewItem)tree.TreeContainerFromItem(item)!;
            var header = container.GetVisualDescendants().OfType<ContentPresenter>()
                .First(p => p.Name == "PART_HeaderPresenter");
            return ((ISolidColorBrush)header.GetVisualDescendants().OfType<TextBlock>().First().Foreground!).Color;
        }
        Color Token(string key) => ((ISolidColorBrush)global::Avalonia.Application.Current!
            .FindResource(key)!).Color;

        Assert.Equal(Token("Brush.Text.Muted"),            LabelColour(folder));
        Assert.Equal(Token("Brush.Text.Status.New"),       LabelColour(fresh));
        Assert.Equal(Token("Brush.Selection.Foreground"),  LabelColour(existing));
    }

    private static ConversationItemViewModel MakeItem(string name, bool isNew)
    {
        var file = new ConversationFile(name, $@"companions\{name}.conversation", "companions",
                                        $@"companions\{name}.stringtable");
        return new ConversationItemViewModel(file, isNew: isNew);
    }
}
