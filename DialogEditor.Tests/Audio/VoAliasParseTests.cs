using DialogEditor.Core.Audio;

namespace DialogEditor.Tests.Audio;

// Shapes taken from the 2026-07-03 audit of all 787 distinct shipped values.
public class VoAliasParseTests
{
    [Fact]
    public void TryParse_CanonicalShape_SplitsFolderConversationAndId()
    {
        var t = VoAliasParse.TryParse("narrator/05_cv_dragon_dais_0001");
        Assert.NotNull(t);
        Assert.Equal("narrator", t!.SpeakerFolder);
        Assert.Equal("05_cv_dragon_dais", t.Conversation);
        Assert.Equal(1, t.NodeId);
    }

    [Fact]
    public void TryParse_HyphenatedConversation_Parses()
    {
        var t = VoAliasParse.TryParse("mt_magran/re_si_post_magransteeth_gods-2_0336");
        Assert.NotNull(t);
        Assert.Equal("re_si_post_magransteeth_gods-2", t!.Conversation);
        Assert.Equal(336, t.NodeId);
    }

    [Fact]
    public void TryParse_FolderWithSpaces_Parses()
    {
        var t = VoAliasParse.TryParse("erol of levi/27_cv_court_of_woedica_player_interrupts_0176");
        Assert.NotNull(t);
        Assert.Equal("erol of levi", t!.SpeakerFolder);
        Assert.Equal(176, t.NodeId);
    }

    [Fact]
    public void TryParse_UppercaseFilename_ParsesPreservingCase()
    {
        var t = VoAliasParse.TryParse("dawnstar_guide/sh_Dawnstar_Guide_09_cv_maren_0005");
        Assert.NotNull(t);
        Assert.Equal("sh_Dawnstar_Guide_09_cv_maren", t!.Conversation);
        Assert.Equal(5, t.NodeId);
    }

    [Fact]
    public void TryParse_FiveDigitId_Parses()
    {
        // The writer pads with {nodeId:0000} — a MINIMUM of four digits.
        var t = VoAliasParse.TryParse("eder/companion_eder_10234");
        Assert.NotNull(t);
        Assert.Equal(10234, t!.NodeId);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("no_slash_0001")]                  // no folder segment
    [InlineData("narrator/no_digit_suffix")]       // no trailing _#### block
    [InlineData("narrator/short_012")]             // fewer than four digits
    [InlineData("a/b/c_0001")]                     // two slashes — not a shipped shape
    [InlineData("narrator/")]                      // empty file segment
    [InlineData("/conv_0001")]                     // empty folder segment
    public void TryParse_NonMatching_ReturnsNull(string? input)
        => Assert.Null(VoAliasParse.TryParse(input));
}
