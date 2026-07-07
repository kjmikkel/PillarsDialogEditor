using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class TokenValidationServiceTests
{
    private readonly TokenValidationService _svc = new();

    // ── Known tokens pass ────────────────────────────────────────────────
    [Fact]
    public void KnownToken_NoIssues()
        => Assert.Empty(_svc.Validate("Hello [Player Name], welcome.", "poe2"));

    [Fact]
    public void ParameterisedToken_WithDigit_NoIssues()
        => Assert.Empty(_svc.Validate("[Specified 0] nods. [Slot 3] waits.", "poe2"));

    // ── Lowercase variant is game-aware ──────────────────────────────────
    [Fact]
    public void LowercaseVariant_Poe2_NoIssues()
        => Assert.Empty(_svc.Validate("a proud [player race]", "poe2"));

    [Fact]
    public void LowercaseVariant_Poe1_IsFlagged()
    {
        var issues = _svc.Validate("a proud [player race]", "poe1");
        var issue  = Assert.Single(issues);
        Assert.Equal(TokenIssueKind.UnknownToken, issue.Kind);
        Assert.Equal("[Player Race]", issue.Suggestion);
    }

    [Fact]
    public void LowercaseOfNonLowercaseToken_IsFlagged()
    {
        // [Player Name] has no lowercase form even in PoE2.
        var issue = Assert.Single(_svc.Validate("hi [player name]", "poe2"));
        Assert.Equal("[Player Name]", issue.Suggestion);
    }

    // ── Fuzzy "did you mean" ─────────────────────────────────────────────
    [Fact]
    public void MisspelledToken_FlaggedWithSuggestion()
    {
        var issue = Assert.Single(_svc.Validate("Hello [Player Nmae]!", "poe2"));
        Assert.Equal(TokenIssueKind.UnknownToken, issue.Kind);
        Assert.Equal("[Player Name]", issue.Suggestion);
        Assert.Equal("[Player Nmae]", issue.Fragment);
    }

    [Fact]
    public void MisspelledParameterisedToken_FlaggedWithFamilySuggestion()
    {
        var issue = Assert.Single(_svc.Validate("[Specfied 0] arrives.", "poe2"));
        Assert.Equal("[Specified n]", issue.Suggestion);
    }

    // ── Free-text conventions are silent (the false-positive guard) ──────
    [Theory]
    [InlineData("[Say nothing.]")]
    [InlineData("[Draw your weapons and attack.]")]
    [InlineData("[Attack]")]
    [InlineData("[Lie]")]
    [InlineData("[Leave]")]
    [InlineData("[Vailian]")]
    [InlineData("[Pained grunt]")]
    [InlineData("[Diplomacy]")]
    public void FreeTextConvention_NoIssues(string convention)
        => Assert.Empty(_svc.Validate($"Player option: {convention}", "poe2"));

    // ── Position reported ────────────────────────────────────────────────
    [Fact]
    public void UnknownToken_PositionIsFragmentStart()
    {
        var issue = Assert.Single(_svc.Validate("ab [Player Nmae]", "poe2"));
        Assert.Equal(3, issue.Position);
    }

    // ── Empty / whitespace ───────────────────────────────────────────────
    [Fact]
    public void EmptyText_NoIssues() => Assert.Empty(_svc.Validate("", "poe2"));

    [Fact]
    public void NoBrackets_NoIssues()
        => Assert.Empty(_svc.Validate("plain narration with no tags", "poe2"));

    // ── Markup balance ───────────────────────────────────────────────────
    [Fact]
    public void BalancedItalics_NoIssues()
        => Assert.Empty(_svc.Validate("she said <i>quietly</i> then left", "poe2"));

    [Fact]
    public void UnclosedItalics_Flagged()
    {
        var issue = Assert.Single(_svc.Validate("she said <i>quietly then left", "poe2"));
        Assert.Equal(TokenIssueKind.UnbalancedMarkup, issue.Kind);
        Assert.Equal("<i>", issue.Fragment);
    }

    [Fact]
    public void StrayClosingTag_Flagged()
    {
        var issue = Assert.Single(_svc.Validate("text</i> more", "poe2"));
        Assert.Equal(TokenIssueKind.UnbalancedMarkup, issue.Kind);
        Assert.Equal("</i>", issue.Fragment);
    }

    [Fact]
    public void NestedMarkup_Balanced_NoIssues()
        => Assert.Empty(_svc.Validate("<i><ispeech>voice</ispeech></i>", "poe2"));

    [Fact]
    public void SelfClosingSprite_NeverUnbalanced()
        => Assert.Empty(_svc.Validate("press <sprite=\"Inline\" name=\"Fire\" tint=1>", "poe2"));

    // Leniency: vanilla ships <link> with a missing closing attribute quote.
    // We only balance tag NAMES, never parse attributes, so this must pass.
    [Fact]
    public void MalformedLinkAttribute_NoMarkupIssue()
    {
        var text = "[Vailian] <link=\"neutralvalue://Vailian: hi>\"Perla\"</link>.";
        Assert.DoesNotContain(_svc.Validate(text, "poe2"),
            i => i.Kind == TokenIssueKind.UnbalancedMarkup);
    }

    [Fact]
    public void UnknownTag_NotBalanceChecked()
        => Assert.Empty(_svc.Validate("<b>bold</b> and <foo>", "poe2"));

    // Colour tags balance by name; the attribute value is not parsed.
    [Fact]
    public void BalancedColour_NoIssues()
        => Assert.Empty(_svc.Validate("<color=\"red\">warn</color>", "poe2"));
}
