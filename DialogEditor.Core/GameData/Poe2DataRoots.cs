namespace DialogEditor.Core.GameData;

/// <summary>One of Deadfire's data folders that exists in an install: its name, the prefix its
/// own bundle files carry (<c>lax1_speakers.gamedatabundle</c>), and its full path.</summary>
public sealed record Poe2DataRoot(string Folder, string Prefix, string Path)
{
    /// <summary>The DLC key ("lax2"), or null for the base game's folder.</summary>
    public string? Key => Prefix.Length == 0 ? null : Prefix.TrimEnd('_');
}

/// <summary>
/// Deadfire's data folders (issue 198). The base game's data is in <c>exported</c>, each DLC's
/// in its own <c>&lt;prefix&gt;exported</c> folder beside it, with the same layout (design/,
/// localized/). The list and its order are the game's own,
/// <c>ProductConfiguration.PackageDataFolders</c> and <c>PackageDataPrefixes</c>: the game loads
/// the folders in this order, so a later folder's entry wins over an earlier one's.
/// </summary>
public static class Poe2DataRoots
{
    public const string DataFolder = "PillarsOfEternityII_Data";
    public const string Base = "exported";

    public static readonly IReadOnlyList<(string Folder, string Prefix)> All =
    [
        ("exported", ""), ("bb_exported", "bb_"), ("lax1_exported", "lax1_"), ("lax2_exported", "lax2_"),
        ("lax3_exported", "lax3_"), ("lax4_exported", "lax4_"), ("laxa_exported", "laxa_"),
        ("laxb_exported", "laxb_"), ("laxc_exported", "laxc_"), ("laxd_exported", "laxd_"),
        ("unused_exported", "unused_"), ("laxe_exported", "laxe_"), ("laxf_exported", "laxf_"),
        ("laxg_exported", "laxg_"), ("laxh_exported", "laxh_"), ("laxi_exported", "laxi_"),
    ];

    public static IReadOnlyList<Poe2DataRoot> Existing(string gameDir) =>
        All.Select(a => new Poe2DataRoot(a.Folder, a.Prefix, System.IO.Path.Combine(gameDir, DataFolder, a.Folder)))
           .Where(r => Directory.Exists(r.Path))
           .ToList();

    public static string? KeyOf(string folder) =>
        All.Where(a => a.Folder == folder && a.Prefix.Length > 0).Select(a => a.Prefix.TrimEnd('_')).FirstOrDefault();
}
