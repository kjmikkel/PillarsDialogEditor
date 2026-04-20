namespace DialogEditor.Core.Models;

public class StringTable
{
    private readonly Dictionary<int, StringEntry> _entries;

    public static readonly StringTable Empty = new([]);

    public StringTable(IEnumerable<StringEntry> entries)
        => _entries = entries.ToDictionary(e => e.Id);

    public StringEntry? Get(int id) => _entries.GetValueOrDefault(id);

    public int Count => _entries.Count;
}
