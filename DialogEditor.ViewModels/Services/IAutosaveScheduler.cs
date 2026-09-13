namespace DialogEditor.ViewModels.Services;

/// Applies a new autosave cadence to the running timer (issue #11). The View owns the
/// DispatcherTimer, so this is the seam that lets a Settings change take effect
/// immediately instead of at next launch — the same shape as IFontScaleApplier, but
/// live-applying rather than deferred.
public interface IAutosaveScheduler
{
    /// Reschedules autosave. 0 stops it entirely; any other value is a fresh interval
    /// in seconds (implementations clamp against AppSettings' Min/Max).
    void Apply(int intervalSeconds);
}
