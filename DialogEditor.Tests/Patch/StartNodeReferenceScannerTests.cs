using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// Issue 172: after a renumber, the status bar says how many references elsewhere in the
/// project still name the old node ID. They are counted, never rewritten.
public class StartNodeReferenceScannerTests
{
    private static ScriptCall Jump(params string[] p) =>
        new("Void StartConversation(Guid, Guid, Int32)", p, ScriptCategory.Enter);

    private static NodeEditSnapshot Added(int id, IReadOnlyList<ScriptCall>? scripts = null, string externalVo = "") =>
        new(id, false, SpeakerCategory.Npc, "", "", "", "", "Conversation", "None", "", "", externalVo,
            false, false, [], [], scripts ?? []);

    private static DialogProject With(string conv, params NodeEditSnapshot[] nodes) =>
        DialogProject.Empty("p").WithPatch(
            new ConversationPatch(conv, ConversationPatch.CurrentSchemaVersion, nodes, [], []));

    [Fact]
    public void Counts_AJumpByName_WithTheOldId() =>
        Assert.Equal(1, StartNodeReferenceScanner.Count(
            With("other", Added(5, [Jump("oei_player", "fresh", "1")])), "fresh", 1, null));

    [Fact]
    public void Counts_AJumpByDeadfireId() =>
        Assert.Equal(1, StartNodeReferenceScanner.Count(
            With("other", Added(5, [Jump("oei_player", NewConversationId.For("fresh").ToString(), "1")])),
            "fresh", 1, null));

    [Fact]
    public void Counts_APoe1Path() =>
        Assert.Equal(1, StartNodeReferenceScanner.Count(
            With("other", Added(5, [Jump("oei_player", "Assets/Data/Conversations/x/Fresh.conversation", "1")])),
            "fresh", 1, null));

    [Fact]
    public void Ignores_OtherConversations_AndOtherIds()
    {
        var project = With("other",
            Added(5, [Jump("oei_player", "elsewhere", "1")]),
            Added(6, [Jump("oei_player", "fresh", "2")]));
        Assert.Equal(0, StartNodeReferenceScanner.Count(project, "fresh", 1, null));
    }

    [Fact]
    public void Ignores_TheConversationItself() =>
        Assert.Equal(0, StartNodeReferenceScanner.Count(
            With("fresh", Added(5, [Jump("oei_player", "fresh", "1")])), "fresh", 1, null));

    [Fact]
    public void Counts_AnExternalVoAlias() =>
        Assert.Equal(1, StartNodeReferenceScanner.Count(
            With("other", Added(5, externalVo: "Narrator\\fresh_0001")), "fresh", 1, "narrator/fresh_0001"));
}
