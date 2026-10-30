using AssetsTools.NET;

namespace DialogEditor.Core.Audio;

/// One m_Container entry of the ResourceManager: a lower-case Resources path and the object it names.
public sealed record Poe1ResourceEntry(string Path, int FileId, long PathId);

/// Reads the ResourceManager's m_Container from PoE1's globalgamemanagers (issue 355).
///
/// globalgamemanagers has no type tree, so the object is read by hand in the Unity 2019.4 layout.
/// The game resolves Resources paths through this table.
public static class Poe1ResourceContainer
{
    /// Reads m_Container; the reader must be positioned at the start of the ResourceManager object.
    /// Stops after the container (m_DependentAssets is not needed).
    public static IReadOnlyList<Poe1ResourceEntry> Parse(AssetsFileReader r)
    {
        var count = r.ReadInt32();
        if (count < 0) throw new InvalidDataException($"ResourceManager container has a negative entry count ({count}).");

        var entries = new List<Poe1ResourceEntry>(Math.Min(count, 1 << 20));
        for (var i = 0; i < count; i++)
        {
            var path = r.ReadCountStringInt32(); r.Align();
            var fileId = r.ReadInt32();
            var pathId = r.ReadInt64();
            entries.Add(new Poe1ResourceEntry(path, fileId, pathId));
        }
        return entries;
    }
}
