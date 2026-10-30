using AssetsTools.NET;
using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.Audio;

/// Issue 355: a PoE1 VOAsset is a MonoBehaviour with no type tree; its VOClip.clip string names
/// the Resources path of the AudioClip the game plays.
public class Poe1VoAssetRecordTests
{
    private static byte[] Build(string name, string clip, long scriptPathId = 84)
    {
        var ms = new MemoryStream();
        var w = new AssetsFileWriter(ms);
        w.Write(0); w.Write(0L);                    // m_GameObject
        w.Write((byte)1); w.Align();                // m_Enabled
        w.Write(1); w.Write(scriptPathId);          // m_Script
        w.WriteCountStringInt32(name); w.Align();   // m_Name
        w.Write(892); w.Write(29); w.Write(0u);     // StringID: StringTable, StringID, Package
        w.WriteCountStringInt32(clip); w.Align();   // VOClip.clip
        w.Write(1f); w.Write(1f); w.Write(1f);      // Min/MaxVolume, PlayFrequency
        w.Flush();
        return ms.ToArray();
    }

    private static string? Read(byte[] bytes) =>
        Poe1VoAssetRecord.ReadClipPath(new AssetsFileReader(new MemoryStream(bytes)));

    [Theory]
    [InlineData("a", "x")]
    [InlineData("abcd", "Audio/Vocalization/VO Wav Files/Speaker/abcd.wav")]
    [InlineData("07_cv_deathguard_0029", "Audio/Vocalization/VO Wav Files/Lord Raedric VII/07_cv_deathguard_0029.wav")]
    public void ReadClipPath_ReturnsTheVoClipString_WhateverTheNamePadding(string name, string clip)
    {
        Assert.Equal(clip, Read(Build(name, clip)));
    }

    [Fact]
    public void ReadClipPath_EmptyClip_IsEmpty()
    {
        Assert.Equal("", Read(Build("c_0001", "")));
    }

    // A MonoBehaviour whose script is missing is not a VOAsset: Resources.LoadAll<VOAsset> skips it.
    [Fact]
    public void ReadClipPath_NoScript_IsNull()
    {
        Assert.Null(Read(Build("c_0001", "Audio/x.wav", scriptPathId: 0)));
    }

    // The 07_cv_deathguard_0029 VOAsset as shipped in PoE1's resources.assets (Unity 2019.4).
    [Fact]
    public void ReadClipPath_ShippedBytes()
    {
        var bytes = Convert.FromHexString(
            "000000000000000000000000" + "01000000" + "01000000" + "5400000000000000" +
            "15000000" + "30375F63765F646561746867756172645F30303239" + "000000" +
            "7C030000" + "1D000000" + "00000000" +
            "4A000000" + Convert.ToHexString(
                "Audio/Vocalization/VO Wav Files/Lord Raedric VII/07_cv_deathguard_0029.wav"u8) + "0000" +
            "0000803F0000803F0000803F" + "FFFFFFFF" + "00000000");

        Assert.Equal("Audio/Vocalization/VO Wav Files/Lord Raedric VII/07_cv_deathguard_0029.wav", Read(bytes));
    }
}
