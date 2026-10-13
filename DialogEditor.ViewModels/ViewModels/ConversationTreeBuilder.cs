using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

/// <summary>The Conversations dock's tree: top-level folders, then the game's top-level conversations.</summary>
public sealed record ConversationTree(
    IReadOnlyList<ConversationFolderViewModel> Folders,
    IReadOnlyList<ConversationItemViewModel> RootItems);

/// <summary>
/// Builds the Conversations dock's tree from the game's conversation files (issue 85, part B;
/// docs/superpowers/specs/2026-10-13-conversations-dock-b-names-design.md).
/// <list type="bullet">
/// <item>Folder paths nest: <c>companions\poi</c> is a subfolder of <c>companions</c>, and a
/// missing parent is created.</item>
/// <item>Folders sort by their raw path, so the game's numbering (02, 03 … 28) still orders them
/// once <see cref="FolderDisplayName"/> hides it.</item>
/// <item>Conversations in the game's top conversations folder stay at the top level, after the
/// folders, instead of in a "(root)" folder that said nothing.</item>
/// <item>DLC content is grouped under the DLC's title, after the base game: PoE1's White March
/// (<c>px1_*</c>, <c>px2_*</c> folders) and, issue 198, PoE2's DLC, by the data folder a
/// conversation was read from (<c>lax2_exported</c>) or a base-game folder named for a DLC
/// (<c>lax02_01_iceberg_dungeon</c>, <c>laxi</c>). Inside a group the prefix is dropped. A data
/// folder with no known title is grouped under its tidied key rather than mislabelled.</item>
/// <item>Developer leftovers go in one collapsed group at the end of the folders (part D;
/// docs/superpowers/specs/2026-10-13-conversations-dock-d-prototypes-design.md): top-level
/// prototype and test folders, and top-level conversations with "test" as a whole word. They are
/// grouped, not hidden, so a writer can still open them and search still finds them. Nothing
/// inside a real folder moves, since that would hide where it lives.</item>
/// </list>
/// </summary>
public static partial class ConversationTreeBuilder
{
    // Virtual paths for the groups. '*' can't occur in a real folder name, so they never collide
    // with one, and as a tooltip "px1_*" says what the group holds.
    public const string PrototypesPath  = "*prototype+test*";

    // DLC groups in display order (issue 198 for PoE2). Keys are the data folders' prefixes
    // without "_" (Poe2DataRoots); PoE1's White March folders carry px1_/px2_.
    private static readonly (string Key, string NameKey)[] DlcGroups =
    [
        ("px1", "Browser_Dlc_WhiteMarch1"), ("px2", "Browser_Dlc_WhiteMarch2"),
        ("lax1", "Browser_Dlc_Lax1"), ("lax2", "Browser_Dlc_Lax2"), ("lax3", "Browser_Dlc_Lax3"),
        ("laxa", "Browser_Dlc_LaxA"), ("laxb", "Browser_Dlc_LaxB"), ("laxc", "Browser_Dlc_LaxC"),
        ("laxd", "Browser_Dlc_LaxD"), ("laxe", "Browser_Dlc_LaxE"), ("laxf", "Browser_Dlc_LaxF"),
        ("laxg", "Browser_Dlc_LaxG"), ("laxi", "Browser_Dlc_LaxI"),
    ];

    public static string GroupPath(string key) => key + "_*";
    public static readonly string WhiteMarch1Path = GroupPath("px1");
    public static readonly string WhiteMarch2Path = GroupPath("px2");

    // A base-game folder named for a DLC: px1_…, lax02_… or lax2_… (both spellings occur),
    // laxa_…, or exactly "laxi". Only keys of a real data folder or a titled group count.
    [System.Text.RegularExpressions.GeneratedRegex(@"^(px[12]|lax0?(?<n>\d)|lax(?<l>[a-z]))(_|$)")]
    private static partial System.Text.RegularExpressions.Regex DlcFolderPrefix();

    private static (string Key, string Token)? DlcOf(string topSegment)
    {
        var m = DlcFolderPrefix().Match(topSegment);
        if (!m.Success) return null;
        var key = m.Groups["n"].Success ? "lax" + m.Groups["n"].Value
                : m.Groups["l"].Success ? "lax" + m.Groups["l"].Value
                : m.Groups[1].Value;
        var known = DlcGroups.Any(g => g.Key == key) || Poe2DataRoots.All.Any(r => r.Prefix == key + "_");
        return known ? (key, m.Groups[1].Value) : null;
    }

    private static string GroupName(string key) =>
        DlcGroups.Where(g => g.Key == key).Select(g => Loc.Get(g.NameKey)).FirstOrDefault()
        ?? FolderDisplayName.For(key);

