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

// ── ConversationItemViewModel ─────────────────────────────────────────────────

public class ConversationItemViewModelTests
{
    public ConversationItemViewModelTests() => Loc.Configure(new StubStringProvider());

    private static ConversationFile MakeFile(string name) =>
        new(name, $@"quests\{name}.conversation", "quests", $@"quests\{name}.stringtable");

    [Fact]
    public void Name_MatchesFileName()
    {
        var vm = new ConversationItemViewModel(MakeFile("intro_scene"));
        Assert.Equal("intro_scene", vm.Name);
    }

    [Fact]
    public void File_IsPreserved()
    {
        var file = MakeFile("intro_scene");
        var vm   = new ConversationItemViewModel(file);
        Assert.Same(file, vm.File);
    }

    [Fact]
    public void IsNew_FalseByDefault()
    {
        var vm = new ConversationItemViewModel(MakeFile("intro"));
        Assert.False(vm.IsNew);
    }

    [Fact]
    public void IsNew_TrueWhenPassedTrue()
    {
        var vm = new ConversationItemViewModel(MakeFile("intro"), isNew: true);
        Assert.True(vm.IsNew);
    }

    [Fact]
    public void DisplayName_WhenNotNew_EqualsName()
    {
        var vm = new ConversationItemViewModel(MakeFile("intro"));
        Assert.Equal("intro", vm.DisplayName);
    }

    [Fact]
    public void DisplayName_WhenNew_ContainsName()
    {
        var vm = new ConversationItemViewModel(MakeFile("intro"), isNew: true);
        // Stub Loc returns the key; suffix key is "Label_NewConversation_Suffix"
        Assert.Contains("intro", vm.DisplayName);
    }
}

// ── ConversationFolderViewModel ───────────────────────────────────────────────

public class ConversationFolderViewModelTests
{
    public ConversationFolderViewModelTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void FolderPath_IsPreserved()
    {
        var vm = new ConversationFolderViewModel("quests");
        Assert.Equal("quests", vm.FolderPath);
    }

    [Fact]
    public void DisplayName_WhenPathIsEmpty_UsesLocKey()
    {
        // Stub returns the key itself; the real app would return a localised label
        var vm = new ConversationFolderViewModel(string.Empty);
        Assert.Equal("Browser_RootFolder", vm.DisplayName);
    }

    [Fact]
    public void DisplayName_WhenPathIsSet_EqualsFolderPath()
    {
        var vm = new ConversationFolderViewModel("quests");
        Assert.Equal("quests", vm.DisplayName);
    }

    [Fact]
    public void IsExpanded_DefaultFalse()
    {
        var vm = new ConversationFolderViewModel("quests");
        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void IsExpanded_CanBeSetViaConstructor()
    {
        var vm = new ConversationFolderViewModel("quests", isExpanded: true);
        Assert.True(vm.IsExpanded);
    }

    [Fact]
    public void Items_StartsEmpty_WhenNoItemsProvided()
    {
        var vm = new ConversationFolderViewModel("quests");
        Assert.Empty(vm.Items);
    }

    [Fact]
    public void Items_PopulatedFromConstructorArgument()
    {
        var file = new ConversationFile("intro", @"quests\intro.conversation", "quests",
                                         @"quests\intro.stringtable");
        var item = new ConversationItemViewModel(file);
        var vm   = new ConversationFolderViewModel("quests", [item]);
        Assert.Single(vm.Items);
        Assert.Same(item, vm.Items[0]);
    }
}

// ── PatchEntryViewModel ───────────────────────────────────────────────────────

public class PatchEntryViewModelTests
{
    [Fact]
    public void IsLoaded_TrueForSuccessConstructor()
    {
        var entry = new PatchEntryViewModel(@"C:\mods\mymod.dialogproject",
                                            DialogProject.Empty("MyMod"));
        Assert.True(entry.IsLoaded);
        Assert.Null(entry.LoadError);
    }

    [Fact]
    public void IsLoaded_FalseForErrorConstructor()
    {
        var entry = new PatchEntryViewModel(@"C:\mods\bad.dialogproject", "File not found");
        Assert.False(entry.IsLoaded);
        Assert.Equal("File not found", entry.LoadError);
    }

    [Fact]
    public void DisplayPath_IsFileNameOnly()
    {
        var entry = new PatchEntryViewModel(@"C:\mods\mymod.dialogproject",
                                            DialogProject.Empty("MyMod"));
        Assert.Equal("mymod.dialogproject", entry.DisplayPath);
    }

    [Fact]
    public void ProjectName_MatchesProjectName()
    {
        var entry = new PatchEntryViewModel(@"C:\mods\mymod.dialogproject",
                                            DialogProject.Empty("MyMod"));
        Assert.Equal("MyMod", entry.ProjectName);
    }

    [Fact]
    public void PatchCount_ReflectsProjectPatchCount()
    {
        var project = DialogProject.Empty("MyMod").WithPatch(
            new DialogEditor.Patch.ConversationPatch(
                "conv1", DialogEditor.Patch.ConversationPatch.CurrentSchemaVersion, [], [], []));
        var entry = new PatchEntryViewModel(@"C:\mods\mymod.dialogproject", project);
        Assert.Equal(1, entry.PatchCount);
    }

    [Fact]
    public void HasConflict_FalseByDefault()
    {
        var entry = new PatchEntryViewModel(@"C:\mods\mymod.dialogproject",
                                            DialogProject.Empty("MyMod"));
        Assert.False(entry.HasConflict);
    }

    [Fact]
    public void HasConflict_CanBeSetToTrue()
    {
        var entry = new PatchEntryViewModel(@"C:\mods\mymod.dialogproject",
                                            DialogProject.Empty("MyMod"));
        entry.HasConflict = true;
        Assert.True(entry.HasConflict);
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
