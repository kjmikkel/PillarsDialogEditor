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

    internal static string Script_Prefix_Enter  => Manager.GetString(nameof(Script_Prefix_Enter),  Culture)!;
    internal static string Script_Prefix_Exit   => Manager.GetString(nameof(Script_Prefix_Exit),   Culture)!;
    internal static string Script_Prefix_Update => Manager.GetString(nameof(Script_Prefix_Update), Culture)!;
    internal static string Condition_Not        => Manager.GetString(nameof(Condition_Not),        Culture)!;
}
