using System.Reflection;
using System.Text;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Xml.Serialization;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;
using DialogEditor.Patch;
using Xunit.Abstractions;

namespace DialogEditor.Tests.GameData;

/// <summary>
/// Opt-in fidelity check against a real game install (issue 117): every shipped
/// conversation and stringtable is saved unchanged, in memory, through the same
/// serializers F5, dialog-patcher and the Patch Manager use, and must come back intact.
/// </summary>
/// <remarks>
/// Runs only when DIALOGEDITOR_POE1_DIR / DIALOGEDITOR_POE2_DIR name an install
/// (<see cref="GameInstallFactAttribute"/>); see docs/game-data-tests.md. Nothing is ever
/// written to disk. The checks, per game, with no allow-list:
/// <list type="bullet">
/// <item>the editor's model of every conversation is identical after the save;</item>
/// <item>PoE1: the saved XML is equivalent to the original in the game's own model —
///   both are deserialized with XmlSerializer over OEIFormats.ConversationData, loaded
///   from the install, and re-serialized. Formatting (&lt;X /&gt; vs &lt;X&gt;&lt;/X&gt;)
///   and elements the game ignores drop out; anything the game would see fails;</item>
/// <item>PoE2: the saved JSON is identical to the original. The game's bundle parser needs
///   Unity, and exact equality is the stronger check anyway;</item>
/// <item>every stringtable in every installed language keeps every entry.</item>
/// </list>
/// These found #111–#116 in the shipped data; they are the regression net for them.
/// </remarks>
[Trait("Category", "GameData")]
public class ShippedConversationRoundTripTests(ITestOutputHelper output)
{
    private const int MaxReported = 25;

    // ── PoE1 ────────────────────────────────────────────────────────────────────

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void Poe1_UnchangedSave_KeepsTheEditorModel() =>
        AssertEveryConversation(Poe1(), (provider, file) =>
        {
            var original = File.ReadAllText(file.ConversationPath);
            var saved    = Poe1ConversationSerializer.Serialize(original, UnchangedSnapshot(provider, file));
            return ModelDifference(Poe1ConversationParser.ParseXml(original), Poe1ConversationParser.ParseXml(saved));
        });

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void Poe1_UnchangedSave_IsEquivalentInTheGamesOwnModel()
    {
        var gameSerializer = Poe1GameSerializer(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable));
        string Canonical(string xml)
        {
            using var reader = new StringReader(xml);
            var data = gameSerializer.Deserialize(reader);
            using var writer = new StringWriter();
            gameSerializer.Serialize(writer, data);
            return writer.ToString();
        }

