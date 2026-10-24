namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Observable "hide private paths" switch (issue #270), shared by the editor and the
/// Patcher. Effective value = session override (<c>--privacy-mode</c>, never saved) OR the
/// persisted <see cref="AppSettings.PrivacyMode"/>. Display code subscribes to
/// <see cref="Changed"/> to refresh masked paths; masking is display-only and must never
/// be written back to any model or setting.
/// </summary>
public static class PrivacyMode
{
    private static bool _sessionForced;

    public static bool IsOn => _sessionForced || AppSettings.PrivacyMode;

    public static event EventHandler? Changed;

    public static void SetPersisted(bool on)
    {
        AppSettings.PrivacyMode = on;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    public static void ForceOnForSession()
    {
        _sessionForced = true;
        Changed?.Invoke(null, EventArgs.Empty);
    }

    internal static void ResetForTests() => _sessionForced = false;
}
