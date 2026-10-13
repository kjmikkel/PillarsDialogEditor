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
/// <item>PoE1's White March folders (<c>px1_*</c>, <c>px2_*</c>) are grouped after the base game,
/// without their prefix. Other prefixes (PoE2's <c>lax02_</c>, PoE1's <c>px4</c>) are not
/// grouped: there's no mapping to a named DLC I'm sure of, and a wrong label is worse than none.</item>
/// <item>Developer leftovers go in one collapsed group at the end of the folders (part D;
/// docs/superpowers/specs/2026-10-13-conversations-dock-d-prototypes-design.md): top-level
/// prototype and test folders, and top-level conversations with "test" as a whole word. They are
/// grouped, not hidden, so a writer can still open them and search still finds them. Nothing
/// inside a real folder moves, since that would hide where it lives.</item>
/// </list>
/// </summary>
public static partial class ConversationTreeBuilder
{
    // Virtual paths for the DLC groups. '*' can't occur in a real folder name, so they never
    // collide with one, and as a tooltip "px1_*" says what the group holds.
    public const string WhiteMarch1Path = "px1_*";
    public const string WhiteMarch2Path = "px2_*";
    public const string PrototypesPath  = "*prototype+test*";

    private static readonly (string Prefix, string Path, string NameKey)[] DlcGroups =
    [
        ("px1_", WhiteMarch1Path, "Browser_Dlc_WhiteMarch1"),
        ("px2_", WhiteMarch2Path, "Browser_Dlc_WhiteMarch2"),
    ];

    public static ConversationTree Build(IEnumerable<ConversationFile> files)
    {
        var byPath = new Dictionary<string, ConversationFolderViewModel>(StringComparer.Ordinal);
        var top = new List<ConversationFolderViewModel>();
        var rootItems = new List<ConversationItemViewModel>();

        ConversationFolderViewModel FolderFor(string path)
        {
            if (byPath.TryGetValue(path, out var existing)) return existing;
            var sep = path.LastIndexOf('\\');
            ConversationFolderViewModel folder;
            if (sep < 0)
            {
                folder = new ConversationFolderViewModel(path, displayName: DlcFolderName(path));
                top.Add(folder);
            }
            else
            {
                folder = new ConversationFolderViewModel(path);
                FolderFor(path[..sep]).Subfolders.Add(folder);
            }
            byPath[path] = folder;
            return folder;
        }

        foreach (var file in files)
        {
            var path = file.FolderPath.Replace('/', '\\').Trim('\\');
            if (path.Length == 0) rootItems.Add(new ConversationItemViewModel(file));
            else FolderFor(path).Items.Add(new ConversationItemViewModel(file));
        }

        var folders = Sorted(top.Where(f => DlcGroupOf(f.FolderPath) is null)).ToList();
        foreach (var (prefix, groupPath, nameKey) in DlcGroups)
        {
            var members = Sorted(top.Where(f => f.FolderPath.StartsWith(prefix, StringComparison.Ordinal))).ToList();
            if (members.Count == 0) continue;
            var group = new ConversationFolderViewModel(groupPath, displayName: Loc.Get(nameKey));
            foreach (var m in members) group.Subfolders.Add(m);
            folders.Add(group);
        }
        foreach (var f in byPath.Values) SortSubfolders(f);

        var leftoverFolders = folders.Where(f => IsPrototypeFolder(f.FolderPath)).ToList();
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

    private static string? DlcGroupOf(string topLevelPath) =>
        DlcGroups.Select(g => g.Prefix).FirstOrDefault(p => topLevelPath.StartsWith(p, StringComparison.Ordinal));

    // A DLC folder's name without its prefix (px1_00_stalwart_village → "Stalwart Village");
    // null for any other folder, which then takes the usual tidied name.
    private static string? DlcFolderName(string topLevelPath) =>
        DlcGroupOf(topLevelPath) is { } prefix ? FolderDisplayName.For(topLevelPath[prefix.Length..]) : null;

    private static IEnumerable<ConversationFolderViewModel> Sorted(IEnumerable<ConversationFolderViewModel> folders) =>
        folders.OrderBy(f => f.FolderPath, StringComparer.Ordinal);

    private static void SortSubfolders(ConversationFolderViewModel folder)
    {
        var sorted = Sorted(folder.Subfolders).ToList();
        folder.Subfolders.Clear();
        foreach (var s in sorted) folder.Subfolders.Add(s);
    }
}
