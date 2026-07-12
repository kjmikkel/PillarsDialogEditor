using System.IO.Compression;
using Avalonia.Platform;
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

    // Cached path of the template.wproj extracted from template.wwise.zip on first encode.
    private static string? _cachedWprojPath;

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
                                   request.PrimaryDestinationPath,
                                   request.Quality, ct);

            if (request.FemSourcePath is not null && request.FemDestinationPath is not null)
                await ProcessSlotAsync(request.FemSourcePath,
                                       request.FemDestinationPath,
                                       request.Quality, ct);

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

    private async Task ProcessSlotAsync(
        string sourcePath, string destPath, WemQuality quality, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);

        if (sourcePath.EndsWith(".wem", StringComparison.OrdinalIgnoreCase))
        {
            File.Copy(sourcePath, destPath, overwrite: true);
            return;
        }

        if (!IsWwiseAvailable)
            throw new InvalidOperationException(
                "Wwise not found. Install Wwise or use a pre-encoded .wem file.");

        await EncodeWavToWemAsync(sourcePath, destPath, quality, ct);
    }

    private async Task EncodeWavToWemAsync(
        string sourcePath, string destPath, WemQuality quality, CancellationToken ct)
    {
        var wprojPath = GetOrExtractTemplateWproj();
        var tempDir   = Path.Combine(Path.GetTempPath(), "PillarsDialogEditor",
                                     "wwise", $"encode_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var wsourcesPath = Path.Combine(tempDir, "sources.wsources");
            await File.WriteAllTextAsync(wsourcesPath,
                GenerateWsourcesXml(sourcePath, destPath, quality), ct);

            // WwiseConsole writes the encoded .wem to:
            //   <wprojDir>\GeneratedSoundBanks\Windows\<destNameWithoutExtension>.wem
            var psi = new System.Diagnostics.ProcessStartInfo(
                _wwiseCliPath!,
                $"convert-external-source \"{wprojPath}\" --platform Windows --source-file \"{wsourcesPath}\"")
            {
                CreateNoWindow        = true,
                UseShellExecute       = false,
                RedirectStandardError = true,
            };
            using var proc = System.Diagnostics.Process.Start(psi)!;
            await proc.WaitForExitAsync(ct);

            if (proc.ExitCode != 0)
            {
                var err = await proc.StandardError.ReadToEndAsync(ct);
                throw new InvalidOperationException(
                    $"WwiseConsole exited {proc.ExitCode}: {err}");
            }

            var wprojDir   = Path.GetDirectoryName(wprojPath)!;
            var outputName = Path.GetFileNameWithoutExtension(destPath) + ".wem";
            var outputWem  = Path.Combine(wprojDir, "GeneratedSoundBanks", "Windows", outputName);

            if (!File.Exists(outputWem))
                throw new FileNotFoundException(
                    $"WwiseConsole did not produce expected output at: {outputWem}");

            File.Move(outputWem, destPath, overwrite: true);
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); }
            catch (Exception ex) { AppLog.Warn($"VoImporter: temp dir cleanup failed: {ex.Message}"); }
        }
    }

    /// <summary>
    /// Generates a .wsources XML string for a single WAV→WEM conversion.
    /// Pure (no I/O) — the primary unit-test target for this class.
    /// </summary>
    internal static string GenerateWsourcesXml(
        string sourcePath, string destPath, WemQuality quality)
    {
        var presetName = quality switch
        {
            WemQuality.Low  => "VorbisLow",
            WemQuality.High => "VorbisHigh",
            _               => "VorbisMedium",
        };

        var sourceDir  = Path.GetDirectoryName(sourcePath)!.Replace('\\', '/');
        var sourceName = Path.GetFileName(sourcePath);
        var destName   = Path.GetFileNameWithoutExtension(destPath);

        return $"""
            <ExternalSourcesList SchemaVersion="1" Root="{sourceDir}">
              <Source Path="{sourceName}"
                      Destination="{destName}"
                      Conversion="{presetName}"/>
            </ExternalSourcesList>
            """;
    }

    private static string GetOrExtractTemplateWproj()
    {
        if (_cachedWprojPath is not null) return _cachedWprojPath;

        var uri = new Uri("avares://DialogEditor.Avalonia/Assets/Wwise/template.wwise.zip");
        using var stream = AssetLoader.Open(uri);
        _cachedWprojPath = ExtractTemplateZip(stream);
        return _cachedWprojPath;
    }

    /// <summary>
    /// Extracts the Wwise project ZIP to a temp directory and returns the path to template.wproj.
    /// Internal for testing — production callers use GetOrExtractTemplateWproj().
    /// </summary>
    internal static string ExtractTemplateZip(Stream zipStream, string? destDir = null)
    {
        // ZIP contains a root "template/" folder; extract one level above so it lands at
        // destDir/template/template.wproj rather than destDir/template.wproj.
        destDir ??= Path.Combine(
            Path.GetTempPath(), "PillarsDialogEditor", "wwise");

        Directory.CreateDirectory(destDir);

        using var zip = new ZipArchive(zipStream, ZipArchiveMode.Read);
        zip.ExtractToDirectory(destDir, overwriteFiles: true);

        return Path.Combine(destDir, "template", "template.wproj");
    }

    private static string? DetectWwiseCli()
    {
        const string ExeName = "WwiseConsole.exe"; // renamed from WwiseCLI.exe in Wwise 2019.1

        // 1. WWISEROOT environment variable
        var wwiseRoot = Environment.GetEnvironmentVariable("WWISEROOT");
        AppLog.Info($"[DetectWwise] WWISEROOT={(wwiseRoot ?? "(not set)")}");
        if (!string.IsNullOrEmpty(wwiseRoot))
        {
            var candidate = Path.Combine(wwiseRoot, "Authoring", "x64", "Release", "bin", ExeName);
            AppLog.Info($"[DetectWwise] env candidate: {candidate} exists={File.Exists(candidate)}");
            if (File.Exists(candidate)) return candidate;
        }

        // 2. Registry: enumerate installed versions, pick the newest
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(
                @"SOFTWARE\Audiokinetic\Wwise\");
            AppLog.Info($"[DetectWwise] registry key SOFTWARE\\Audiokinetic\\Wwise\\ found={key is not null}");
            if (key is not null)
            {
                var versions = key.GetSubKeyNames()
                    .OrderByDescending(v => v)
                    .ToList();
                AppLog.Info($"[DetectWwise] registry versions: [{string.Join(", ", versions)}]");
                foreach (var version in versions)
                {
                    using var verKey = key.OpenSubKey(version);
                    var installDir = verKey?.GetValue("InstallDir") as string;
                    if (string.IsNullOrEmpty(installDir)) continue;
                    var candidate = Path.Combine(installDir,
                        "Authoring", "x64", "Release", "bin", ExeName);
                    AppLog.Info($"[DetectWwise] registry candidate: {candidate} exists={File.Exists(candidate)}");
                    if (File.Exists(candidate)) return candidate;
                }
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"VoImporter: registry lookup failed: {ex.Message}");
        }

        // 3. Common install-path fallback — check both the default Program Files location
        //    and C:\Audiokinetic\ (the Wwise Launcher's non-standard default on some systems)
        var searchRoots = new[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Audiokinetic"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),    "Audiokinetic"),
            @"C:\Audiokinetic",
        };
        foreach (var root in searchRoots)
        {
            AppLog.Info($"[DetectWwise] fallback root: {root} exists={Directory.Exists(root)}");
            if (!Directory.Exists(root)) continue;
            foreach (var dir in Directory.GetDirectories(root, "Wwise*")
                                         .OrderByDescending(d => d))
            {
                var candidate = Path.Combine(dir, "Authoring", "x64", "Release", "bin", ExeName);
                AppLog.Info($"[DetectWwise] fallback candidate: {candidate} exists={File.Exists(candidate)}");
                if (File.Exists(candidate)) return candidate;
            }
        }

        AppLog.Warn("[DetectWwise] WwiseConsole.exe not found — WAV encoding unavailable");
        return null;
    }
}
