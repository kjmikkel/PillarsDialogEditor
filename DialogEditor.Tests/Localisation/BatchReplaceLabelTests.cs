using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// The label half of the FieldPath split (see BatchFieldIdentityTests for the
/// identity half). Tier one: against StubStringProvider, which echoes each key, so
/// a label built from a hard-coded literal cannot pass.
/// </summary>
public class BatchReplaceLabelTests
{
    public BatchReplaceLabelTests() => Loc.Configure(new StubStringProvider());

    private static string Label(BatchField field) =>
        new BatchReplaceMatchViewModel(7, field, "before", "after").FieldLabel;

    [Theory]
    [InlineData(BatchFieldKind.DefaultText,  "BatchReplace_Field_DefaultText")]
    [InlineData(BatchFieldKind.FemaleText,   "BatchReplace_Field_FemaleText")]
    [InlineData(BatchFieldKind.SpeakerGuid,  "BatchReplace_Field_SpeakerGuid")]
    [InlineData(BatchFieldKind.ListenerGuid, "BatchReplace_Field_ListenerGuid")]
    public void PlainFieldLabels_ComeFromResources(BatchFieldKind kind, string expectedKey)
    {
        Assert.Equal(expectedKey, Label(new BatchField(kind)));
    }

    [Fact]
    public void ScriptParamLabel_ComesFromResources()
    {
        // StubStringProvider returns the key itself, so string.Format leaves it
        // unchanged — what this pins is that the key is the one being looked up.
        Assert.Equal("BatchReplace_Field_ScriptParam",
            Label(new BatchField(BatchFieldKind.ScriptParam, ScriptCategory.Enter, 0, 1)));
    }

    [Fact]
    public void ConditionParamLabel_ComesFromResources()
    {
        Assert.Equal("BatchReplace_Field_ConditionParam",
            Label(new BatchField(BatchFieldKind.ConditionParam, Index: 2, ParamIndex: 3)));
    }
}

/// <summary>
/// Tier two: the real Strings.axaml through the real AvaloniaStringProvider, which
/// is what proves the keys exist and that their {0}/{1}/{2} holes are filled in the
/// order the ViewModel passes them. The row label also replaces a broken
/// StringFormat='Node {NodeId} — {FieldPath}' in BatchReplaceWindow.axaml, which
/// Avalonia's positional-only StringFormat could never have rendered.
/// </summary>
public class BatchReplaceLabelResourceEndToEndTests
{
    public BatchReplaceLabelResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    private static BatchReplaceMatchViewModel Match(BatchField field) =>
        new(7, field, "before", "after");

    [AvaloniaFact]
    public void PlainFieldLabels_UseRealResources()
    {
        Assert.Equal("Default Text",  Match(new BatchField(BatchFieldKind.DefaultText)).FieldLabel);
        Assert.Equal("Female Text",   Match(new BatchField(BatchFieldKind.FemaleText)).FieldLabel);
        Assert.Equal("Speaker GUID",  Match(new BatchField(BatchFieldKind.SpeakerGuid)).FieldLabel);
        Assert.Equal("Listener GUID", Match(new BatchField(BatchFieldKind.ListenerGuid)).FieldLabel);
    }

    [AvaloniaFact]
    public void ScriptParamLabel_UsesRealResourcesInArgumentOrder()
    {
        Assert.Equal("Script Enter[0] Param 1",
            Match(new BatchField(BatchFieldKind.ScriptParam, ScriptCategory.Enter, 0, 1)).FieldLabel);
    }

    [AvaloniaFact]
    public void ConditionParamLabel_UsesRealResourcesInArgumentOrder()
    {
        Assert.Equal("Condition[2] Param 3",
            Match(new BatchField(BatchFieldKind.ConditionParam, Index: 2, ParamIndex: 3)).FieldLabel);
    }

    [AvaloniaFact]
    public void RowLabel_CombinesNodeIdAndFieldLabel()
    {
        Assert.Equal("Node 7 \u2014 Default Text",
            Match(new BatchField(BatchFieldKind.DefaultText)).RowLabel);
    }
}
