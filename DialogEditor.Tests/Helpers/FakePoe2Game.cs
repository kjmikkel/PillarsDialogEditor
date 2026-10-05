using System.Text.Json;
using DialogEditor.Core.GameData;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Helpers;

/// A throwaway PoE2 install on disk: one conversation "test_conv" (node 1, ExternalVO ""),
/// string tables for each requested language, and a VO folder holding "existing.wem".
public sealed class FakePoe2Game : IDisposable
{
    public const string OneNodeBundle = """
        {"Conversations": [{
          "Nodes": [
            {
              "$type": "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats",
              "SpeakerGuid": "aaaa-0000", "ListenerGuid": "bbbb-0000",
              "IsQuestionNode": false, "DisplayType": 0, "Persistence": 0,
              "NodeID": 1, "ContainerNodeID": -1, "Links": [],
              "ClassExtender": {"ExtendedProperties": []},
              "Conditionals": {"Operator": 0, "Components": []},
              "OnEnterScripts": [], "OnExitScripts": [], "OnUpdateScripts": [],
              "HideSpeaker": false, "HasVO": false, "ExternalVO": "",
              "IsTempText": false, "PlayVOAs3DSound": false, "PlayType": 0,
              "NoPlayRandomWeight": 0, "VOPositioning": 0, "NotSkippable": false
            }
          ]
        }]}
        """;

    public static string StringTable(string text) => $"""
        <StringTableFile>
          <Entries>
            <Entry><ID>1</ID><DefaultText>{text}</DefaultText><FemaleText></FemaleText></Entry>
          </Entries>
        </StringTableFile>
        """;

    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"fakepoe2_{Guid.NewGuid():N}");
    public string DataRoot => Path.Combine(Root, "PillarsOfEternityII_Data");
    public string ConvDir  => Path.Combine(DataRoot, "exported", "design", "conversations");
    public string StDir(string lang) => Path.Combine(DataRoot, "exported", "localized", lang, "text", "conversations");
    public string VoDir    => Path.Combine(DataRoot, "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
    public string ConvPath(string name = "test_conv") => Path.Combine(ConvDir, name + ".conversationbundle");
    public string StPath(string lang, string name = "test_conv") => Path.Combine(StDir(lang), name + ".stringtable");

    public FakePoe2Game(params string[] languages)
    {
        if (languages.Length == 0) languages = ["en"];
        Directory.CreateDirectory(ConvDir);
        File.WriteAllText(ConvPath(), OneNodeBundle);
        foreach (var lang in languages)
        {
            Directory.CreateDirectory(StDir(lang));
            File.WriteAllText(StPath(lang), StringTable($"vanilla {lang}"));
        }
        Directory.CreateDirectory(VoDir);
        File.WriteAllBytes(Path.Combine(VoDir, "existing.wem"), [1, 2, 3]);
    }

    /// The canonical PoE2 conversation (issue 122) instead of the one-node test_conv:
    /// "canonical" in "en" and "de", with its .wem files. See Fixtures/Canonical/README.md.
    public static FakePoe2Game Canonical() => new(canonical: true);

    private FakePoe2Game(bool canonical) => CanonicalFixture.CopyTo("poe2", Root);

    public ConversationFile CanonicalFile =>
        Provider.EnumerateConversations().Single(f => f.Name == "canonical");

    public IGameDataProvider Provider => new Poe2GameDataProvider(Root);

    /// Every file under the game's data folder (not the patcher backup), keyed by relative path,
    /// plus every folder (key ending in a separator, empty value) — so a restore that leaves
    /// behind a folder it created no longer compares equal (issue 125).
    public SortedDictionary<string, string> SnapshotGameData() =>
        new(Directory.EnumerateFiles(DataRoot, "*", SearchOption.AllDirectories)
            .ToDictionary(f => Path.GetRelativePath(DataRoot, f),
                          f => Convert.ToHexString(File.ReadAllBytes(f)))
            .Concat(Directory.EnumerateDirectories(DataRoot, "*", SearchOption.AllDirectories)
                .Select(d => KeyValuePair.Create(Path.GetRelativePath(DataRoot, d) + Path.DirectorySeparatorChar, "")))
            .ToDictionary());

    public string ReadExternalVo(string name = "test_conv")
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(ConvPath(name)));
        return doc.RootElement.GetProperty("Conversations")[0].GetProperty("Nodes")[0]
                  .GetProperty("ExternalVO").GetString()!;
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ } }

    // ── Mod builders ─────────────────────────────────────────────────────

    /// A mod that sets node 1's ExternalVO on test_conv to <paramref name="value"/>.
    public static DialogProject ExternalVoMod(string modName, string value)
    {
        var mod = new NodeModification(1,
            new Dictionary<string, FieldChange>
            {
                ["ExternalVO"] = new(JsonSerializer.Serialize(""), JsonSerializer.Serialize(value)),
            }, [], []);
        return DialogProject.Empty(modName)
            .WithPatch(new ConversationPatch("test_conv", ConversationPatch.CurrentSchemaVersion, [], [], [mod]));
    }
}
