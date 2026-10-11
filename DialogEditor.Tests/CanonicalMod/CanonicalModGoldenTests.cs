using System.IO.Compression;

namespace DialogEditor.Tests.CanonicalMod;

/// The checked-in PoE2 canonical mod must equal a fresh build (issue 138, spec §4). If the
/// editor's output changed on purpose, rerun with DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1:
/// that rewrites the files AND fails, so a regeneration is always a reviewed diff, never a
/// silent pass.
[Trait("Category", "Canonical")]
public class CanonicalModGoldenTests
{
    private const string RegenerateVar = "DIALOGEDITOR_REGENERATE_CANONICAL_MOD";

    private static string TempDir(string tag) => Path.Combine(Path.GetTempPath(), $"canonical {tag} {Guid.NewGuid():N}");

    [Fact]
    public async Task Poe2_CheckedInMod_MatchesAFreshBuild()
    {
        var fresh = TempDir("golden");
        try
        {
            await CanonicalModBuilder.BuildAllAsync("poe2", Poe2Scenario.Steps, fresh);
            var golden = CanonicalModBuilder.GoldenDir("poe2");

            if (Environment.GetEnvironmentVariable(RegenerateVar) == "1")
            {
                if (Directory.Exists(golden)) Directory.Delete(golden, true);
                GoldenDiff.CopyTree(fresh, golden);
                Assert.Fail($"Regenerated {golden}. Review the diff, commit it, and rerun without {RegenerateVar}.");
            }

            Assert.Null(GoldenDiff.First(golden, fresh));
        }
        finally { try { Directory.Delete(fresh, true); } catch (Exception) { /* best-effort */ } }
    }

    // Review Focus 1: nothing nondeterministic (layout, ordering, clock) leaks into the output.
    [Fact]
    public async Task Poe2_TwoBuilds_AreIdentical()
    {
        var a = TempDir("a");
        var b = TempDir("b");
        try
        {
            await CanonicalModBuilder.BuildAllAsync("poe2", Poe2Scenario.Steps, a);
            await CanonicalModBuilder.BuildAllAsync("poe2", Poe2Scenario.Steps, b);
            Assert.Null(GoldenDiff.First(a, b));
        }
        finally
        {
            foreach (var d in new[] { a, b })
                try { Directory.Delete(d, true); } catch (Exception) { /* best-effort */ }
        }
    }

    // Review Focus 5: the pack and the project beside it can't drift apart.
    [Fact]
    public void Poe2_PackCarriesTheCheckedInProject()
    {
        var golden = CanonicalModBuilder.GoldenDir("poe2");
        using var zip    = ZipFile.OpenRead(Path.Combine(golden, CanonicalModBuilder.ProjectName + ".dialogpack"));
        using var reader = new StreamReader(zip.GetEntry("project.dialogproject")!.Open());
        Assert.Equal(GoldenDiff.Normalize(File.ReadAllText(Path.Combine(golden, CanonicalModBuilder.ProjectName + ".dialogproject"))),
                     GoldenDiff.Normalize(reader.ReadToEnd()));
    }

    [Fact]
    public void GoldenDiff_ReportsTheFirstDifferingLine()
    {
        var a = TempDir("diff-a");
        var b = TempDir("diff-b");
        try
        {
            Directory.CreateDirectory(a); Directory.CreateDirectory(b);
            File.WriteAllText(Path.Combine(a, "x.md"), "same\r\nold\n");
            File.WriteAllText(Path.Combine(b, "x.md"), "same\nnew\n");
            Assert.Equal("x.md:2: expected 'old' / actual 'new'", GoldenDiff.First(a, b));

            File.WriteAllText(Path.Combine(b, "x.md"), "same\r\nold\r\n");   // line endings alone are not a difference
            Assert.Null(GoldenDiff.First(a, b));
        }
        finally
        {
            foreach (var d in new[] { a, b })
                try { Directory.Delete(d, true); } catch (Exception) { /* best-effort */ }
        }
    }
}
