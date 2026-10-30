using AssetsTools.NET;
using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.Audio;

/// The Unity 2019.4 AudioClip layout in PoE1's resources.assets has no type tree, so the
/// reader walks it by hand. These tests build the bytes the way Unity lays them out,
/// including the 4-byte alignment after the name, the flag bytes and the resource source.
public class Poe1AudioClipRecordTests
{
    private const int Sentinel = 0x7EADBEEF;

    private static byte[] Build(string name, int frequency, float length, string source,
                                ulong offset, ulong size)
    {
        var ms = new MemoryStream();
        var w = new AssetsFileWriter(ms);
        w.WriteCountStringInt32(name); w.Align();
        w.Write(1);            // m_LoadType
        w.Write(2);            // m_Channels
        w.Write(frequency);
        w.Write(16);           // m_BitsPerSample
        w.Write(length);
        w.Write(false); w.Write(false); w.Align();                 // tracker, ambisonic
        w.Write(0);            // m_SubsoundIndex
        w.Write(true); w.Write(false); w.Write(false); w.Align();  // preload, background, legacy3D
        w.WriteCountStringInt32(source); w.Align();
        w.Write(offset); w.Write(size);
        w.Write(3);            // m_CompressionFormat
        w.Write(Sentinel);     // proves the reader stopped on the right byte
        w.Flush();
        return ms.ToArray();
    }

    private static (Poe1AudioClipRecord Record, int Next) Read(byte[] bytes)
    {
        var r = new AssetsFileReader(new MemoryStream(bytes));
        var rec = Poe1AudioClipRecord.Parse(r);
        return (rec, r.ReadInt32());
    }

    [Theory]
    [InlineData("a")]                  // 1 byte: 3 bytes of padding
    [InlineData("ab")]
    [InlineData("abc")]
    [InlineData("abcd")]               // already aligned: no padding
    [InlineData("00_cv_beodmar_0089")] // 18 bytes: 2 bytes of padding
    public void Parse_ReadsEveryField_WhateverTheNamePadding(string name)
    {
        var (rec, next) = Read(Build(name, 44100, 2.5f, "resources.resource", 123456789012UL, 4321UL));

        Assert.Equal(name, rec.Name);
        Assert.Equal(44100, rec.Frequency);
        Assert.Equal(2.5f, rec.Length);
        Assert.Equal("resources.resource", rec.Source);
        Assert.Equal(123456789012L, rec.Offset);
        Assert.Equal(4321L, rec.Size);
        Assert.Equal(Sentinel, next);
    }

    [Theory]
    [InlineData("x")]
    [InlineData("archive:/CAB-1/res")] // 18 bytes: padding 2
    public void Parse_AlignsAfterTheSource(string source)
    {
        var (rec, next) = Read(Build("clip", 48000, 1f, source, 7UL, 9UL));

        Assert.Equal(source, rec.Source);
        Assert.Equal(7L, rec.Offset);
        Assert.Equal(9L, rec.Size);
        Assert.Equal(Sentinel, next);
    }

    // The game skips clips of exactly 14,976 samples at 44,100 Hz: stand-ins with no audio.
    [Theory]
    [InlineData(44100, 14976 / 44100f, true)]
    [InlineData(44100, 14977 / 44100f, false)]
    [InlineData(22050, 14976 / 22050f, false)]
    [InlineData(44100, 3f, false)]
    public void IsPlaceholder_IsFrequency44100AndExactly14976Samples(int frequency, float length, bool expected)
    {
        var rec = new Poe1AudioClipRecord("c_0001", frequency, length, "resources.resource", 0, 10);

        Assert.Equal(expected, rec.IsPlaceholder);
    }
}
