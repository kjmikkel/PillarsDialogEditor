using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

// ── ConnectorViewModel ────────────────────────────────────────────────────────

public class ConnectorViewModelTests
{
    public ConnectorViewModelTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void GetNodeId_WithoutOwner_ReturnsMinusOne()
    {
        var vm = new ConnectorViewModel();
        Assert.Equal(-1, vm.GetNodeId());
    }

    [Fact]
    public void GetNodeId_WhenOwnerSetViaCanvas_ReturnsOwnerNodeId()
    {
        var canvas = new ConversationViewModel(new StubDispatcher());
        var node   = MakeNode(42);
        canvas.AddNode(node, new LayoutPoint(0, 0));
        // AddNode wires Owner on Input and Output
        Assert.Equal(42, node.Input.GetNodeId());
        Assert.Equal(42, node.Output.GetNodeId());
    }

    private static NodeViewModel MakeNode(int id)
    {
        var n = new ConversationNode(id, false, SpeakerCategory.Npc, "", "", [], [], [],
                                     "Conversation", "None");
        return new NodeViewModel(n, new StringEntry(id, "text", ""));
    }
}

// ── SettingsViewModel ─────────────────────────────────────────────────────────

public class SettingsViewModelTests
{
    public SettingsViewModelTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public async Task BrowseBackupDirectory_WhenPickerReturnsPath_SetsBackupDirectory()
    {
        var picker = new StubFolderPicker(@"C:\Backups");
        var vm     = new SettingsViewModel(string.Empty, picker);
        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);
        Assert.Equal(@"C:\Backups", vm.BackupDirectory);
    }

    [Fact]
    public async Task BrowseBackupDirectory_WhenPickerCancelled_DoesNotChangeBackupDirectory()
    {
        var picker = new StubFolderPicker(result: null);
        var vm     = new SettingsViewModel(string.Empty, picker);
        var before = vm.BackupDirectory;
        await vm.BrowseBackupDirectoryCommand.ExecuteAsync(null);
        Assert.Equal(before, vm.BackupDirectory);
    }
}
