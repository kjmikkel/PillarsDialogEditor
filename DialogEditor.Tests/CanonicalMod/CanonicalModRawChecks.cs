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

    private static string Pack => CanonicalGame.Poe2.Pack;
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

    /// A second conversation the mod never names: a copy of the canonical bundle and its
    /// stringtables under another name, added before the install. The fixture has only the
    /// one conversation, so without this the check would compare nothing but VO files.
    private static IReadOnlyList<string> AddUntouchedConversation(string root)
    {
        var copies = new List<string>();
        foreach (var lang in new[] { "en", "de" })
        {
            var st = Path.Combine(root, "PillarsOfEternityII_Data", "exported", "localized", lang, "text", "conversations");
            File.Copy(Path.Combine(st, "canonical.stringtable"), Path.Combine(st, "untouched.stringtable"));
            copies.Add(Path.Combine(st, "untouched.stringtable"));
        }
        File.Copy(BundlePath(root, "canonical"), BundlePath(root, "untouched"));
        copies.Add(BundlePath(root, "untouched"));
        return copies;
    }

    [Fact]
    public async Task NodeMinus200_AndUntouchedFiles_AreUnchanged()
    {
        var root      = _paths.FreshInstall("poe2");
        var untouched = AddUntouchedConversation(root);
        var before    = DeliveryPaths.GameFiles(root);
        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);
        var after     = DeliveryPaths.GameFiles(root);

        static bool ModOwned(string rel) =>
            rel.Contains("/canonical.") || rel.Contains("/canonical_mod_new.") || rel.Contains("/narrator/canonical_0011");
        var compared = before.Where(kv => !ModOwned(kv.Key)).ToList();
        Assert.Equal(untouched.Count + 2, compared.Count);   // the three untouched files + node 7's two .wem
        foreach (var (rel, hash) in compared)
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

    private static List<int> BankChildren(string root) =>
        [.. NodeOf(Conversation(root, "canonical"), 5)["ChildNodeIDs"]!.AsArray().Select(n => n!.GetValue<int>())];

    [Fact]
    public async Task Bank_KeepsItsRemainingChild()
        => Assert.Contains(8, BankChildren(await InstalledAsync()));

    [Fact]
    public async Task Bank_DropsTheDeletedChild()
        => Assert.DoesNotContain(9, BankChildren(await InstalledAsync()));   // node 9 was deleted

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
        var alias = NodeOf(Conversation(root, "canonical"), 10)["ExternalVO"]!.GetValue<string>();
        Assert.False(string.IsNullOrEmpty(alias));   // the fixture's node 10 has none, so this is the mod's
        Assert.True(File.Exists(Path.Combine(voices, alias.Replace('/', Path.DirectorySeparatorChar) + ".wem")), alias);
    }

    [Fact]
    public async Task NewConversation_HasTheConversationLevelProperties()
    {
        var root     = await InstalledAsync();
        var expected = Conversation(Fixture, "canonical").Select(kv => kv.Key).Order(StringComparer.Ordinal);
        var actual   = Conversation(root, Poe2Scenario.NewConversation).Select(kv => kv.Key).Order(StringComparer.Ordinal);
        Assert.Equal(expected, actual);
    }

    [Fact]
    public async Task NewConversation_StartsAtNode0()
    {
        var root = await InstalledAsync();
        Assert.Contains(Conversation(root, Poe2Scenario.NewConversation)["Nodes"]!.AsArray(), n => n!["NodeID"]!.GetValue<int>() == 0);
    }

    // Issue #162: Deadfire finds a conversation only through a manifest entry, by the file
    // name of its Filename, and loads it by the entry's ID.
    [Fact]
    public async Task NewConversation_IsRegisteredUnderItsBundleId()
    {
        var root     = await InstalledAsync();
        var path     = CanonicalGame.Poe2.Provider(root).ConversationManifestPath(Poe2Scenario.NewConversation)!;
        var entry    = Assert.Single(JsonNode.Parse(File.ReadAllText(path))!["Entries"]!.AsArray())!;
        Assert.Equal(Conversation(root, Poe2Scenario.NewConversation)["ID"]!.GetValue<string>(), entry["ID"]!.GetValue<string>());
        Assert.Equal(Poe2Scenario.NewConversation, Path.GetFileNameWithoutExtension(entry["Filename"]!.GetValue<string>()));
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
        await DeliveryPaths.ViaTestPatchAndRestoreAsync(CanonicalGame.Poe2, root);
        var diff = DeliveryPaths.Describe(DeliveryPaths.GameFiles(_paths.FreshInstall("poe2")), DeliveryPaths.GameFiles(root));
        Assert.True(diff is null, diff);
    }
}
