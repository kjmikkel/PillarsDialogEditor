using AssetsTools.NET;

namespace DialogEditor.Core.Audio;

/// A PoE1 VOAsset MonoBehaviour from resources.assets (issue 355).
///
/// There is no type tree, so the object is read by hand in the Unity 2019.4 layout. Its
/// VOClip.clip string names the Resources path of the AudioClip the game plays for the line.
public static class Poe1VoAssetRecord
{
    /// Reads the VOClip.clip string; the reader must be positioned at the start of the object.
    /// Null when the script reference is missing: that is not a VOAsset (the game's
    /// Resources.LoadAll skips it).
    public static string? ReadClipPath(AssetsFileReader r)
    {
        r.ReadInt32(); r.ReadInt64();                   // m_GameObject
        r.ReadBoolean(); r.Align();                     // m_Enabled
        r.ReadInt32();                                  // m_Script fileID
        var scriptPathId = r.ReadInt64();
        if (scriptPathId == 0) return null;
        r.ReadCountStringInt32(); r.Align();            // m_Name
        r.ReadInt32(); r.ReadInt32(); r.ReadUInt32();   // StringID: StringTable, StringID, Package
        var clip = r.ReadCountStringInt32(); r.Align();
        return clip;
    }
}
