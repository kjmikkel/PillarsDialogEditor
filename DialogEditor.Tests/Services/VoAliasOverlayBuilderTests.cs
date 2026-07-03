using System.Text.Json;
using DialogEditor.Patch;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VoAliasOverlayBuilderTests
{
    private static ConversationPatch PatchWith(
        string conv,
        (int id, string alias)[] added,
        (int id, string alias)[] modified)
        => new(
            ConversationName: conv,
            SchemaVersion: ConversationPatch.CurrentSchemaVersion,
            AddedNodes: added.Select(a => new DialogEditor.Core.Editing.NodeEditSnapshot(
                NodeId: a.id, IsPlayerChoice: false,
                SpeakerCategory: DialogEditor.Core.Models.SpeakerCategory.Npc,
                SpeakerGuid: "", ListenerGuid: "",
                DefaultText: "", FemaleText: "",
                DisplayType: "Conversation", Persistence: "None",
                ActorDirection: "", Comments: "",
                ExternalVO: a.alias, HasVO: false, HideSpeaker: false,
                Links: [], Conditions: [], Scripts: [])).ToList(),
            DeletedNodeIds: [],
            ModifiedNodes: modified.Select(m => new NodeModification(
                m.id,
                new Dictionary<string, FieldChange>
                {
                    ["ExternalVO"] = new FieldChange(
                        JsonSerializer.Serialize(""), JsonSerializer.Serialize(m.alias))
                },
                [], [])).ToList());

    [Fact]
    public void Build_CollectsAddedAndModifiedNodes()
    {
        var patches = new Dictionary<string, ConversationPatch>
        {
            ["conv_a"] = PatchWith("conv_a",
                added: [(100, "narrator/x_0001")],
                modified: [(3, "narrator/y_0002")]),
        };

        var uses = VoAliasOverlayBuilder.Build(patches, null, null);

        Assert.Contains(new VoAliasUse("conv_a", 100, "narrator/x_0001"), uses);
        Assert.Contains(new VoAliasUse("conv_a", 3, "narrator/y_0002"), uses);
    }

    [Fact]
    public void Build_OpenCanvasNodes_WinOverPatchForSameConversation()
    {
        var patches = new Dictionary<string, ConversationPatch>
        {
            ["conv_a"] = PatchWith("conv_a", added: [], modified: [(3, "narrator/old_0001")]),
        };

        var uses = VoAliasOverlayBuilder.Build(
            patches, "conv_a", [(3, "narrator/new_0002"), (4, "")]);

        Assert.Contains(new VoAliasUse("conv_a", 3, "narrator/new_0002"), uses);
        Assert.DoesNotContain(uses, u => u.AliasPath == "narrator/old_0001");
        // Empty alias still shadows the disk entry (means "no longer aliased").
        Assert.Contains(new VoAliasUse("conv_a", 4, ""), uses);
    }

    [Fact]
    public void Build_NullInputs_ReturnsEmpty()
        => Assert.Empty(VoAliasOverlayBuilder.Build(null, null, null));
}
