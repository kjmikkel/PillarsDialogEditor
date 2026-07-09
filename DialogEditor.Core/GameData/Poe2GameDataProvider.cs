using System.Text.Json;
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

    public IReadOnlyDictionary<string, string> LoadChatterPrefixes() =>
        File.Exists(SpeakersBundle)
            ? Poe2SpeakerNameParser.ParseChatterPrefixesFile(SpeakersBundle)
            : new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    private string GameDataRoot => Path.Combine(ExportedRoot, "design", "gamedata");

    public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
    {
        var result = new Dictionary<string, IReadOnlyList<GameDataEntry>>();
        var gdRoot = GameDataRoot;

        // ── Phase 1 — generic sweep ──────────────────────────────────────────
        // Parse every bundle once, bucket objects by short $type, and register each
        // bucket under GameDataKindMapper.TypeToKind. This lights up every
        // bundle-backed lookup kind the generated catalogue references (Ship, Team,
        // Affliction, Schedule, CreatureType, …) and any future kind, with zero
        // per-kind code. Spec: docs/superpowers/specs/2026-07-09-lookup-kind-sweep-design.md.
        if (Directory.Exists(gdRoot))
        {
            var byKind = new Dictionary<string, List<GameDataEntry>>(StringComparer.Ordinal);
            foreach (var file in Directory.EnumerateFiles(gdRoot, "*.gamedatabundle"))
            {
                foreach (var (shortType, entries) in Poe2GameDataBundleParser.ParseAllByTypeFile(file))
                {
                    var kind = GameDataKindMapper.TypeToKind(shortType);
                    if (!byKind.TryGetValue(kind, out var list))
                        byKind[kind] = list = [];
                    list.AddRange(entries);
                }
            }
            foreach (var (kind, entries) in byKind)
                result[kind] = entries;
        }

        // ── Phase 2 — explicit overrides (overwrite the sweep) ──────────────
        // Only kinds needing cleaning/filtering the sweep can't express.
        void FactionBundle(string kind, string typeFilter, Func<string, string>? clean = null)
        {
            var entries = Poe2GameDataBundleParser.ParseFile(
                Path.Combine(gdRoot, "factions.gamedatabundle"), clean, typeFilter);
            if (entries.Count > 0) result[kind] = entries;
        }

        // Disposition DebugName format: "<Name>Disposition" — strip the suffix.
        FactionBundle("Disposition", "DispositionGameData",
                      n => n.EndsWith("Disposition", StringComparison.Ordinal)
                           ? n[..^"Disposition".Length].TrimEnd() : n);
        // PaladinOrder DebugName format: "Bleak_Walkers" — replace underscores.
        FactionBundle("PaladinOrder", "PaladinOrderGameData",
                      n => n.Replace('_', ' '));

        // Class: BaseStatsGameData includes NPC creature archetypes — keep only
        // playable classes (IsPlayerClass:"true"); the sweep's bucket is unfiltered.
        var classEntries = Poe2GameDataBundleParser.ParseFile(
            Path.Combine(gdRoot, "characters.gamedatabundle"),
            typeFilter: "BaseStatsGameData", componentFilter: IsPlayerClassComponent);
        if (classEntries.Count > 0) result["Class"] = classEntries;

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

        // ── Conversations ─────────────────────────────────────────────────────
        // Each .conversationbundle has a root Conversations[0].ID GUID — parse all
        // bundles and expose the filename (without extension) as the display name.
        if (Directory.Exists(ConversationsRoot))
        {
            var conversations = Directory
                .EnumerateFiles(ConversationsRoot, "*.conversationbundle", SearchOption.AllDirectories)
                .Select(path => (
                    id:   Poe2ConversationParser.ParseRootId(File.ReadAllText(path)),
                    name: Path.GetFileNameWithoutExtension(path)))
                .Where(t => !string.IsNullOrWhiteSpace(t.id))
                .Select(t => new GameDataEntry(Id: t.id!, Name: t.name))
                .OrderBy(e => e.Name)
                .ToList();
            if (conversations.Count > 0) result["Conversation"] = conversations;
        }

        // Kinds with no bundle-backed $type stay dormant (empty suggestions, raw GUID
        // display — safe): ProgressionUnlockable, AttackBase, and the generic "GameData"
        // fallback kind. ArmorTypeGameData is also absent from shipped bundles; if a
        // patch ever adds it, the sweep registers it automatically.

        return result;
    }

    private static bool IsPlayerClassComponent(IReadOnlyList<JsonElement> components) =>
        components.Any(c =>
            c.TryGetProperty("IsPlayerClass", out var p) &&
            p.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

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
