using DialogEditor.Core.Localisation;
using System.Text.Json.Nodes;
using System.Text.Json;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Models;
using DialogEditor.Core.Parsing;
using DialogEditor.Core.Resources;
using DialogEditor.Core.Serialization;

namespace DialogEditor.Core.GameData;

public class Poe2GameDataProvider(string rootPath) : IGameDataProvider
{
    // See Poe1GameDataProvider: the title is translatable copy, GameId is not.
    public string GameName => CoreStrings.Game_Poe2_Name;
    public string GameId   => "poe2";
    public string Language { get; set; } = "en";

    private string ExportedRoot    => Path.Combine(rootPath, "PillarsOfEternityII_Data", "exported");
    private string LocalizedRoot   => Path.Combine(ExportedRoot, "localized");
    internal string ConversationsRoot => Path.Combine(ExportedRoot, "design", "conversations");

    // Every data folder the install has, base first, in the game's order (issue 198).
    private IReadOnlyList<Poe2DataRoot> Roots => Poe2DataRoots.Existing(rootPath);
    private static string ConversationsOf(Poe2DataRoot root) => Path.Combine(root.Path, "design", "conversations");
    internal string StringTablesRoot  => GetStringTablesRoot(Language);

    public string GetStringTablesRoot(string language) =>
        Path.Combine(LocalizedRoot, language, "text", "conversations");

    public IReadOnlyList<string> AvailableLanguages =>
        Directory.Exists(LocalizedRoot)
            ? [.. Directory.GetDirectories(LocalizedRoot).Select(Path.GetFileName).Order()!]
            : ["en"];

    // A relative path can be in several data folders: the three expansions each ship their own
    // lax/lax_cv_craftsperson, with the same ID. The game's ConversationDataManager loads the
    // path from every folder in order and keeps the last (ResourceManager.LoadBundles), so the
    // editor lists one conversation per path, from the last folder that has it (issue 198).
    public IReadOnlyList<ConversationFile> EnumerateConversations()
    {
        var byPath = new Dictionary<string, (int Order, ConversationFile File)>(StringComparer.OrdinalIgnoreCase);
        var roots = Roots;
        for (var i = 0; i < roots.Count; i++)
        {
            var conversations = ConversationsOf(roots[i]);
            if (!Directory.Exists(conversations)) continue;
            foreach (var path in Directory.EnumerateFiles(conversations, "*.conversationbundle", SearchOption.AllDirectories))
                byPath[Path.GetRelativePath(conversations, path)] = (i, BuildConversationFile(roots[i], conversations, path));
        }
        return byPath.Values
            .OrderBy(v => v.Order).ThenBy(v => v.File.FolderPath).ThenBy(v => v.File.Name)
            .Select(v => v.File)
            .ToList();
    }

    private static ConversationFile BuildConversationFile(Poe2DataRoot root, string conversationsRoot, string convPath)
    {
        var relative = Path.GetRelativePath(conversationsRoot, convPath);
        return new ConversationFile(
            Name: Path.GetFileNameWithoutExtension(convPath),
            FolderPath: Path.GetDirectoryName(relative) ?? string.Empty,
            ConversationPath: convPath,
            StringTablePath: string.Empty,
            DataRoot: root.Folder);
    }

    public Conversation LoadConversation(ConversationFile file)
    {
        var nodes = Poe2ConversationParser.ParseFile(file.ConversationPath);
        return new Conversation(file.Name, nodes, LoadText(file, Language));
    }

    public string GetStringTablePath(ConversationFile file)
        => StringTablePathFor(file, Language);

    public string GetStringTablePath(ConversationFile file, string language)
        => StringTablePathFor(file, language);

    // Every data folder's table for the conversation's relative path in one language, in the
    // game's folder order (issue 198). A DLC can ship text for a conversation whose bundle is in
    // another folder: lax2_exported has lines for the base game's 12_cv_eothas_final.
    private IReadOnlyList<string> TextTablesFor(ConversationFile file, string language)
    {
        var relative = Path.ChangeExtension(Path.GetRelativePath(
            Path.Combine(rootPath, Poe2DataRoots.DataFolder, file.DataRoot, "design", "conversations"), file.ConversationPath), null);
        return Roots.Select(r => Path.Combine(r.Path, "localized", language, "text", "conversations", relative + ".stringtable"))
                    .Where(File.Exists)
                    .ToList();
    }

    // The game looks each line up from the last folder back to the first
    // (StringTableManager.LoadedStringTable.GetEntry), so the text is merged per line, the later
    // folder winning. A damaged table is reported as before (issue 119).
    private StringTable LoadText(ConversationFile file, string language) =>
        StringTable.MergeLaterWins(TextTablesFor(file, language).Select(StringTableParser.LoadFile).ToList());

