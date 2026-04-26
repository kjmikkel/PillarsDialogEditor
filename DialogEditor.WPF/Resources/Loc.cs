using System.Windows;

namespace DialogEditor.WPF.Resources;

public static class Loc
{
    public static string Get(string key) =>
        Application.Current.Resources[key] as string ?? $"[{key}]";

    public static string Format(string key, params object[] args) =>
        string.Format(Get(key), args);
}
