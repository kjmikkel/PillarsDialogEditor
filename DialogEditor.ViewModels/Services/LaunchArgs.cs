namespace DialogEditor.ViewModels.Services;

/// Command-line switches shared by the editor and the Patcher.
public static class LaunchArgs
{
    public const string PrivacyModeArg = "--privacy-mode";

    public static bool HasPrivacyMode(string[]? args) =>
        args is not null && args.Any(a =>
            string.Equals(a, PrivacyModeArg, StringComparison.OrdinalIgnoreCase));
}
