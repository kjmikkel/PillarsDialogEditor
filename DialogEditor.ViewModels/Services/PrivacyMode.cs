namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Observable "hide private paths" switch (issue 270), shared by the editor and the
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

    /// Subscribes without rooting <paramref name="owner"/>: the view-models that mask paths
    /// have no dispose path, and a plain static-event subscription would keep every one of
    /// them alive. The handler unhooks itself once the owner has been collected.
    /// It runs on the subscriber's synchronization context: the owner is usually bound to
    /// UI controls, and Changed is static, so it may be raised from any thread. Calling
    /// the owner from there is a cross-thread binding update, which silently stopped
    /// layout for the rest of a headless test run.
    public static void WeakSubscribe<T>(T owner, Action<T> onChanged) where T : class
    {
        var weak = new WeakReference<T>(owner);
        var context = SynchronizationContext.Current;
        EventHandler? handler = null;
        handler = (_, _) =>
        {
            if (!weak.TryGetTarget(out var target)) { Changed -= handler; return; }
            if (context is null || context == SynchronizationContext.Current) onChanged(target);
            else context.Post(_ => onChanged(target), null);
        };
        Changed += handler;
    }

    internal static void ResetForTests() => _sessionForced = false;
}
