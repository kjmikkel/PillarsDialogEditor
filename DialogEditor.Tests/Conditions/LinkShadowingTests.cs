using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.Conditions;

public class LinkShadowingTests
{
    private static ShadowLink T(LinkTargetKind k) => new(Truth.True, k);
    private static ShadowLink U(LinkTargetKind k) => new(Truth.Unknown, k);
    private static ShadowLink F(LinkTargetKind k) => new(Truth.False, k);
    private const LinkTargetKind N = LinkTargetKind.Npc;
    private const LinkTargetKind R = LinkTargetKind.Response;
    private const LinkTargetKind O = LinkTargetKind.Opaque;

    private static void Check(int?[] expected, string playType, params ShadowLink?[] links)
        => Assert.Equal(expected, LinkShadowing.HiddenBy(playType, links).ToArray());

    [Fact]
    public void CertainNpcFirst_HidesAllLater()
        => Check([null, 0, 0], "Normal", T(N), U(N), U(R));

    [Fact]
    public void CertainResponseFirst_HidesLaterNpcOnly()
        => Check([null, 0, null, 0], "Normal", T(R), U(N), U(R), T(N));

    [Fact]
    public void UnknownFirst_HidesNothingBeforeCertain()
        => Check([null, null, 1], "Normal", U(N), T(N), U(N));

    [Fact]
    public void MaybeResponseBeforeCertainNpc_KeepsLaterResponses()
        => Check([null, null, null, 1], "Normal", U(R), T(N), U(R), U(N));

    [Fact]
    public void OpaqueCountsAsMaybeResponse()
        => Check([null, null, null], "Normal", U(O), T(N), U(R));

    [Fact]
    public void FalseLinksSkipped()
        => Check([null, null, 1], "Normal", F(N), T(N), U(N));

    [Fact]
    public void DanglingIgnored()
        => Check([null, null, null, 1], "Normal", null, T(N), null, U(N));

    [Fact]
    public void OnlyFirstCertainHides()
        => Check([null, 0], "Normal", T(N), T(N));

    [Theory]
    [InlineData("Random")]
    [InlineData("CycleLoop")]
    [InlineData("Unknown(9)")]
    public void NonNormalPlayType_HidesNothing(string playType)
        => Check([null, null], playType, T(N), U(N));

    [Theory]
    [InlineData("")]
    [InlineData("Normal")]
    public void NormalOrEmptyPlayType_Hides(string playType)
        => Check([null, 0], playType, T(N), U(N));

    private static ConditionLeaf Global(string name, string value)
        => new("Boolean IsGlobalValue(String, Operator, Int32)", [name, "EqualTo", value], Not: false, Operator: "And");

    private static GameState State()
    {
        var s = new GameState();
        s.Globals["g"] = 0;
        return s;
    }

    private static ShadowTarget Target(bool player = false, ScriptNodeKind kind = ScriptNodeKind.Script,
        bool question = false, string persistence = "None", IReadOnlyList<ConditionNode>? conds = null)
        => new(player, kind, question, persistence, conds ?? []);

    [Fact]
    public void Describe_NoConditionsNonePersistence_IsTrue()
        => Assert.Equal(new ShadowLink(Truth.True, N),
            LinkShadowing.Describe([], Target(), new GameState(), "poe2"));

    [Fact]
    public void Describe_OncePerConversationTarget_IsUnknown()
        => Assert.Equal(new ShadowLink(Truth.Unknown, N),
            LinkShadowing.Describe([], Target(persistence: "OncePerConversation"), new GameState(), "poe2"));

    [Fact]
    public void Describe_BankTarget_IsOpaqueUnknown()
        => Assert.Equal(new ShadowLink(Truth.Unknown, O),
            LinkShadowing.Describe([], Target(kind: ScriptNodeKind.Bank), new GameState(), "poe2"));

    [Fact]
    public void Describe_QuestionNodeTarget_IsOpaque()
        => Assert.Equal(new ShadowLink(Truth.Unknown, O),
            LinkShadowing.Describe([], Target(player: true, question: true), new GameState(), "poe2"));

    [Fact]
    public void Describe_PlayerChoiceTarget_IsResponse()
        => Assert.Equal(new ShadowLink(Truth.True, R),
            LinkShadowing.Describe([], Target(player: true, persistence: ""), new GameState(), "poe2"));

    [Fact]
    public void Describe_FalseUnderState_IsFalse()
        => Assert.Equal(new ShadowLink(Truth.False, N),
            LinkShadowing.Describe([Global("g", "1")], Target(), State(), "poe2"));

    [Fact]
    public void Describe_ConditionUnknownWithEmptyState_IsUnknown()
        => Assert.Equal(new ShadowLink(Truth.Unknown, N),
            LinkShadowing.Describe([Global("g", "1")], Target(), new GameState(), "poe2"));
}
