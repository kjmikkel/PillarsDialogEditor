using DialogEditor.Avalonia.Audio;

namespace DialogEditor.Tests.Audio;

public class VoAudioPlayerTests : IDisposable
{
    private readonly string _wavPath;

    public VoAudioPlayerTests()
    {
        // Write a minimal valid PCM WAV (44-byte RIFF header, 0 audio frames).
        _wavPath = Path.Combine(Path.GetTempPath(), $"votest_{Guid.NewGuid():N}.wav");
        WriteMinimalWav(_wavPath);
    }

    public void Dispose()
    {
        try { File.Delete(_wavPath); } catch { }
    }

    // Before the fix, Play() on a .wav file always attempts to spawn vgmstream-cli.exe.
    // When vgmstream is absent (IsAvailable == false), the Process.Start inside Task.Run
    // throws Win32Exception, which is caught and logged — so the synchronous call doesn't
    // throw, but PlaybackStopped never fires because the decode "failed".
    //
    // After the fix, .wav files skip vgmstream entirely and go directly to NAudio.
    // NAudio can initialise AudioFileReader on a zero-frame WAV without throwing.
    // PlaybackStopped fires almost immediately (empty file = instant playback end).
    //
    // The test distinguishes the two cases: only after the fix does PlaybackStopped fire
    // for a .wav when vgmstream is absent.
    [Fact]
    public async Task Play_WavFile_FiresPlaybackStopped_WhenVgmstreamAbsent()
    {
        var player = new VoAudioPlayer();
        if (player.IsAvailable)
            return; // vgmstream is present in this env — test only meaningful without it

        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        player.PlaybackStopped += () => tcs.TrySetResult(true);

        player.Play(_wavPath);

        // Give the async pipeline up to 5 s to complete.
        var completed = await Task.WhenAny(tcs.Task, Task.Delay(5000)) == tcs.Task;
        Assert.True(completed, "PlaybackStopped did not fire — .wav may still be routed through vgmstream");
    }

    private static void WriteMinimalWav(string path)
    {
        // 44-byte RIFF/PCM header, 0 data bytes, 16-bit stereo 44100 Hz
        using var fs = File.OpenWrite(path);
        using var w  = new BinaryWriter(fs);
        w.Write("RIFF"u8); w.Write(36);          // chunk size (header only, 0 data)
        w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16);           // subchunk1 size
        w.Write((short)1);                         // PCM
        w.Write((short)2);                         // 2 channels
        w.Write(44100);                            // sample rate
        w.Write(176400);                           // byte rate
        w.Write((short)4);                         // block align
        w.Write((short)16);                        // bits per sample
        w.Write("data"u8); w.Write(0);             // 0 bytes of audio data
    }
}
