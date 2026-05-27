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

    /// All registered importers and their file type descriptions for file pickers.
    /// Each tuple is (extension, label) e.g. (".csv", "CSV Dialog")
    public static IReadOnlyList<(string Extension, string Label)> AllFileTypes =>
        _all.SelectMany(i => i.FileExtensions.Select(ext => (ext, LabelFor(i)))).ToList();

    /// Returns the importer for the given file path (matched by extension, case-insensitive).
    /// Returns null if no importer handles that extension.
    public static IDialogImporter? GetForFile(string path)
    {
        var ext = Path.GetExtension(path);
        return _all.FirstOrDefault(i =>
            i.FileExtensions.Any(e => e.Equals(ext, StringComparison.OrdinalIgnoreCase)));
    }

    private static string LabelFor(IDialogImporter importer) => importer switch
    {
        CsvDialogImporter     => "CSV Dialog",
        JsonDialogImporter    => "JSON Dialog",
        ArticyXmlImporter     => "Articy Draft XML",
        YarnSpinnerImporter   => "Yarn Spinner",
        _                     => "Dialog File",
    };
}
