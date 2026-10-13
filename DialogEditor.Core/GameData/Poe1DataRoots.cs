namespace DialogEditor.Core.GameData;

/// <summary>One of Pillars of Eternity's data folders that exists in an install.</summary>
public sealed record Poe1DataRoot(string Folder, string Path);

/// <summary>
/// Pillars of Eternity's data folders (issue 201). The base game's data is in <c>data</c>; The White
/// March's text is in <c>data_expansion1</c> and <c>data_expansion2</c>, with the same layout
/// (localized/), while its conversations sit in <c>data</c>. The list and its order are the game's
/// own <c>ProductConfiguration.PackageDataFolders</c>, less <c>data_backerbeta</c>, which
/// <c>StringTableManager</c> skips. A later folder's line wins over an earlier one's.
/// </summary>
public static class Poe1DataRoots
{
    public const string DataFolder = "PillarsOfEternity_Data";
    public const string Base = "data";

    public static readonly IReadOnlyList<string> All = ["data", "data_expansion1", "data_expansion2", "data_expansion4"];

    public static IReadOnlyList<Poe1DataRoot> Existing(string gameDir) =>
        All.Select(f => new Poe1DataRoot(f, System.IO.Path.Combine(gameDir, DataFolder, f)))
           .Where(r => Directory.Exists(r.Path))
           .ToList();
}
