namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Copies or encodes a voice-over file into the project's _vo/ folder.
/// Implemented by VoImporter (Avalonia layer) and NullVoImporter (no-op default).
/// </summary>
public interface IVoImporter
{
    /// False when Wwise is absent — WAV files cannot be encoded.
    bool IsWwiseAvailable { get; }

    Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct);
}

public sealed class NullVoImporter : IVoImporter
{
    public static readonly NullVoImporter Instance = new();
    private NullVoImporter() { }

    public bool IsWwiseAvailable => false;

    public Task<VoImportResult> ImportAsync(VoImportRequest request, CancellationToken ct)
        => Task.FromResult(new VoImportResult(false, "No importer configured."));
}

/// <param name="PrimaryDestinationPath">Expected .wem path inside _vo/ for the primary slot.</param>
/// <param name="PrimarySourcePath">.wav or .wem picked by the user for the primary slot.</param>
/// <param name="FemDestinationPath">Expected .wem path inside _vo/ for the female slot (null = not applicable).</param>
/// <param name="FemSourcePath">.wav or .wem picked by the user for the female slot (null = not provided).</param>
public record VoImportRequest(
    string  PrimaryDestinationPath,
    string  PrimarySourcePath,
    string? FemDestinationPath,
    string? FemSourcePath);

public record VoImportResult(bool Success, string? ErrorMessage);

/// Passed to ShowImportDialog so the dialog knows where files will be saved.
public record VoImportPaths(string PrimaryDestinationPath, string? FemDestinationPath);

/// Returned by ShowImportDialog with the user's source-file selections.
public record VoImportDialogResult(string PrimarySourcePath, string? FemSourcePath);
