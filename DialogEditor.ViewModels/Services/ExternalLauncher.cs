using System.Diagnostics;

namespace DialogEditor.ViewModels.Services;

/// Opens a URL or local file via the OS default handler. Default implementation behind the
/// view-models' opener seams so tests can substitute a fake. Returns false (and logs) on
/// failure rather than throwing.
public static class ExternalLauncher
{
    public static bool Open(string target)
    {
        try
        {
            Process.Start(new ProcessStartInfo(target) { UseShellExecute = true });
            return true;
        }
        catch (Exception ex)
        {
            AppLog.Warn($"ExternalLauncher: failed to open '{target}': {ex.Message}");
            return false;
        }
    }
}
