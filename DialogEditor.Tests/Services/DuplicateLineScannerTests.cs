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

    /// Flexible fixture: rows are (language, nodeId, defaultText, femaleText), grouped
    /// into Translations by language. PatchWith above stays for the single-language,
    /// default-text-only cases it already serves.
    private static ConversationPatch PatchTr(
        string conv, params (string Lang, int Id, string Def, string Fem)[] rows)
    {
        var byLang = rows
            .GroupBy(r => r.Lang)
            .ToDictionary(
                g => g.Key,
                g => (IReadOnlyList<NodeTranslation>)g
                    .Select(r => new NodeTranslation(r.Id, r.Def, r.Fem)).ToList());
        return new ConversationPatch(conv, ConversationPatch.CurrentSchemaVersion, [], [], [])
        {
            Translations = byLang
        };
    }

    private static DuplicateScanOptions Opts(
        bool female = false, bool otherLangs = false,
        double threshold = DuplicateLineScanner.DefaultNearThreshold) =>
        new(threshold, female, otherLangs);

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

        var loosened = DuplicateLineScanner.Scan(project, "en", Opts(threshold: 0.70));
        Assert.Single(loosened.Near);
    }

    [Fact] // A stricter bar drops a pair the default accepts.
    public void Near_RaisedThreshold_RejectsPairAcceptedAtDefault()
    {
        var project = Project(PatchWith("c1",
            (1, "the wind howls through the rigging tonight"),
            (2, "the wind howls through the rigging today")));

        Assert.Single(DuplicateLineScanner.Scan(project, "en").Near);
        Assert.Empty(DuplicateLineScanner.Scan(project, "en", Opts(threshold: 0.95)).Near);
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

        var report = DuplicateLineScanner.Scan(project, "en", Opts(threshold: 0.75));

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

        var report = DuplicateLineScanner.Scan(project, "en", Opts(threshold: threshold));

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

        Assert.Empty(DuplicateLineScanner.Scan(project, "en", Opts(threshold: threshold)).Near);
    }

    // ── Widened field scope: female text + other languages (issue #14) ───────

    private const string A = "the lantern swings against the mast";        // 6 words
    private const string B = "the lantern swings against the rail";         // near-variant of A
    private const string C = "she counts the coins beneath the lantern";    // 7 words, unrelated

    [Fact] // Default scope is unchanged: female text contributes nothing.
    public void FemaleText_Off_NotScanned()
    {
        var project = Project(PatchTr("c1", ("en", 1, C, A), ("en", 2, "a wholly different line here", A)));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    [Fact] // Enabled, the same two female lines are an exact duplicate.
    public void FemaleText_On_Scanned()
    {
        var project = Project(PatchTr("c1", ("en", 1, C, A), ("en", 2, "a wholly different line here", A)));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(female: true));

        var group = Assert.Single(report.Exact);
        Assert.Equal(2, group.Members.Count);
        Assert.All(group.Members, m => Assert.True(m.IsFemale));
    }

    /// Rule 1, exact tier. A node whose female text is identical to its own default is
    /// the ordinary case for an ungendered line — reporting it would fire on a large
    /// share of the project the moment female text is switched on.
    [Fact]
    public void SameNode_DefaultEqualsFemale_NotReportedAsExact()
    {
        var project = Project(PatchTr("c1", ("en", 1, A, A)));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(female: true));

        Assert.Empty(report.Exact);
        Assert.Empty(report.Near);
    }

    /// Rule 1, near tier — the highest-value test here. A female variant differing by a
    /// pronoun is what the data is SUPPOSED to look like; pairing it with its own default
    /// would manufacture one false finding per gendered line.
    [Fact]
    public void SameNode_FemaleNearVariantOfDefault_NotReportedAsNear()
    {
        var project = Project(PatchTr("c1", ("en", 1, A, B)));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(female: true));

        Assert.Empty(report.Near);
        Assert.Empty(report.Exact);
    }

    [Fact] // A genuine cross-node female duplicate still surfaces alongside rule 1.
    public void DifferentNodes_FemaleDuplicate_StillReported()
    {
        var project = Project(PatchTr("c1", ("en", 1, A, C), ("en", 2, B, C)));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(female: true));

        var group = Assert.Single(report.Exact);
        Assert.Equal([1, 2], group.Members.Select(m => m.NodeId).OrderBy(n => n));
    }

    [Fact] // Default scope is unchanged: non-primary languages contribute nothing.
    public void OtherLanguages_Off_NotScanned()
    {
        var project = Project(PatchTr("c1", ("en", 1, C, ""), ("de", 1, A, ""), ("de", 2, A, "")));

        var report = DuplicateLineScanner.Scan(project, "en");

        Assert.Empty(report.Exact);
    }

    [Fact]
    public void OtherLanguages_On_Scanned()
    {
        var project = Project(PatchTr("c1", ("en", 1, C, ""), ("de", 1, A, ""), ("de", 2, A, "")));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(otherLangs: true));

        var group = Assert.Single(report.Exact);
        Assert.All(group.Members, m => Assert.Equal("de", m.Language));
    }

    /// Rule 2. Levenshtein across two languages is noise — an English line and a German
    /// line that happen to be textually close are not a copy-paste artifact.
    [Fact]
    public void CrossLanguage_NearPair_NeverFormed()
    {
        var project = Project(PatchTr("c1", ("en", 1, A, ""), ("de", 2, B, "")));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(otherLangs: true));

        Assert.Empty(report.Near);
        Assert.Empty(report.Exact);
    }

    [Fact] // Rule 2, exact tier: same text in two languages is two groups, not one of four.
    public void SameTextInTwoLanguages_FormsTwoSeparateGroups()
    {
        var project = Project(PatchTr("c1",
            ("en", 1, A, ""), ("en", 2, A, ""),
            ("de", 3, A, ""), ("de", 4, A, "")));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(otherLangs: true));

        Assert.Equal(2, report.Exact.Count);
        Assert.All(report.Exact, g => Assert.Equal(2, g.Members.Count));
        Assert.All(report.Exact, g => Assert.Single(g.Members.Select(m => m.Language).Distinct()));
    }

    /// Rule 3. The candidate dictionary was keyed (Conv, Node), which silently kept only
    /// the first text per node. All four facets of node 1 must coexist.
    [Fact]
    public void WidenedKey_KeepsDefaultFemaleAndOtherLanguageForOneNode()
    {
        var project = Project(PatchTr("c1",
            ("en", 1, A, C),        // node 1: default A, female C
            ("en", 2, C, ""),       // pairs with node 1's FEMALE text
            ("de", 1, B, ""),       // node 1 again, other language
            ("de", 2, B, "")));     // pairs with node 1's German text

        var report = DuplicateLineScanner.Scan(project, "en", Opts(female: true, otherLangs: true));

        Assert.Equal(2, report.Exact.Count);
        Assert.Contains(report.Exact, g => g.Members.All(m => m.Language == ""));
        Assert.Contains(report.Exact, g => g.Members.All(m => m.Language == "de"));
    }

    [Fact] // Primary language is reported as "" (matching TextTagIssueRow), others by code.
    public void LineRef_Language_EmptyForPrimary_CodeOtherwise()
    {
        var project = Project(PatchTr("c1",
            ("en", 1, A, ""), ("en", 2, A, ""),
            ("de", 3, C, ""), ("de", 4, C, "")));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(otherLangs: true));

        var langs = report.Exact.SelectMany(g => g.Members).Select(m => m.Language).Distinct().Order();
        Assert.Equal(["", "de"], langs);
    }

    /// Documented, deliberate limitation: IgnoredDuplicate.Keys is normalized text with no
    /// language facet, so an ignore silences a byte-identical line in every language. Pinned
    /// so it stays a decision rather than drifting into an accident.
    [Fact]
    public void IgnoredExact_SilencesTheSameTextInEveryLanguage()
    {
        var norm = A.ToLowerInvariant();
        var project = Project(PatchTr("c1",
                ("en", 1, A, ""), ("en", 2, A, ""),
                ("de", 3, A, ""), ("de", 4, A, "")))
            .WithIgnoredDuplicate(new IgnoredDuplicate(DuplicateKind.Exact, [norm], norm));

        var report = DuplicateLineScanner.Scan(project, "en", Opts(otherLangs: true));

        Assert.Empty(report.Exact);
    }
}