        AssertEveryConversation(Poe1(), (provider, file) =>
        {
            var original = File.ReadAllText(file.ConversationPath);
            var saved    = Poe1ConversationSerializer.Serialize(original, UnchangedSnapshot(provider, file));
            return FirstLineDifference(Canonical(original), Canonical(saved));
        });
    }

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void Poe1_UnchangedStringTables_KeepEveryEntry() => AssertEveryStringTable(Poe1());

    // ── PoE2 ────────────────────────────────────────────────────────────────────

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void Poe2_UnchangedSave_KeepsTheEditorModel() =>
        AssertEveryConversation(Poe2(), (provider, file) =>
        {
            var original = File.ReadAllText(file.ConversationPath);
            var saved    = Poe2ConversationSerializer.Serialize(original, UnchangedSnapshot(provider, file));
            return ModelDifference(Poe2ConversationParser.ParseJson(original), Poe2ConversationParser.ParseJson(saved));
        });

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void Poe2_UnchangedSave_IsIdenticalJson() =>
        AssertEveryConversation(Poe2(), (provider, file) =>
        {
            var original = File.ReadAllText(file.ConversationPath);
            var saved    = Poe2ConversationSerializer.Serialize(original, UnchangedSnapshot(provider, file));
            return JsonNode.DeepEquals(JsonNode.Parse(original), JsonNode.Parse(saved))
                ? null
                : FirstNodeDifference(original, saved);
        });

    [GameInstallFact(GameInstallFactAttribute.Poe2Variable)]
    public void Poe2_UnchangedStringTables_KeepEveryEntry() => AssertEveryStringTable(Poe2());

    // ── Harness ─────────────────────────────────────────────────────────────────

    private static Poe1GameDataProvider Poe1() =>
        new(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable));

    private static Poe2GameDataProvider Poe2() =>
        new(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe2Variable));

    // What an unchanged save writes: the conversation exactly as the editor loads it.
    private static Core.Editing.ConversationEditSnapshot UnchangedSnapshot(IGameDataProvider provider, ConversationFile file) =>
        ConversationSnapshotBuilder.Build(provider.LoadConversation(file));

    /// <param name="check">Returns null when the conversation passes, else what differs.</param>
    private static void AssertEveryConversation(
        IGameDataProvider provider, Func<IGameDataProvider, ConversationFile, string?> check)
    {
        var files = provider.EnumerateConversations();
        Assert.NotEmpty(files);   // a wrong folder must not pass vacuously

        var failures = new List<string>();
        foreach (var file in files)
        {
            string? problem;
            try   { problem = check(provider, file); }
            catch (Exception ex) { problem = $"{ex.GetType().Name}: {ex.Message}"; }
            if (problem is not null) failures.Add($"{file.FolderPath}/{file.Name}: {problem}");
        }
        AssertNoFailures(failures, files.Count, "conversations");
    }

    private void AssertEveryStringTable(IGameDataProvider provider)
    {
        var files = provider.EnumerateConversations();
        Assert.NotEmpty(files);

        var failures   = new List<string>();
        var unreadable = new List<string>();
        var checkedCount = 0;
        foreach (var language in provider.AvailableLanguages)
        foreach (var file in files)
        {
            var path = provider.GetStringTablePath(file, language);
            if (!File.Exists(path)) continue;

            // An install file that is not XML at all (a GOG Deadfire install has one
            // binary German stringtable, #119) is not a round-trip failure: there is
            // nothing to round-trip. Reported, not failed. Only the ORIGINAL is exempt.
            string original;
            List<(int Id, string? DefaultText, string? FemaleText)> entries;
            try
            {
                original = File.ReadAllText(path);
                entries  = Entries(original);
            }
            catch (System.Xml.XmlException ex)
            {
                unreadable.Add($"{language}/{file.FolderPath}/{file.Name}: {ex.Message}");
                continue;
            }

            checkedCount++;
            try
            {
                var saved = StringTableSerializer.SerializeTranslations(original,
                    entries.Select(e => new NodeTranslation(e.Id, e.DefaultText, e.FemaleText)));
                if (!entries.SequenceEqual(Entries(saved)))
                    failures.Add($"{language}/{file.FolderPath}/{file.Name}: entries differ after save");
            }
            catch (Exception ex)
            {
                failures.Add($"{language}/{file.FolderPath}/{file.Name}: {ex.GetType().Name}: {ex.Message}");
            }
        }
        foreach (var u in unreadable) output.WriteLine($"Skipped, not readable XML in the install: {u}");
        Assert.True(checkedCount > 0, "No stringtables found under the install.");
        AssertNoFailures(failures, checkedCount, "stringtables");
    }

    // Read straight from the XML rather than through StringTableParser, so the check does
    // not share the code it is checking. A missing element is null, not "".
    private static List<(int Id, string? DefaultText, string? FemaleText)> Entries(string xml) =>
        XDocument.Parse(xml).Descendants("Entry")
            .Select(e => ((int)e.Element("ID")!, (string?)e.Element("DefaultText"), (string?)e.Element("FemaleText")))
            .ToList();

    private static void AssertNoFailures(List<string> failures, int total, string what)
    {
        if (failures.Count == 0) return;
        var report = new StringBuilder($"{failures.Count} of {total} {what} did not round-trip:\n");
        foreach (var f in failures.Take(MaxReported)) report.AppendLine("  " + f);
        if (failures.Count > MaxReported) report.AppendLine($"  … and {failures.Count - MaxReported} more");
        Assert.Fail(report.ToString());
    }

    private static string? ModelDifference(IReadOnlyList<ConversationNode> before, IReadOnlyList<ConversationNode> after)
    {
        var a = before.OrderBy(n => n.NodeId).Select(Describe).ToList();
        var b = after.OrderBy(n => n.NodeId).Select(Describe).ToList();
        if (a.Count != b.Count) return $"{a.Count} nodes before, {b.Count} after";
        for (var i = 0; i < a.Count; i++)
            if (a[i] != b[i]) return $"model differs:\n    before {a[i]}\n    after  {b[i]}";
        return null;
    }

    // Records compare lists by reference, so describe each node structurally instead.
    private static string Describe(ConversationNode n) =>
        $"#{n.NodeId} {n.SpeakerCategory} player={n.IsPlayerChoice} speaker={n.SpeakerGuid} listener={n.ListenerGuid} " +
        $"display={n.DisplayType} persist={n.Persistence} dir={n.ActorDirection} comments={n.Comments} " +
        $"vo={n.ExternalVO}/{n.HasVO} hide={n.HideSpeaker} " +
        $"links=[{string.Join("; ", n.Links.Select(l => $"{l.FromNodeId}>{l.ToNodeId} w={l.RandomWeight} q={l.QuestionNodeTextDisplay} c={Describe(l.Conditions)}"))}] " +
        $"conds={Describe(n.Conditions)} " +
        $"scripts=[{string.Join("; ", n.Scripts.Select(s => $"{s.Category}:{s.FullName}({string.Join(",", s.Parameters)})"))}]";

    private static string Describe(IReadOnlyList<ConditionNode> conditions) =>
        "[" + string.Join(", ", conditions.Select(c => c switch
        {
            ConditionLeaf l   => $"{(l.Not ? "!" : "")}{l.FullName}({string.Join(",", l.Parameters)}){l.Operator}",
            ConditionBranch b => $"{(b.Not ? "!" : "")}{Describe(b.Components)}{b.Operator}",
            _                 => c.ToString(),
        })) + "]";

    private static string? FirstLineDifference(string a, string b)
    {
        if (a == b) return null;
        var la = a.Split('\n');
        var lb = b.Split('\n');
        var i  = 0;
        while (i < la.Length && i < lb.Length && la[i] == lb[i]) i++;
        return $"differs in the game model at line {i + 1}:\n    before {Line(la, i)}\n    after  {Line(lb, i)}";
    }

    private static string Line(string[] lines, int i) => i < lines.Length ? lines[i].Trim() : "<end>";

    private static string FirstNodeDifference(string originalJson, string savedJson)
    {
        var before = JsonNode.Parse(originalJson)!["Conversations"]![0]!["Nodes"]!.AsArray();
        var after  = JsonNode.Parse(savedJson)!["Conversations"]![0]!["Nodes"]!.AsArray();
        if (before.Count != after.Count) return $"{before.Count} nodes before, {after.Count} after";
        for (var i = 0; i < before.Count; i++)
            if (!JsonNode.DeepEquals(before[i], after[i]))
                return $"node at index {i} (NodeID {before[i]?["NodeID"]}) differs";
        return "JSON outside Nodes differs";
    }

    // The game's own XmlSerializer model, loaded from the install at run time: the
    // assembly ships with the game and is never referenced or redistributed by this repo.
    private static XmlSerializer Poe1GameSerializer(string installDir)
    {
        var dll = Path.Combine(installDir, "PillarsOfEternity_Data", "Managed", "OEIFormats.dll");
        Assert.True(File.Exists(dll), $"OEIFormats.dll not found at {dll}");
        var type = Assembly.LoadFrom(dll).GetType("OEIFormats.FlowCharts.Conversations.ConversationData", throwOnError: true)!;
        return new XmlSerializer(type);
    }
}
