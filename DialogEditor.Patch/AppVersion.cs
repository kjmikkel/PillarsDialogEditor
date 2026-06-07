using System.Reflection;

namespace DialogEditor.Patch;

/// Single source of truth for the application version string, shared by the GUI About
/// dialog and the CLI `--version` so they never drift. Reads the assembly's
/// AssemblyInformationalVersion (fed from the VERSION file at build time).
public static class AppVersion
{
    public static string Current => FromAssembly(Assembly.GetEntryAssembly());

    public static string FromAssembly(Assembly? assembly) =>
        assembly?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";
}
