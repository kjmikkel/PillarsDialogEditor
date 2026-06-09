using System.Text;
using Avalonia.Media;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Approval-style golden lock: serialises every palette's every Color primitive to a sorted
/// text block and compares it to a committed file. When a palette's values change intentionally,
/// copy the generated .received.txt over .approved.txt in the same commit. Locks all Layer 1
/// values, including the Okabe-Ito colourblind remaps, so they cannot silently drift (spec §5).
/// </summary>
public class PaletteGoldenTests
{
    private static string Render()
    {
        var sb = new StringBuilder();
        foreach (var set in PaletteHarness.AllSets)
        {
            var dict = PaletteHarness.Load(set);
            foreach (var key in dict.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal))
            {
                if (dict.TryGetResource(key, null, out var v) && v is Color c)
                    sb.Append(set).Append('\t').Append(key).Append('\t')
                      .AppendFormat("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string Dir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DialogEditor.slnx")))
            d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "DialogEditor.Tests", "Theming");
    }

    [AvaloniaFact]
    public void AllPaletteValuesMatchApproved()
    {
        var actual = Render();
        var approvedPath = Path.Combine(Dir(), "palette-golden.approved.txt");
        var receivedPath = Path.Combine(Dir(), "palette-golden.received.txt");

        var approved = File.Exists(approvedPath)
            ? File.ReadAllText(approvedPath).Replace("\r\n", "\n")
            : "";

        if (actual != approved)
        {
            File.WriteAllText(receivedPath, actual);
            Assert.Fail($"Palette golden mismatch. If intentional, copy {receivedPath} over {approvedPath}.");
        }
        else if (File.Exists(receivedPath))
        {
            File.Delete(receivedPath);
        }
    }
}
