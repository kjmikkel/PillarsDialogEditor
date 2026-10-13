using System.Text.RegularExpressions;

namespace DialogEditor.ViewModels;

/// <summary>
/// The Conversations dock's display name for a game folder (issue 85, part B):
/// <c>07_neketaka_temple_district</c> → "Neketaka Temple District". The leading order number is
/// dropped (the dock still sorts by the raw name, so the game's order survives), underscores
/// become spaces, and each word is capitalised. The rule is deliberately generic: an odd name
/// stays readable (<c>lax02_01_iceberg_dungeon</c> → "Lax02 01 Iceberg Dungeon"), and the raw
/// folder path is the row's tooltip. Conversation names are identifiers and are never tidied.
/// </summary>
public static partial class FolderDisplayName
{
    [GeneratedRegex(@"^\d+_")]
    private static partial Regex OrderPrefix();

    public static string For(string segment)
    {
        var name = OrderPrefix().Replace(segment, string.Empty).Replace('_', ' ');
        var words = name.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        // Nothing left once the prefix is gone ("07_"): the raw name beats an empty label.
        if (words.Length == 0) return segment;
        return string.Join(' ', words.Select(w => char.ToUpperInvariant(w[0]) + w[1..]));
    }
}
