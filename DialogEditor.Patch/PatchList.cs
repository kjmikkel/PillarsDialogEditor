namespace DialogEditor.Patch;

public record PatchList(
    int SchemaVersion,
    string GameFolder,
    IReadOnlyList<PatchListEntry> Entries)
{
    public static readonly int CurrentSchemaVersion = 1;
    public static PatchList Empty() => new(CurrentSchemaVersion, string.Empty, []);

    public PatchList WithEntry(PatchListEntry entry)
        => this with { Entries = [.. Entries, entry] };

    public PatchList WithoutEntry(PatchListEntry entry)
        => this with { Entries = Entries.Where(e => e != entry).ToList() };

    public PatchList WithGameFolder(string folder)
        => this with { GameFolder = folder };

    public PatchList MoveEntryUp(PatchListEntry entry)
    {
        var list = Entries.ToList();
        var i = list.IndexOf(entry);
        if (i <= 0) return this;
        list.RemoveAt(i);
        list.Insert(i - 1, entry);
        return this with { Entries = list };
    }

    public PatchList MoveEntryDown(PatchListEntry entry)
    {
        var list = Entries.ToList();
        var i = list.IndexOf(entry);
        if (i < 0 || i >= list.Count - 1) return this;
        list.RemoveAt(i);
        list.Insert(i + 1, entry);
        return this with { Entries = list };
    }
}

public record PatchListEntry(
    string RelativePath,
    string AbsolutePath);
