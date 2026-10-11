using System.Text.Json.Nodes;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Checks on the written files themselves, for what the editor model does not carry
/// (issue 138, spec §3): untouched data, #116 property sets, bank and trigger fields, #115
/// per-script conditionals, VO placement, and that both undo routes restore the fixture.
[Trait("Category", "Canonical")]
public sealed class CanonicalModRawChecks : IDisposable
{
    private readonly DeliveryPaths _paths = new();
    public void Dispose() => _paths.Dispose();

    private static string Pack    => Path.Combine(CanonicalModBuilder.GoldenDir("poe2"), CanonicalModBuilder.ProjectName + ".dialogpack");
    private static string Project => Path.Combine(CanonicalModBuilder.GoldenDir("poe2"), CanonicalModBuilder.ProjectName + ".dialogproject");
    private static string Fixture => CanonicalFixture.SourceDir("poe2");

    private static string BundlePath(string root, string conv) =>
        Path.Combine(root, "PillarsOfEternityII_Data", "exported", "design", "conversations", conv + ".conversationbundle");
    private static JsonObject Conversation(string root, string conv) =>
        JsonNode.Parse(File.ReadAllText(BundlePath(root, conv)))!["Conversations"]![0]!.AsObject();
    private static JsonObject NodeOf(JsonObject conv, int id) =>
        conv["Nodes"]!.AsArray().Single(n => n!["NodeID"]!.GetValue<int>() == id)!.AsObject();
    private static string TypeOf(JsonNode node) => node["$type"]!.GetValue<string>().Split(',')[0];

    private async Task<string> InstalledAsync()
    {
        var root = _paths.FreshInstall("poe2");
        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);
        return root;
    }

    [Fact]
    public async Task NodeMinus200_AndUntouchedFiles_AreUnchanged()
    {
        var root   = await InstalledAsync();
        var before = DeliveryPaths.GameFiles(_paths.FreshInstall("poe2"));
        var after  = DeliveryPaths.GameFiles(root);
        static bool ModOwned(string rel) =>
            rel.Contains("/canonical.") || rel.Contains("/canonical_mod_new.") || rel.Contains("/narrator/canonical_0011");
        foreach (var (rel, hash) in before.Where(kv => !ModOwned(kv.Key)))
            Assert.True(after.TryGetValue(rel, out var h) && h == hash, $"{rel} changed");

        var fixture = Conversation(Fixture, "canonical");
        var written = Conversation(root, "canonical");
        Assert.True(JsonNode.DeepEquals(fixture["ConversationScriptNode"], written["ConversationScriptNode"]));
        Assert.True(JsonNode.DeepEquals(NodeOf(fixture, -200), NodeOf(written, -200)));
    }

    // #116: the oracle is the hand-written fixture, which covers every node type; the
    // product's own Poe2NodeProperties table is not used, since checking it against itself
    // would prove nothing.
    [Fact]
    public async Task EveryWrittenNode_CarriesOnlyPropertiesItsTypeHasInTheFixture()
    {
        var oracle = Conversation(Fixture, "canonical")["Nodes"]!.AsArray()
            .GroupBy(n => TypeOf(n!))
            .ToDictionary(g => g.Key, g => g.SelectMany(n => n!.AsObject().Select(kv => kv.Key)).ToHashSet());
        var root     = await InstalledAsync();
        var problems = new List<string>();
        foreach (var conv in Poe2Scenario.Conversations)
            foreach (var node in Conversation(root, conv)["Nodes"]!.AsArray())
            {
                var type = TypeOf(node!);
                if (!oracle.TryGetValue(type, out var allowed)) { problems.Add($"{conv}: {type} not in the fixture"); continue; }
                foreach (var key in node!.AsObject().Select(kv => kv.Key).Where(k => !allowed.Contains(k)))
                    problems.Add($"{conv} node {node["NodeID"]}: {type} has '{key}'");
            }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }

    [Fact(Skip = "#161: a deleted bank child stays in ChildNodeIDs")]
    public async Task BankChildren_DropTheDeletedNode()
    {
        var root = await InstalledAsync();
        Assert.Equal([8], NodeOf(Conversation(root, "canonical"), 5)["ChildNodeIDs"]!.AsArray().Select(n => n!.GetValue<int>()));   // 9 was deleted
    }

    [Fact]
    public async Task BankPlayType_AndTriggerTarget_Survive()
    {
        var root    = await InstalledAsync();
        var fixture = Conversation(Fixture, "canonical");
        var written = Conversation(root, "canonical");
        Assert.Equal(NodeOf(fixture, 5)["BankNodePlayType"]!.GetValue<int>(), NodeOf(written, 5)["BankNodePlayType"]!.GetValue<int>());
        foreach (var key in new[] { "ConversationGuid", "StartNodeID" })
            Assert.True(JsonNode.DeepEquals(NodeOf(fixture, 6)[key], NodeOf(written, 6)[key]), key);
    }

    [Fact]
    public async Task Node4_ExistingScript_KeepsItsConditional()   // #115
    {
        var root     = await InstalledAsync();
        var original = NodeOf(Conversation(Fixture, "canonical"), 4)["OnEnterScripts"]![0]!;
        Assert.NotNull(original["Conditional"]);   // the fixture really has one to keep
        var kept = NodeOf(Conversation(root, "canonical"), 4)["OnEnterScripts"]!.AsArray()
            .Single(sc => JsonNode.DeepEquals(sc!["Data"], original["Data"]));
        Assert.True(JsonNode.DeepEquals(original["Conditional"], kept!["Conditional"]));
    }

    [Fact]
    public async Task ImportedVo_IsWhereTheGameLooks_AndTheAliasResolves()
    {
        var root   = await InstalledAsync();
        var voices = Path.Combine(root, "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Assert.Equal(File.ReadAllBytes(CanonicalModInputs.Poe2Wem), File.ReadAllBytes(Path.Combine(voices, "narrator", "canonical_0011.wem")));
        Assert.True(File.Exists(Path.Combine(voices, "narrator", "canonical_0007.wem")));   // node 10's alias target
    }

    [Fact]
    public async Task RemoveAllMods_RestoresTheFixtureExactly()
    {
        var root = await InstalledAsync();
        await DeliveryPaths.RemoveAllModsAsync(root);
        var diff = DeliveryPaths.Describe(DeliveryPaths.GameFiles(_paths.FreshInstall("poe2")), DeliveryPaths.GameFiles(root));
        Assert.True(diff is null, diff);
    }

    [Fact]
    public async Task TestPatchRestore_RestoresTheFixtureExactly()
    {
        var root = _paths.FreshInstall("poe2");
        await _paths.ViaTestPatchAndRestoreAsync(root, Project);
        var diff = DeliveryPaths.Describe(DeliveryPaths.GameFiles(_paths.FreshInstall("poe2")), DeliveryPaths.GameFiles(root));
        Assert.True(diff is null, diff);
    }
}
