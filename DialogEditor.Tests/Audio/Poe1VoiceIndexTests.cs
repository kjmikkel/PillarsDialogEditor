using System.Text;
using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.Audio;

public class Poe1VoiceIndexTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poe1vo-" + Guid.NewGuid().ToString("N"));
    private string DataDir => Path.Combine(_root, "data");
    private string CacheDir => Path.Combine(_root, "cache");

    public Poe1VoiceIndexTests() => Directory.CreateDirectory(DataDir);

    public void Dispose()
    {
        try { Directory.Delete(_root, true); } catch { /* best-effort */ }
    }

    private static Poe1AudioClipRecord Rec(string name, int hz = 48000, float len = 2f,
                                           string source = "resources.resource", long off = 0, long size = 8) =>
        new(name, hz, len, source, off, size);

    private Poe1VoiceIndex Index(params Poe1AudioClipRecord[] recs) => new(recs, DataDir);

    // ---- naming -----------------------------------------------------------------------

    [Fact]
    public void Find_MatchesConversationAndZeroPaddedNode()
    {
        var idx = Index(Rec("00_cv_beodmar_0089"));

        var clip = idx.Find("00_cv_beodmar", 89, false);

        Assert.NotNull(clip);
        Assert.Equal("00_cv_beodmar_0089", clip!.Name);
    }

    [Fact]
    public void Find_IsCaseInsensitive()
    {
        var idx = Index(Rec("00_CV_Beodmar_0089"));

        Assert.NotNull(idx.Find("00_cv_BEODMAR", 89, false));
    }

    [Fact]
    public void Find_FemaleUsesTheFemSuffix_AndMaleNeverReturnsIt()
    {
        var idx = Index(Rec("conv_0005_fem"), Rec("conv_0006"), Rec("conv_0006_fem"));

        Assert.Null(idx.Find("conv", 5, false));
        Assert.Equal("conv_0005_fem", idx.Find("conv", 5, true)!.Name);
        Assert.Equal("conv_0006", idx.Find("conv", 6, false)!.Name);
        Assert.Equal("conv_0006_fem", idx.Find("conv", 6, true)!.Name);
    }

    [Fact]
    public void Find_Miss_IsNull()
    {
        Assert.Null(Index(Rec("conv_0001")).Find("conv", 2, false));
    }

    [Fact]
    public void NonVoNames_AreNotIndexed()
    {
        var idx = Index(Rec("music_theme"), Rec("conv_001"), Rec("conv_00001x"), Rec("conv_0001"));

        Assert.Equal(1, idx.Count);
    }

    // ---- placeholders -----------------------------------------------------------------

    [Fact]
    public void Find_PlaceholderClip_IsNull()
    {
        var idx = Index(Rec("conv_0001", hz: 44100, len: 14976 / 44100f));

        Assert.Null(idx.Find("conv", 1, false));
    }

    [Fact]
    public void Find_RealClipWinsOverAPlaceholderOfTheSameName()
    {
        var idx = Index(Rec("conv_0001", hz: 44100, len: 14976 / 44100f), Rec("conv_0001", off: 40));

        Assert.Equal(40, idx.Find("conv", 1, false)!.Offset);
    }

    // ---- ambiguous names --------------------------------------------------------------

    [Fact]
    public void Find_TwoRealClipsWithTheSameNameButDifferentData_IsNull_AndCounted()
    {
        var idx = Index(Rec("conv_0001", off: 10, size: 8), Rec("conv_0001", off: 99, size: 8), Rec("conv_0002"));

        Assert.Null(idx.Find("conv", 1, false));
        Assert.NotNull(idx.Find("conv", 2, false));
        Assert.Equal(1, idx.AmbiguousCount);
    }

    [Fact]
    public void Find_AmbiguousWhenOnlyTheSourceDiffers()
    {
        var idx = Index(Rec("conv_0001", source: "a.resource"), Rec("conv_0001", source: "b.resource"));

        Assert.Null(idx.Find("conv", 1, false));
    }

    [Fact]
    public void Find_IdenticalDuplicates_AreNotAmbiguous()
    {
        var idx = Index(Rec("conv_0001", off: 10), Rec("conv_0001", off: 10));

        Assert.Equal(10, idx.Find("conv", 1, false)!.Offset);
        Assert.Equal(0, idx.AmbiguousCount);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void Find_RealClipBeatsAPlaceholder_InEitherOrder_WithoutBeingAmbiguous(bool placeholderFirst)
    {
        var stub = Rec("conv_0001", hz: 44100, len: 14976 / 44100f, off: 5);
        var real = Rec("conv_0001", off: 40);
        var idx = placeholderFirst ? Index(stub, real) : Index(real, stub);

        Assert.Equal(40, idx.Find("conv", 1, false)!.Offset);
        Assert.Equal(0, idx.AmbiguousCount);
    }

    // ---- extract ----------------------------------------------------------------------

    private static byte[] Fsb(int total, byte fill = 7)
    {
        var b = new byte[total];
        Array.Fill(b, fill);
        Encoding.ASCII.GetBytes("FSB5").CopyTo(b, 0);
        return b;
    }

    private void WriteSource(byte[] prefix, byte[] slice, byte[] suffix, string file = "resources.resource")
    {
        File.WriteAllBytes(Path.Combine(DataDir, file), prefix.Concat(slice).Concat(suffix).ToArray());
    }

    [Fact]
    public void Extract_WritesExactlyTheSlice_ToCacheDirNamedAfterTheClip()
    {
        var slice = Fsb(40);
        WriteSource([1, 2, 3, 4, 5], slice, [9, 9, 9]);
        var idx = Index(Rec("conv_0001", off: 5, size: 40));

        var path = idx.Extract(idx.Find("conv", 1, false)!, CacheDir);

        Assert.Equal(Path.Combine(CacheDir, "conv_0001.fsb"), path);
        Assert.Equal(slice, File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(CacheDir, "*.tmp"));
    }

    [Theory]
    [InlineData("resources.resource")]
    [InlineData("archive:/CAB-abc/resources.resource")]
    [InlineData("archive:\\x\\resources.resource")]
    public void Extract_ResolvesTheSourceByFileNameNextToTheAssets(string source)
    {
        var slice = Fsb(16);
        WriteSource([], slice, []);
        var idx = Index(Rec("conv_0001", source: source, off: 0, size: 16));

        var path = idx.Extract(idx.Find("conv", 1, false)!, CacheDir);

        Assert.Equal(slice, File.ReadAllBytes(path));
    }

    [Fact]
    public void Extract_ReusesAnExistingFileOfTheRightSize()
    {
        WriteSource([], Fsb(16), []);
        var idx = Index(Rec("conv_0001", off: 0, size: 16));
        Directory.CreateDirectory(CacheDir);
        var existing = Path.Combine(CacheDir, "conv_0001.fsb");
        var marker = Fsb(16, fill: 0x55);
        File.WriteAllBytes(existing, marker);

        var path = idx.Extract(idx.Find("conv", 1, false)!, CacheDir);

        Assert.Equal(existing, path);
        Assert.Equal(marker, File.ReadAllBytes(path)); // untouched
    }

    [Fact]
    public void Extract_ReplacesAFileOfTheWrongSize()
    {
        var slice = Fsb(16);
        WriteSource([], slice, []);
        var idx = Index(Rec("conv_0001", off: 0, size: 16));
        Directory.CreateDirectory(CacheDir);
        File.WriteAllBytes(Path.Combine(CacheDir, "conv_0001.fsb"), [1, 2, 3]);

        var path = idx.Extract(idx.Find("conv", 1, false)!, CacheDir);

        Assert.Equal(slice, File.ReadAllBytes(path));
    }

    [Fact]
    public void Extract_NonFsb5Magic_ThrowsAndLeavesNothingBehind()
    {
        var bad = Fsb(16);
        bad[0] = (byte)'X';
        WriteSource([], bad, []);
        var idx = Index(Rec("conv_0001", off: 0, size: 16));

        Assert.Throws<InvalidDataException>(() => idx.Extract(idx.Find("conv", 1, false)!, CacheDir));

        Assert.False(Directory.Exists(CacheDir) && Directory.GetFileSystemEntries(CacheDir).Length > 0);
    }

    [Fact]
    public void Extract_SlicePastEndOfFile_Throws()
    {
        WriteSource([], Fsb(16), []);
        var idx = Index(Rec("conv_0001", off: 8, size: 16));

        Assert.Throws<InvalidDataException>(() => idx.Extract(idx.Find("conv", 1, false)!, CacheDir));
    }

    [Fact]
    public void Extract_SliceShorterThanTheMagic_Throws()
    {
        WriteSource([], Fsb(16), []);
        var idx = Index(Rec("conv_0001", off: 0, size: 2));

        Assert.Throws<InvalidDataException>(() => idx.Extract(idx.Find("conv", 1, false)!, CacheDir));
    }

    // ---- load -------------------------------------------------------------------------

    [Fact]
    public void Load_MissingGameFolder_IsNull()
    {
        Assert.Null(Poe1VoiceIndex.Load(Path.Combine(_root, "nope")));
    }

    [Fact]
    public void Load_FolderWithoutResourcesAssets_IsNull()
    {
        Assert.Null(Poe1VoiceIndex.Load(_root));
    }

    [Fact]
    public void Load_UnreadableAssetsFile_IsNull()
    {
        var data = Path.Combine(_root, "PillarsOfEternity_Data");
        Directory.CreateDirectory(data);
        File.WriteAllBytes(Path.Combine(data, "resources.assets"), [1, 2, 3, 4]);

        Assert.Null(Poe1VoiceIndex.Load(_root));
    }
}
