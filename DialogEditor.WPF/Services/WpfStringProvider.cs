using System.Windows;

namespace DialogEditor.WPF.Services;

public sealed class WpfStringProvider : IStringProvider
{
    public string Get(string key) =>
        Application.Current.Resources[key] as string ?? $"[{key}]";
}
