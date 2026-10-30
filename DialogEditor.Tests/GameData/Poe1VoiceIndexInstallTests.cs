using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.GameData;

/// Issue 4 against a real PoE1 install (DIALOGEDITOR_POE1_DIR; docs/game-data-tests.md).
/// Reads the game folder only; extraction goes to a temp folder.
[Trait("Category", "GameData")]
public class Poe1VoiceIndexInstallTests
{
    private static string Dir => GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable);

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void RealInstall_IndexesTheVoiceClips_AndExtractsAnFsb5Bank()
    {
        var index = Poe1VoiceIndex.Load(Dir);

        Assert.NotNull(index);
        // 11,267 voice-named records ship, but 272 repeat a name (placeholder stubs, expansion
        // copies), leaving 10,995 distinct names; the floor sits just under that.
        Assert.True(index!.Count >= 10900, $"only {index.Count} voice clips indexed");

        // Two different real clips share this name on the shipped game: ambiguous, so no clip.
        Assert.True(index.AmbiguousCount > 0);
        Assert.Null(index.Find("07_cv_deathguard", 29, false));

        var clip = index.Find("00_cv_beodmar", 89, false);
        Assert.NotNull(clip);

        var cache = Path.Combine(Path.GetTempPath(), "poe1vo-install-" + Guid.NewGuid().ToString("N"));
        try
        {
            var path = index.Extract(clip!, cache);

            using var fs = File.OpenRead(path);
            var magic = new byte[4];
            fs.ReadExactly(magic);
            Assert.Equal("FSB5", System.Text.Encoding.ASCII.GetString(magic));
            Assert.Equal(clip!.Size, fs.Length);
        }
        finally
        {
            try { Directory.Delete(cache, true); } catch { /* best-effort */ }
        }
    }
}
