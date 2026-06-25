using System.Diagnostics;
using Microsoft.Win32;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Audio;

/// <summary>
/// Copies .wem files directly or encodes .wav → .wem via Wwise CLI into the project's _vo/ folder.
///
/// Wwise detection order:
///   1. WWISEROOT environment variable → WwiseCLI.exe relative to it.
///   2. Registry HKLM\SOFTWARE\Audiokinetic\Wwise\ → newest installed version.
///   3. Common install-path fallback.
/// IsWwiseAvailable is cached at construction — restart the editor after installing Wwise.
/// </summary>
public sealed class VoImporter : IVoImporter
{
    private readonly string? _wwiseCliPath;

    public bool IsWwiseAvailable => _wwiseCliPath is not null;

    public VoImporter()
    {
        _wwiseCliPath = DetectWwiseCli();
    }

    public async Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct)
    {
        try
        {
            await ProcessSlotAsync(request.PrimarySourcePath,
                                   request.PrimaryDestinationPath, ct);

            if (request.FemSourcePath is not null && request.FemDestinationPath is not null)
                await ProcessSlotAsync(request.FemSourcePath,
                                       request.FemDestinationPath, ct);

            return new VoImportResult(true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error("VoImporter.ImportAsync failed", ex);
            return new VoImportResult(false, ex.Message);
        }
    }

    private async Task ProcessSlotAsync(string sourcePath, string destPath, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        if (sourcePath.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destPath, overwrite: true);
            return;
        }

        // .wav → .wem via Wwise CLI
        if (!IsWwiseAvailable)
            throw new InvalidOperationException(
                "Wwise not found. Install Wwise or use a pre-encoded .wem file.");

        // NOTE: The exact WwiseCLI.exe flags for WAV→WEM conversion must be verified against
        // a real Wwise installation during implementation. The command below is a placeholder
        // that encodes a WAV to WEM using the default conversion settings.
        // Expected usage: WwiseCLI.exe <wav> -output <dest.wem>
        // Implementer: test with a real Wwise install and update the arguments accordingly.
        var psi = new ProcessStartInfo(_wwiseCliPath!, $"\"{sourcePath}\" -output \"{destPath}\"")
        {
            CreateNoWindow  = true,
            UseShellExecute = false,
            RedirectStandardError = true,
        };
        using var proc = Process.Start(psi)!;
        await proc.WaitForExitAsync(ct);

        if (proc.ExitCode != 0)
        {
            var err = await proc.StandardError.ReadToEndAsync(ct);
            throw new InvalidOperationException(
                $"WwiseCLI exited {proc.ExitCode}: {err}");
        }
    }

    private static string? DetectWwiseCli()
    {
        // 1. WWISEROOT environment variable
        var wwiseRoot = Environment.GetEnvironmentVariable("WWISEROOT");
        if (!string.IsNullOrEmpty(wwiseRoot))
        {
            var candidate = Path.Combine(wwiseRoot, "Authoring", "x64", "Release", "bin", "WwiseCLI.exe");
            if (File.Exists(candidate)) return candidate;
        }

        // 2. Registry: enumerate installed versions, pick the newest
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Audiokinetic\Wwise\");
            if (key is not null)
            {
                var versions = key.GetSubKeyNames()
                    .OrderByDescending(v => v)
                    .ToList();
                foreach (var version in versions)
                {
                    using var verKey = key.OpenSubKey(version);
                    var installDir = verKey?.GetValue("InstallDir") as string;
                    if (string.IsNullOrEmpty(installDir)) continue;
                    var candidate = Path.Combine(installDir,
                        "Authoring", "x64", "Release", "bin", "WwiseCLI.exe");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"VoImporter: registry lookup failed: {ex.Message}");
        }

        // 3. Common install-path fallback (try a few recent versions)
        var commonRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
            "Audiokinetic");
        if (Directory.Exists(commonRoot))
        {
            foreach (var dir in Directory.GetDirectories(commonRoot, "Wwise*")
                                         .OrderByDescending(d => d))
            {
                var candidate = Path.Combine(dir, "Authoring", "x64", "Release", "bin", "WwiseCLI.exe");
                if (File.Exists(candidate)) return candidate;
            }
        }

        return null;
    }
}
