using System.Xml.Linq;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Logging;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.GameData;

/// <summary>
/// The canonical conversations (issue 122): one hand-written conversation per game that
/// exercises every construct the editor reads or writes. Fixtures/Canonical/README.md maps
/// each construct to its node ID. These tests keep the fixture honest (every construct is
/// really there), show it loads cleanly, and run the #117 round-trip checks over it.
/// </summary>
[Trait("Category", "Canonical")]
public class CanonicalConversationTests
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    // ── PoE1: coverage ──────────────────────────────────────────────────────────

    [Fact]
    public void Poe1_Fixture_CoversEveryConstruct()
    {
        using var game = new FakePoe1Game();
        var doc   = XDocument.Load(game.ConvPath());
        var nodes = doc.Descendants("FlowChartNode").ToList();
        var links = doc.Descendants("FlowChartLink").ToList();
        string Type(XElement e) => (string?)e.Attribute(Xsi + "type") ?? "";

        Assert.Equal(["BankNode", "PlayerResponseNode", "ScriptNode", "TalkNode", "TriggerConversationNode"],
            nodes.Select(Type).Distinct().Order());
        Assert.Equal(["Bark", "Conversation", "Hidden", "Overlay"],
            nodes.Select(n => (string?)n.Element("DisplayType")).OfType<string>().Distinct().Order());
        Assert.Equal(["MarkAsRead", "None", "OnceEver", "OncePerConversation"],
            nodes.Select(n => (string?)n.Element("Persistence")).OfType<string>().Distinct().Order());
        Assert.Contains(nodes, n => (string?)n.Element("BankNodePlayType") == "PlayRandom");

        // Links: optional elements present and absent (#112), and every non-default value.
        // PoE1 links have no Conditionals at all (#141); the enum names are the game's (#139).
        Assert.Contains(links, l => l.Element("RandomWeight") is null && l.Element("QuestionNodeTextDisplay") is null);
        Assert.Contains(links, l => (string?)l.Element("RandomWeight") is { } w && w != "1");
        Assert.Equal(["ShowAlways", "ShowNever", "ShowOnce"],
            links.Select(l => (string?)l.Element("QuestionNodeTextDisplay")).OfType<string>().Distinct().Order());
        Assert.All(links, l => Assert.Null(l.Element("Conditionals")));

        // Conditions: a nested expression holding both operators, and a negated call.
        Assert.Contains(doc.Descendants("ExpressionComponent"), e =>
            Type(e) == "ConditionalExpression"
            && e.Element("Components")!.Elements().Any(c => Type(c) == "ConditionalExpression"));
        var ops = doc.Descendants("ExpressionComponent").Select(e => (string?)e.Element("Operator")).ToHashSet();
        Assert.Contains("And", ops);
        Assert.Contains("Or", ops);
        Assert.Contains(doc.Descendants("ExpressionComponent"), e => (string?)e.Element("Not") == "true");

        // Scripts: all three lists used; ScriptCall written bare, as the game requires (#111).
        foreach (var list in new[] { "OnEnterScripts", "OnExitScripts", "OnUpdateScripts" })
            Assert.Contains(nodes, n => n.Element(list)?.HasElements == true);
        Assert.All(doc.Descendants("ScriptCall"), s => Assert.Null(s.Attribute(Xsi + "type")));

        AssertTextCoverage(game.Provider, game.File, ["de", "en"]);
    }

    // ── PoE1: loads cleanly ─────────────────────────────────────────────────────

    [Fact]
    public void Poe1_Fixture_LoadsWithoutWarnings()
    {
        using var game = new FakePoe1Game();
        AssertLoadsCleanly(game.Provider, game.File);
    }

    // ── Shared assertions ───────────────────────────────────────────────────────

    /// The node every fixture leaves without a stringtable entry, on purpose (README).
    private const int MissingStringNodeId = 9;

    private static void AssertTextCoverage(IGameDataProvider provider, ConversationFile file, string[] languages)
    {
        Assert.Equal(languages, provider.AvailableLanguages.Order());
        foreach (var lang in languages)
        {
            var entries = RoundTripChecks.StringTableEntries(File.ReadAllText(provider.GetStringTablePath(file, lang)));
            Assert.DoesNotContain(entries, e => e.Id == MissingStringNodeId);
            Assert.Contains(entries, e => !string.IsNullOrEmpty(e.FemaleText));
            Assert.Contains(entries, e => e.DefaultText?.Contains("[Player Name]") == true);
            Assert.Contains(entries, e => e.DefaultText?.Contains("<i>") == true);   // stored escaped as &lt;i&gt;
        }
    }

    /// Loads in every language with no WARN/ERROR logged, every link points at a node that
    /// exists, and every text-bearing node except the deliberate one has text.
    private static void AssertLoadsCleanly(IGameDataProvider provider, ConversationFile file)
    {
        foreach (var lang in provider.AvailableLanguages)
        {
            provider.Language = lang;
            var logBefore = LogLength();
            var conv      = provider.LoadConversation(file);
            var logged    = NewLogText(logBefore);
            Assert.DoesNotContain("WARN", logged);
            Assert.DoesNotContain("ERROR", logged);

            var ids = conv.Nodes.Select(n => n.NodeId).ToHashSet();
            Assert.All(conv.Nodes.SelectMany(n => n.Links), l => Assert.Contains(l.ToNodeId, ids));
            Assert.All(conv.Nodes.Where(n => n.SpeakerCategory != SpeakerCategory.Script && n.NodeId != MissingStringNodeId),
                n => Assert.False(string.IsNullOrEmpty(conv.Strings.Get(n.NodeId)?.DefaultText),
                                  $"{lang}: node {n.NodeId} has no text"));
            Assert.Null(conv.Strings.Get(MissingStringNodeId));
        }
    }

    private static long LogLength() =>
        File.Exists(AppLog.LogPath) ? new FileInfo(AppLog.LogPath).Length : 0;

    private static string NewLogText(long from)
    {
        if (!File.Exists(AppLog.LogPath)) return "";
        using var stream = new FileStream(AppLog.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        stream.Seek(Math.Min(from, stream.Length), SeekOrigin.Begin);
        return new StreamReader(stream).ReadToEnd();
    }
}
