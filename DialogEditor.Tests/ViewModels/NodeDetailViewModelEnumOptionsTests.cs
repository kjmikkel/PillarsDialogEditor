using System.ComponentModel;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Issue 132: the Display Type / Persistence dropdowns offered only two of each enum's four
/// values, and their items were localised labels, so picking one in a translated UI wrote
/// the translated word into the model. Items are now the stored names; the view localises.
public class NodeDetailViewModelEnumOptionsTests
{
    private readonly NodeDetailViewModel _vm = new();

    public NodeDetailViewModelEnumOptionsTests() => Loc.Configure(new StubStringProvider());

    private static NodeViewModel MakeNode(string displayType = "Conversation", string persistence = "None") =>
        new(new ConversationNode(
                NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
                DisplayType: displayType, Persistence: persistence, ActorDirection: "",
                Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false),
            new StringEntry(1, "Hello", ""));

    [Fact]
    public void DisplayTypeOptions_AreAllFourGameValues_ByStoredName()
    {
        _vm.Load(MakeNode());
        Assert.Equal(["Hidden", "Conversation", "Bark", "Overlay"], NodeDetailViewModel.DisplayTypeOptions);
    }

    [Fact]
    public void PersistenceOptions_AreAllFourGameValues_ByStoredName()
    {
        _vm.Load(MakeNode());
        Assert.Equal(["None", "OnceEver", "OncePerConversation", "MarkAsRead"], NodeDetailViewModel.PersistenceOptions);
    }

    [Fact]
    public void Options_DoNotDependOnTheUiLanguage()
    {
        Loc.Configure(new EchoStringProvider(new() { ["Option_DisplayBark"] = "Ausruf" }));
        _vm.Load(MakeNode());
        Assert.Contains("Bark", NodeDetailViewModel.DisplayTypeOptions);
        Assert.DoesNotContain("Ausruf", NodeDetailViewModel.DisplayTypeOptions);
    }

    [Fact]
    public void SelectingAnOption_StoresTheEnumName()
    {
        var node = MakeNode();
        _vm.Load(node);
        _vm.DisplayType = "Overlay";
        _vm.Persistence = "MarkAsRead";
        Assert.Equal("Overlay",    node.DisplayType);
        Assert.Equal("MarkAsRead", node.Persistence);
    }

    [Fact]
    public void UnknownValue_IsExposedAsUnlisted_NotAddedToTheOptions()
    {
        _vm.Load(MakeNode(displayType: "Unknown(7)", persistence: "Unknown(9)"));
        Assert.Equal("Unknown(7)", _vm.DisplayTypeUnlisted);
        Assert.Equal("Unknown(9)", _vm.PersistenceUnlisted);
        Assert.DoesNotContain("Unknown(7)", NodeDetailViewModel.DisplayTypeOptions);
    }

    [Fact]
    public void AbsentValue_IsExposedAsUnlisted()
    {
        // "" = the property is absent (e.g. a BankNode); the view shows it as "(not set)".
        _vm.Load(MakeNode(displayType: ""));
        Assert.Equal("", _vm.DisplayTypeUnlisted);
    }

    [Fact]
    public void ListedValue_HasNoUnlistedText()
    {
        _vm.Load(MakeNode(displayType: "Bark", persistence: "OnceEver"));
        Assert.Null(_vm.DisplayTypeUnlisted);
        Assert.Null(_vm.PersistenceUnlisted);
    }

    [Fact]
    public void ComboBoxClearingTheSelection_DoesNotOverwriteTheValue()
    {
        // An unlisted value leaves the ComboBox with nothing selected, and it pushes null
        // back through the TwoWay SelectedItem binding; that must not wipe the value.
        var node = MakeNode(displayType: "Unknown(7)", persistence: "Unknown(9)");
        _vm.Load(node);
        _vm.DisplayType = null!;
        _vm.Persistence = null!;
        Assert.Equal("Unknown(7)", node.DisplayType);
        Assert.Equal("Unknown(9)", node.Persistence);
    }

    [Fact]
    public void Load_AnnouncesTheUnlistedValues()
    {
        var raised = new List<string?>();
        ((INotifyPropertyChanged)_vm).PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        _vm.Load(MakeNode(displayType: "Unknown(7)"));
        Assert.Contains(nameof(_vm.DisplayTypeUnlisted), raised);
        Assert.Contains(nameof(_vm.PersistenceUnlisted), raised);
    }
}