    public static ConversationTree Build(IEnumerable<ConversationFile> files)
    {
        var byId   = new Dictionary<string, ConversationFolderViewModel>(StringComparer.Ordinal);
        var top    = new List<ConversationFolderViewModel>();                                 // base game
        var groups = new Dictionary<string, ConversationFolderViewModel>(StringComparer.Ordinal); // DLC key → group
        var rootItems = new List<ConversationItemViewModel>();

        ConversationFolderViewModel Group(string key)
        {
            if (!groups.TryGetValue(key, out var g))
                groups[key] = g = new ConversationFolderViewModel(GroupPath(key), displayName: GroupName(key));
            return g;
        }

        // A folder is identified by its group and path, so the base game's 16_wahaki_island and
        // Beast of Winter's stay apart, while both games' lax02_01_iceberg_dungeon merge.
        ConversationFolderViewModel FolderFor(string? group, string path)
        {
            var id = (group ?? string.Empty) + "|" + path;
            if (byId.TryGetValue(id, out var existing)) return existing;
            var sep = path.LastIndexOf('\\');
            ConversationFolderViewModel folder;
            if (sep >= 0)
            {
                folder = new ConversationFolderViewModel(path);
                FolderFor(group, path[..sep]).Subfolders.Add(folder);
            }
            else if (group is null)
            {
                folder = new ConversationFolderViewModel(path);
                top.Add(folder);
            }
            else
            {
                // Inside a group the DLC prefix says nothing: px1_00_stalwart_village → "Stalwart Village".
                var token = DlcOf(path)?.Token;
                var shown = token is null ? path : path[token.Length..].TrimStart('_');
                folder = new ConversationFolderViewModel(path, displayName: FolderDisplayName.For(shown));
                Group(group).Subfolders.Add(folder);
            }
            byId[id] = folder;
            return folder;
        }

        foreach (var file in files)
        {
            var path = file.FolderPath.Replace('/', '\\').Trim('\\');
            var item = new ConversationItemViewModel(file);
            var topSegment = path.Split('\\')[0];

            string? group = file.DataRoot != ConversationFile.BaseDataRoot
                ? Poe2DataRoots.KeyOf(file.DataRoot)
                : DlcOf(topSegment)?.Key;

            if (group is null)
            {
                if (path.Length == 0) rootItems.Add(item);
                else FolderFor(null, path).Items.Add(item);
                continue;
            }

            // A top folder named just for the DLC ("laxi") is the group itself.
            if (DlcOf(topSegment) is { } dlc && topSegment == dlc.Token && dlc.Key == group)
                path = path.Length == topSegment.Length ? string.Empty : path[(topSegment.Length + 1)..];

            if (path.Length == 0) Group(group).Items.Add(item);
            else FolderFor(group, path).Items.Add(item);
        }

        var folders = Sorted(top).ToList();
        foreach (var (key, _) in DlcGroups)
            if (groups.TryGetValue(key, out var g)) folders.Add(g);
        foreach (var key in groups.Keys.Where(k => DlcGroups.All(d => d.Key != k)).Order(StringComparer.Ordinal))
            folders.Add(groups[key]);
        foreach (var f in byId.Values.Concat(groups.Values)) SortSubfolders(f);

        // Prototype and test content (part D), from the base game's top level only.
        var leftoverFolders = top.Where(f => IsPrototypeFolder(f.FolderPath)).ToList();
        var leftoverItems = rootItems.Where(i => IsTestConversation(i.Name)).ToList();
        if (leftoverFolders.Count + leftoverItems.Count > 0)
        {
            var group = new ConversationFolderViewModel(PrototypesPath, leftoverItems,
                displayName: Loc.Get("Browser_PrototypesFolder"), toolTip: Loc.Get("ToolTip_PrototypesFolder"));
            foreach (var f in leftoverFolders) { folders.Remove(f); group.Subfolders.Add(f); }
            foreach (var i in leftoverItems) rootItems.Remove(i);
            folders.Add(group);
        }

        return new ConversationTree(folders, rootItems);
    }

    // 00_prototype (PoE2), prototype_2 and test (PoE1): the name after the order prefix.
    [System.Text.RegularExpressions.GeneratedRegex(@"^(\d+_)?(prototype(_\d+)?|test)$")]
    private static partial System.Text.RegularExpressions.Regex PrototypeFolder();

    // "test" as a whole underscore-separated word: si_test, test_cv_zuhira_stub, cv_test_pet_sac;
    // not contest_scene or testament.
    [System.Text.RegularExpressions.GeneratedRegex(@"(^|_)test(_|$)")]
    private static partial System.Text.RegularExpressions.Regex TestWord();

    private static bool IsPrototypeFolder(string topLevelPath) => PrototypeFolder().IsMatch(topLevelPath);
    private static bool IsTestConversation(string name) => TestWord().IsMatch(name);

    private static IEnumerable<ConversationFolderViewModel> Sorted(IEnumerable<ConversationFolderViewModel> folders) =>
        folders.OrderBy(f => f.FolderPath, StringComparer.Ordinal);

    private static void SortSubfolders(ConversationFolderViewModel folder)
    {
        var sorted = Sorted(folder.Subfolders).ToList();
        folder.Subfolders.Clear();
        foreach (var s in sorted) folder.Subfolders.Add(s);
    }
}
