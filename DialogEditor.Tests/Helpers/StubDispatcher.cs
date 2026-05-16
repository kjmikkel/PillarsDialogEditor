using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

public sealed class StubDispatcher : IDispatcher
{
    public Task YieldToBackground() => Task.CompletedTask;
}
