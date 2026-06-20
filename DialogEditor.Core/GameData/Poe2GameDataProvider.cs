using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Core.GameData;

public class Poe2GameDataProvider(string rootPath) : IGameDataProvider
{
    public string GameName => "Pillars of Eternity II: Deadfire";
    public string GameId   => "poe2";
    public string Language { get; set; } = "en";

    private string ExportedRoot    => Path.Combine(rootPath, "PillarsOfEternityII_Data", "exported");
    private string LocalizedRoot   => Path.Combine(ExportedRoot, "localized");
    internal string ConversationsRoot => Path.Combine(ExportedRoot, "design", "conversations");
    internal string StringTablesRoot  => Path.Combine(LocalizedRoot, Language, "text", "conversations");
    private string SpeakersBundle  => Path.Combine(ExportedRoot, "design", "gamedata", "speakers.gamedatabundle");

    public IReadOnlyList<string> AvailableLanguages =>
        Directory.Exists(LocalizedRoot)
            ? [.. Directory.GetDirectories(LocalizedRoot).Select(Path.GetFileName).Order()!]
            : ["en"];

    public IReadOnlyList<ConversationFile> EnumerateConversations()
    {
        if (!Directory.Exists(ConversationsRoot)) return [];

        return Directory
            .EnumerateFiles(ConversationsRoot, "*.conversationbundle", SearchOption.AllDirectories)
            .Select(BuildConversationFile)
            .OrderBy(f => f.FolderPath)
            .ThenBy(f => f.Name)
            .ToList();
    }

    private ConversationFile BuildConversationFile(string convPath)
    {
        var relative = Path.GetRelativePath(ConversationsRoot, convPath);
        return new ConversationFile(
            Name: Path.GetFileNameWithoutExtension(convPath),
            FolderPath: Path.GetDirectoryName(relative) ?? string.Empty,
            ConversationPath: convPath,
            StringTablePath: string.Empty
        );
    }

    public Conversation LoadConversation(ConversationFile file)
    {
        var nodes = Poe2ConversationParser.ParseFile(file.ConversationPath);
        var stPath = StringTablePathFor(file.ConversationPath, Language);
        var strings = File.Exists(stPath)
            ? StringTableParser.ParseFile(stPath)
            : StringTable.Empty;
        return new Conversation(file.Name, nodes, strings);
    }

    public string GetStringTablePath(ConversationFile file)
        => StringTablePathFor(file.ConversationPath, Language);

    public string GetStringTablePath(ConversationFile file, string language)
        => StringTablePathFor(file.ConversationPath, language);

