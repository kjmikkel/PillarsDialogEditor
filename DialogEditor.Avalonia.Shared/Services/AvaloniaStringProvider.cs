using Avalonia;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Shared.Services;

public sealed class AvaloniaStringProvider : IStringProvider
{
    public string Get(string key)
    {
        if (Application.Current is { } app &&
            app.TryGetResource(key, null, out var value))
            return value as string ?? $"[{key}]";
        return $"[{key}]";
    }

    public bool TryGet(string key, out string value)
    {
        if (Application.Current is { } app &&
            app.TryGetResource(key, null, out var raw) && raw is string s)
        {
            value = s;
            return true;
        }
        value = string.Empty;
        return false;
    }
}
