using System.Text.Json.Nodes;
using DialogEditor.Patch;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Tests.Patch.Schema;

public class SchemaMigratorTests
{
    /// Test-only step: records that it ran by appending its FromVersion to "Trail".
    private sealed class TrailStep(SchemaFileKind kind, int from) : ISchemaMigrationStep
    {
        public SchemaFileKind Kind => kind;
        public int FromVersion => from;
        public void Apply(JsonObject root)
        {
            var trail = root["Trail"] as JsonArray ?? [];
            trail.Add(from);
            root["Trail"] = trail;
        }
    }

    private static readonly Dictionary<SchemaFileKind, int> Four = new()
    {
        [SchemaFileKind.Project] = 4, [SchemaFileKind.ConversationPatch] = 4, [SchemaFileKind.PatchList] = 4,
    };

    private static SchemaMigrator ChainOf(SchemaFileKind kind) =>
        new([new TrailStep(kind, 1), new TrailStep(kind, 2), new TrailStep(kind, 3)], Four);

    private static JsonObject Doc(int version) => new() { ["SchemaVersion"] = version };

    [Fact]
    public void OlderFile_RunsEveryStepInOrder_AndEndsAtCurrent()
    {
        var root = Doc(1);
        ChainOf(SchemaFileKind.PatchList).Migrate(root, SchemaFileKind.PatchList);

        Assert.Equal([1, 2, 3], root["Trail"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(4, root["SchemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void FileInTheMiddle_RunsOnlyTheRemainingSteps()
    {
        var root = Doc(3);
        ChainOf(SchemaFileKind.PatchList).Migrate(root, SchemaFileKind.PatchList);

        Assert.Equal([3], root["Trail"]!.AsArray().Select(n => n!.GetValue<int>()));
    }

    [Fact]
    public void CurrentFile_IsUntouched()
    {
        var root = Doc(4);
        ChainOf(SchemaFileKind.PatchList).Migrate(root, SchemaFileKind.PatchList);

        Assert.Equal("""{"SchemaVersion":4}""", root.ToJsonString());
    }

    [Fact]
    public void StepsForOtherKinds_AreIgnored()
    {
        var root = Doc(4);
        new SchemaMigrator([new TrailStep(SchemaFileKind.Project, 3)], Four)
            .Migrate(root, SchemaFileKind.PatchList);

        Assert.Null(root["Trail"]);
        Assert.Equal(4, root["SchemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void NewerFile_Throws_WithKindAndVersions()
    {
        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() =>
            ChainOf(SchemaFileKind.PatchList).Migrate(Doc(5), SchemaFileKind.PatchList));

        Assert.Equal(SchemaFileKind.PatchList, ex.Kind);
        Assert.Equal(5, ex.Found);
        Assert.Equal(4, ex.Supported);
        Assert.Null(ex.ConversationName);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"SchemaVersion":0}""")]
    [InlineData("""{"SchemaVersion":-3}""")]
    [InlineData("""{"SchemaVersion":"two"}""")]
    public void MissingOrInvalidVersion_IsRejectedAsInvalidData_NotMigrated(string json)
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ChainOf(SchemaFileKind.PatchList).Migrate(JsonNode.Parse(json)!.AsObject(), SchemaFileKind.PatchList));

        Assert.IsNotType<UnsupportedSchemaVersionException>(ex);
    }

    [Fact]
    public void Project_MigratesEveryNestedPatch()
    {
        var root = new JsonObject
        {
            ["SchemaVersion"] = 4,
            ["Patches"] = new JsonObject { ["a"] = Doc(2), ["b"] = Doc(4) },
        };
        ChainOf(SchemaFileKind.ConversationPatch).Migrate(root, SchemaFileKind.Project);

        var a = root["Patches"]!["a"]!;
        Assert.Equal([2, 3], a["Trail"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(4, a["SchemaVersion"]!.GetValue<int>());
        Assert.Null(root["Patches"]!["b"]!["Trail"]);
    }

    [Fact]
    public void Project_NewerNestedPatch_Throws_NamingTheConversation()
    {
        var root = new JsonObject
        {
            ["SchemaVersion"] = 4,
            ["Patches"] = new JsonObject { ["greeting"] = Doc(9) },
        };

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() =>
            ChainOf(SchemaFileKind.ConversationPatch).Migrate(root, SchemaFileKind.Project));

        Assert.Equal(SchemaFileKind.ConversationPatch, ex.Kind);
        Assert.Equal(9, ex.Found);
        Assert.Equal("greeting", ex.ConversationName);
    }

    [Fact]
    public void Project_NewerRoot_IsRefusedBeforeLookingAtPatches()
    {
        var root = new JsonObject { ["SchemaVersion"] = 5, ["Patches"] = "not even an object" };

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() =>
            ChainOf(SchemaFileKind.ConversationPatch).Migrate(root, SchemaFileKind.Project));

        Assert.Equal(SchemaFileKind.Project, ex.Kind);
    }

    // ── The real registry (#104 invariants) ──────────────────────────────

    [Fact]
    public void RealRegistry_HasExactlyOneStepPerVersion_NoGaps()
        => Assert.Empty(SchemaMigrator.FindRegistryProblems(SchemaMigrator.RegisteredSteps, SchemaMigrator.CurrentVersions));

    [Fact]
    public void RealCurrentVersions_MatchTheModelConstants()
    {
        Assert.Equal(DialogProject.CurrentSchemaVersion,     SchemaMigrator.CurrentVersions[SchemaFileKind.Project]);
        Assert.Equal(ConversationPatch.CurrentSchemaVersion, SchemaMigrator.CurrentVersions[SchemaFileKind.ConversationPatch]);
        Assert.Equal(PatchList.CurrentSchemaVersion,         SchemaMigrator.CurrentVersions[SchemaFileKind.PatchList]);
    }

    [Fact]
    public void FindRegistryProblems_ReportsAGap()
    {
        var problems = SchemaMigrator.FindRegistryProblems(
            [new TrailStep(SchemaFileKind.PatchList, 1), new TrailStep(SchemaFileKind.PatchList, 3)],
            new Dictionary<SchemaFileKind, int> { [SchemaFileKind.PatchList] = 4 });

        Assert.Contains(problems, p => p.Contains("PatchList") && p.Contains('2'));
    }

    [Fact]
    public void FindRegistryProblems_ReportsADuplicate_AndAStepAtOrPastCurrent()
    {
        var problems = SchemaMigrator.FindRegistryProblems(
            [new TrailStep(SchemaFileKind.PatchList, 1), new TrailStep(SchemaFileKind.PatchList, 1),
             new TrailStep(SchemaFileKind.PatchList, 2)],
            new Dictionary<SchemaFileKind, int> { [SchemaFileKind.PatchList] = 2 });

        Assert.Equal(2, problems.Count);   // duplicate v1, and v2 step with current = 2
    }

    [Fact]
    public void RealPatchStep_1To2_KeepsContent_AndSetsVersion2()
    {
        var root = new JsonObject
        {
            ["ConversationName"] = "c", ["SchemaVersion"] = 1,
            ["AddedNodes"] = new JsonArray(), ["DeletedNodeIds"] = new JsonArray(), ["ModifiedNodes"] = new JsonArray(),
        };
        var before = root.DeepClone();
        before["SchemaVersion"] = 2;

        SchemaMigrator.Default.Migrate(root, SchemaFileKind.ConversationPatch);

        Assert.True(JsonNode.DeepEquals(before, root));
    }
}
