using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.CanonicalMod;

/// The PoE1 canonical mod (issue 138, spec §2): the PoE2 scenario minus what PoE1 lacks
/// (link conditions, per-script conditionals, .wem VO, HideSpeaker), plus Comments,
/// ActorDirection and VOFilename. Node ids follow Fixtures/Canonical/README.md. All
/// content is invented.
public static class Poe1Scenario
{
    public const string Edited          = "canonical";
    public const string NewConversation = "canonical_mod_new";

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
            "Player choice 3 converted to an NPC line with the detail pane's Node Type control, NPC speaking to the player",
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
                Assert.Equal(SpeakerCategory.Npc, v.Node(Edited, 3).SpeakerCategory);   // written as a TalkNode (#160)
                Assert.Equal((CanonicalIds.Npc, CanonicalIds.Player), (v.Node(Edited, 3).SpeakerGuid, v.Node(Edited, 3).ListenerGuid));
            }),

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
            "Bank 5 and trigger 6 get comments; their bank and trigger fields survive",
            async s =>
            {
                // Comments, not Persistence: PoE1's BankNode declares no Persistence, but
                // every PoE1 node type declares Comments.
                s.Open(Edited);
                s.Node(5).Comments = "Bank comment from the canonical mod.";
                s.Node(6).Comments = "Trigger comment from the canonical mod.";
                s.Save();
                await Task.CompletedTask;
            },
            // ChildNodeIDs and the trigger target are checked on the raw XML
            // (Poe1CanonicalModRawChecks).
            v =>
            {
                Assert.Equal("Bank comment from the canonical mod.",    v.Node(Edited, 5).Comments);
                Assert.Equal("Trigger comment from the canonical mod.", v.Node(Edited, 6).Comments);
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

        new("conditions.node-nested",
            "Node 12: A and (B or not C), each a multi-parameter call",
            async s =>
            {
                s.Open(Edited);
                var name = s.ConditionName("IsGlobalValue");
                var a = new ConditionLeaf(name, ["cm_flag_a", "EqualTo", "1"], false, "And");
                var b = new ConditionLeaf(name, ["cm_flag_b", "GreaterThan", "2"], false, "Or");
                var c = new ConditionLeaf(name, ["cm_flag_c", "EqualTo", "0"], true, "And");
                s.EditConditions(s.Node(12), [a, new ConditionBranch([b, c], false, "And")]);
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                var conds  = v.Node(Edited, 12).Conditions;
                Assert.Equal(2, conds.Count);
                var branch = Assert.IsType<ConditionBranch>(conds[1]);
                Assert.True(Assert.IsType<ConditionLeaf>(branch.Components[1]).Not);
                Assert.Equal(["cm_flag_b", "GreaterThan", "2"], Assert.IsType<ConditionLeaf>(branch.Components[0]).Parameters);
            }),

        // PoE1 links have no conditions (#141), so the existing nested tree on node 1 is
        // edited instead: the game checks a node's conditions when a link leads to it.
        new("conditions.replace-nested",
            "Node 1's nested condition replaced by one negated call (PoE1 links carry no conditions)",
            async s =>
            {
                s.Open(Edited);
                s.EditConditions(s.Node(1),
                    [new ConditionLeaf(s.ConditionName("IsGlobalValue"), ["cm_flag_node", "EqualTo", "1"], true, "And")]);
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                var leaf = Assert.IsType<ConditionLeaf>(Assert.Single(v.Node(Edited, 1).Conditions));
                Assert.True(leaf.Not);
                Assert.Equal("cm_flag_node", leaf.Parameters[0]);
            }),

        new("conditions.clear",
            "Node 2's condition removed entirely",
            async s =>
            {
                s.Open(Edited);
                s.EditConditions(s.Node(2), []);
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.Empty(v.Node(Edited, 2).Conditions)),

        new("scripts.add-all-categories",
            "Node 13 gets OnEnter, OnExit and OnUpdate scripts",
            async s =>
            {
                s.Open(Edited);
                var set = s.ScriptName("SetGlobalValue");
                s.EditScripts(s.Node(13),
                [
                    new ScriptCall(set, ["cm_enter", "1"],  ScriptCategory.Enter),
                    new ScriptCall(set, ["cm_exit", "2"],   ScriptCategory.Exit),
                    new ScriptCall(set, ["cm_update", "3"], ScriptCategory.Update),
                ]);
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.Equal([ScriptCategory.Enter, ScriptCategory.Exit, ScriptCategory.Update],
                              v.Node(Edited, 13).Scripts.Select(x => x.Category).Order())),

        new("scripts.add-to-existing",
            "Node 4 gains a second OnExit script; its three existing scripts stay",
            async s =>
            {
                s.Open(Edited);
                var n = s.Node(4);
                s.EditScripts(n, [.. n.Scripts,
                    new ScriptCall(s.ScriptName("SetGlobalValue"), ["cm_extra", "1"], ScriptCategory.Exit)]);
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.Equal(4, v.Node(Edited, 4).Scripts.Count);
                Assert.Contains(v.Node(Edited, 4).Scripts, x => x.Parameters.SequenceEqual(["cm_extra", "1"]));
            }),

        new("scripts.remove",
            "Node 0's OnEnter script removed",
            async s =>
            {
                s.Open(Edited);
                s.EditScripts(s.Node(0), []);
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.Empty(v.Node(Edited, 0).Scripts)),

        // Every value of both enums is written as a change from the fixture: new nodes start
        // at Conversation/None, so those two are covered by editing 8 and 3 back to them.
        new("props.display-and-persistence",
            "Every DisplayType and Persistence value: 11 Hidden/OnceEver, 12 Bark/OncePerConversation, 13 Overlay/MarkAsRead, 8 Conversation, 3 None",
            async s =>
            {
                s.Open(Edited);
                s.Node(11).DisplayType = "Hidden";  s.Node(11).Persistence = "OnceEver";
                s.Node(12).DisplayType = "Bark";    s.Node(12).Persistence = "OncePerConversation";
                s.Node(13).DisplayType = "Overlay"; s.Node(13).Persistence = "MarkAsRead";
                s.Node(8).DisplayType  = "Conversation";
                s.Node(3).Persistence  = "None";
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.Equal(("Hidden", "OnceEver"),          (v.Node(Edited, 11).DisplayType, v.Node(Edited, 11).Persistence));
                Assert.Equal(("Bark", "OncePerConversation"), (v.Node(Edited, 12).DisplayType, v.Node(Edited, 12).Persistence));
                Assert.Equal(("Overlay", "MarkAsRead"),       (v.Node(Edited, 13).DisplayType, v.Node(Edited, 13).Persistence));
                Assert.Equal("Conversation", v.Node(Edited, 8).DisplayType);
                Assert.Equal("None",         v.Node(Edited, 3).Persistence);
            }),

        new("props.speaker-listener",
            "Node 10's speaker becomes the narrator and its listener the NPC",
            async s =>
            {
                s.Open(Edited);
                s.Node(10).SpeakerGuid  = CanonicalIds.Narrator;
                s.Node(10).ListenerGuid = CanonicalIds.Npc;
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.Equal((CanonicalIds.Narrator, CanonicalIds.Npc),
                              (v.Node(Edited, 10).SpeakerGuid, v.Node(Edited, 10).ListenerGuid))),

        new("props.comments-and-direction",
            "Node 11 gets comments and an actor direction; node 10's comments and direction are cleared",
            async s =>
            {
                s.Open(Edited);
                s.Node(11).Comments       = "Added by the canonical mod.";
                s.Node(11).ActorDirection = "Nods slowly.";
                s.Node(10).Comments       = "";
                s.Node(10).ActorDirection = "";
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.Equal(("Added by the canonical mod.", "Nods slowly."), (v.Node(Edited, 11).Comments, v.Node(Edited, 11).ActorDirection));
                Assert.Equal(("", ""), (v.Node(Edited, 10).Comments, v.Node(Edited, 10).ActorDirection));
            }),

        new("text.edit-with-female",
            "Node 0's text and its female variant edited (keeping the [Player Name] token)",
            async s =>
            {
                s.Open(Edited);
                s.Node(0).DefaultText = "Welcome back, [Player Name]. The mod changed this line.";
                s.Node(0).FemaleText  = "Welcome back, [Player Name]. The mod changed this line, too.";
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.Equal("Welcome back, [Player Name]. The mod changed this line.",      v.Text(Edited, "en", 0)!.DefaultText);
                Assert.Equal("Welcome back, [Player Name]. The mod changed this line, too.", v.Text(Edited, "en", 0)!.FemaleText);
            }),

        new("text.markup",
            "Node 12's text uses <i> markup",
            async s =>
            {
                s.Open(Edited);
                s.Node(12).DefaultText = "Ask about the <i>canonical</i> mod.";
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.Equal("Ask about the <i>canonical</i> mod.", v.Text(Edited, "en", 12)!.DefaultText)),

        new("text.second-language",
            "German text for new node 11 and edited node 0; English stays as edited",
            async s =>
            {
                s.Open(Edited);
                await s.SwitchLanguageAsync("de");
                s.Node(11).DefaultText = "Eine neue Schlusszeile aus dem Kanon-Mod.";
                s.Node(0).DefaultText  = "Willkommen zurück, [Player Name]. Der Mod hat diese Zeile geändert.";
                s.Save();
                await s.SwitchLanguageAsync("en");
            },
            v =>
            {
                Assert.Equal("Eine neue Schlusszeile aus dem Kanon-Mod.", v.Text(Edited, "de", 11)!.DefaultText);
                Assert.Equal("Willkommen zurück, [Player Name]. Der Mod hat diese Zeile geändert.", v.Text(Edited, "de", 0)!.DefaultText);
                Assert.Equal("A new closing line, written by the canonical mod.", v.Text(Edited, "en", 11)!.DefaultText);
            }),

        // PoE1 VO is a file name on the node (VOFilename, the editor's ExternalVO); the
        // editor imports no audio for PoE1. The name is invented.
        new("vo.filename",
            "Node 11's VOFilename set",
            async s =>
            {
                s.Open(Edited);
                s.Node(11).ExternalVO = "canonical_mod_0011";
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.Equal("canonical_mod_0011", v.Node(Edited, 11).ExternalVO)),

        // Last on purpose, as in PoE2: the new conversation exists only in the project until
        // installed, so no later step has to reopen it.
        new("conversation.new",
            "New conversation canonical_mod_new: NPC → two player choices (one conditioned) → end, in en and de",
            async s =>
            {
                await s.NewConversationAsync(NewConversation);
                var start = s.AddNode(s.Vm.Canvas.NextNodeId(), false, SpeakerCategory.Npc, CanonicalIds.Npc, CanonicalIds.Player, 0, 100);
                start.DefaultText = "Hello from a conversation the mod created.";
                var a = s.AddNode(s.Vm.Canvas.NextNodeId(), true, SpeakerCategory.Player, CanonicalIds.Player, CanonicalIds.Npc, 300, 0);
                a.DefaultText = "First choice.";
                var b = s.AddNode(s.Vm.Canvas.NextNodeId(), true, SpeakerCategory.Player, CanonicalIds.Player, CanonicalIds.Npc, 300, 200);
                b.DefaultText = "Second choice, only when cm_flag_a is set.";
                s.EditConditions(b, [new ConditionLeaf(s.ConditionName("IsGlobalValue"), ["cm_flag_a", "EqualTo", "1"], false, "And")]);
                var end = s.AddNode(s.Vm.Canvas.NextNodeId(), false, SpeakerCategory.Npc, CanonicalIds.Npc, CanonicalIds.Player, 600, 100);
                end.DefaultText = "Goodbye.";
                s.Link(start.NodeId, a.NodeId);
                s.Link(start.NodeId, b.NodeId);
                s.Link(a.NodeId, end.NodeId);
                s.Link(b.NodeId, end.NodeId);
                s.Save();

                await s.SwitchLanguageAsync("de");
                s.Node(start.NodeId).DefaultText = "Hallo aus einem Gespräch, das der Mod erstellt hat.";
                s.Save();
                await s.SwitchLanguageAsync("en");
            },
            v =>
            {
                var ids = Enumerable.Range(0, 16).Where(id => v.Has(NewConversation, id)).ToList();
                Assert.Equal(4, ids.Count);
                Assert.Single(ids, id => v.Node(NewConversation, id).Conditions.Count == 1);
                Assert.Contains(ids, id => v.Text(NewConversation, "en", id)?.DefaultText == "Hello from a conversation the mod created.");
                Assert.Contains(ids, id => v.Text(NewConversation, "de", id)?.DefaultText == "Hallo aus einem Gespräch, das der Mod erstellt hat.");
            }),
    ];
}
