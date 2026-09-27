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

    /// True only when the string table could not be found on disk.
    public bool IsMissing { get; private init; }
}
