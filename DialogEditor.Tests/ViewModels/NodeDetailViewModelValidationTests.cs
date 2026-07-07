using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelValidationTests
{
    public NodeDetailViewModelValidationTests() => Loc.Configure(new StubStringProvider());

    // Build a NodeViewModel with the given Default/Female text (mirrors the
    // construction in NodeDetailViewModelTests.MakeNode).
    private static NodeViewModel MakeNode(string defaultText, string femaleText = "")
    {
        var node = new ConversationNode(
            NodeId: 1, IsPlayerChoice: false, SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "", Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None", ActorDirection: "",
            Comments: "", ExternalVO: "", HasVO: false, HideSpeaker: false);
        return new NodeViewModel(node, new StringEntry(1, defaultText, femaleText));
    }

    private static NodeDetailViewModel Loaded(string defaultText, string femaleText = "")
    {
        var vm = new NodeDetailViewModel { ActiveGameId = "poe2" };
        vm.Load(MakeNode(defaultText, femaleText));
        return vm;
    }

    [Fact]
    public void TokenWarnings_CleanText_IsEmpty()
        => Assert.Empty(Loaded("Hello [Player Name].").TokenWarnings);

    [Fact]
    public void TokenWarnings_MisspelledToken_UsesSuggestionBranch()
    {
        var msg = Assert.Single(Loaded("Hi [Player Nmae]!").TokenWarnings);
        // The stub echoes resource keys, so the message IS the key of the branch taken.
        Assert.Equal("Validation_UnknownToken_Suggest", msg);
    }

    [Fact]
    public void TokenWarnings_RecomputeOnDefaultTextEdit()
    {
        var vm = Loaded("clean text");
        Assert.Empty(vm.TokenWarnings);
        vm.DefaultText = "now with [Player Nmae]";
        Assert.NotEmpty(vm.TokenWarnings);
    }

    [Fact]
    public void TokenWarnings_ValidatesFemaleText()
        => Assert.NotEmpty(Loaded("clean", femaleText: "she says [Player Nmae]").TokenWarnings);
}
