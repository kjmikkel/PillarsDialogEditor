using DialogEditor.Core.Models;

namespace DialogEditor.Tests.CanonicalMod;

/// The PoE2 canonical mod (issue 138, spec §2): edits to the canonical conversation, made
/// through the real view models, each paired with the expectation that proves it reached
/// the game files. Node ids follow Fixtures/Canonical/README.md. All content is invented.
public static class Poe2Scenario
{
    public const string Edited          = "canonical";
    public const string NewConversation = "canonical_mod_new";

    public static IReadOnlyList<string> Conversations { get; } = [Edited];

    public static IReadOnlyList<ScenarioStep> Steps { get; } =
    [
        new("nodes.add-npc-line",
            "New NPC line 11, linked from 10",
            async s =>
            {
                s.Open(Edited);
                var n = s.AddNode(11, playerChoice: false, SpeakerCategory.Npc, CanonicalIds.Npc, CanonicalIds.Player, 1500, 0);
                n.DefaultText = "A new closing line, written by the canonical mod.";
                s.Link(10, 11);
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.Equal("A new closing line, written by the canonical mod.", v.Text(Edited, "en", 11)!.DefaultText);
                Assert.Contains(v.Node(Edited, 10).Links, l => l.ToNodeId == 11);
            }),
    ];
}
