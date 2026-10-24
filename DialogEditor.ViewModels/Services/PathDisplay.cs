using DialogEditor.Core.Diagnostics;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Display-only path masking for privacy mode (issue #270). Never feed the result back
/// into settings, the patch list or any model.
/// </summary>
public static class PathDisplay
{
    private static readonly Lazy<CrashReportScrubber> Env = new(CrashReportScrubber.FromEnvironment);

    public static string? ForDisplay(string? path) =>
        PrivacyMode.IsOn ? ForDisplay(path, Env.Value, true) : path;

    public static string? ForDisplay(string? path, CrashReportScrubber? scrubber, bool privacyOn)
    {
        if (!privacyOn || string.IsNullOrEmpty(path) || scrubber is null) return path;

        var scrubbed = scrubber.Scrub(path);
        if (!string.Equals(scrubbed, path, StringComparison.Ordinal)) return scrubbed;
        if (!IsAbsolute(path)) return path;

        var segments = path.Split(['\\', '/'], StringSplitOptions.RemoveEmptyEntries);
        var last = segments.LastOrDefault();
        if (last is null || (segments.Length == 1 && last.EndsWith(':'))) return path;
        var trailing = path[^1] is '\\' or '/' ? "\\" : "";
        return "…\\" + last + trailing;
    }

    private static bool IsAbsolute(string p) =>
        (p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] is '\\' or '/'))
        || p.StartsWith(@"\\", StringComparison.Ordinal)
        || p.StartsWith('/');
}
