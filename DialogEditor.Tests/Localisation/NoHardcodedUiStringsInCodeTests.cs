using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// C#-side counterpart to NoHardcodedUiStringsTests, which scans *.axaml only —
/// a gap that let FlowIssueViewModel.DisplayText ship as $"Node {id} — {snippet}".
///
/// Shape: opt-in marker. Any literal the predicate calls user-visible prose is a
/// defect unless an enclosing declaration carries [NotLocalised("reason")], so the
/// legitimate exceptions (file-format emitters, expression serialisers, developer
/// diagnostics) are documented at the site rather than in a drifting allowlist.
/// </summary>
public class HardcodedStringScannerTests
{
    private const string Marked = "using DialogEditor.Core.Localisation;\n";

    [Fact]
    public void Scan_FlagsProseInAnExpressionBodiedProperty()
    {
        var offenders = HardcodedStringScanner.Scan("""
            class C { public string Description => "Add annotation"; }
            """);

        Assert.Equal("Add annotation", Assert.Single(offenders).Text);
    }

    [Fact]
    public void Scan_RebuildsInterpolatedStringWithNumberedPlaceholders()
    {
        // A translator sees the whole sentence, not the fragments either side of a
        // hole — so the predicate must judge "Node {0} — {1}", not "Node " and " — ".
        var offenders = HardcodedStringScanner.Scan("""
            class C { public string T => $"Node {Id} — {Snippet}"; }
            """);

        Assert.Equal("Node {0} — {1}", Assert.Single(offenders).Text);
    }

