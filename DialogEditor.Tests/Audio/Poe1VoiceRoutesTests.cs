using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.Audio;

/// Issue 355: the game reaches a line's AudioClip through
/// "audio/vocalization/vo assets/conversation/name" (a VOAsset) and the VOAsset's clip path
/// (GameResources.GetDialogueAudio, GlobalAudioPlayer.Coroutine_StreamClip).
public class Poe1VoiceRoutesTests
{
    private const int Res = 3;
    private const string VoA = "audio/vocalization/vo assets/";
    private const string Wav = "audio/vocalization/vo wav files/";

    private static Poe1ResourceEntry E(string path, long pathId, int fileId = Res) => new(path, fileId, pathId);

    private static IReadOnlyDictionary<string, long> Build(Dictionary<long, string?> clips, params Poe1ResourceEntry[] container) =>
        Poe1VoiceRoutes.Build(container, Res, id => clips.TryGetValue(id, out var c) ? c : null);

    [Theory]
    [InlineData("Audio/Vocalization/VO Wav Files/Lord Raedric VII/07_cv_deathguard_0029.wav",
                "audio/vocalization/vo wav files/lord raedric vii/07_cv_deathguard_0029")]
    [InlineData("Data/Audio/X/clip.WAV", "audio/x/clip")]
    [InlineData("audio/x/clip", "audio/x/clip")]
    [InlineData("audio/data/x/clip.ogg", "audio/x/clip")]   // the game drops every "data/"
    public void ResourcePathOf_NormalisesLikeTheGame(string clip, string expected)
    {
        Assert.Equal(expected, Poe1VoiceRoutes.ResourcePathOf(clip));
    }

    [Theory]
    [InlineData("conv", 1, false, "conv/conv_0001")]
    [InlineData("conv", 12345, true, "conv/conv_12345_fem")]
    public void Key_IsConversationFolderSlashClipName(string conv, int node, bool female, string expected)
    {
        Assert.Equal(expected, Poe1VoiceRoutes.Key(conv, node, female));
    }

    [Fact]
    public void Build_FollowsTheVoAssetToTheClipItNames()
    {
        var routes = Build(
            new() { [100] = "Audio/Vocalization/VO Wav Files/Lord Raedric VII/c_0001.wav" },
            E(VoA + "c/c_0001", 100),
            E(Wav + "raedric/c_0001", 7),
            E(Wav + "lord raedric vii/c_0001", 8));

        Assert.Equal(8, routes["c/c_0001"]);
        Assert.Single(routes);
    }

    [Fact]
    public void Build_KeysAreCaseInsensitive()
    {
        var routes = Build(new() { [100] = "a/x.wav" }, E(VoA + "c/c_0001", 100), E("a/x", 8));

        Assert.Equal(8, routes["C/C_0001"]);
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("audio/nowhere/c_0001.wav")]
    public void Build_NoRoute_WhenTheClipIsEmptyMissingOrUnresolved(string? clip)
    {
        var routes = Build(new() { [100] = clip }, E(VoA + "c/c_0001", 100), E("a/x", 8));

        Assert.Empty(routes);
    }

    [Fact]
    public void Build_IgnoresVoAssetsAndClipsInOtherFiles()
    {
        var clips = new Dictionary<long, string?> { [100] = "a/x.wav", [101] = "a/y.wav" };
        var routes = Build(clips,
            E(VoA + "c/c_0001", 100, fileId: 7),   // VOAsset elsewhere: the delegate reads only our file
            E(VoA + "c/c_0002", 101),
            E("a/x", 8),
            E("a/y", 9, fileId: 7));             // clip elsewhere: not a record we have

        Assert.Empty(routes);
    }

    [Fact]
    public void Build_OnlyReadsVoAssetsUnderTheVoAssetsFolder()
    {
        var read = new List<long>();
        Poe1VoiceRoutes.Build([E("art/thing", 1), E(Wav + "s/c_0001", 2), E(VoA + "c/c_0001", 3)], Res,
            id => { read.Add(id); return null; });

        Assert.Equal([3L], read);
    }

    [Fact]
    public void Build_DuplicatePaths_FirstWins()
    {
        var routes = Build(new() { [100] = "a/x.wav" },
            E(VoA + "c/c_0001", 100), E("a/x", 8), E("a/x", 9));

        Assert.Equal(8, routes["c/c_0001"]);
    }
}
