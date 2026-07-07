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
}
