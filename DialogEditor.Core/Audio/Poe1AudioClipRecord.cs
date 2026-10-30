using AssetsTools.NET;

namespace DialogEditor.Core.Audio;

/// One Unity AudioClip object from PoE1's resources.assets (issue 4).
///
/// resources.assets has no type tree, so the object is read by hand in the Unity 2019.4
/// layout. The fields the editor never uses are still read: each one moves the cursor.
public sealed record Poe1AudioClipRecord(
    string Name, int Frequency, float Length, string Source, long Offset, long Size)
{
    /// The game skips clips of exactly this many samples at this rate: they stand in for
    /// "no voice" (GameResources.GetDialogueAudio).
    private const int PlaceholderFrequency = 44100;
    private const int PlaceholderSamples = 14976;

    /// The object's PathId in resources.assets: what the ResourceManager container points at.
    /// Not part of the object body, so Parse leaves it 0 and Load sets it.
    public long PathId { get; init; }

    public bool IsPlaceholder =>
        Frequency == PlaceholderFrequency
        && (int)Math.Round(Length * Frequency) == PlaceholderSamples;

    /// Reads one AudioClip body; the reader must be positioned at the start of the object.
    public static Poe1AudioClipRecord Parse(AssetsFileReader r)
    {
        var name = r.ReadCountStringInt32(); r.Align();
        r.ReadInt32();                                  // m_LoadType
        r.ReadInt32();                                  // m_Channels
        var frequency = r.ReadInt32();
        r.ReadInt32();                                  // m_BitsPerSample
        var length = r.ReadSingle();
        r.ReadBoolean(); r.ReadBoolean(); r.Align();    // m_IsTrackerFormat, m_Ambisonic
        r.ReadInt32();                                  // m_SubsoundIndex
        r.ReadBoolean(); r.ReadBoolean(); r.ReadBoolean(); r.Align(); // preload, background, legacy3D
        var source = r.ReadCountStringInt32(); r.Align();
        var offset = r.ReadUInt64();
        var size = r.ReadUInt64();
        r.ReadInt32();                                  // m_CompressionFormat
        return new Poe1AudioClipRecord(name, frequency, length, source, (long)offset, (long)size);
    }
}
