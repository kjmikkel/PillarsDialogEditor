using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class TokenValidationFalsePositiveTests
{
    private readonly TokenValidationService _svc = new();

    public static TheoryData<string> ShippedConventions => new()
    {
        // Stage directions
        "[Say nothing.]", "[Attack]", "[Leave]", "[Lie]", "[Remain silent.]",
        "[Draw your weapons and attack.]", "[Hand over the coins.]", "[Nod.]",
        "[Refuse.]", "[Wait.]", "[Give the letter to him.]", "[Lie] \"I saw nothing.\"",
        // Language markers
        "[Vailian]", "[Huana]", "[Rauataian]", "[Engwithan]", "[Ixamitl]",
        "[Eld Aedyran]", "[Ordhjóma]", "[Lembur]",
        // VO / chatter annotations (PoE1)
        "[Pained grunt]", "[Sighs]", "[Laughs]", "[A low whistle]",
        // Skill & disposition labels (engine-built, never authored — but appear in text)
        "[Diplomacy]", "[Honest]", "[Perception]", "[Might]", "[Bluff]",
    };

    [Theory]
    [MemberData(nameof(ShippedConventions))]
    public void ShippedConvention_ProducesNoIssues_Poe2(string convention)
        => Assert.Empty(_svc.Validate($"Option: {convention}", "poe2"));

    [Theory]
    [MemberData(nameof(ShippedConventions))]
    public void ShippedConvention_ProducesNoIssues_Poe1(string convention)
        => Assert.Empty(_svc.Validate($"Option: {convention}", "poe1"));

    [Fact]
    public void ShippedMalformedLink_ProducesNoIssues()
    {
        // Real shape: neutralvalue attribute missing its closing quote.
        var text = "[Vailian] <link=\"neutralvalue://Vailian: Do you speak Vailian, sir?>" +
                   "\"Perla Vailian, fentre?\"</link>";
        Assert.Empty(_svc.Validate(text, "poe2"));
    }
}
