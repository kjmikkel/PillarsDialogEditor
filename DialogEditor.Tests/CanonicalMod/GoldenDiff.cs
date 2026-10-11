using System.IO.Compression;

namespace DialogEditor.Tests.CanonicalMod;

/// Compares a checked-in golden tree with a fresh build (issue 138). Text is compared with
/// line endings normalised (git's autocrlf rewrites them on checkout); a .dialogpack by its
/// entry list and each entry's content, ignoring zip metadata such as timestamps; anything
/// else byte for byte. Returns the first difference, or null when the trees match.
public static class GoldenDiff
{
    private static readonly HashSet<string> TextExtensions =
        new(StringComparer.OrdinalIgnoreCase) { ".dialogproject", ".md", ".json", ".txt" };

    public static string Normalize(string text) => text.Replace("\r\n", "\n");

    public static string? First(string expectedDir, string actualDir)
    {
        var expected = Files(expectedDir);
        var actual   = Files(actualDir);
        if (expected.Except(actual).FirstOrDefault() is { } missing) return $"{missing}: missing from the fresh build";
        if (actual.Except(expected).FirstOrDefault() is { } extra)   return $"{extra}: not in the checked-in copy";

        foreach (var rel in expected)
        {
            var a = Path.Combine(expectedDir, rel);
            var b = Path.Combine(actualDir, rel);
            var diff = rel.EndsWith(".dialogpack", StringComparison.OrdinalIgnoreCase)
                ? PackDiff(a, b)
                : ContentDiff(Path.GetExtension(rel), File.ReadAllBytes(a), File.ReadAllBytes(b));
            if (diff is not null) return $"{rel}{diff}";
        }
        return null;
    }

    public static void CopyTree(string from, string to)
    {
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var d = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(d)!);
            File.Copy(f, d, true);
        }
    }

    private static List<string> Files(string dir) =>
        [.. Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)
                     .Select(f => Path.GetRelativePath(dir, f).Replace('\\', '/'))
                     .Order(StringComparer.Ordinal)];

    private static string? PackDiff(string a, string b)
    {
        using var za = ZipFile.OpenRead(a);
        using var zb = ZipFile.OpenRead(b);
        var ea = za.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
        var eb = zb.Entries.Select(e => e.FullName).Order(StringComparer.Ordinal).ToList();
        if (!ea.SequenceEqual(eb)) return $": entries differ: [{string.Join(", ", ea)}] / [{string.Join(", ", eb)}]";
        foreach (var name in ea)
            if (ContentDiff(Path.GetExtension(name), Read(za.GetEntry(name)!), Read(zb.GetEntry(name)!)) is { } d)
                return $"!{name}{d}";
        return null;
    }

    private static byte[] Read(ZipArchiveEntry entry)
    {
        using var s  = entry.Open();
        using var ms = new MemoryStream();
        s.CopyTo(ms);
        return ms.ToArray();
    }

    private static string? ContentDiff(string extension, byte[] a, byte[] b)
    {
        if (!TextExtensions.Contains(extension))
            return a.AsSpan().SequenceEqual(b) ? null : ": bytes differ";

        var la = Normalize(System.Text.Encoding.UTF8.GetString(a)).Split('\n');
        var lb = Normalize(System.Text.Encoding.UTF8.GetString(b)).Split('\n');
        for (var i = 0; i < Math.Max(la.Length, lb.Length); i++)
        {
            var x = i < la.Length ? la[i] : "<end of file>";
            var y = i < lb.Length ? lb[i] : "<end of file>";
            if (x != y) return $":{i + 1}: expected '{x}' / actual '{y}'";
        }
        return null;
    }
}
