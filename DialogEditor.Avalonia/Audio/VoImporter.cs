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

        // WwiseCLI.exe requires a .wproj project file and a .wsources XML input to
        // convert WAV → WEM; standalone single-file conversion is not supported.
        // To implement this the editor would need to bundle a minimal Wwise project
        // template (.wproj with Vorbis conversion settings) and generate a temporary
        // .wsources file for each call. Deferred until a bundled template ships.
        // Users should convert WAV → WEM with the Wwise authoring tool and import
        // the resulting .wem directly.
        throw new NotSupportedException(
            "WAV → WEM encoding requires a bundled Wwise project template that is not yet " +
            "included with this release. Convert your file to .wem using the Wwise authoring " +
            "tool and import the .wem directly.");
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
