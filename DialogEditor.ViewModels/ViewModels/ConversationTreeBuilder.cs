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
/// </list>
/// </summary>
public static class ConversationTreeBuilder
{
    // Virtual paths for the DLC groups. '*' can't occur in a real folder name, so they never
    // collide with one, and as a tooltip "px1_*" says what the group holds.
    public const string WhiteMarch1Path = "px1_*";
    public const string WhiteMarch2Path = "px2_*";

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

        return new ConversationTree(folders, rootItems);
    }

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
