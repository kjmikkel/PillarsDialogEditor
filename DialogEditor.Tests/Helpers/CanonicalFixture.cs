namespace DialogEditor.Tests.Helpers;

/// The checked-in canonical conversations (issue 122), each laid out as a miniature game
/// install under Fixtures/Canonical/{poe1,poe2}. See Fixtures/Canonical/README.md.
internal static class CanonicalFixture
{
    public static string SourceDir(string game) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Canonical", game);

    /// Copies the fixture's install tree into <paramref name="destRoot"/>, so a test can
    /// write to it without touching the checked-in copy.
    public static void CopyTo(string game, string destRoot)
    {
        var source = SourceDir(game);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Canonical fixture not copied to output: {source}");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(destRoot, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
    }
}
