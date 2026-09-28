using System.Text.Json.Nodes;
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

    // ── PoE2: coverage ──────────────────────────────────────────────────────────

    [Fact]
    public void Poe2_Fixture_CoversEveryConstruct()
    {
        using var game = FakePoe2Game.Canonical();
        var conv  = JsonNode.Parse(File.ReadAllText(game.ConvPath("canonical")))!["Conversations"]![0]!;
        var nodes = conv["Nodes"]!.AsArray().Select(n => n!).ToList();
        var shown = nodes.Where(n => n["NodeID"]!.GetValue<int>() >= 0).ToList();
        var links = nodes.SelectMany(n => n["Links"]!.AsArray().Select(l => l!)).ToList();
        string Type(JsonNode n) => n["$type"]!.GetValue<string>().Split(',')[0].Split('.')[^1];
        IEnumerable<JsonNode> Components(JsonNode? c) =>
            c?["Components"]?.AsArray().Select(x => x!).SelectMany(x => Components(x).Prepend(x)) ?? [];

        // The conversation-level script node, both where the bundle declares it and as the
        // hidden node -200 in Nodes, which the editor must carry through untouched (#115).
        Assert.NotNull(conv["ConversationScriptNode"]);
        Assert.Contains(nodes, n => n["NodeID"]!.GetValue<int>() == -200 && Type(n) == "ScriptNode");

        Assert.Equal(["BankNode", "PlayerResponseNode", "ScriptNode", "TalkNode", "TriggerConversationNode"],
            shown.Select(Type).Distinct().Order());
        Assert.Equal([0, 1, 2, 3],   // Hidden, Conversation, Bark, Overlay (#114)
            shown.Select(n => n["DisplayType"]?.GetValue<int>()).OfType<int>().Distinct().Order());
        Assert.Equal([0, 1, 2, 3],   // None, OnceEver, OncePerConversation, MarkAsRead (#114)
            shown.Select(n => n["Persistence"]?.GetValue<int>()).OfType<int>().Distinct().Order());
        Assert.Contains(shown, n => n["BankNodePlayType"]?.GetValue<int>() == 2);   // PlayRandom

        Assert.Contains(links, l => l["RandomWeight"]!.GetValue<int>() != 1);
        Assert.Equal([0, 1, 2], links.Select(l => l["QuestionNodeTextDisplay"]!.GetValue<int>()).Distinct().Order());
        Assert.Contains(links, l => l["Conditionals"]!["Components"]!.AsArray().Count > 0);

        var conditions = shown.SelectMany(n => Components(n["Conditionals"]))
            .Concat(links.SelectMany(l => Components(l["Conditionals"]))).ToList();
        Assert.Contains(conditions, c => Type(c) == "ConditionalExpression"
                                         && Components(c).Any(x => Type(x) == "ConditionalExpression"));
        Assert.Contains(conditions, c => c["Operator"]!.GetValue<int>() == 1);
        Assert.Contains(conditions, c => c["Not"]?.GetValue<bool>() == true);

        string[] lists = ["OnEnterScripts", "OnExitScripts", "OnUpdateScripts"];
        foreach (var list in lists)
            Assert.Contains(shown, n => n[list]!.AsArray().Count > 0);
        var scripts = nodes.SelectMany(n => lists.SelectMany(k => n[k]!.AsArray().Select(s => s!))).ToList();
        Assert.Contains(scripts, s => s["Conditional"]!["Components"]!.AsArray().Count > 0);   // #115
        Assert.All(scripts, s => Assert.NotNull(s["Data"]!["FunctionHash"]));

        // VO: a node with its own file (and female variant), and another aliasing it.
        Assert.Contains(shown, n => n["HasVO"]?.GetValue<bool>() == true);
        var alias = shown.Select(n => n["ExternalVO"]?.GetValue<string>()).Single(v => !string.IsNullOrEmpty(v))!;
        Assert.True(File.Exists(Path.Combine(game.VoDir, alias + ".wem")));
        Assert.True(File.Exists(Path.Combine(game.VoDir, alias + "_fem.wem")));

        AssertTextCoverage(game.Provider, game.CanonicalFile, ["de", "en"]);
    }

    [Fact]
    public void Poe2_Fixture_LoadsWithoutWarnings()
    {
        using var game = FakePoe2Game.Canonical();
        AssertLoadsCleanly(game.Provider, game.CanonicalFile);
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
            Assert.DoesNotContain("[WARN]", logged);
            Assert.DoesNotContain("[ERROR]", logged);

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
        // Shorter than before means AppLog rotated mid-load: everything in it is new.
        stream.Seek(stream.Length < from ? 0 : from, SeekOrigin.Begin);
        return new StreamReader(stream).ReadToEnd();
    }
}
