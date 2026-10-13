namespace DialogEditor.Core.Models;

public class StringTable
{
    private readonly Dictionary<int, StringEntry> _entries;

    /// A loaded (or deliberately empty, e.g. brand-new conversation) table with no entries.
    public static readonly StringTable Empty = new([]);

    /// Sentinel for "the conversation's .stringtable file does not exist". Distinct from
    /// <see cref="Empty"/> so the UI can tell a genuinely missing table (warn the user)
    /// apart from a loaded table that just has no entry for a node — which is normal for
    /// script/trigger nodes (#84).
    public static readonly StringTable Missing = new([]) { IsMissing = true };

    public StringTable(IEnumerable<StringEntry> entries)
        => _entries = entries.ToDictionary(e => e.Id);

    public StringEntry? Get(int id) => _entries.GetValueOrDefault(id);

    public int Count => _entries.Count;

    /// Every entry, e.g. to merge several data folders' tables line by line (issue 198).
    public IEnumerable<StringEntry> Entries => _entries.Values;

    /// Several data folders' tables for one conversation, in the game's folder order, merged as
    /// both games read them: each line from the last folder that has it (issues 198, 201). No
    /// table is <see cref="Missing"/>; a damaged one is reported as before (issue 119).
    public static StringTable MergeLaterWins(IReadOnlyList<StringTable> tables)
    {
        if (tables.Count == 0) return Missing;
        if (tables.FirstOrDefault(t => t.IsUnreadable) is { } unreadable) return unreadable;
        if (tables.Count == 1) return tables[0];

        var lines = new Dictionary<int, StringEntry>();
        foreach (var table in tables)
            foreach (var entry in table.Entries) lines[entry.Id] = entry;
        return new StringTable(lines.Values);
    }

    /// A stringtable file that exists but is not readable XML (issue 119 — e.g. a damaged install
    /// file). The conversation still opens: the table counts as missing, so its text shows
    /// the missing-text placeholder, and <see cref="IsUnreadable"/> lets the UI say why.
    public static StringTable Unreadable(string path) =>
        new([]) { IsMissing = true, IsUnreadable = true, UnreadablePath = path };

    /// True when the string table could not be found on disk, or could not be read.
    public bool IsMissing { get; private init; }

    /// True when the file exists but is not readable XML (see <see cref="Unreadable"/>).
    public bool IsUnreadable { get; private init; }

    /// The damaged file, when <see cref="IsUnreadable"/>.
    public string? UnreadablePath { get; private init; }
}
