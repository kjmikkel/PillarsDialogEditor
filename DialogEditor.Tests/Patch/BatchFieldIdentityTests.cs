using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Patch;

/// <summary>
/// BatchFieldMatch's field member is the match's IDENTITY: Apply re-loads each
/// conversation and pairs its fresh snapshot back to the DryRun matches by that
/// member. It used to be the very string the preview displayed ("Default Text"),
/// which meant localising the label — the repo owner's stated intent — would have
/// made the Apply key language-dependent, and a UI language change between preview
/// and apply would have made Apply a silent no-op.
///
/// These tests pin the identity as typed data so the label can move to the
/// ViewModel. See BatchReplaceLabelTests for the label half.
/// </summary>
public class BatchFieldIdentityTests
{
    private static ConversationFile MakeFile(string name) =>
        new(name, $@"quests\{name}.conversation", "quests", $@"quests\{name}.stringtable");

    private static NodeEditSnapshot MakeNode(
        int id, string defaultText = "", string femaleText = "",
        string speakerGuid = "", string listenerGuid = "",
        IReadOnlyList<ScriptCall>? scripts = null,
        IReadOnlyList<ConditionNode>? conditions = null) =>
        new(id, false, SpeakerCategory.Npc, speakerGuid, listenerGuid,
            defaultText, femaleText, "Conversation", "None", "", "", "", false, false,
            [], conditions ?? [], scripts ?? []);

    private static BatchFieldMatch OnlyMatch(BatchReplaceQuery query, params NodeEditSnapshot[] nodes)
    {
        var file    = MakeFile("conv");
        var results = BatchReplaceService.DryRun(
            query, [file], new StubProvider(file, new ConversationEditSnapshot(nodes)));
        return results[0].Matches[0];
    }

    [Fact]
    public void DryRun_DefaultTextMatch_IsIdentifiedByKindNotByItsLabel()
    {
        var match = OnlyMatch(
            new BatchReplaceQuery("world", "earth", false, InNodeText: true),
            MakeNode(1, defaultText: "Hello world"));

        Assert.Equal(new BatchField(BatchFieldKind.DefaultText), match.Field);
    }

    [Fact]
    public void DryRun_FemaleTextMatch_IsIdentifiedByKind()
    {
        var match = OnlyMatch(
            new BatchReplaceQuery("hello", "hi", false, InNodeText: true),
            MakeNode(1, femaleText: "She said hello"));

        Assert.Equal(new BatchField(BatchFieldKind.FemaleText), match.Field);
    }

    [Fact]
    public void DryRun_SpeakerAndListenerGuidMatches_AreDistinctKinds()
    {
        var file    = MakeFile("conv");
        var results = BatchReplaceService.DryRun(
            new BatchReplaceQuery("aaa", "bbb", false, InNodeText: false, InSpeakerGuids: true),
            [file],
            new StubProvider(file, new ConversationEditSnapshot(
                [MakeNode(1, speakerGuid: "aaa-1", listenerGuid: "aaa-2")])));

        Assert.Equal(
            [new BatchField(BatchFieldKind.SpeakerGuid), new BatchField(BatchFieldKind.ListenerGuid)],
            results[0].Matches.Select(m => m.Field));
    }

    [Fact]
    public void DryRun_ScriptParamMatch_CarriesCategoryAndBothIndices()
    {
        // The label reads "Script Enter[0] Param 1"; the identity keeps the three
        // values separate so the ViewModel can order them per language.
        var match = OnlyMatch(
            new BatchReplaceQuery("myFlag", "renamedFlag", false, InScriptParams: true),
            MakeNode(1, scripts: [new ScriptCall(
                "Void SetGlobalValue(String, Int32)", ["1", "myFlag"], ScriptCategory.Enter)]));

        Assert.Equal(
            new BatchField(BatchFieldKind.ScriptParam, ScriptCategory.Enter, Index: 0, ParamIndex: 1),
            match.Field);
    }

    [Fact]
    public void DryRun_ConditionParamMatch_CarriesLeafAndParamIndices()
    {
        var match = OnlyMatch(
            new BatchReplaceQuery("myFlag", "renamedFlag", false, InConditionParams: true),
            MakeNode(1, conditions: [new ConditionLeaf(
                "Boolean IsGlobalValue(String, Operator, Int32)",
                ["myFlag", "EqualTo", "1"], false, "And")]));

        Assert.Equal(
            new BatchField(BatchFieldKind.ConditionParam, Index: 0, ParamIndex: 0),
            match.Field);
    }

    [Fact]
    public void BatchField_IsAValueSoApplyCanIndexMatchesByIt()
    {
        // Apply does patches.ToDictionary(p => p.Field); record value equality is what
        // makes that pairing work without reconstructing a display string.
        Assert.Equal(
            new BatchField(BatchFieldKind.ScriptParam, ScriptCategory.Exit, 2, 3),
            new BatchField(BatchFieldKind.ScriptParam, ScriptCategory.Exit, 2, 3));
        Assert.NotEqual(
            new BatchField(BatchFieldKind.ScriptParam, ScriptCategory.Exit, 2, 3),
            new BatchField(BatchFieldKind.ScriptParam, ScriptCategory.Enter, 2, 3));
    }
}
