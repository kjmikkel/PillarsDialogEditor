namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Platform-agnostic dispatcher abstraction used by ViewModels.
/// Backed by Dispatcher.UIThread at Background priority.
/// </summary>
public interface IDispatcher
{
    /// <summary>
    /// Yields control to the UI thread at below-render priority so layout,
    /// rendering, and input processing can occur before the next batch of work.
    /// Equivalent to awaiting Dispatcher at DispatcherPriority.Background.
    /// </summary>
    Task YieldToBackground();
}