    private string StringTablePathFor(string convPath, string language)
    {
        var relative   = Path.GetRelativePath(ConversationsRoot, convPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        var root       = Path.Combine(LocalizedRoot, language, "text", "conversations");
        return Path.Combine(root, withoutExt + ".stringtable");
    }

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        File.Exists(SpeakersBundle)
            ? Poe2SpeakerNameParser.ParseFile(SpeakersBundle)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private string GameDataRoot => Path.Combine(ExportedRoot, "design", "gamedata");

    public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
    {
        var result = new Dictionary<string, IReadOnlyList<GameDataEntry>>();
        var gdRoot = GameDataRoot;

        void Bundle(string kind, string filename, Func<string, string>? clean = null, string? typeFilter = null)
        {
            var entries = Poe2GameDataBundleParser.ParseFile(Path.Combine(gdRoot, filename), clean, typeFilter);
            if (entries.Count > 0) result[kind] = entries;
        }

        void FactionBundle(string kind, string typeFilter, Func<string, string>? clean = null)
            => Bundle(kind, "factions.gamedatabundle", clean, typeFilter);

        void CharBundle(string kind, string typeFilter, Func<string, string>? clean = null)
            => Bundle(kind, "characters.gamedatabundle", clean, typeFilter);

        // ── Single-kind bundles ──────────────────────────────────────────────
        Bundle("Item",         "items.gamedatabundle");
        Bundle("Ability",      "abilities.gamedatabundle");
        Bundle("StatusEffect", "statuseffects.gamedatabundle");
        // abilities.gamedatabundle also contains phrases — register separately with $type filter.
        Bundle("Phrase",  "abilities.gamedatabundle", typeFilter: "PhraseGameData");
        Bundle("Keyword", "gui.gamedatabundle",       typeFilter: "KeywordGameData");
        Bundle("Map",     "worldmap.gamedatabundle",  typeFilter: "MapGameData");

        // ── factions.gamedatabundle — multi-kind ─────────────────────────────
        // All share the file; a $type filter extracts each kind independently.
        FactionBundle("Faction",             "FactionGameData");
        FactionBundle("Deity",               "DeityGameData");
        // Disposition DebugName format: "<Name>Disposition" — strip the suffix.
        FactionBundle("Disposition",         "DispositionGameData",
                      n => n.EndsWith("Disposition", StringComparison.Ordinal)
                           ? n[..^"Disposition".Length].TrimEnd() : n);
        // DispositionStrength is stored as ChangeStrengthGameData (DebugName: "Average" etc.)
        FactionBundle("DispositionStrength", "ChangeStrengthGameData");
        // PaladinOrder DebugName format: "Bleak_Walkers" — replace underscores.
        FactionBundle("PaladinOrder",        "PaladinOrderGameData",
                      n => n.Replace('_', ' '));

        // ── characters.gamedatabundle — multi-kind ───────────────────────────
        // Confirmed: Class=BaseStatsGameData, Race=RaceGameData.
        // Subrace/Background/Culture $type names inferred; return empty if wrong (plain text fallback).
        CharBundle("Class",      "BaseStatsGameData");
        CharBundle("Race",       "RaceGameData");
        CharBundle("Subrace",    "SubraceGameData");
        CharBundle("Background", "BackgroundGameData");
        CharBundle("Culture",    "CultureGameData");

        // ── global.gamedatabundle ────────────────────────────────────────────
        Bundle("Skill", "global.gamedatabundle", typeFilter: "SkillGameData");
        // WeaponType conditions store DebugName (e.g. "Unarmed"), not the GUID → strip Id.
        var weaponEntries = Poe2GameDataBundleParser
            .ParseFile(Path.Combine(gdRoot, "global.gamedatabundle"), typeFilter: "WeaponTypeGameData")
            .Select(e => new GameDataEntry(Id: string.Empty, Name: e.Name))
            .ToList();
        if (weaponEntries.Count > 0) result["WeaponType"] = weaponEntries;

        // ── Quest ────────────────────────────────────────────────────────────
        // quests.questbundle lives in design/quests/, not design/gamedata/, and uses a
        // different JSON envelope {Hash, Quests:[{ID, Filename}]} — requires its own parser.
        var quests = Poe2QuestBundleParser.ParseFile(
            Path.Combine(ExportedRoot, "design", "quests", "quests.questbundle"));
        if (quests.Count > 0) result["Quest"] = quests;

        // ── GlobalVariables.csv ──────────────────────────────────────────────
        var vars = GlobalVariablesCsvParser.ParseFile(Path.Combine(gdRoot, "GlobalVariables.csv"));
        if (vars.Count > 0) result["GlobalVariable"] = vars;

        // Deferred — no data source located:
        // ArmorType:    conditions store enum string (e.g. "Heavy") but no ArmorTypeGameData found.
        // CreatureType: GUID-keyed but CreatureTypeGameData absent from all bundles.
        // Conversation: would require parsing each .conversationbundle for its root ID.
        // Parameters for these kinds fall back to plain-text input.

        return result;
    }

    public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots()
        => (ConversationsRoot, StringTablesRoot);

    public void SaveConversation(ConversationFile file, ConversationEditSnapshot snapshot)
    {
        Poe2ConversationSerializer.SaveToFile(file.ConversationPath, snapshot);
    }

    public ConversationFile BuildNewConversationFile(string name)
    {
        var convPath = Path.Combine(ConversationsRoot, name + ".conversationbundle");
        var stPath   = Path.Combine(StringTablesRoot,  name + ".stringtable");
        return new ConversationFile(
            Name: name,
            FolderPath: string.Empty,
            ConversationPath: convPath,
            StringTablePath: stPath);
    }

    private const string Poe2BlankTemplate =
        """{"Conversations":[{"Nodes":[]}]}""";

    public void InitializeConversationFile(ConversationFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.ConversationPath)!);
        File.WriteAllText(file.ConversationPath, Poe2BlankTemplate, System.Text.Encoding.UTF8);
    }
}
