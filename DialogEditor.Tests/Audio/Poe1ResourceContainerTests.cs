using AssetsTools.NET;
using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.Audio;

/// Issue 355: the ResourceManager's m_Container (globalgamemanagers) maps Resources paths to
/// objects; PoE1 follows it from a conversation's VOAsset to the AudioClip it plays.
public class Poe1ResourceContainerTests
{
    private const int Sentinel = 0x7EADBEEF;

    private static byte[] Build(params (string Path, int FileId, long PathId)[] entries)
    {
        var ms = new MemoryStream();
        var w = new AssetsFileWriter(ms);
        w.Write(entries.Length);
        foreach (var (path, fileId, pathId) in entries)
        {
            w.WriteCountStringInt32(path); w.Align();
            w.Write(fileId);
            w.Write(pathId);
        }
        w.Write(Sentinel);     // proves the reader stopped on the right byte
        w.Flush();
        return ms.ToArray();
    }

    [Fact]
    public void Parse_ReadsEveryEntryInOrder_WhateverThePathPadding()
    {
        var bytes = Build(("a", 3, 1L), ("ab/cd", 7, 123456789012L), ("abcd", 0, -5L));
        var r = new AssetsFileReader(new MemoryStream(bytes));

        var entries = Poe1ResourceContainer.Parse(r);

        Assert.Equal(
            [new Poe1ResourceEntry("a", 3, 1L), new Poe1ResourceEntry("ab/cd", 7, 123456789012L),
             new Poe1ResourceEntry("abcd", 0, -5L)],
            entries);
        Assert.Equal(Sentinel, r.ReadInt32());
    }

    [Fact]
    public void Parse_EmptyContainer_IsEmpty()
    {
        var r = new AssetsFileReader(new MemoryStream(Build()));

        Assert.Empty(Poe1ResourceContainer.Parse(r));
        Assert.Equal(Sentinel, r.ReadInt32());
    }

    [Fact]
    public void Parse_NegativeCount_Throws()
    {
        var ms = new MemoryStream();
        var w = new AssetsFileWriter(ms);
        w.Write(-1);
        w.Flush();

        Assert.Throws<InvalidDataException>(() =>
            Poe1ResourceContainer.Parse(new AssetsFileReader(new MemoryStream(ms.ToArray()))));
    }
}
