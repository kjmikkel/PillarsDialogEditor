using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Audio;

namespace DialogEditor.Tests.Audio;

public class VoAudioPlayerTests : IDisposable
{
    private readonly string _wavPath;
    private readonly string _longWavPath;

    public VoAudioPlayerTests()
    {
        // Write a minimal valid PCM WAV (44-byte RIFF header, 0 audio frames)
        // plus a ~1 s silence WAV for tests that need playback to still be
        // in progress while something else happens.
        _wavPath     = Path.Combine(Path.GetTempPath(), $"votest_{Guid.NewGuid():N}.wav");
        _longWavPath = Path.Combine(Path.GetTempPath(), $"votest_{Guid.NewGuid():N}_long.wav");
        WriteMinimalWav(_wavPath);
        WriteMinimalWav(_longWavPath, dataBytes: 176_400); // 1 s of 16-bit stereo 44.1 kHz silence
    }

    public void Dispose()
    {
        try { File.Delete(_wavPath); } catch { }
        try { File.Delete(_longWavPath); } catch { }
    }

    // B-008: a PlaybackStopped notification raised by a superseded output must be
    // ignored. Sequence: track A finishes naturally → NAudio raises its stopped
    // event → the handler posts to the UI thread. Before that post runs, the user
    // starts track B. Processing the stale post used to dispose B's fresh output
    // and raise a spurious PlaybackStopped (resetting ▶/■ glyphs while B plays).
    //
    // [AvaloniaFact] runs on the headless UI thread, so Dispatcher posts queue up
    // until RunJobs() — making the race deterministic: A's stale post is guaranteed
    // to execute only AFTER Play(B).
    [AvaloniaFact]
    public void StaleStopFromSupersededTrack_DoesNotRaisePlaybackStopped()
    {
        using var player = new VoAudioPlayer();
        var stopped = 0;
        player.PlaybackStopped += () => stopped++;

        player.Play(_wavPath);          // A: zero frames — finishes ~instantly
        Thread.Sleep(300);              // let NAudio raise A's stopped event (post queues, we aren't pumping)

        player.Play(_longWavPath);      // B: still playing when the stale post runs
        Dispatcher.UIThread.RunJobs();  // execute A's stale posted handler

        Assert.Equal(0, stopped);
    }

    // Companion guard: natural completion of the CURRENT track must still raise
    // PlaybackStopped exactly once — the stale-event suppression must not
    // over-suppress the legitimate case.
    [AvaloniaFact]
    public void NaturalCompletionOfCurrentTrack_RaisesPlaybackStopped()
    {
        using var player = new VoAudioPlayer();
        var stopped = 0;
        player.PlaybackStopped += () => stopped++;

        player.Play(_wavPath);          // zero frames — finishes ~instantly

        var deadline = DateTime.UtcNow.AddSeconds(5);
        while (stopped == 0 && DateTime.UtcNow < deadline)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(25);
        }

        Assert.Equal(1, stopped);
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

    private static void WriteMinimalWav(string path, int dataBytes = 0)
    {
        // 44-byte RIFF/PCM header + dataBytes of silence, 16-bit stereo 44100 Hz
        using var fs = File.OpenWrite(path);
        using var w  = new BinaryWriter(fs);
        w.Write("RIFF"u8); w.Write(36 + dataBytes); // chunk size
        w.Write("WAVE"u8);
        w.Write("fmt "u8); w.Write(16);           // subchunk1 size
        w.Write((short)1);                         // PCM
        w.Write((short)2);                         // 2 channels
        w.Write(44100);                            // sample rate
        w.Write(176400);                           // byte rate
        w.Write((short)4);                         // block align
        w.Write((short)16);                        // bits per sample
        w.Write("data"u8); w.Write(dataBytes);     // audio data (silence)
        if (dataBytes > 0)
            w.Write(new byte[dataBytes]);
    }
}
