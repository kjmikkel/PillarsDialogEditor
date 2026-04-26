namespace DialogEditor.WPF.Services;

/// <summary>
/// Platform-agnostic dispatcher abstraction used by ViewModels.
/// WPF: backed by System.Windows.Threading.Dispatcher at Background priority.
/// Avalonia: back with Dispatcher.UIThread at Background priority.
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
