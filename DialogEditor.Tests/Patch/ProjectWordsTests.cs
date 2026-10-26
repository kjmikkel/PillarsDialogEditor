using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// GitHub issue 9: a per-project spelling word list, saved with the project so a mod's
/// invented names travel with it. Editor metadata only, never in game files.
public class ProjectWordsTests
{
    [Fact]
    public void Empty_HasNoProjectWords()
        => Assert.Null(DialogProject.Empty("P").ProjectWords);

    [Fact]
    public void WithProjectWord_AddsTheTrimmedWord()
    {
        var p = DialogProject.Empty("P").WithProjectWord("  Xoti ");
        Assert.Equal(["Xoti"], p.ProjectWords);
    }

    [Fact]
    public void WithProjectWord_SameWordTwice_IsANoOp()
    {
        var once = DialogProject.Empty("P").WithProjectWord("Xoti");
        Assert.Same(once, once.WithProjectWord("Xoti"));
    }

    [Fact]
    public void WithProjectWord_Blank_IsANoOp()
    {
        var p = DialogProject.Empty("P");
        Assert.Same(p, p.WithProjectWord("   "));
    }

    [Fact]
    public void WithProjectWord_CaseDiffers_KeepsBoth()
    {
        // The checker is case-tolerant one way only ("adra" also accepts "Adra", but "Xoti"
        // does not accept "xoti"), matching the user dictionary, so both are worth keeping.
        var p = DialogProject.Empty("P").WithProjectWord("Xoti").WithProjectWord("xoti");
        Assert.Equal(["Xoti", "xoti"], p.ProjectWords);
    }

    [Fact]
    public void MergeWith_UnionsProjectWords()
    {
        var a = DialogProject.Empty("A").WithProjectWord("Xoti").WithProjectWord("Rauataian");
        var b = DialogProject.Empty("B").WithProjectWord("Rauataian").WithProjectWord("Ondra");
        Assert.Equal(["Xoti", "Rauataian", "Ondra"], a.MergeWith(b).ProjectWords);
    }

    [Fact]
    public void MergeWith_NeitherHasWords_StaysNull()
        => Assert.Null(DialogProject.Empty("A").MergeWith(DialogProject.Empty("B")).ProjectWords);

    [Fact]
    public void Serializer_RoundTripsProjectWords()
    {
        var p = DialogProject.Empty("P").WithProjectWord("Xoti");
        var back = DialogProjectSerializer.Deserialize(DialogProjectSerializer.Serialize(p));
        Assert.Equal(["Xoti"], back.ProjectWords);
    }

    [Fact]
    public void Deserialize_ProjectSavedBeforeTheField_LoadsWithNoWords()
    {
        const string json = """{ "Name": "Old", "SchemaVersion": 1, "Patches": {} }""";
        Assert.Null(DialogProjectSerializer.Deserialize(json).ProjectWords);
    }
}
