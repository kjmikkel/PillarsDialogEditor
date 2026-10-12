using System.Xml.Linq;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// PoE1 checks on the written XML itself, for what the editor model does not carry
/// (issue 138, spec §3): untouched data, bank and trigger fields, link conditionals (#141),
/// and that both undo routes restore the fixture.
[Trait("Category", "Canonical")]
public sealed class Poe1CanonicalModRawChecks : IDisposable
{
    private static readonly CanonicalGame Game = CanonicalGame.Poe1;
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private readonly DeliveryPaths _paths = new();
    public void Dispose() => _paths.Dispose();

    private static string ConvDir(string root) =>
        Path.Combine(root, "PillarsOfEternity_Data", "data", "conversations", "canonical");
    private static string StDir(string root, string lang) =>
        Path.Combine(root, "PillarsOfEternity_Data", "data", "localized", lang, "text", "conversations", "canonical");
    private static XDocument Conversation(string root) =>
        XDocument.Load(Path.Combine(ConvDir(root), "canonical.conversation"));
    private static XElement NodeOf(string root, int id) =>
        Conversation(root).Descendants("FlowChartNode").Single(n => (int)n.Element("NodeID")! == id);

    private async Task<string> InstalledAsync()
    {
        var root = _paths.FreshInstall(Game.Id);
        await DeliveryPaths.ViaPatchManagerAsync(root, Game.Pack);
        return root;
    }

    /// A second conversation the mod never names: a copy of the canonical one and its
    /// stringtables, each in its own folder as PoE1 lays them out, added before the install.
    private static IReadOnlyList<string> AddUntouchedConversation(string root)
    {
        var copies = new List<string>();
        void Copy(string dir, string ext)
        {
            var dest = Path.Combine(Path.GetDirectoryName(dir)!, "untouched");
            Directory.CreateDirectory(dest);
            File.Copy(Path.Combine(dir, "canonical" + ext), Path.Combine(dest, "untouched" + ext));
            copies.Add(Path.Combine(dest, "untouched" + ext));
        }
        Copy(ConvDir(root), ".conversation");
        foreach (var lang in Game.Languages) Copy(StDir(root, lang), ".stringtable");
        return copies;
    }

    [Fact]
    public async Task UntouchedConversation_IsUnchanged()
    {
        var root      = _paths.FreshInstall(Game.Id);
        var untouched = AddUntouchedConversation(root);
        var before    = DeliveryPaths.GameFiles(root);
        await DeliveryPaths.ViaPatchManagerAsync(root, Game.Pack);
        var after     = DeliveryPaths.GameFiles(root);

        var compared = before.Where(kv => kv.Key.Contains("/untouched/")).ToList();
        Assert.Equal(untouched.Count, compared.Count);
        foreach (var (rel, hash) in compared)
            Assert.True(after.TryGetValue(rel, out var h) && h == hash, $"{rel} changed");
    }

    /// Every conversation the mod writes, the new one included: it goes through a different
    /// write path (a blank template, not an existing file).
    private static IReadOnlyList<XDocument> WrittenConversations(string root) =>
        [.. Game.Conversations.Select(c => XDocument.Load(Game.Provider(root).FindConversation(c)!.ConversationPath))];

    [Fact]
    public async Task NoLink_GainsConditionals()   // #141: PoE1's DialogueLink has none
    {
        var root          = await InstalledAsync();
        var conversations = WrittenConversations(root);
        Assert.Equal(2, conversations.Count);
        foreach (var conv in conversations)
        {
            Assert.NotEmpty(conv.Descendants("FlowChartLink"));   // otherwise the check below proves nothing
            Assert.DoesNotContain(conv.Descendants("FlowChartLink"), l => l.Element("Conditionals") is not null);
        }
    }

    [Fact]
    public async Task NewConversation_StartsAtNode0()
    {
        var root = await InstalledAsync();
        var conv = XDocument.Load(Game.Provider(root).FindConversation(Game.NewConversation)!.ConversationPath);
        Assert.Contains(conv.Descendants("FlowChartNode"), n => (int)n.Element("NodeID")! == 0);
    }

    private static List<int> BankChildren(string root) =>
        [.. NodeOf(root, 5).Element("ChildNodeIDs")!.Elements("int").Select(e => (int)e)];

    [Fact]
    public async Task Bank_KeepsItsRemainingChild()
        => Assert.Contains(8, BankChildren(await InstalledAsync()));

    [Fact(Skip = "#161: a deleted bank child stays in ChildNodeIDs")]
    public async Task Bank_DropsTheDeletedChild()
        => Assert.DoesNotContain(9, BankChildren(await InstalledAsync()));   // node 9 was deleted

    [Fact]
    public async Task BankPlayType_AndTriggerTarget_Survive()
    {
        var root    = await InstalledAsync();
        var fixture = CanonicalFixture.SourceDir(Game.Id);
        Assert.Equal((string?)NodeOf(fixture, 5).Element("BankNodePlayType"), (string?)NodeOf(root, 5).Element("BankNodePlayType"));
        foreach (var key in new[] { "ConversationFilename", "StartNodeID" })
            Assert.Equal((string?)NodeOf(fixture, 6).Element(key), (string?)NodeOf(root, 6).Element(key));
        Assert.Equal("TriggerConversationNode", (string?)NodeOf(root, 6).Attribute(Xsi + "type"));
    }

    [Fact]
    public async Task RemoveAllMods_RestoresTheFixtureExactly()
    {
        var root = await InstalledAsync();
        await DeliveryPaths.RemoveAllModsAsync(root);
        var diff = DeliveryPaths.Describe(DeliveryPaths.GameFiles(_paths.FreshInstall(Game.Id)), DeliveryPaths.GameFiles(root));
        Assert.True(diff is null, diff);
    }

    [Fact]
    public async Task TestPatchRestore_RestoresTheFixtureExactly()
    {
        var root = _paths.FreshInstall(Game.Id);
        await DeliveryPaths.ViaTestPatchAndRestoreAsync(Game, root);
        var diff = DeliveryPaths.Describe(DeliveryPaths.GameFiles(_paths.FreshInstall(Game.Id)), DeliveryPaths.GameFiles(root));
        Assert.True(diff is null, diff);
    }
}
