using Avalonia;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Services;

public sealed class AvaloniaStringProvider : IStringProvider
{
    public string Get(string key)
    {
        if (Application.Current is { } app &&
            app.TryGetResource(key, null, out var value))
            return value as string ?? $"[{key}]";
        return $"[{key}]";
    }
}
