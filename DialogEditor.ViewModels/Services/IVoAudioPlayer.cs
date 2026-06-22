namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Plays a single .wem voice-over file at a time.
/// Implemented by VoAudioPlayer (Avalonia layer) and NullVoAudioPlayer (no-op default).
/// </summary>
public interface IVoAudioPlayer
{
    /// False when vgmstream-cli.exe is absent — hides play buttons in the UI.
    bool IsAvailable { get; }

    /// Fired when playback ends naturally (NOT when Stop() is called explicitly).
    /// Always raised on the UI thread.
    event Action? PlaybackStopped;

    void Play(string wemPath);

    /// Stops any current playback. Does NOT fire PlaybackStopped.
    void Stop();
}

/// No-op player used as the default in NodeDetailViewModel so existing tests
/// that do not set up a player continue to compile and pass.
public sealed class NullVoAudioPlayer : IVoAudioPlayer
{
    public static readonly NullVoAudioPlayer Instance = new();
    private NullVoAudioPlayer() { }

    public bool IsAvailable => false;
    public event Action? PlaybackStopped;
    public void Play(string wemPath) { }
    public void Stop() { }
}
