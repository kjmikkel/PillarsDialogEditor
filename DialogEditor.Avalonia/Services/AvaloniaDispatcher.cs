using Avalonia.Threading;

namespace DialogEditor.Avalonia.Services;

public sealed class AvaloniaDispatcher : DialogEditor.ViewModels.Services.IDispatcher
{
    public Task YieldToBackground() =>
        Dispatcher.UIThread.InvokeAsync(static () => { }, DispatcherPriority.Background).GetTask();
}
