using System.Diagnostics;
using Avalonia.Threading;
using NAudio.Wave;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Audio;

/// <summary>
/// Plays a single PoE2 .wem voice-over file at a time by shelling out to
/// vgmstream-cli.exe (ISC, bundled in tools/) to decode it to a temp WAV,
/// then playing that WAV via NAudio. All public members are UI-thread-safe
/// because Play/Stop are called from commands on the UI thread.
///
/// PlaybackStopped is always raised on the UI thread so NodeDetailViewModel
/// (which has no Avalonia reference) can call SetPlaying() directly.
/// Stop() does NOT raise PlaybackStopped — only natural track completion does.
/// </summary>
public sealed class VoAudioPlayer : IVoAudioPlayer, IDisposable
{
    private static readonly string ToolPath =
        Path.Combine(AppContext.BaseDirectory, "tools", "vgmstream-cli.exe");

    public bool IsAvailable { get; } = File.Exists(ToolPath);

    public event Action? PlaybackStopped;

    private WaveOutEvent?   _output;
    private AudioFileReader? _reader;
    private string? _tempFile;
    private bool _manualStop;
    // Incremented on every Play/Stop to cancel in-flight background work.
    private volatile int _generation;

    public void Play(string path)
    {
        StopAndCleanup();        // increments _generation, cleans up previous
        _manualStop = false;
        var gen = ++_generation; // this play's identity token

        if (path.EndsWith(".wav", StringComparison.OrdinalIgnoreCase))
        {
            // .wav: no decode step needed — open directly with NAudio on the calling UI thread.
            try
            {
                _reader = new AudioFileReader(path);
                _output = new WaveOutEvent();
                _output.PlaybackStopped += OnNaturalPlaybackStopped;
                _output.Init(_reader);
                // _tempFile stays null — no temp file to clean up for direct WAV playback.
                _output.Play();
            }
            catch (Exception ex)
            {
                AppLog.Error("VoAudioPlayer: NAudio failed to start WAV", ex);
            }
            return;
        }

        _ = Task.Run(async () =>
        {
            var tempFile = Path.Combine(Path.GetTempPath(), $"vo_{Guid.NewGuid():N}.wav");
            try
            {
                var psi = new ProcessStartInfo(ToolPath, $"-o \"{tempFile}\" \"{path}\"")
                {
                    CreateNoWindow  = true,
                    UseShellExecute = false,
                };
                using var proc = Process.Start(psi)!;
                await proc.WaitForExitAsync();

                if (proc.ExitCode != 0)
                {
                    AppLog.Warn($"vgmstream-cli exited {proc.ExitCode} for: {path}");
                    TryDeleteTemp(tempFile);
                    return;
                }

                // Abort if a newer Play/Stop invalidated this request.
                if (gen != _generation) { TryDeleteTemp(tempFile); return; }

                Dispatcher.UIThread.Post(() =>
                {
                    if (gen != _generation) { TryDeleteTemp(tempFile); return; }

                    try
                    {
                        _reader = new AudioFileReader(tempFile);
                        _output = new WaveOutEvent();
                        _output.PlaybackStopped += OnNaturalPlaybackStopped;
                        _output.Init(_reader);
                        _tempFile = tempFile;
                        _output.Play();
                    }
                    catch (Exception ex)
                    {
                        AppLog.Error("VoAudioPlayer: NAudio failed to start", ex);
                        TryDeleteTemp(tempFile);
                    }
                });
            }
            catch (Exception ex)
            {
                AppLog.Error($"VoAudioPlayer.Play failed for: {path}", ex);
                TryDeleteTemp(tempFile);
            }
        });
    }

    public void Stop()
    {
        _manualStop = true;
        StopAndCleanup();
    }

    private void StopAndCleanup()
    {
        _generation++;          // invalidates any in-flight Task.Run
        _output?.Stop();
        Cleanup();
    }

    private void OnNaturalPlaybackStopped(object? sender, StoppedEventArgs e)
    {
        // NAudio calls this from its own thread; marshal to UI before notifying ViewModel.
        Dispatcher.UIThread.Post(() =>
        {
            Cleanup();
            if (!_manualStop)
                PlaybackStopped?.Invoke();
        });
    }

    private void Cleanup()
    {
        // Null before dispose so a concurrent second Cleanup call is a no-op.
        var output   = _output;   _output   = null;
        var reader   = _reader;   _reader   = null;
        var tempFile = _tempFile; _tempFile = null;
        if (output is not null)
            output.PlaybackStopped -= OnNaturalPlaybackStopped; // unsubscribe before dispose
        output?.Dispose();
        reader?.Dispose();
        TryDeleteTemp(tempFile);
    }

    private static void TryDeleteTemp(string? path)
    {
        if (path is null) return;
        try { File.Delete(path); } catch (Exception) { /* best-effort; file may still be open */ }
    }

    public void Dispose()
    {
        _manualStop = true;
        StopAndCleanup();
    }
}
