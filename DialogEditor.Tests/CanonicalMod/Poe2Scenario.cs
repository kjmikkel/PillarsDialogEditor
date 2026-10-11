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

        new("conditions.node-nested",
            "Node 12: A and (B or not C), each a multi-parameter call",
            async s =>
            {
                s.Open(Edited);
                var name = CanonicalModSession.ConditionName("IsGlobalValue");
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

        new("conditions.link",
            "Link 1→4's conditions replaced by one negated call (PoE2 links carry conditions)",
            async s =>
            {
                s.Open(Edited);
                s.EditLinkConditions(1, 4,
                    [new ConditionLeaf(CanonicalModSession.ConditionName("IsGlobalValue"), ["cm_flag_link", "EqualTo", "1"], true, "And")]);
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                var leaf = Assert.IsType<ConditionLeaf>(Assert.Single(v.Node(Edited, 1).Links.Single(l => l.ToNodeId == 4).Conditions));
                Assert.True(leaf.Not);
                Assert.Equal("cm_flag_link", leaf.Parameters[0]);
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
                var set = CanonicalModSession.ScriptName("SetGlobalValue");
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

        new("scripts.keep-per-script-conditional",
            "Node 4 gains an OnExit script; its existing OnEnter script keeps its own conditional (#115)",
            async s =>
            {
                s.Open(Edited);
                var n = s.Node(4);
                s.EditScripts(n, [.. n.Scripts,
                    new ScriptCall(CanonicalModSession.ScriptName("SetGlobalValue"), ["cm_extra", "1"], ScriptCategory.Exit)]);
                s.Save();
                await Task.CompletedTask;
            },
            // The conditional itself is checked on the raw bundle (CanonicalModRawChecks).
            v => Assert.Contains(v.Node(Edited, 4).Scripts, x => x.Parameters.SequenceEqual(["cm_extra", "1"]))),

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
                Assert.Equal(("Hidden", "OnceEver"),              (v.Node(Edited, 11).DisplayType, v.Node(Edited, 11).Persistence));
                Assert.Equal(("Bark", "OncePerConversation"),     (v.Node(Edited, 12).DisplayType, v.Node(Edited, 12).Persistence));
                Assert.Equal(("Overlay", "MarkAsRead"),           (v.Node(Edited, 13).DisplayType, v.Node(Edited, 13).Persistence));
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

        new("props.hide-speaker",
            "HideSpeaker turned on for 11 and off for 10",
            async s =>
            {
                s.Open(Edited);
                s.Node(11).HideSpeaker = true;
                s.Node(10).HideSpeaker = false;
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.True(v.Node(Edited, 11).HideSpeaker);
                Assert.False(v.Node(Edited, 10).HideSpeaker);
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

        new("vo.import",
            "Node 11 becomes a narrator line and gets an imported .wem (HasVO)",
            async s =>
            {
                s.Open(Edited);
                s.Node(11).SpeakerGuid = CanonicalIds.Narrator;   // the narrator has a VO folder
                await s.ImportVoAsync(11, CanonicalModInputs.Poe2Wem);
                s.Save();
            },
            // Where the file lands is checked on disk (CanonicalModRawChecks).
            v => Assert.True(v.Node(Edited, 11).HasVO)),

        new("vo.alias",
            "Node 10 aliases node 7's VO (ExternalVO; only TalkNodes declare it in PoE2)",
            async s =>
            {
                s.Open(Edited);
                s.Node(10).ExternalVO = "narrator/canonical_0007";
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.Equal("narrator/canonical_0007", v.Node(Edited, 10).ExternalVO)),

        new("vo.off",
            "Node 7's HasVO switched off",
            async s =>
            {
                s.Open(Edited);
                s.Node(7).HasVO = false;
                s.Save();
                await Task.CompletedTask;
            },
            v => Assert.False(v.Node(Edited, 7).HasVO)),
    ];
}
