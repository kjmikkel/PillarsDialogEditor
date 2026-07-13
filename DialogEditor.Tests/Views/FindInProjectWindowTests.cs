using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using Xunit;

namespace DialogEditor.Tests.Views;

public class FindInProjectWindowTests
{
    private const string SpeakerGuid = "9c5f12c9-e93d-4952-9f1a-726c9498f8fb";

    public FindInProjectWindowTests() => Loc.Configure(new StubStringProvider());

    [AvaloniaFact]
    public void Window_BindsResults()
    {
        var (project, provider) = ProjectWith("c1", 1, "The Watcher");
        var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
        { SearchText = "Watcher" };
        vm.SearchCommand.Execute(null);
        var win = new FindInProjectWindow(vm);
        win.Show();
        var list = win.FindControl<ItemsControl>("ResultsList")!;
        Assert.Equal(1, list.ItemCount);
    }

    [AvaloniaFact]
    public void ResultsList_EnterKey_NavigatesToSelectedRow()
    {
        var (project, provider) = ProjectWith("c1", 1, "The Watcher");
        var vm = new ProjectFindViewModel(project, provider, "en", () => (null, null))
        { SearchText = "Watcher" };
        vm.SearchCommand.Execute(null);
        var win = new FindInProjectWindow(vm);
        win.Show();

        var expected = vm.Results[0];
        var raisedCount = 0;
        string? navConv = null;
        int? navNodeId = null;
        vm.RequestNavigate += (conversationName, nodeId) =>
        {
            raisedCount++;
            navConv = conversationName;
            navNodeId = nodeId;
        };

        var list = win.FindControl<ListBox>("ResultsList")!;
        list.SelectedItem = list.ItemsSource!.Cast<object>().First();
        list.RaiseEvent(new KeyEventArgs
        {
            RoutedEvent = InputElement.KeyDownEvent,
            Key = Key.Enter,
        });

        Assert.Equal(1, raisedCount);
        Assert.Equal(expected.ConversationName, navConv);
        Assert.Equal(expected.NodeId, navNodeId);
    }

    // -- helpers (mirrors DialogEditor.Tests.ViewModels.ProjectFindViewModelTests) --

    private static ConversationNode MakeNode(int id) =>
        new(id, false, SpeakerCategory.Npc, SpeakerGuid, "", [], [], [], "Conversation", "None");

    private static ConversationPatch EmptyPatch(string convName) =>
        new(convName, ConversationPatch.CurrentSchemaVersion, [], [], []);

    private static (DialogProject, IGameDataProvider) ProjectWith(string conv, int nodeId, string defaultText)
    {
        var node = MakeNode(nodeId);
        var strings = new StringTable([new StringEntry(nodeId, defaultText, "")]);
        var convObj = new Conversation(conv, [node], strings);
        var provider = new FakeGameDataProvider("poe2", "en", convObj);
        var project = DialogProject.Empty("P").WithPatch(EmptyPatch(conv));
        return (project, provider);
    }
}
