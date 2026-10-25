using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 264 (F-38): Delete removed the selected node whenever focus wasn't in a TextBox,
/// so pressing it on a Node Details combo box or check box deleted the node with no warning.
/// Delete now acts on the node only when the key comes from the canvas.
/// </summary>
public class MainWindowDeleteKeyTests
{
    public MainWindowDeleteKeyTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Delete_FromTheCanvas_TargetsTheNode()
    {
        var view = new ConversationView { DataContext = new ConversationViewModel(new StubDispatcher()) };
        var w = new Window { Content = view };
        w.Show();
        try
        {
            Assert.True(MainWindow.DeleteTargetsCanvasNode(view.FindControl<Control>("Editor")));
            Assert.False(MainWindow.DeleteTargetsCanvasNode(view.GetVisualDescendants().OfType<TextBox>().First()));
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Delete_FromAnotherControl_DoesNotTargetTheNode()
    {
        var combo = new ComboBox();
        var check = new CheckBox();
        var w = new Window { Content = new StackPanel { Children = { combo, check } } };
        w.Show();
        try
        {
            Assert.False(MainWindow.DeleteTargetsCanvasNode(combo));
            Assert.False(MainWindow.DeleteTargetsCanvasNode(check));
            Assert.False(MainWindow.DeleteTargetsCanvasNode(null));
        }
        finally { w.Close(); }
    }
}
