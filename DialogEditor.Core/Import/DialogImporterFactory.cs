namespace DialogEditor.Core.Import;

public static class DialogImporterFactory
{
    private static readonly IReadOnlyList<IDialogImporter> _all =
    [
        new CsvDialogImporter(),
        new JsonDialogImporter(),
        new ArticyXmlImporter(),
        new YarnSpinnerImporter(),
    ];

    /// All file extensions handled by any registered importer.
    public static IReadOnlyList<string> AllExtensions =>
        _all.SelectMany(i => i.FileExtensions).ToList();

    /// Returns the importer for the given file path (matched by extension, case-insensitive).
    /// Returns null if no importer handles that extension.
    public static IDialogImporter? GetForFile(string path)
    {
        var ext = Path.GetExtension(path);
        return _all.FirstOrDefault(i =>
            i.FileExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)));
    }
}