    [Fact]
    public void Scan_IgnoresLiteralUnderMemberMarkedNotLocalised()
    {
        var offenders = HardcodedStringScanner.Scan(Marked + """
            class C {
                [NotLocalised("Yarn file syntax, not prose")]
                public string T => $"title: {Id}";
            }
            """);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scan_IgnoresLiteralUnderTypeMarkedNotLocalised()
    {
        var offenders = HardcodedStringScanner.Scan(Marked + """
            [NotLocalised("Emits Yarn Spinner files")]
            class C { public string T => "-> choice text here"; }
            """);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scan_IgnoresResourceKeysPassedToLoc()
    {
        // Keys are identifiers, not prose — and they are how localisation is DONE.
        var offenders = HardcodedStringScanner.Scan("""
            class C {
                public string A => Loc.Get("Some_Key");
                public string B => Loc.Format("Other_Key With Space", 1);
            }
            """);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scan_IgnoresDeveloperDiagnostics()
    {
        var offenders = HardcodedStringScanner.Scan("""
            class C {
                void M() {
                    AppLog.Error("Could not open the project file");
                    AppLog.Warn($"Skipping node {Id} because it is broken");
                }
            }
            """);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scan_IgnoresAttributeArgumentsIncludingTheMarkerItself()
    {
        var offenders = HardcodedStringScanner.Scan(Marked + """
            class C {
                [NotLocalised("this reason string is not user-visible text")]
                public string T => "-> emitted verbatim";
            }
            """);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scan_IgnoresMultiLineDataTemplates()
    {
        // Poe2ConversationSerializer builds game JSON from a literal template. It has
        // words and holes, but embedded quotes and newlines mark it as data, not prose.
        var offenders = HardcodedStringScanner.Scan(""""
            class C { const string T = """
                { "$type": "{0}", "NodeID": {1}, "IsQuestionNode": false }
                """; }
            """");

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scan_IgnoresAssemblyQualifiedTypeNames()
    {
        var offenders = HardcodedStringScanner.Scan("""
            class C { const string T = "OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats"; }
            """);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scan_IgnoresIdentifiersThatMerelyContainAPlaceholder()
    {
        // "node_{0}" is a CSV column key; the underscore-joined shape is an identifier,
        // not a sentence, even though it pairs a word with a hole.
        var offenders = HardcodedStringScanner.Scan("""
            class C { string T(int i) => $"node_{i}"; }
            """);

        Assert.Empty(offenders);
    }

    [Fact]
    public void Scan_StillFlagsProseThatPairsAWordWithAPlaceholder()
    {
        // The counterpart to the exclusions above: this shape must survive them,
        // because it is the FlowIssueViewModel bug the whole guard exists to catch.
        var offenders = HardcodedStringScanner.Scan("""
            class C { public string T => $"Node {Id} — {Snippet}"; }
            """);

        Assert.Single(offenders);
    }

    [Fact]
    public void Scan_ReportsTheLineOfTheOffendingLiteral()
    {
        var offenders = HardcodedStringScanner.Scan("class C {\n\n  string T => \"Edit speaker category\";\n}", "Foo.cs");

        var only = Assert.Single(offenders);
        Assert.Equal("Foo.cs", only.File);
        Assert.Equal(3, only.Line);
    }
}

public class NotLocalisedMarkerTests
{
    // End-to-end proof that the opt-out works on REAL production source, not just the
    // synthetic snippets above: CoreLocale is the code that makes localisation work, so
    // its own fallback warning categorically cannot be localised. If the attribute ever
    // stops being honoured, this is the test that says so.
    [Fact]
    public void CoreLocale_FallbackWarning_IsExemptedByTheMarker()
    {
        var file = Path.Combine(TestPaths.SolutionRoot(),
            "DialogEditor.Core", "Resources", "CoreLocale.cs");

        Assert.Empty(HardcodedStringScanner.Scan(File.ReadAllText(file), "CoreLocale.cs"));
    }
}

/// <summary>
/// The repo-wide application of the scanner, ratcheted against a baseline. Production
/// projects only — tests and the UIA tooling are developer-facing and out of scope.
///
/// The guard arrived long after the violations, so it starts from a recorded baseline of
/// pre-existing sites. Two things fail: a NEW hard-coded string, and a baseline entry
/// that no longer offends. The second is what makes it a ratchet rather than an
/// allowlist — cleaned-up sites must leave the file, so it can only shrink.
/// </summary>
public class NoHardcodedUiStringsInCodeTests
{
    private static readonly string[] ScannedProjects =
    [
        "DialogEditor.Core",
        "DialogEditor.Patch",
        "DialogEditor.ViewModels",
        "DialogEditor.Avalonia",
        "DialogEditor.Avalonia.Shared",
    ];

    private static string BaselinePath(string root) =>
        Path.Combine(root, "DialogEditor.Tests", "Localisation", "hardcoded-strings.baseline");

    private static List<LiteralOffender> ScanRepository(string root)
    {
        var offenders = new List<LiteralOffender>();
        foreach (var project in ScannedProjects)
        {
            var dir = Path.Combine(root, project);
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                var parts = file.Split(Path.DirectorySeparatorChar);
                if (parts.Contains("bin") || parts.Contains("obj")) continue;
                offenders.AddRange(HardcodedStringScanner.Scan(
                    File.ReadAllText(file),
                    // Forward slashes so the baseline file is identical on every OS.
                    Path.GetRelativePath(root, file).Replace('\\', '/')));
            }
        }
        return offenders;
    }

    [Fact]
    public void NoNewHardcodedUserVisibleTextInCSharp()
    {
        var root      = TestPaths.SolutionRoot();
        var offenders = ScanRepository(root);

        // Regeneration hatch for the ratchet: after cleaning up a batch, refresh the file
        // rather than hand-editing dozens of lines. Safe because "stale" is also a
        // failure — a casual regeneration cannot quietly re-license a fixed site.
        if (Environment.GetEnvironmentVariable("DIALOGEDITOR_UPDATE_BASELINE") == "1")
        {
            string[] header =
            [
                "# Pre-existing hard-coded UI strings, recorded when the C# localisation",
                "# guard was added. This list may only SHRINK. Clean a site up (route it",
                "# through Loc, or mark it [NotLocalised(...)]) and delete its line.",
                "# Regenerate: DIALOGEDITOR_UPDATE_BASELINE=1 dotnet test",
            ];
            File.WriteAllText(BaselinePath(root),
                string.Join((char)10, header) + (char)10
                + string.Join((char)10, offenders.Select(OffenderBaseline.Key)
                                                 .Distinct()
                                                 .Order(StringComparer.Ordinal))
                + (char)10);
            return;
        }

        var baseline = OffenderBaseline.Parse(File.ReadAllText(BaselinePath(root)));
        var (added, stale) = OffenderBaseline.Compare(baseline, offenders);

        Assert.True(added.Count == 0,
            $"""
             {added.Count} NEW user-visible string literal(s) hard-coded in C#.
             Route the text through Loc.Get/Loc.Format against a resource key, or — if a
             translator must never touch it (a file format, an expression serialisation, a
             command-line argument, a developer diagnostic) — mark the enclosing
             declaration [NotLocalised("why")].

             {string.Join(Environment.NewLine, added)}
             """);

        Assert.True(stale.Count == 0,
            $"""
             {stale.Count} baseline entr(y/ies) no longer offend — delete them so the ratchet
             cannot silently re-license the same mistake later.
             Regenerate with: DIALOGEDITOR_UPDATE_BASELINE=1 dotnet test

             {string.Join(Environment.NewLine, stale)}
             """);
    }
}
