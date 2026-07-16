using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

public class IgnoredDuplicateTests
{
    private static IgnoredDuplicate Exact(string key) =>
        new(DuplicateKind.Exact, [key], key);

    [Fact]
    public void WithIgnoredDuplicate_AddsEntry()
    {
        var p = DialogProject.Empty("P").WithIgnoredDuplicate(Exact("the wind howls tonight"));
        Assert.Single(p.IgnoredDuplicates!);
        Assert.Equal("the wind howls tonight", p.IgnoredDuplicates![0].Keys[0]);
    }

    [Fact]
    public void WithIgnoredDuplicate_DedupesOnKindAndKeys()
    {
        var p = DialogProject.Empty("P")
            .WithIgnoredDuplicate(Exact("same"))
            .WithIgnoredDuplicate(Exact("same"));
        Assert.Single(p.IgnoredDuplicates!);
    }

    [Fact]
    public void WithoutIgnoredDuplicate_RemovesMatching_AndNullsWhenEmpty()
    {
        var p = DialogProject.Empty("P").WithIgnoredDuplicate(Exact("gone"));
        var p2 = p.WithoutIgnoredDuplicate(Exact("gone"));
        Assert.Null(p2.IgnoredDuplicates);
    }

    [Fact]
    public void Serialization_RoundTrips_IgnoredDuplicates()
    {
        var near = new IgnoredDuplicate(DuplicateKind.Near, ["line a text here", "line b text here"], "«a» ~ «b»");
        var p = DialogProject.Empty("P").WithIgnoredDuplicate(near);

        var json = DialogProjectSerializer.Serialize(p);
        var back = DialogProjectSerializer.Deserialize(json);

        var entry = Assert.Single(back.IgnoredDuplicates!);
        Assert.Equal(DuplicateKind.Near, entry.Kind);
        Assert.Equal(["line a text here", "line b text here"], entry.Keys);
        Assert.Equal("«a» ~ «b»", entry.DisplayText);
    }

    [Fact]
    public void Deserialize_OldFileWithoutField_LoadsAsNull()
    {
        // A project JSON saved before this field existed omits the property entirely.
        const string oldJson = """{ "Name": "P", "SchemaVersion": 1, "Patches": {} }""";
        var back = DialogProjectSerializer.Deserialize(oldJson);
        Assert.Null(back.IgnoredDuplicates);
    }
}
