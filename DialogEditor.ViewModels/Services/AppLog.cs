using System.IO;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Lightweight file logger. Thread-safe. Never throws — logging failures are silently
/// ignored so the logger itself cannot break the application.
/// Log location: %LOCALAPPDATA%\PillarsDialogEditor\app.log (rotates at 1 MB).
/// </summary>
public static class AppLog
{
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "PillarsDialogEditor", "app.log");

    private static readonly object _lock = new();

    public static void Info(string message)                        => Write("INFO",  message, null);
    public static void Warn(string message)                        => Write("WARN",  message, null);
    public static void Error(string message, Exception? ex = null) => Write("ERROR", message, ex);

    private static void Write(string level, string message, Exception? ex)
    {
        var text = ex is null
            ? message
            : $"{message} — {ex.GetType().Name}: {ex.Message}{Environment.NewLine}{ex.StackTrace}";

        var line = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [{level}] {text}";

        lock (_lock)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);

                var info = new FileInfo(LogPath);
                if (info.Exists && info.Length > 1_048_576)
                {
                    var old = Path.ChangeExtension(LogPath, ".log.old");
                    if (File.Exists(old)) File.Delete(old);
                    File.Move(LogPath, old);
                }

                File.AppendAllText(LogPath, line + Environment.NewLine);
            }
            catch { /* logging must never itself throw */ }
        }
    }
}
