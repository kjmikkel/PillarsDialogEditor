using System.Xml.Linq;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// <summary>
/// GitHub issue 252: before a game folder is open the work area was blank, with only the
/// status bar's small "Open a game folder to begin." as guidance. A welcome panel now fills
/// the dock area until the layout exists, with the open-folder action and the guided tour.
/// </summary>
public class MainWindowWelcomeTests
{
    public MainWindowWelcomeTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void Welcome_ShowsUntilTheDockLayoutExists()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        Assert.True(vm.ShowWelcome);

        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.DockLayout = new object();

        Assert.False(vm.ShowWelcome);
        Assert.Contains(nameof(MainWindowViewModel.ShowWelcome), raised);
    }

    private static XElement Welcome()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        var doc = XDocument.Load(Path.Combine(dir!.FullName, "DialogEditor.Avalonia", "Views", "MainWindow.axaml"));
        var panel = doc.Descendants().SingleOrDefault(e => e.Attribute("IsVisible")?.Value == "{Binding ShowWelcome}");
        Assert.True(panel is not null, "MainWindow has no element with IsVisible=\"{Binding ShowWelcome}\"");
        return panel!;
    }

    [Fact]
    public void Welcome_ExplainsWhatToDo()
    {
        var state = Welcome().Descendants().Single(e => e.Name.LocalName == "EmptyState");
        Assert.Equal("{DynamicResource EmptyState_Main_NoGame}", state.Attribute("Message")?.Value);
        Assert.Equal("{DynamicResource EmptyState_Main_NoGameHint}", state.Attribute("Hint")?.Value);
    }

    [Theory]
    [InlineData("{Binding OpenFolderCommand}")]
    [InlineData("{Binding StartGuidedTourCommand}")]
    public void Welcome_OffersTheAction_WithATooltip(string command)
    {
        var button = Welcome().Descendants()
            .SingleOrDefault(e => e.Name.LocalName == "Button" && e.Attribute("Command")?.Value == command);
        Assert.True(button is not null, $"welcome panel has no Button bound to {command}");
        Assert.StartsWith("{DynamicResource ", button!.Attribute("Content")?.Value);
        Assert.StartsWith("{DynamicResource ", button.Attribute("ToolTip.Tip")?.Value);
        Assert.Equal(button.Attribute("ToolTip.Tip")?.Value, button.Attribute("AutomationProperties.HelpText")?.Value);
    }
}
