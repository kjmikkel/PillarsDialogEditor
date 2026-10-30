using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.GameData;

/// Issues 4 and 355 against a real PoE1 install (DIALOGEDITOR_POE1_DIR; docs/game-data-tests.md).
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

        // Two different real clips share this name on the shipped game ("raedric" and "lord raedric
        // vii" folders). Issue 355: the conversation's VOAsset picks one, so it resolves. Of the 96
        // shipped ambiguous names, 77 have a VOAsset naming a clip; the rest (soundset barks, one
        // empty VOAsset) are silent in the game's conversations too.
        Assert.True(index.AmbiguousCount > 0);
        Assert.True(index.ResolvedAmbiguousCount >= 70, $"only {index.ResolvedAmbiguousCount} ambiguous names resolved");
        Assert.NotNull(index.Find("07_cv_deathguard", 29, false));

        // This node's VOAsset reuses a line recorded for another conversation: the game plays
        // that clip, which only the VOAsset route can know.
        Assert.Equal("px2_02_cv_final_battle_prep_0183", index.Find("px2_00_cv_rabble_rouser", 130, false)?.Name);

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
