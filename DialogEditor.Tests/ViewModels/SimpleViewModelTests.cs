using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

// ── ConversationFolderViewModel ───────────────────────────────────────────────

public class ConversationFolderViewModelTests
{
    public ConversationFolderViewModelTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void FolderViewModel_DisplayName_EmptyPath_ReturnsRootFolderKey()
    {
        var vm = new ConversationFolderViewModel(string.Empty);
        // StubStringProvider returns the key itself, so this asserts the right key is used
        Assert.Equal("Browser_RootFolder", vm.DisplayName);
    }

    [Fact]
    public void FolderViewModel_DisplayName_NonEmptyPath_ReturnsPath()
    {
        var vm = new ConversationFolderViewModel("some/folder");
        Assert.Equal("some/folder", vm.DisplayName);
    }

    [Fact]
    public void FolderViewModel_IsExpanded_DefaultsFalse()
    {
        var vm = new ConversationFolderViewModel("x");
        Assert.False(vm.IsExpanded);
    }

    [Fact]
    public void FolderViewModel_IsExpanded_SetTrue_RaisesPropertyChanged()
    {
        var vm = new ConversationFolderViewModel("x");
        var raised = false;
        vm.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(vm.IsExpanded)) raised = true;
        };
        vm.IsExpanded = true;
        Assert.True(raised);
    }
}

// ── ConversationItemViewModel ─────────────────────────────────────────────────

public class ConversationItemViewModelTests
{
    public ConversationItemViewModelTests() => Loc.Configure(new StubStringProvider());

    private static ConversationFile MakeFile(string name)
        => new(name, Path.Combine("conversations", name + ".conversation"), "conversations", "");

    [Fact]
    public void ItemViewModel_DisplayName_IsNewFalse_ReturnsName()
    {
        var vm = new ConversationItemViewModel(MakeFile("myconv"), isNew: false);
        Assert.Equal("myconv", vm.DisplayName);
    }

    [Fact]
    public void ItemViewModel_DisplayName_IsNewTrue_AppendsSuffix()
    {
        var vm = new ConversationItemViewModel(MakeFile("myconv"), isNew: true);
        // StubStringProvider returns "Label_NewConversation_Suffix" as the suffix value
        Assert.StartsWith("myconv", vm.DisplayName);
        Assert.Contains("Label_NewConversation_Suffix", vm.DisplayName);
    }
}

// ── PatchEntryViewModel ───────────────────────────────────────────────────────

public class PatchEntryViewModelTests
{
    [Fact]
    public void PatchEntry_SuccessPath_IsLoaded_True()
    {
        var project = DialogProject.Empty("MyProject");
        var vm = new PatchEntryViewModel("/projects/my.dialogproject", project);
        Assert.True(vm.IsLoaded);
        Assert.Null(vm.LoadError);
    }

    [Fact]
    public void PatchEntry_SuccessPath_ProjectName_FromProject()
    {
        var project = DialogProject.Empty("MyProject");
        var vm = new PatchEntryViewModel("/projects/my.dialogproject", project);
        Assert.Equal("MyProject", vm.ProjectName);
    }

    [Fact]
    public void PatchEntry_ErrorPath_IsLoaded_False()
    {
        var vm = new PatchEntryViewModel("/projects/bad.dialogproject", "File not found");
        Assert.False(vm.IsLoaded);
        Assert.Equal("File not found", vm.LoadError);
    }

    [Fact]
    public void PatchEntry_ErrorPath_ProjectName_UsesDisplayPath()
    {
        var vm = new PatchEntryViewModel("/projects/bad.dialogproject", "File not found");
        // DisplayPath is Path.GetFileName(fullPath) = "bad.dialogproject"
        Assert.Equal("bad.dialogproject", vm.ProjectName);
    }
}
