namespace DialogEditor.Core.Export;

public static class DialogExporterFactory
{
    private static readonly Dictionary<string, IDialogExporter> _byFormat =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["csv"]  = new CsvDialogExporter(),
            ["json"] = new JsonDialogExporter(),
            ["yarn"] = new YarnSpinnerExporter(),
        };

    /// All supported (format key, file extension, human-readable description) tuples.
    public static IReadOnlyList<(string Format, string Extension, string Description)> AllFormats =>
    [
        ("csv",  ".csv",  "CSV"),
        ("json", ".json", "JSON"),
        ("yarn", ".yarn", "Yarn Spinner"),
    ];

    /// Returns the exporter for the given format key, or null if unknown.
    public static IDialogExporter? GetForFormat(string format) =>
        _byFormat.TryGetValue(format, out var exporter) ? exporter : null;
}
