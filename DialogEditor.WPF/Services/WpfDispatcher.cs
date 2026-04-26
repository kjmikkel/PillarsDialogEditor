using System.Windows.Threading;

namespace DialogEditor.WPF.Services;

public sealed class WpfDispatcher(Dispatcher dispatcher) : IDispatcher
{
    public Task YieldToBackground() =>
        dispatcher.InvokeAsync(static () => { }, DispatcherPriority.Background).Task;
}
