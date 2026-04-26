using System.Globalization;
using System.Resources;

namespace DialogEditor.Core.Resources;

internal static class CoreStrings
{
    private static ResourceManager? _manager;

    private static ResourceManager Manager =>
        _manager ??= new ResourceManager(
            "DialogEditor.Core.Resources.Strings",
            typeof(CoreStrings).Assembly);

    internal static CultureInfo? Culture { get; set; }

    internal static string Script_Prefix_Enter  => GetOrFallback(nameof(Script_Prefix_Enter));
    internal static string Script_Prefix_Exit   => GetOrFallback(nameof(Script_Prefix_Exit));
    internal static string Script_Prefix_Update => GetOrFallback(nameof(Script_Prefix_Update));
    internal static string Condition_Not        => GetOrFallback(nameof(Condition_Not));

    private static string GetOrFallback(string key) =>
        Manager.GetString(key, Culture) ?? $"[{key}]";
}
