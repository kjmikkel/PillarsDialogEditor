using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138, spec §4: the files the in-game run copies into a real Deadfire install so the
/// game can reach the canonical conversation. They are hand-written, so these tests tie
/// them to the fixture and the mod they serve.
[Trait("Category", "Canonical")]
public class InGameSetupTests
{
    private static string Overlay => Path.Combine(AppContext.BaseDirectory, "Fixtures", "CanonicalMod", "poe2-ingame",
                                                  "PillarsOfEternityII_Data", "exported", "design");
    private static string Manifest => Path.Combine(Overlay, "manifests", "canonical_conversations.manifest");
    private static string Globals  => Path.Combine(Overlay, "globalvariables", "canonical.globalvariablesbundle");
    private static string FixtureBundle => Path.Combine(CanonicalFixture.SourceDir("poe2"),
        "PillarsOfEternityII_Data", "exported", "design", "conversations", "canonical.conversationbundle");

    // ConversationDataManager loads design/manifests/*conversations.manifest and resolves a
    // typed name by the entry's file name; LoadConversationData then finds the bundle by ID.
    [Fact]
    public void Manifest_RegistersTheCanonicalBundle_UnderItsOwnId()
    {
        Assert.Matches(new Regex("conversations\\.manifest$"), Path.GetFileName(Manifest));
        var entry = Assert.Single(JsonNode.Parse(File.ReadAllText(Manifest))!["Entries"]!.AsArray())!;
        var id    = JsonNode.Parse(File.ReadAllText(FixtureBundle))!["Conversations"]![0]!["ID"]!.GetValue<string>();
        Assert.Equal(id, entry["ID"]!.GetValue<string>());
        Assert.Equal("/conversations/canonical.conversationbundle", entry["Filename"]!.GetValue<string>());
        Assert.StartsWith("OEIFormats.Tools.ManifestData+ConversationManifestEntry", entry["$type"]!.GetValue<string>());
    }

    // Deadfire finds a conversation's stringtable from the bundle's inner Filename, not from
    // where the bundle sits (Conversation.GetStringTableName): the part after "conversations/",
    // with .stringtable, under localized/<lang>/text/conversations. The editor uses the
    // bundle's location instead, so a mismatch passes every editor test and fails in the game.
    [Theory]
    [InlineData("en")]
    [InlineData("de")]
    public void FixtureStringTable_IsWhereTheGameLooks(string lang)
    {
        var filename = JsonNode.Parse(File.ReadAllText(FixtureBundle))!["Conversations"]![0]!["Filename"]!.GetValue<string>();
        const string marker = "conversations/";
        var at       = filename.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        Assert.True(at >= 0, filename);
        var relative = Path.ChangeExtension(filename[(at + marker.Length)..], ".stringtable");
        var expected = Path.Combine(CanonicalFixture.SourceDir("poe2"), "PillarsOfEternityII_Data", "exported", "localized",
                                    lang, "text", "conversations", relative.Replace('/', Path.DirectorySeparatorChar));
        Assert.True(File.Exists(expected), $"the game looks for {relative} (from Filename '{filename}')");
    }

    /// Every global the fixture and the PoE2 mod read or write: the first parameter of each
    /// *GlobalValue call, found in the raw JSON so the check shares no code with the editor.
    private static SortedSet<string> GlobalsUsed()
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in new[] { FixtureBundle, CanonicalGame.Poe2.Project })
            foreach (var call in DescendantObjects(JsonNode.Parse(File.ReadAllText(path))!)
                         .Where(o => o["FullName"] is JsonValue v && v.GetValue<string>().Contains("GlobalValue(")))
                used.Add(call["Parameters"]![0]!.GetValue<string>());
        return used;
    }

    private static IEnumerable<JsonObject> DescendantObjects(JsonNode node)
    {
        switch (node)
        {
            case JsonObject obj:
                yield return obj;
                foreach (var (_, child) in obj)
                    if (child is not null)
                        foreach (var o in DescendantObjects(child)) yield return o;
                break;
            case JsonArray arr:
                foreach (var child in arr)
                    if (child is not null)
                        foreach (var o in DescendantObjects(child)) yield return o;
                break;
        }
    }

    [Fact]
    public void GlobalsBundle_DeclaresEveryGlobalTheFixtureAndTheModUse()
    {
        var declared = JsonNode.Parse(File.ReadAllText(Globals))!["GlobalVariablesSets"]![0]!["GlobalVariables"]!.AsArray()
            .Select(g => g!["Tag"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var used = GlobalsUsed();
        Assert.NotEmpty(used);
        Assert.Empty(used.Where(g => !declared.Contains(g)));
    }
}
