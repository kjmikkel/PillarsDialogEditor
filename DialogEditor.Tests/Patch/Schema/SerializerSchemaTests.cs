using DialogEditor.Patch;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Tests.Patch.Schema;

public class SerializerSchemaTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Patch", "Schema", "Fixtures", name));

    [Fact]
    public void Project_NewerVersion_IsRefused()
    {
        var json = DialogProjectSerializer.Serialize(DialogProject.Empty("P"))
            .Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 2");

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => DialogProjectSerializer.Deserialize(json));
        Assert.Equal(SchemaFileKind.Project, ex.Kind);
    }

    [Fact]
    public void Project_NewerVersion_IsRefusedEvenWhenItsShapeWouldNotParse()
    {
        // A future format might change a field's type; the version check must win over a JSON type error.
        const string json = """{ "Name": 42, "SchemaVersion": 7, "Patches": [ "future shape" ] }""";

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => DialogProjectSerializer.Deserialize(json));
        Assert.Equal(7, ex.Found);
    }

    [Fact]
    public void Project_WithNewerNestedPatch_IsRefused_NamingTheConversation()
    {
        var project = DialogProject.Empty("P")
            .WithPatch(new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [7], []));
        var json = DialogProjectSerializer.Serialize(project)
            .Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 3");

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => DialogProjectSerializer.Deserialize(json));
        Assert.Equal(SchemaFileKind.ConversationPatch, ex.Kind);
        Assert.Equal("greeting", ex.ConversationName);
    }

    [Fact]
    public void Project_MissingVersion_IsInvalidData()
        => Assert.Throws<InvalidDataException>(() =>
            DialogProjectSerializer.Deserialize("""{ "Name": "P", "Patches": {} }"""));

    [Fact]
    public void Project_CurrentVersion_RoundTripsByteIdentical()
    {
        var project = DialogProject.Empty("P")
            .WithPatch(new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [7], []))
            .WithNewConversation("brand_new");
        var json = DialogProjectSerializer.Serialize(project);

        Assert.Equal(json, DialogProjectSerializer.Serialize(DialogProjectSerializer.Deserialize(json)));
    }

    [Fact]
    public void Fixture_V1ProjectWithV1Patch_OpensAtCurrentVersions()
    {
        var project = DialogProjectSerializer.Deserialize(Fixture("project-v1-patch-v1.dialogproject"));

        Assert.Equal("LegacyV1", project.Name);
        Assert.Equal(DialogProject.CurrentSchemaVersion, project.SchemaVersion);
        var patch = project.Patches["greeting"];
        Assert.Equal(ConversationPatch.CurrentSchemaVersion, patch.SchemaVersion);
        Assert.Equal([7], patch.DeletedNodeIds);
        Assert.Empty(patch.Translations);
    }

    [Fact]
    public void Patch_NewerVersion_IsRefused()
    {
        var json = PatchSerializer.Serialize(new ConversationPatch("c", ConversationPatch.CurrentSchemaVersion, [], [], []))
            .Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 3");

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => PatchSerializer.Deserialize(json));
        Assert.Equal(SchemaFileKind.ConversationPatch, ex.Kind);
    }

    [Fact]
    public void Patch_V1_IsMigratedToCurrent()
    {
        var json = PatchSerializer.Serialize(new ConversationPatch("c", 1, [], [3], []));

        Assert.Equal(ConversationPatch.CurrentSchemaVersion, PatchSerializer.Deserialize(json).SchemaVersion);
    }

    [Fact]
    public void PatchList_NewerVersion_IsRefused()
    {
        const string json = """{ "SchemaVersion": 2, "GameFolder": "", "Entries": [] }""";

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => PatchListSerializer.Deserialize(json));
        Assert.Equal(SchemaFileKind.PatchList, ex.Kind);
    }

    [Fact]
    public void PatchList_LoadFromFile_UsesTheSameCheck()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schema_{Guid.NewGuid():N}.patchlist");
        File.WriteAllText(path, """{ "SchemaVersion": 2, "GameFolder": "", "Entries": [] }""");
        try
        {
            Assert.Throws<UnsupportedSchemaVersionException>(() => PatchListSerializer.LoadFromFile(path));
        }
        finally { try { File.Delete(path); } catch (Exception) { /* best-effort */ } }
    }
}