    // Where the conversation's text is written: the last folder that already has a table for it
    // in this language, which the game reads first for every line it holds, so an edit there
    // always shows. With no table anywhere, the bundle's own folder (issue 198).
    private string StringTablePathFor(ConversationFile file, string language)
    {
        if (TextTablesFor(file, language) is { Count: > 0 } tables) return tables[^1];
        var dataRoot   = Path.Combine(rootPath, Poe2DataRoots.DataFolder, file.DataRoot);
        var relative   = Path.GetRelativePath(Path.Combine(dataRoot, "design", "conversations"), file.ConversationPath);
        var withoutExt = Path.ChangeExtension(relative, null);
        return Path.Combine(dataRoot, "localized", language, "text", "conversations", withoutExt + ".stringtable");
    }

    // A bundle every data folder may have, named with that folder's prefix
    // (speakers.gamedatabundle, lax2_speakers.gamedatabundle, …), in the game's order (issue 198).
    private IEnumerable<string> BundlesNamed(string subfolder, string name) =>
        Roots.Select(r => Path.Combine(r.Path, "design", subfolder, r.Prefix + name)).Where(File.Exists);

    // Later folders win, as the game loads them in this order.
    private static IReadOnlyDictionary<string, string> MergeLaterWins(IEnumerable<IReadOnlyDictionary<string, string>> maps)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var map in maps)
            foreach (var (key, value) in map) result[key] = value;
        return result;
    }

    public IReadOnlyDictionary<string, string> LoadSpeakerNames() =>
        MergeLaterWins(BundlesNamed("gamedata", "speakers.gamedatabundle").Select(Poe2SpeakerNameParser.ParseFile));

    public IReadOnlyDictionary<string, string> LoadChatterPrefixes() =>
        MergeLaterWins(BundlesNamed("gamedata", "speakers.gamedatabundle").Select(Poe2SpeakerNameParser.ParseChatterPrefixesFile));

    public IReadOnlyDictionary<string, IReadOnlyList<GameDataEntry>> LoadGameDataNames()
    {
        var result = new Dictionary<string, IReadOnlyList<GameDataEntry>>();

        // ── Phase 1 — generic sweep ──────────────────────────────────────────
        // Parse every bundle once, bucket objects by short $type, and register each
        // bucket under GameDataKindMapper.TypeToKind. This lights up every
        // bundle-backed lookup kind the generated catalogue references (Ship, Team,
        // Affliction, Schedule, CreatureType, …) and any future kind, with zero
        // per-kind code. Spec: docs/superpowers/specs/2026-07-09-lookup-kind-sweep-design.md.
        var gameDataFolders = Roots.Select(r => Path.Combine(r.Path, "design", "gamedata")).Where(Directory.Exists).ToList();
        if (gameDataFolders.Count > 0)
        {
            var byKind = new Dictionary<string, List<GameDataEntry>>(StringComparer.Ordinal);
            foreach (var file in gameDataFolders.SelectMany(d => Directory.EnumerateFiles(d, "*.gamedatabundle")))
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
                result[kind] = LastOfEach(entries);   // a DLC overriding an object by ID (issue 198)

            // ── Phase 1.5 — inheritance composition ─────────────────────────
            // The sweep buckets by EXACT $type, but a param whose DataTypeID names a
            // base class accepts every subclass instance. Compose buckets to match,
            // for the base kinds the catalogue references. Hierarchy verified against
            // the decompiled Game.GameData sources (2026-07-09):
            //   WeaponGameData ⊂ EquippableGameData ⊂ ItemGameData; ConsumableGameData ⊂ ItemGameData
            //   AfflictionGameData ⊂ StatusEffectGameData
            //   GenericAbilityGameData / PhraseGameData ⊂ ProgressionUnlockableGameData
            //   Attack*GameData ⊂ AttackBaseGameData
            void ComposeInto(string parent, params string[] children)
            {
                var union = result.TryGetValue(parent, out var own)
                    ? new List<GameDataEntry>(own) : [];
                foreach (var child in children)
                    if (result.TryGetValue(child, out var entries))
                        union.AddRange(entries);
                if (union.Count > 0) result[parent] = union;
            }

            ComposeInto("Equippable", "Weapon");
            ComposeInto("Item", "Equippable", "Consumable"); // Equippable already holds weapons
            ComposeInto("StatusEffect", "Affliction");
            ComposeInto("ProgressionUnlockable", "Ability", "Phrase");
            ComposeInto("AttackBase",
                result.Keys.Where(k => k.StartsWith("Attack", StringComparison.Ordinal)
                                    && k != "AttackBase").ToArray());
        }

        // ── Phase 2 — explicit overrides (overwrite the sweep) ──────────────
        // Only kinds needing cleaning/filtering the sweep can't express.
        void FactionBundle(string kind, string typeFilter, Func<string, string>? clean = null)
        {
            var entries = BundlesNamed("gamedata", "factions.gamedatabundle")
                .SelectMany(f => Poe2GameDataBundleParser.ParseFile(f, clean, typeFilter)).ToList();
            if (entries.Count > 0) result[kind] = LastOfEach(entries);
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
        var classEntries = BundlesNamed("gamedata", "characters.gamedatabundle")
            .SelectMany(f => Poe2GameDataBundleParser.ParseFile(f, typeFilter: "BaseStatsGameData", componentFilter: IsPlayerClassComponent))
            .ToList();
        if (classEntries.Count > 0) result["Class"] = LastOfEach(classEntries);

        var weaponEntries = BundlesNamed("gamedata", "global.gamedatabundle")
            .SelectMany(f => Poe2GameDataBundleParser.ParseFile(f, typeFilter: "WeaponTypeGameData"))
            .Select(e => new GameDataEntry(Id: string.Empty, Name: e.Name))
            .ToList();
        if (weaponEntries.Count > 0) result["WeaponType"] = LastOfEach(weaponEntries);

        // ── Quest ────────────────────────────────────────────────────────────
        // quests.questbundle lives in design/quests/, not design/gamedata/, and uses a
        // different JSON envelope {Hash, Quests:[{ID, Filename}]} — requires its own parser.
        var quests = BundlesNamed("quests", "quests.questbundle").SelectMany(Poe2QuestBundleParser.ParseFile).ToList();
        if (quests.Count > 0) result["Quest"] = LastOfEach(quests);

        // ── Global variables ─────────────────────────────────────────────────
        // Every data folder's design/globalvariables/*.globalvariablesbundle, keyed on the tag,
        // as GlobalVariables.LoadMetadata reads them. pillars1's names are only kept aside by the
        // game for importing a PoE1 save, not declared as variables, so they aren't offered
        // (issue 199). Sorted by name, as the suggestions are browsed alphabetically.
        var vars = Roots.Select(r => Path.Combine(r.Path, "design", "globalvariables"))
            .Where(Directory.Exists)
            .SelectMany(d => Directory.EnumerateFiles(d, "*.globalvariablesbundle"))
            .Where(f => !Path.GetFileNameWithoutExtension(f).Equals("pillars1", StringComparison.OrdinalIgnoreCase))
            .SelectMany(Poe2GlobalVariablesBundleParser.ParseFile)
            .DistinctBy(v => v.Name, StringComparer.Ordinal)
            .OrderBy(v => v.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();
        if (vars.Count > 0) result["GlobalVariable"] = vars;

        // ── Conversations ─────────────────────────────────────────────────────
        // Each .conversationbundle has a root Conversations[0].ID GUID — parse all
        // bundles and expose the filename (without extension) as the display name.
        // One per relative path, the copy the game loads (issue 198).
        var conversations = EnumerateConversations().Select(f => f.ConversationPath)
            .Select(path => (
                id:   Poe2ConversationParser.ParseRootId(File.ReadAllText(path)),
                name: Path.GetFileNameWithoutExtension(path)))
            .Where(t => !string.IsNullOrWhiteSpace(t.id))
            .Select(t => new GameDataEntry(Id: t.id!, Name: t.name))
            .OrderBy(e => e.Name)
            .ToList();
        if (conversations.Count > 0) result["Conversation"] = conversations;

        // Kinds with no bundle-backed $type stay dormant (empty suggestions, raw GUID
        // display — safe): ProgressionUnlockable, AttackBase, and the generic "GameData"
        // fallback kind. ArmorTypeGameData is also absent from shipped bundles; if a
        // patch ever adds it, the sweep registers it automatically.

        return result;
    }

    // Entries merged from several data folders: the last copy of each object wins, as the game
    // loads later folders after earlier ones (issue 198). Objects are keyed by ID, or by name for
    // the kinds that store no ID (WeaponType).
    private static IReadOnlyList<GameDataEntry> LastOfEach(IReadOnlyList<GameDataEntry> entries) =>
        entries.Reverse().DistinctBy(e => e.Id.Length > 0 ? "id:" + e.Id : "name:" + e.Name).Reverse().ToList();

    private static bool IsPlayerClassComponent(IReadOnlyList<JsonElement> components) =>
        components.Any(c =>
            c.TryGetProperty("IsPlayerClass", out var p) &&
            p.GetString()?.Equals("true", StringComparison.OrdinalIgnoreCase) == true);

    public (string ConversationsRoot, string StringTablesRoot) GetBackupRoots()
        => (ConversationsRoot, StringTablesRoot);

    // Each DLC data folder's conversations and its text per installed language (issue 198).
    public IReadOnlyList<ExtraBackupFolder> GetExtraBackupFolders() =>
        Roots.Where(r => r.Key is not null).SelectMany(r =>
        {
            var folders = new List<ExtraBackupFolder>();
            if (Directory.Exists(ConversationsOf(r)))
                folders.Add(new($"dlc/{r.Folder}/conversations", ConversationsOf(r)));
            foreach (var lang in AvailableLanguages)
            {
                var text = Path.Combine(r.Path, "localized", lang, "text", "conversations");
                if (Directory.Exists(text)) folders.Add(new($"dlc/{r.Folder}/stringtables/{lang}", text));
            }
            return folders;
        }).ToList();

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

    public void InitializeConversationFile(ConversationFile file)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(file.ConversationPath)!);
        File.WriteAllText(file.ConversationPath, NewConversationTemplate(file.Name), System.Text.Encoding.UTF8);
    }

    /// One manifest per new conversation, prefixed so it can't collide with a shipped or DLC
    /// manifest, and so a Test Patch of one project can't overwrite another mod's entries.
    /// The game loads every *conversations.manifest (issue 162).
    public string? ConversationManifestPath(string name) =>
        Path.Combine(ExportedRoot, "design", "manifests", $"dialogeditor_{name}.conversations.manifest");

    /// The entry maps the conversation's ID to its bundle, relative to design/. The game
    /// resolves a name by the file name of Filename.
    [NotLocalised("Deadfire file format: type names and property values the game reads")]
    public void WriteConversationManifest(string name)
    {
        var path  = ConversationManifestPath(name)!;
        var entry = new JsonObject
        {
            ["$type"]          = "OEIFormats.Tools.ManifestData+ConversationManifestEntry, OEIFormats",
            ["DisplayType"]    = 0,
            ["IncludesPlayer"] = true,
            ["ID"]             = NewConversationId.For(name).ToString(),
            ["Filename"]       = $"/conversations/{name}.conversationbundle",
        };
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path,
            new JsonObject { ["Entries"] = new JsonArray(entry) }.ToJsonString(new JsonSerializerOptions { WriteIndented = true }),
            System.Text.Encoding.UTF8);
    }

    private const string ScriptNodeTypeName = "OEIFormats.FlowCharts.Conversations.ScriptNode, OEIFormats";

    /// A new conversation's bundle, with the conversation-level shape a shipped one has
    /// (issue 162). Deadfire loads a conversation by the ID its manifest entry names, and
    /// finds its text from the inner Filename (the part after "conversations/", as a
    /// .stringtable), so both must be set. The -200 conversation script node appears both
    /// as ConversationScriptNode and in Nodes, as in shipped bundles. The serializer copies
    /// -200 nodes verbatim and the parser never shows them.
    [NotLocalised("Deadfire file format: type names and property values the game reads")]
    private static string NewConversationTemplate(string name)
    {
        JsonObject ScriptNode() => new()
        {
            ["RequiresValidChildNode"] = false, ["NotSkippable"] = false, ["IsQuestionNode"] = false,
            ["HideSpeaker"] = false, ["IsTempText"] = false, ["PlayVOAs3DSound"] = false,
            ["PlayType"] = 0, ["Persistence"] = 0, ["NoPlayRandomWeight"] = 0, ["VOPositioning"] = 0,
            ["DisplayType"] = 1, ["NodeID"] = -200, ["ContainerNodeID"] = -1,
            ["Links"] = new JsonArray(),
            ["ClassExtender"] = new JsonObject { ["ExtendedProperties"] = new JsonArray() },
            ["Conditionals"] = new JsonObject { ["Operator"] = 0, ["Components"] = new JsonArray() },
            ["OnEnterScripts"] = new JsonArray(), ["OnExitScripts"] = new JsonArray(), ["OnUpdateScripts"] = new JsonArray(),
        };

        var nodeInList = new JsonObject { ["$type"] = ScriptNodeTypeName };
        foreach (var (key, value) in ScriptNode()) nodeInList[key] = value?.DeepClone();

        var conversation = new JsonObject
        {
            ["ID"]                     = NewConversationId.For(name).ToString(),
            ["Filename"]               = $"Conversations/{name}.conversation",
            ["ConversationScriptNode"] = ScriptNode(),
            ["Nodes"]                  = new JsonArray(nodeInList),
            ["ConversationType"]       = 0,
            ["CharacterMappings"]      = new JsonArray(),
            ["ClassExtender"]          = new JsonObject { ["ExtendedProperties"] = new JsonArray("DisplayType,Conversation") },
            ["UObjectName"]            = $"{name}_Base Game",
        };
        return new JsonObject { ["Conversations"] = new JsonArray(conversation) }
            .ToJsonString(new JsonSerializerOptions { WriteIndented = true });
    }
}
