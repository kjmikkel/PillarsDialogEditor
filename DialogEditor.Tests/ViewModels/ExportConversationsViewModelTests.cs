using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class ExportConversationsViewModelTests
{
    public ExportConversationsViewModelTests() =>
        Loc.Configure(new StubStringProvider());

    private static ExportConversationsViewModel MakeVm(
        string? currentConversation = null,
        string? saveResult = null,
        string? folderResult = null) =>
        new(
            ["conv_a", "conv_b", "conv_c"],
            currentConversation,
            _ => [],
            new StubFilePicker(saveResult: saveResult),
            new StubFolderPicker(folderResult));

    [Fact]
    public void Constructor_CurrentConversation_IsPreChecked()
    {
        var vm = MakeVm(currentConversation: "conv_b");
        Assert.True(vm.ConversationItems.Single(i => i.Name == "conv_b").IsChecked);
    }

    [Fact]
    public void Constructor_OtherConversations_AreNotPreChecked()
    {
        var vm = MakeVm(currentConversation: "conv_b");
        Assert.False(vm.ConversationItems.Single(i => i.Name == "conv_a").IsChecked);
        Assert.False(vm.ConversationItems.Single(i => i.Name == "conv_c").IsChecked);
    }

    [Fact]
    public void Constructor_NullCurrentConversation_NoneChecked()
    {
        var vm = MakeVm(currentConversation: null);
        Assert.All(vm.ConversationItems, i => Assert.False(i.IsChecked));
    }

    [Fact]
    public void SelectAll_ChecksEveryItem()
    {
        var vm = MakeVm();
        vm.SelectAllCommand.Execute(null);
        Assert.All(vm.ConversationItems, i => Assert.True(i.IsChecked));
    }

    [Fact]
    public void SelectNone_UnchecksEveryItem()
    {
        var vm = MakeVm(currentConversation: "conv_a");
        vm.SelectNoneCommand.Execute(null);
        Assert.All(vm.ConversationItems, i => Assert.False(i.IsChecked));
    }

    [Fact]
    public void ExportCommand_CannotExecute_WhenNothingChecked()
    {
        var vm = MakeVm();
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void ExportCommand_CanExecute_WhenOneItemChecked()
    {
        var vm = MakeVm();
        vm.ConversationItems[0].IsChecked = true;
        Assert.True(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void ExportCommand_CannotExecute_AfterUnchecking()
    {
        var vm = MakeVm(currentConversation: "conv_a");
        vm.ConversationItems[0].IsChecked = false;
        Assert.False(vm.ExportCommand.CanExecute(null));
    }

    [Fact]
    public void SelectedFormat_DefaultsTo_Csv()
    {
        var vm = MakeVm();
        Assert.Equal("csv", vm.SelectedFormat);
    }

    [Fact]
    public async Task ExportCommand_SingleItem_WritesFile()
    {
        var path = Path.GetTempFileName() + ".csv";
        try
        {
            var nodes = new List<NodeEditSnapshot>
            {
                new(NodeId: 1, IsPlayerChoice: false,
                    SpeakerCategory: SpeakerCategory.Npc,
                    SpeakerGuid: "", ListenerGuid: "",
                    DefaultText: "Hi.", FemaleText: "",
                    DisplayType: "Conversation", Persistence: "None",
                    ActorDirection: "", Comments: "", ExternalVO: "",
                    HasVO: false, HideSpeaker: false,
                    Links: [], Conditions: [], Scripts: [])
            };
            var vm = new ExportConversationsViewModel(
                ["test_conv"],
                "test_conv",
                _ => nodes,
                new StubFilePicker(saveResult: path),
                new StubFolderPicker());

            await vm.ExportCommand.ExecuteAsync(null);
            Assert.True(File.Exists(path));
            Assert.NotEmpty(File.ReadAllText(path));
        }
        finally { if (File.Exists(path)) File.Delete(path); }
    }
}
