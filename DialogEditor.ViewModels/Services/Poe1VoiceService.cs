using DialogEditor.Core.Audio;
using DialogEditor.Core.Logging;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Session-wide PoE1 voice-over index (issue 4), the counterpart of VoAliasIndexService.
/// Rebuilt per game-root open on a background task: reading resources.assets takes seconds.
/// A generation counter keeps a slow build for an older root from overwriting a newer one
/// (or a Clear). <see cref="Changed"/> is raised after a build lands and on Clear, from
/// whichever thread did it, so subscribers must marshal to their own thread.
/// </summary>
public static class Poe1VoiceService
{
    private static readonly object Gate = new();
    private static Poe1VoiceIndex? _index;
    private static int _generation;

    /// Seam for tests; production reads the real game files.
    internal static Func<string, Poe1VoiceIndex?> Loader { get; set; } = Poe1VoiceIndex.Load;

    internal static void ResetLoaderForTests() => Loader = Poe1VoiceIndex.Load;

    public static bool IsReady => Volatile.Read(ref _index) is not null;

    public static event Action? Changed;

    /// Where <see cref="Extract"/> writes a clip. Under the temp folder, so the OS may reclaim it.
    public static string CachePathFor(Poe1VoiceClip clip) => Path.Combine(CacheDir, clip.CacheFileName);

    private static string CacheDir =>
        Path.Combine(Path.GetTempPath(), "PillarsDialogEditor", "poe1-vo");

    /// Builds the index on a background task. Never throws: a failed build leaves the
    /// service not ready (and logs). The returned task completes when the build is done.
    public static Task Rebuild(string gameRoot)
    {
        int generation;
        lock (Gate) generation = ++_generation;

        return Task.Run(() =>
        {
            Poe1VoiceIndex? built = null;
            try { built = Loader(gameRoot); }
            catch (Exception ex)
            {
                AppLog.Warn($"PoE1 voice: index build failed: {ex.Message}");
            }

            lock (Gate)
            {
                if (generation != _generation) return;   // superseded by a newer Rebuild or a Clear
                Volatile.Write(ref _index, built);
            }
            Changed?.Invoke();
        });
    }

    /// Subscribes without rooting <paramref name="owner"/> (view-models have no dispose path), on
    /// the subscriber's synchronization context: Changed is raised from the build thread, and
    /// the owner is bound to UI controls. The handler unhooks itself once the owner is collected.
    public static void WeakSubscribe<T>(T owner, Action<T> onChanged) where T : class
    {
        var weak = new WeakReference<T>(owner);
        var context = SynchronizationContext.Current;
        Action? handler = null;
        handler = () =>
        {
            if (!weak.TryGetTarget(out var target)) { Changed -= handler; return; }
            if (context is null || context == SynchronizationContext.Current) onChanged(target);
            else context.Post(_ => onChanged(target), null);
        };
        Changed += handler;
    }

    public static Poe1VoiceClip? Find(string conversationName, int nodeId, bool female)
        => Volatile.Read(ref _index)?.Find(conversationName, nodeId, female);

    /// Copies the clip into the cache and returns its path; null (after a warning) when the
    /// service is not ready or the clip cannot be read.
    public static string? Extract(Poe1VoiceClip clip)
    {
        var index = Volatile.Read(ref _index);
        if (index is null) return null;
        try { return index.Extract(clip, CacheDir); }
        catch (Exception ex)
        {
            AppLog.Warn($"PoE1 voice: could not extract '{clip.Name}': {ex.Message}");
            return null;
        }
    }

    /// Test seam — mirrors VoAliasIndexService.RegisterForTests.
    public static void RegisterForTests(Poe1VoiceIndex? index)
    {
        lock (Gate)
        {
            _generation++;
            Volatile.Write(ref _index, index);
        }
        Changed?.Invoke();
    }

    public static void Clear()
    {
        lock (Gate)
        {
            _generation++;
            Volatile.Write(ref _index, null);
        }
        Changed?.Invoke();
    }
}
