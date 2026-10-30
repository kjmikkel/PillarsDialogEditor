using DialogEditor.Core.Audio;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class Poe1VoiceServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "poe1svc-" + Guid.NewGuid().ToString("N"));
    private string DataDir => Path.Combine(_root, "data");

    public Poe1VoiceServiceTests()
    {
        Directory.CreateDirectory(DataDir);
        Poe1VoiceService.Clear();
    }

    public void Dispose()
    {
        Poe1VoiceService.Clear();
        Poe1VoiceService.ResetLoaderForTests();
        try { Directory.Delete(_root, true); } catch { /* best-effort */ }
    }

    private Poe1VoiceIndex IndexOf(string name, long off = 0, long size = 8) =>
        new([new Poe1AudioClipRecord(name, 48000, 2f, "resources.resource", off, size)], DataDir);

    [Fact]
    public async Task Rebuild_NonexistentRoot_EndsNotReadyWithoutThrowing()
    {
        await Poe1VoiceService.Rebuild(Path.Combine(_root, "no-such-game"));

        Assert.False(Poe1VoiceService.IsReady);
        Assert.Null(Poe1VoiceService.Find("c", 1, false));
    }

    [Fact]
    public async Task Rebuild_LoadedIndex_BecomesReadyAndRaisesChanged()
    {
        Poe1VoiceService.Loader = _ => IndexOf("c_0001");
        var raised = 0;
        void Handler() => raised++;
        Poe1VoiceService.Changed += Handler;
        try
        {
            await Poe1VoiceService.Rebuild("any");
        }
        finally { Poe1VoiceService.Changed -= Handler; }

        Assert.True(Poe1VoiceService.IsReady);
        Assert.Equal(1, raised);
        Assert.NotNull(Poe1VoiceService.Find("c", 1, false));
    }

    [Fact]
    public async Task Rebuild_LoaderThrows_EndsNotReadyWithoutThrowing()
    {
        Poe1VoiceService.Loader = _ => throw new InvalidOperationException("boom");

        await Poe1VoiceService.Rebuild("any");

        Assert.False(Poe1VoiceService.IsReady);
    }

    [Fact]
    public async Task Rebuild_StaleBuildFinishingLate_DoesNotOverwriteNewerOne()
    {
        var gate = new ManualResetEventSlim();
        Poe1VoiceService.Loader = root =>
        {
            if (root == "old") { gate.Wait(TimeSpan.FromSeconds(10)); return IndexOf("old_0001"); }
            return IndexOf("new_0001");
        };

        var oldBuild = Poe1VoiceService.Rebuild("old");
        await Poe1VoiceService.Rebuild("new");
        gate.Set();
        await oldBuild;

        Assert.NotNull(Poe1VoiceService.Find("new", 1, false));
        Assert.Null(Poe1VoiceService.Find("old", 1, false));
    }

    [Fact]
    public async Task Clear_WhileBuilding_DiscardsTheInFlightBuild()
    {
        var gate = new ManualResetEventSlim();
        Poe1VoiceService.Loader = _ => { gate.Wait(TimeSpan.FromSeconds(10)); return IndexOf("c_0001"); };

        var build = Poe1VoiceService.Rebuild("any");
        Poe1VoiceService.Clear();
        gate.Set();
        await build;

        Assert.False(Poe1VoiceService.IsReady);
    }

    [Fact]
    public void Clear_RaisesChanged()
    {
        var raised = 0;
        void Handler() => raised++;
        Poe1VoiceService.Changed += Handler;
        try { Poe1VoiceService.Clear(); }
        finally { Poe1VoiceService.Changed -= Handler; }
        Assert.Equal(1, raised);
    }

    [Fact]
    public void CachePathFor_IsUnderTempPillarsCacheAsFsb()
    {
        var path = Poe1VoiceService.CachePathFor(new Poe1VoiceClip("c_0001", "resources.resource", 0, 8));

        Assert.Equal(Path.Combine(Path.GetTempPath(), "PillarsDialogEditor", "poe1-vo", "c_0001.fsb"), path);
    }

    [Fact]
    public void Extract_NotReady_ReturnsNull()
    {
        Assert.Null(Poe1VoiceService.Extract(new Poe1VoiceClip("c_0001", "resources.resource", 0, 8)));
    }

    [Fact]
    public void Extract_BadSlice_ReturnsNullInsteadOfThrowing()
    {
        File.WriteAllBytes(Path.Combine(DataDir, "resources.resource"), [1, 2, 3]);
        Poe1VoiceService.RegisterForTests(IndexOf("c_0001"));
        var clip = Poe1VoiceService.Find("c", 1, false)!;

        Assert.Null(Poe1VoiceService.Extract(clip));
    }

    [Fact]
    public void Extract_ValidSlice_WritesFileAtCachePath()
    {
        File.WriteAllBytes(Path.Combine(DataDir, "resources.resource"), "FSB5abcd"u8.ToArray());
        var name = "svc_" + Guid.NewGuid().ToString("N") + "_0001";
        Poe1VoiceService.RegisterForTests(IndexOf(name));
        var clip = Poe1VoiceService.Find(name[..^5], 1, false)!;

        var path = Poe1VoiceService.Extract(clip);

        try
        {
            Assert.Equal(Poe1VoiceService.CachePathFor(clip), path);
            Assert.True(File.Exists(path));
        }
        finally { if (path is not null) File.Delete(path); }
    }
}
