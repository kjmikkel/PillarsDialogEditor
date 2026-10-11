using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;

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

        new("nodes.add-player-choice",
            "New player choice 12 under 0",
            async s =>
            {
                s.Open(Edited);
                var n = s.AddNode(12, playerChoice: true, SpeakerCategory.Player, CanonicalIds.Player, CanonicalIds.Npc, 300, 400);
                n.DefaultText = "Ask about the canonical mod.";
                s.Link(0, 12);
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.True(v.Node(Edited, 12).IsPlayerChoice);
                Assert.Contains(v.Node(Edited, 0).Links, l => l.ToNodeId == 12);
            }),

        new("nodes.add-script",
            "New script node 13 after 12",
            async s =>
            {
                s.Open(Edited);
                s.AddNode(13, playerChoice: false, SpeakerCategory.Script, "", "", 600, 400);
                s.Link(12, 13);
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.Equal(SpeakerCategory.Script, v.Node(Edited, 13).SpeakerCategory);
                Assert.Contains(v.Node(Edited, 12).Links, l => l.ToNodeId == 13);
            }),

        new("nodes.convert-player-to-npc",
            "Player choice 3 converted to an NPC line with the detail pane's Node Type control (#160)",
            async s =>
            {
                s.Open(Edited);
                s.Vm.Canvas.SelectNode(s.Node(3));
                s.Vm.Detail.NodeTypeString = Loc.Get("Option_NpcLine");   // what the dropdown does
                s.Node(3).SpeakerGuid  = CanonicalIds.Npc;
                s.Node(3).ListenerGuid = CanonicalIds.Player;
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.False(v.Node(Edited, 3).IsPlayerChoice);
                Assert.Equal(CanonicalIds.Npc, v.Node(Edited, 3).SpeakerGuid);
            },
            KnownIssue: "#160"),

        new("nodes.delete",
            "Node 9 deleted; bank 5 keeps child 8",
            async s =>
            {
                s.Open(Edited);
                s.Delete(9);
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.False(v.Has(Edited, 9));
                Assert.True(v.Has(Edited, 8));
            }),

        new("kept.bank-and-trigger",
            "Bank 5 and trigger 6 get a new Persistence; their bank and trigger fields survive",
            async s =>
            {
                // Persistence, not Comments: PoE2 bundles have no Comments field at all.
                s.Open(Edited);
                s.Node(5).Persistence = "OnceEver";
                s.Node(6).Persistence = "OncePerConversation";
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.Equal("OnceEver",            v.Node(Edited, 5).Persistence);
                Assert.Equal("OncePerConversation", v.Node(Edited, 6).Persistence);
                // The editor model does not carry ChildNodeIDs or the trigger target; those
                // are checked on the raw bundle (CanonicalModRawChecks). Here: types kept.
                Assert.Equal(SpeakerCategory.Script, v.Node(Edited, 5).SpeakerCategory);
            }),

        new("links.remove",
            "Link 2→5 removed",
            async s =>
            {
                s.Open(Edited);
                s.Unlink(2, 5);
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.DoesNotContain(v.Node(Edited, 2).Links, l => l.ToNodeId == 5)),

        new("links.weight",
            "Link 0→2 re-weighted to 3",
            async s =>
            {
                s.Open(Edited);
                s.LinkOf(0, 2).RandomWeight = 3;
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.Equal(3, v.Node(Edited, 0).Links.Single(l => l.ToNodeId == 2).RandomWeight)),

        new("links.display-values",
            "0→1 ShowNever, 0→2 ShowOnce, 0→12 ShowAlways",
            async s =>
            {
                s.Open(Edited);
                s.LinkOf(0, 1).QuestionNodeTextDisplay  = "ShowNever";
                s.LinkOf(0, 2).QuestionNodeTextDisplay  = "ShowOnce";
                s.LinkOf(0, 12).QuestionNodeTextDisplay = "ShowAlways";
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                var links = v.Node(Edited, 0).Links;
                Assert.Equal("ShowNever",  links.Single(l => l.ToNodeId == 1).QuestionNodeTextDisplay);
                Assert.Equal("ShowOnce",   links.Single(l => l.ToNodeId == 2).QuestionNodeTextDisplay);
                Assert.Equal("ShowAlways", links.Single(l => l.ToNodeId == 12).QuestionNodeTextDisplay);
            }),
    ];
}
