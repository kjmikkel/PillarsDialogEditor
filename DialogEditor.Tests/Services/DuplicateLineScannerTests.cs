using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class DuplicateLineScannerTests
{
    private const string L = "the wind howls through the rigging tonight"; // 7 words

    private static ConversationPatch PatchWith(string conv, params (int Id, string Text)[] lines)
    {
        var entries = lines.Select(l => new NodeTranslation(l.Id, l.Text, "")).ToList();
        return new ConversationPatch(conv, ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = new Dictionary<string, IReadOnlyList<NodeTranslation>> { ["en"] = entries }
        };
    }

    private static DialogProject Project(params ConversationPatch[] patches)
    {
        var p = DialogProject.Empty("P");
        foreach (var patch in patches) p = p.WithPatch(patch);
        return p;
    }

    [Fact] // Exact across two conversations; case/whitespace normalized
    public void Exact_AcrossConversations_Grouped()
    {
        var project = Project(
            PatchWith("c1", (1, L)),
            PatchWith("c2", (2, "  THE WIND   howls through the rigging tonight ")));

        var report = DuplicateLineScanner.Scan(project, "en");

        var group = Assert.Single(report.Exact);
        Assert.Equal(2, group.Members.Count);
        Assert.Empty(report.Near);
    }

    [Fact] // One-word change stays above 0.85 → near pair
    public void Near_OneWordChange_Flagged()
    {
        var project = Project(PatchWith("c1",
            (1, L),
            (2, "the wind howls through the rigging today")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        var pair = Assert.Single(report.Near);
        Assert.True(pair.SimilarityPercent >= 85);
    }

    [Fact] // Clearly different long lines → nothing
    public void Near_BelowThreshold_NotFlagged()
    {
        var project = Project(PatchWith("c1",
            (1, "the wind howls through the rigging tonight"),
            (2, "a merchant counts his coins beneath the lantern")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // Fewer than 4 words → excluded from both tiers
    public void ShortLines_Excluded()
    {
        var project = Project(PatchWith("c1", (1, "hello there friend"), (2, "hello there friend")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // A candidate in an exact cluster is never also a near pair
    public void ExactMembers_NotAlsoNear()
    {
        var project = Project(PatchWith("c1",
            (1, L),
            (2, L),
            (3, "the wind howls through the rigging today")));  // near to L, but L is an exact cluster

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Single(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // Ignored exact key filtered from the active report
    public void IgnoredExact_Filtered()
    {
        var norm = "the wind howls through the rigging tonight";
        var project = Project(PatchWith("c1", (1, L), (2, L)))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Exact, [norm], norm));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
    }

    [Fact] // Ignored near key-pair filtered
    public void IgnoredNear_Filtered()
    {
        var a = "the wind howls through the rigging tonight";
        var b = "the wind howls through the rigging today";
        var keys = new[] { a, b }.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var project = Project(PatchWith("c1", (1, a), (2, b)))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Near, keys, "«a» ~ «b»"));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Near);
    }

    // ── Configurable threshold (issue #14) ───────────────────────────────────

    [Fact] // The default argument preserves the historical 0.85 behaviour.
    public void DefaultNearThreshold_Is085()
    {
        Assert.Equal(0.85, DuplicateLineScanner.DefaultNearThreshold);
    }

    [Fact] // A looser bar surfaces a pair the default rejects.
    public void Near_LoweredThreshold_FlagsPairRejectedAtDefault()
    {
        var project = Project(PatchWith("c1",
            (1, "the wind howls through the rigging tonight"),
            (2, "the wind howls through the rigging in the dark")));

        Assert.Empty(DuplicateLineScanner.Scan(project, "en").Near);

        var loosened = DuplicateLineScanner.Scan(project, "en", 0.70);
        Assert.Single(loosened.Near);
    }

    [Fact] // A stricter bar drops a pair the default accepts.
    public void Near_RaisedThreshold_RejectsPairAcceptedAtDefault()
    {
        var project = Project(PatchWith("c1",
            (1, "the wind howls through the rigging tonight"),
            (2, "the wind howls through the rigging today")));

        Assert.Single(DuplicateLineScanner.Scan(project, "en").Near);
        Assert.Empty(DuplicateLineScanner.Scan(project, "en", 0.95).Near);
    }

    /// Regression for the length-blocking loop break, which divides by the threshold to
    /// decide when no later (longer) candidate can qualify. If that break keeps the old
    /// 0.85 constant while the accept test becomes configurable, a LOWERED threshold
    /// silently misses pairs — a wrong answer with no error. These two lines are 34 and
    /// 42 chars: 42 > 34/0.85 (= 40.0), so the stale break bails out before comparing
    /// them, but 42 < 34/0.75 (= 45.3), so a correct implementation still finds them.
    [Fact]
    public void Near_LoweredThreshold_LengthBlockUsesConfiguredValue()
    {
        var project = Project(PatchWith("c1",
            (1, "the wind howls through the rigging"),
            (2, "the wind howls through the rigging tonight")));

        var report = DuplicateLineScanner.Scan(project, "en", 0.75);

        var pair = Assert.Single(report.Near);
        Assert.True(pair.SimilarityPercent >= 75,
            $"expected the pair to clear the configured bar, got {pair.SimilarityPercent}%");
    }

    [Theory] // settings.json is hand-editable; a nonsense threshold must clamp, not crash.
    [InlineData(0.0)]     // would make the length-block divisor +infinity
    [InlineData(-1.0)]
    [InlineData(1.5)]
    [InlineData(double.NaN)]
    public void Threshold_OutOfRange_ClampsWithoutCrashing(double threshold)
    {
        var project = Project(PatchWith("c1",
            (1, "the wind howls through the rigging tonight"),
            (2, "the wind howls through the rigging today")));

        var report = DuplicateLineScanner.Scan(project, "en", threshold);

        Assert.NotNull(report);
        Assert.All(report.Near, p => Assert.InRange(p.SimilarityPercent, 0, 100));
    }

    /// Ignore keys are the normalized texts, not the threshold that surfaced the pair,
    /// so loosening or tightening the bar must never resurrect something the writer
    /// already dismissed as intentional.
    [Theory]
    [InlineData(0.75)]
    [InlineData(0.85)]
    [InlineData(0.95)]
    public void IgnoredNear_StaysIgnored_AtEveryThreshold(double threshold)
    {
        var a = "the wind howls through the rigging tonight";
        var b = "the wind howls through the rigging today";
        var keys = new[] { a, b }.OrderBy(s => s, StringComparer.Ordinal).ToList();
        var project = Project(PatchWith("c1", (1, a), (2, b)))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Near, keys, "«a» ~ «b»"));

        Assert.Empty(DuplicateLineScanner.Scan(project, "en", threshold).Near);
    }
}
