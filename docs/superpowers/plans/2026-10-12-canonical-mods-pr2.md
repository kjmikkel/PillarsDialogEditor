# Canonical Mods PR 2 (PoE1) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the PoE1 canonical mod to the PR 1 harness: generated, golden-checked, applied through every delivery path, checked on the raw XML, and (opt-in) loaded through the game's own `OEIFormats` model.

**Architecture:** PR 1's harness is PoE2-shaped in a few places (`"poe2"` literals in `CanonicalModSession`, the PoE2 data path in `DeliveryPaths`, `Poe2Scenario` named directly by the test classes). Task 1 lifts those into one `CanonicalGame` description per game without changing a byte of the PoE2 mod. The PoE1 scenario, tests and golden files then plug into the same harness.

**Tech Stack:** .NET / xUnit (serial), `System.Xml.Linq`, the game's `OEIFormats.dll` loaded at run time (never referenced or redistributed).

**Spec:** `docs/superpowers/specs/2026-10-12-canonical-mods-design.md` (§2 "PoE1 (PR 2)", §3 "Game model", §4 delivery).

## Global Constraints

- Strict red/green TDD (CLAUDE.md). A refactor runs under an already-green suite.
- Never run bare `dotnet test`: always `--filter "Category!=Gui"` (it launches the GUI otherwise).
- Game-data tests are opt-in: `[GameInstallFact(GameInstallFactAttribute.Poe1Variable)]` + `[Trait("Category", "GameData")]`, env var `DIALOGEDITOR_POE1_DIR`.
- All mod content is invented. The repo is public: no game data, no local paths in issues or files.
- Bugs the mod exposes: **file and skip.** Search first (`gh issue list --state all --search …`); if an existing issue covers it (e.g. #160, #161), comment that PoE1 is affected too instead of filing. Mark the failing step `KnownIssue: "#NNN"` or the raw check `Skip = "#NNN: …"`.
- Golden regeneration: `DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1` rewrites and fails; review the diff, commit, rerun without it.
- Issues and PR text in first person singular ("I"). CHANGELOG is frozen.
- Test-project code may swallow cleanup exceptions with `/* best-effort */`.

## Review Focus

1. **The PoE2 mod must not change.** Task 1 is a refactor; `Poe2_CheckedInMod_MatchesAFreshBuild` must stay green with no regeneration.
2. **What the game drops.** PoE1's XmlSerializer silently ignores elements a type doesn't declare (e.g. `SpeakerGuid` on a `PlayerResponseNode`, `Conditionals` on a link). An expectation that holds in the editor's parser can still be false in the game. Task 5 reruns every step's expectation on the game's view of the files.
3. **Enum names the game rejects** (#139 class): any DisplayType/Persistence/QuestionNodeTextDisplay value the editor writes must deserialize — Task 5.
4. **A new PoE1 conversation** sits at the conversations root, not in a subfolder, with a stringtable per language. Restore (F6) and Remove all mods must delete all of it — Task 4's restore checks.
5. **No link gains `<Conditionals>`** (#141) — not even a new link — Task 4.

---

### Task 1: Generalise the harness over games (refactor, PoE2 unchanged)

**Files:**
- Create: `DialogEditor.Tests/CanonicalMod/CanonicalGame.cs`
- Create: `DialogEditor.Tests/CanonicalMod/BuiltMod.cs` (replaces `BuiltPoe2Mod.cs`)
- Delete: `DialogEditor.Tests/CanonicalMod/BuiltPoe2Mod.cs`
- Modify: `CanonicalModSession.cs`, `DeliveryPaths.cs`, `Poe2Scenario.cs`, `Poe2ScenarioStepTests.cs`, `CanonicalModApplyTests.cs`, `CanonicalModRawChecks.cs`, `CanonicalModGoldenTests.cs`

**Interfaces:**
- Produces:
  - `CanonicalGame` record: `string Id`, `IReadOnlyList<ScenarioStep> Steps`, `string Edited`, `string NewConversation`, `IReadOnlyList<string> Conversations`, `IReadOnlyList<string> Languages`, `IGameDataProvider Provider(string root)`, `string NewConversationPath(string root)`, `string Pack`, `string Project`; statics `CanonicalGame.Poe2`, `CanonicalGame.For(string id)`. (`Poe1` is added in Task 2.)
  - `CanonicalModSession.Game` (string, set by `LoadNewProjectAsync`/`LoadExistingAsync`); `ConditionName`/`ScriptName` become **instance** methods using `Game`; `EditConditions`/`EditLinkConditions`/`EditScripts` pass `Game` instead of `"poe2"`.
  - `DeliveryPaths.ViaTestPatchAsync(CanonicalGame game, string gameRoot)` and `ViaTestPatchAndRestoreAsync(CanonicalGame game, string gameRoot)` (project path from `game.Project`; F5 proof uses `game.NewConversationPath(root)`).
  - `abstract class BuiltMod(CanonicalGame game)` with `Root`, `ProjectPath`, `GameRoot`, `Applied`; `sealed class BuiltPoe2Mod() : BuiltMod(CanonicalGame.Poe2)`.

- [ ] **Step 1: Confirm the baseline is green**

Run: `dotnet test DialogEditor.Tests --filter "Category=Canonical" > $WS/t1-before.log; tail -5 $WS/t1-before.log`
Expected: all pass, the 4 known skips (#160, #161, #162 ×2).

- [ ] **Step 2: Add `CanonicalGame`**

```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.CanonicalMod;

/// One game's canonical mod (issue 138): its scenario and where its files live. The tests
/// take one of these instead of naming a game, so PoE1 and PoE2 share every check.
public sealed record CanonicalGame(
    string Id,
    IReadOnlyList<ScenarioStep> Steps,
    string Edited,
    string NewConversation,
    Func<string, IGameDataProvider> CreateProvider)
{
    public IReadOnlyList<string> Conversations => [Edited, NewConversation];
    public IReadOnlyList<string> Languages     => ["en", "de"];

    public IGameDataProvider Provider(string root) => CreateProvider(root);

    /// Where the editor puts the new conversation's file in an install: the F5 proof and
    /// the restore checks look there.
    public string NewConversationPath(string root) =>
        Provider(root).BuildNewConversationFile(NewConversation).ConversationPath;

    public string Pack    => Path.Combine(CanonicalModBuilder.GoldenDir(Id), CanonicalModBuilder.ProjectName + ".dialogpack");
    public string Project => Path.Combine(CanonicalModBuilder.GoldenDir(Id), CanonicalModBuilder.ProjectName + ".dialogproject");

    public static CanonicalGame Poe2 { get; } = new("poe2", Poe2Scenario.Steps, Poe2Scenario.Edited,
        Poe2Scenario.NewConversation, root => new Poe2GameDataProvider(root));

    public static CanonicalGame For(string id) => id switch
    {
        "poe2" => Poe2,
        _      => throw new ArgumentOutOfRangeException(nameof(id), id, null),
    };
}
```

- [ ] **Step 3: Make the session game-aware**

In `CanonicalModSession`: add `public string Game { get; private set; } = "";`. `LoadNewProjectAsync(string game, …)` sets `Game = game` first. `LoadExistingAsync(string gameRoot, string projectPath)` gains a leading `string game` parameter and sets it. Replace the four `"poe2"` literals with `Game`, and make the catalogue helpers instance methods:

```csharp
    /// The catalogue's FullName for a method in this session's game; throws if absent,
    /// because an unknown function would be written into the mod and fail in the game.
    public string ConditionName(string method) =>
        ConditionCatalogue.Instance.ForGame(Game).First(e => e.MethodName == method).ReflectionFullName;

    public string ScriptName(string method) =>
        ScriptCatalogue.Instance.ForGame(Game).First(e => e.MethodName == method).ReflectionFullName;
```

In `Poe2Scenario.cs` replace every `CanonicalModSession.ConditionName(` with `s.ConditionName(` and `CanonicalModSession.ScriptName(` with `s.ScriptName(`. In `conditions.node-nested` the local `var name = …` already sits inside the lambda; keep it.

- [ ] **Step 4: Generalise `DeliveryPaths`, `BuiltMod` and the test classes**

`DeliveryPaths`:

```csharp
    /// Test Patch (F5) from the editor, with the game's checked-in project open over the install.
    public static async Task ViaTestPatchAsync(CanonicalGame game, string gameRoot)
    {
        using var session = new CanonicalModSession();
        await session.LoadExistingAsync(game.Id, gameRoot, game.Project);
        await session.Vm.TestPatchCommand.ExecuteAsync(null);
    }

    /// Test Patch (F5), then Restore Conversation (F6) in the same editor session.
    public static async Task ViaTestPatchAndRestoreAsync(CanonicalGame game, string gameRoot)
    {
        using var session = new CanonicalModSession();
        await session.LoadExistingAsync(game.Id, gameRoot, game.Project);
        await session.Vm.TestPatchCommand.ExecuteAsync(null);
        // Test Patch reports a refusal only in the status bar, and a restore with nothing to
        // undo leaves the fixture as it was. Prove F5 really wrote before checking F6.
        Assert.True(File.Exists(game.NewConversationPath(gameRoot)), $"Test Patch wrote nothing: {session.Vm.StatusText}");
        await session.Vm.RestoreConversationCommand.ExecuteAsync(null);
    }
```

(`ViaTestPatchAsync` / `ViaTestPatchAndRestoreAsync` become static: they used no instance state.)

`BuiltMod.cs` (move the body of `BuiltPoe2Mod`, replacing `"poe2"`, `Poe2Scenario.*` and `new Poe2GameDataProvider(GameRoot)` with `game.Id`, `game.Steps`, `game.Conversations`, `game.Languages`, `game.Provider(GameRoot)`); end the file with:

```csharp
public sealed class BuiltPoe2Mod() : BuiltMod(CanonicalGame.Poe2);
```

`Poe2ScenarioStepTests`: use `Poe2Scenario.Steps` as before (unchanged), but the vanilla check builds its provider with `CanonicalGame.Poe2.Provider(root)`.

`CanonicalModApplyTests`: `Routes` becomes `TheoryData<string, string>` of `(game, route)` with the three routes for `"poe2"` only (Task 3 adds poe1); `ApplyAsync(CanonicalGame game, string route)`; `AllThreeRoutes_WriteIdenticalGameFiles`, `ApplyingTwice_GivesTheSameFiles` and `MissingModLanguage_IsSkipped_AndEnglishStillApplies` become `[Theory] [InlineData("poe2")]` taking `string gameId` and using `CanonicalGame.For(gameId)`. In the missing-language test, find the folder to delete with `Directory.EnumerateDirectories(root, "de", SearchOption.AllDirectories).Single()` instead of the PoE2 path, and keep the node-11 English assertion as it is (Task 2 gives PoE1 the same node-11 text, so Task 3 only adds `InlineData("poe1")`).

`CanonicalModRawChecks` and `CanonicalModGoldenTests`: replace `Pack`/`Project` getters with `CanonicalGame.Poe2.Pack`/`.Project`, and the golden tests become `[Theory] [InlineData("poe2")]` over `CanonicalGame.For(gameId)`. `TestPatchRestore_RestoresTheFixtureExactly` calls `DeliveryPaths.ViaTestPatchAndRestoreAsync(CanonicalGame.Poe2, root)`.

- [ ] **Step 5: Run the canonical tests — the PoE2 mod must be byte-identical**

Run: `dotnet build DialogEditor.Tests -warnaserror -v q && dotnet test DialogEditor.Tests --filter "Category=Canonical" > $WS/t1-after.log; tail -5 $WS/t1-after.log; git status --short DialogEditor.Tests/Fixtures`
Expected: same pass/skip counts as Step 1; `git status` shows no change under `Fixtures`.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod
git commit -m "test: canonical mod harness takes a CanonicalGame (#138)"
```

---

### Task 2: The PoE1 scenario, its golden files and per-step tests

**Files:**
- Create: `DialogEditor.Tests/CanonicalMod/Poe1Scenario.cs`
- Create: `DialogEditor.Tests/CanonicalMod/Poe1ScenarioStepTests.cs`
- Modify: `CanonicalGame.cs` (add `Poe1`), `BuiltMod.cs` (add `BuiltPoe1Mod`), `CanonicalModGoldenTests.cs` (add `InlineData("poe1")`)
- Create (generated): `DialogEditor.Tests/Fixtures/CanonicalMod/poe1/{canonical_mod.dialogproject,canonical_mod.dialogpack,README.md,…}`

**Interfaces:**
- Consumes: Task 1's `CanonicalGame`, `BuiltMod`, instance `ConditionName`/`ScriptName`.
- Produces: `Poe1Scenario.Steps`, `Poe1Scenario.Edited = "canonical"`, `Poe1Scenario.NewConversation = "canonical_mod_new"`, `CanonicalGame.Poe1`, `BuiltPoe1Mod`.

PoE1 mirrors PoE2 (spec §2) minus link conditions, per-script conditionals, `.wem` import and HideSpeaker, and adds Comments, ActorDirection and `VOFilename` (the editor's `ExternalVO`). Bank 5 and trigger 6 get **Comments** (every PoE1 node type declares it; BankNode has no Persistence). The PoE2 link-conditions step becomes an edit of node 1's existing nested tree.

- [ ] **Step 1: Write the step tests first**

`Poe1ScenarioStepTests.cs` — same shape as `Poe2ScenarioStepTests`, over `BuiltPoe1Mod`/`Poe1Scenario`, with `EveryKnownIssueStep_HasItsOwnSkippedTest` asserting `[]` for now (Step 6 adds any known issue found), `EveryExpectation_FailsOnTheUnmodifiedFixture` using `CanonicalGame.Poe1.Provider(root)` and `CanonicalFixture.CopyTo("poe1", root)`, and `Tags_AreUnique`.

- [ ] **Step 2: Run them — they fail to compile**

Run: `dotnet build DialogEditor.Tests -v q`
Expected: errors naming `Poe1Scenario` / `BuiltPoe1Mod`.

- [ ] **Step 3: Write `Poe1Scenario` and register the game**

```csharp
using DialogEditor.Core.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.CanonicalMod;

/// The PoE1 canonical mod (issue 138, spec §2): the PoE2 scenario minus what PoE1 lacks
/// (link conditions, per-script conditionals, .wem VO, HideSpeaker), plus Comments,
/// ActorDirection and VOFilename. Node ids follow Fixtures/Canonical/README.md.
public static class Poe1Scenario
{
    public const string Edited          = "canonical";
    public const string NewConversation = "canonical_mod_new";

    public static IReadOnlyList<ScenarioStep> Steps { get; } =
    [
        new("nodes.add-npc-line", "New NPC line 11, linked from 10",
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

        new("nodes.add-player-choice", "New player choice 12 under 0",
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

        new("nodes.add-script", "New script node 13 after 12",
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
                s.Vm.Detail.NodeTypeString = Loc.Get("Option_NpcLine");
                s.Node(3).SpeakerGuid  = CanonicalIds.Npc;
                s.Node(3).ListenerGuid = CanonicalIds.Player;
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.False(v.Node(Edited, 3).IsPlayerChoice);
                Assert.Equal((CanonicalIds.Npc, CanonicalIds.Player), (v.Node(Edited, 3).SpeakerGuid, v.Node(Edited, 3).ListenerGuid));
            }),

        new("nodes.delete", "Node 9 deleted; bank 5 keeps child 8",
            async s => { s.Open(Edited); s.Delete(9); s.Save(); await Task.CompletedTask; },
            v => { Assert.False(v.Has(Edited, 9)); Assert.True(v.Has(Edited, 8)); }),

        new("kept.bank-and-trigger",
            "Bank 5 and trigger 6 get comments; their bank and trigger fields survive",
            async s =>
            {
                // Comments, not Persistence: PoE1's BankNode declares no Persistence.
                s.Open(Edited);
                s.Node(5).Comments = "Bank comment from the canonical mod.";
                s.Node(6).Comments = "Trigger comment from the canonical mod.";
                s.Save();
                await Task.CompletedTask;
            },
            v =>
            {
                Assert.Equal("Bank comment from the canonical mod.",    v.Node(Edited, 5).Comments);
                Assert.Equal("Trigger comment from the canonical mod.", v.Node(Edited, 6).Comments);
            }),

        new("links.remove", "Link 2→5 removed",
            async s => { s.Open(Edited); s.Unlink(2, 5); s.Save(); await Task.CompletedTask; },
            v => Assert.DoesNotContain(v.Node(Edited, 2).Links, l => l.ToNodeId == 5)),

        new("links.weight", "Link 0→2 re-weighted to 3",
            async s => { s.Open(Edited); s.LinkOf(0, 2).RandomWeight = 3; s.Save(); await Task.CompletedTask; },
            v => Assert.Equal(3, v.Node(Edited, 0).Links.Single(l => l.ToNodeId == 2).RandomWeight)),

        new("links.display-values", "0→1 ShowNever, 0→2 ShowOnce, 0→12 ShowAlways",
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

        new("conditions.node-nested", "Node 12: A and (B or not C), each a multi-parameter call",
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
        new("conditions.replace-nested", "Node 1's nested condition replaced by one negated call",
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

        new("conditions.clear", "Node 2's condition removed entirely",
            async s => { s.Open(Edited); s.EditConditions(s.Node(2), []); s.Save(); await Task.CompletedTask; },
            v => Assert.Empty(v.Node(Edited, 2).Conditions)),

        new("scripts.add-all-categories", "Node 13 gets OnEnter, OnExit and OnUpdate scripts",
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

        new("scripts.add-to-existing", "Node 4 gains a second OnExit script; its three existing scripts stay",
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

        new("scripts.remove", "Node 0's OnEnter script removed",
            async s => { s.Open(Edited); s.EditScripts(s.Node(0), []); s.Save(); await Task.CompletedTask; },
            v => Assert.Empty(v.Node(Edited, 0).Scripts)),

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

        new("props.speaker-listener", "Node 10's speaker becomes the narrator and its listener the NPC",
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

        new("text.edit-with-female", "Node 0's text and its female variant edited (keeping the [Player Name] token)",
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

        new("text.markup", "Node 12's text uses <i> markup",
            async s => { s.Open(Edited); s.Node(12).DefaultText = "Ask about the <i>canonical</i> mod."; s.Save(); await Task.CompletedTask; },
            v => Assert.Equal("Ask about the <i>canonical</i> mod.", v.Text(Edited, "en", 12)!.DefaultText)),

        new("text.second-language", "German text for new node 11 and edited node 0; English stays as edited",
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
        new("vo.filename", "Node 11's VOFilename set",
            async s => { s.Open(Edited); s.Node(11).ExternalVO = "canonical_mod_0011"; s.Save(); await Task.CompletedTask; },
            v => Assert.Equal("canonical_mod_0011", v.Node(Edited, 11).ExternalVO)),

        // Last on purpose, as in PoE2: no later step has to reopen the new conversation.
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
```

Add to `CanonicalGame`:

```csharp
    public static CanonicalGame Poe1 { get; } = new("poe1", Poe1Scenario.Steps, Poe1Scenario.Edited,
        Poe1Scenario.NewConversation, root => new Poe1GameDataProvider(root));
```

and `"poe1" => Poe1` in `For`. Add `public sealed class BuiltPoe1Mod() : BuiltMod(CanonicalGame.Poe1);` to `BuiltMod.cs`.

- [ ] **Step 4: Run the PoE1 step tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1ScenarioStepTests" > $WS/t2.log; grep -E "Failed|Passed!|Failed!" $WS/t2.log | head -40`
Expected: `EveryExpectation_FailsOnTheUnmodifiedFixture` and `Tags_AreUnique` pass. Each `Step_HoldsAfterInstall` case either passes or fails with a real defect. A scenario step that fails because the *step* is wrong (e.g. an enum or catalogue name PoE1 lacks) is fixed in the step, with a ledger ruling.

- [ ] **Step 5: Generate and commit the golden mod**

Add `[InlineData("poe1")]` to each golden theory. Run with `DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1` (fails, writes `Fixtures/CanonicalMod/poe1`), inspect the `.dialogproject` and README by eye, then run again without the variable.
Expected: second run passes; `Poe1_TwoBuilds_AreIdentical` passes.

- [ ] **Step 6: File and skip what Step 4 found**

For each genuinely failing step: search issues (`gh issue list --state all --search "<words>"`); comment on an existing issue (#160 node type, #161 bank child, #162 new conversation) that PoE1 is affected, else file a new one with no local paths. Set `KnownIssue: "#NNN"` on the step, add the matching `[Fact(Skip = "#NNN: …")]` in `Poe1ScenarioStepTests`, update `EveryKnownIssueStep_HasItsOwnSkippedTest`, regenerate the golden README (the Known issue column changes).
Expected: `dotnet test DialogEditor.Tests --filter "Category=Canonical"` green with only `#`-named skips.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod DialogEditor.Tests/Fixtures/CanonicalMod/poe1
git commit -m "test: generated PoE1 canonical mod with golden and per-step checks (#138)"
```

---

### Task 3: Apply the PoE1 mod through every route

**Files:**
- Modify: `DialogEditor.Tests/CanonicalMod/CanonicalModApplyTests.cs`

**Interfaces:**
- Consumes: `CanonicalGame.Poe1`, its golden pack and project (Task 2), `DeliveryPaths` (Task 1).

- [ ] **Step 1: Add the PoE1 cases**

`Routes` gains `("poe1", "patcher")`, `("poe1", "patchmanager")`, `("poe1", "testpatch")`. The three single-game theories gain `[InlineData("poe1")]`. In `MissingModLanguage_IsSkipped_AndEnglishStillApplies`, find the `de` folder generically (`Directory.EnumerateDirectories(root, "de", SearchOption.AllDirectories).Single()`), install, then assert the node-11 English text (same string in both scenarios).

- [ ] **Step 2: Run them**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalModApplyTests" > $WS/t3.log; grep -E "Failed |Passed!|Failed!" $WS/t3.log`
Expected: all pass. A failure here that Task 2's install did not show is a route-specific defect: file and skip it (Global Constraints).

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod/CanonicalModApplyTests.cs
git commit -m "test: apply the PoE1 canonical mod through patcher, Patch Manager and Test Patch (#138)"
```

---

### Task 4: Raw XML checks for PoE1

**Files:**
- Create: `DialogEditor.Tests/CanonicalMod/Poe1CanonicalModRawChecks.cs`

**Interfaces:**
- Consumes: `CanonicalGame.Poe1`, `DeliveryPaths`, `CanonicalFixture`.

Checks that the editor model does not carry, on the installed XML (paths: `PillarsOfEternity_Data/data/conversations/canonical/canonical.conversation`, stringtables under `data/localized/<lang>/text/conversations/canonical/`):

- [ ] **Step 1: Write the checks**

```csharp
using System.Xml.Linq;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// PoE1 checks on the written XML itself, for what the editor model does not carry
/// (issue 138, spec §3): untouched data, bank and trigger fields, link conditionals (#141),
/// and that both undo routes restore the fixture.
[Trait("Category", "Canonical")]
public sealed class Poe1CanonicalModRawChecks : IDisposable
{
    private static readonly CanonicalGame Game = CanonicalGame.Poe1;
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";
    private readonly DeliveryPaths _paths = new();
    public void Dispose() => _paths.Dispose();

    private static string ConvDir(string root) =>
        Path.Combine(root, "PillarsOfEternity_Data", "data", "conversations", "canonical");
    private static string StDir(string root, string lang) =>
        Path.Combine(root, "PillarsOfEternity_Data", "data", "localized", lang, "text", "conversations", "canonical");
    private static XElement NodeOf(string root, int id) =>
        XDocument.Load(Path.Combine(ConvDir(root), "canonical.conversation"))
            .Descendants("FlowChartNode").Single(n => (int)n.Element("NodeID")! == id);

    private async Task<string> InstalledAsync()
    {
        var root = _paths.FreshInstall("poe1");
        await DeliveryPaths.ViaPatchManagerAsync(root, Game.Pack);
        return root;
    }

    /// A second conversation the mod never names, copied from the canonical one before the
    /// install, in its own folder as PoE1 lays them out.
    private static IReadOnlyList<string> AddUntouchedConversation(string root)
    {
        var copies = new List<string>();
        void Copy(string dir, string ext)
        {
            var dest = Path.Combine(Path.GetDirectoryName(dir)!, "untouched");
            Directory.CreateDirectory(dest);
            File.Copy(Path.Combine(dir, "canonical" + ext), Path.Combine(dest, "untouched" + ext));
            copies.Add(Path.Combine(dest, "untouched" + ext));
        }
        Copy(ConvDir(root), ".conversation");
        foreach (var lang in new[] { "en", "de" }) Copy(StDir(root, lang), ".stringtable");
        return copies;
    }

    [Fact]
    public async Task UntouchedConversation_IsUnchanged()
    {
        var root      = _paths.FreshInstall("poe1");
        var untouched = AddUntouchedConversation(root);
        var before    = DeliveryPaths.GameFiles(root);
        await DeliveryPaths.ViaPatchManagerAsync(root, Game.Pack);
        var after     = DeliveryPaths.GameFiles(root);
        var compared  = before.Where(kv => kv.Key.Contains("/untouched/")).ToList();
        Assert.Equal(untouched.Count, compared.Count);
        foreach (var (rel, hash) in compared)
            Assert.True(after.TryGetValue(rel, out var h) && h == hash, $"{rel} changed");
    }

    [Fact]
    public async Task NoLink_GainsConditionals()   // #141: PoE1's DialogueLink has none
    {
        var root = await InstalledAsync();
        var conv = XDocument.Load(Path.Combine(ConvDir(root), "canonical.conversation"));
        Assert.Empty(conv.Descendants("FlowChartLink").Where(l => l.Element("Conditionals") is not null));
    }

    private static List<int> BankChildren(string root) =>
        [.. NodeOf(root, 5).Element("ChildNodeIDs")!.Elements("int").Select(e => (int)e)];

    [Fact]
    public async Task Bank_KeepsItsRemainingChild() => Assert.Contains(8, BankChildren(await InstalledAsync()));

    [Fact]
    public async Task Bank_DropsTheDeletedChild() => Assert.DoesNotContain(9, BankChildren(await InstalledAsync()));

    [Fact]
    public async Task BankPlayType_AndTriggerTarget_Survive()
    {
        var root = await InstalledAsync();
        var fixtureRoot = CanonicalFixture.SourceDir("poe1");
        Assert.Equal((string?)NodeOf(fixtureRoot, 5).Element("BankNodePlayType"), (string?)NodeOf(root, 5).Element("BankNodePlayType"));
        foreach (var key in new[] { "ConversationFilename", "StartNodeID" })
            Assert.Equal((string?)NodeOf(fixtureRoot, 6).Element(key), (string?)NodeOf(root, 6).Element(key));
        Assert.Equal("TriggerConversationNode", (string?)NodeOf(root, 6).Attribute(Xsi + "type"));
    }

    [Fact]
    public async Task RemoveAllMods_RestoresTheFixtureExactly()
    {
        var root = await InstalledAsync();
        await DeliveryPaths.RemoveAllModsAsync(root);
        var diff = DeliveryPaths.Describe(DeliveryPaths.GameFiles(_paths.FreshInstall("poe1")), DeliveryPaths.GameFiles(root));
        Assert.True(diff is null, diff);
    }

    [Fact]
    public async Task TestPatchRestore_RestoresTheFixtureExactly()
    {
        var root = _paths.FreshInstall("poe1");
        await DeliveryPaths.ViaTestPatchAndRestoreAsync(Game, root);
        var diff = DeliveryPaths.Describe(DeliveryPaths.GameFiles(_paths.FreshInstall("poe1")), DeliveryPaths.GameFiles(root));
        Assert.True(diff is null, diff);
    }
}
```

- [ ] **Step 2: Run them**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1CanonicalModRawChecks" > $WS/t4.log; grep -E "Failed |Passed!|Failed!" -A3 $WS/t4.log | head -40`
Expected: each passes, or fails on a real defect. `Bank_DropsTheDeletedChild` is expected to fail as #161 does on PoE2: confirm the cause, comment on #161 that PoE1 is affected, and mark it `[Fact(Skip = "#161: a deleted bank child stays in ChildNodeIDs")]`. Any other failure: file and skip.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod/Poe1CanonicalModRawChecks.cs
git commit -m "test: PoE1 canonical mod raw-XML and reversibility checks (#138)"
```

---

### Task 5: The game's own model (opt-in)

**Files:**
- Create: `DialogEditor.Tests/CanonicalMod/Poe1CanonicalModGameModelTests.cs`
- Modify: `DialogEditor.Tests/Helpers/RoundTripChecks.cs` (add `Poe1GameStringTableSerializer`)
- Create: `DialogEditor.Tests/GameData/GameInstallTheoryAttribute.cs`; Modify: `GameInstallFactAttribute.cs` (share its skip check)

**Interfaces:**
- Consumes: `RoundTripChecks.Poe1GameSerializer(installDir)`, `RoundTripChecks.Poe1GameCanonical(serializer, xml)`, `GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable)`, `BuiltPoe1Mod`.
- Produces: `RoundTripChecks.Poe1GameStringTableSerializer(string installDir)` → `XmlSerializer` over `OEIFormats.Strings.StringTableFile`.

The installed files are rewritten through the game's own `XmlSerializer` (deserialize → serialize). That throws on anything the game cannot load, and drops what it ignores. Then every scenario expectation runs on the editor's parse of *that* — so a feature counts only if the game still sees it.

- [ ] **Step 1: Write the tests**

```csharp
using System.Xml.Serialization;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138, spec §3 "Game model": the installed PoE1 canonical mod as the game itself
/// reads it. Each file goes through the install's own OEIFormats XmlSerializer (loaded at
/// run time, never redistributed): a file the game cannot load throws, and anything the
/// game ignores (an element its type does not declare) drops out. The scenario's own
/// expectations then run on what is left, so a feature passes only if the game sees it.
[Trait("Category", "GameData")]
public sealed class Poe1CanonicalModGameModelTests(BuiltPoe1Mod mod) : IClassFixture<BuiltPoe1Mod>
{
    private static string Install => GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable);

    /// A copy of the installed game folder with every conversation and stringtable the mod
    /// owns replaced by the game's canonical form of it.
    private string GameSeen()
    {
        var conv = RoundTripChecks.Poe1GameSerializer(Install);
        var st   = RoundTripChecks.Poe1GameStringTableSerializer(Install);
        var copy = Path.Combine(mod.Root, "game-seen " + Guid.NewGuid().ToString("N"));
        GoldenDiff.CopyTree(mod.GameRoot, copy);
        foreach (var file in Directory.EnumerateFiles(copy, "*.*", SearchOption.AllDirectories)
                     .Where(f => f.EndsWith(".conversation") || f.EndsWith(".stringtable")))
        {
            var serializer = file.EndsWith(".conversation") ? conv : st;
            File.WriteAllText(file, RoundTripChecks.Poe1GameCanonical(serializer, File.ReadAllText(file)));
        }
        return copy;
    }

    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    public void EveryInstalledFile_LoadsInTheGamesOwnModel() => GameSeen();   // throws on the first file the game rejects

    public static TheoryData<string> Tags => [.. Poe1Scenario.Steps.Where(s => s.KnownIssue is null).Select(s => s.Tag)];

    [GameInstallTheory(GameInstallFactAttribute.Poe1Variable)]
    [MemberData(nameof(Tags))]
    public void Step_HoldsAsTheGameSeesIt(string tag)
    {
        var view = ModView.FromGame(CanonicalGame.Poe1.Provider(GameSeen()), CanonicalGame.Poe1.Conversations, CanonicalGame.Poe1.Languages);
        Poe1Scenario.Steps.Single(s => s.Tag == tag).Expect(view);
    }
}
```

`GameInstallTheoryAttribute` does not exist yet: add it beside `DialogEditor.Tests/GameData/GameInstallFactAttribute.cs` as a `TheoryAttribute` with the same skip logic (one constructor taking the variable name), sharing the skip check rather than copying it. `GameSeen` per test is acceptable: the conversation is small.

`RoundTripChecks`:

```csharp
    public static XmlSerializer Poe1GameStringTableSerializer(string installDir)
    {
        var dll = Path.Combine(installDir, "PillarsOfEternity_Data", "Managed", "OEIFormats.dll");
        Assert.True(File.Exists(dll), $"OEIFormats.dll not found at {dll}");
        var type = Assembly.LoadFrom(dll).GetType("OEIFormats.Strings.StringTableFile", throwOnError: true)!;
        return new XmlSerializer(type);
    }
```

- [ ] **Step 2: Run without the install — they skip**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1CanonicalModGameModelTests" > $WS/t5-skip.log; tail -3 $WS/t5-skip.log`
Expected: all skipped (env var unset).

- [ ] **Step 3: Run with the install**

Run (PowerShell): `$env:DIALOGEDITOR_POE1_DIR = '<PoE1 install>'; dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe1CanonicalModGameModelTests" > $WS/t5.log`
Expected: `EveryInstalledFile_LoadsInTheGamesOwnModel` passes. Each step case passes or fails on a real defect; a failure here but not in Task 2 means the game drops something the editor parser keeps (Review Focus 2). File it, add the tag to a `private static readonly Dictionary<string, string> GameOnlyIssues` (tag → `"#NNN"`) that `Tags` excludes, and add a `[Fact(Skip = "#NNN: …")]` for it, mirroring the KnownIssue pattern. A step that already has a `KnownIssue` is excluded by `Tags` as written.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod/Poe1CanonicalModGameModelTests.cs DialogEditor.Tests/Helpers DialogEditor.Tests/GameData
git commit -m "test: PoE1 canonical mod loaded through the game's own model (#138)"
```

---

### Task 6: Docs and final suite

**Files:**
- Modify: `DialogEditor.Tests/Fixtures/Canonical/README.md` (the *Known gaps* paragraph says both mods exist)
- Modify: `docs/game-data-tests.md` (list the new opt-in game-model test, if that doc lists them)

- [ ] **Step 1: Update the docs** — one sentence each, no local paths.

- [ ] **Step 2: Full suite**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui" > $WS/final.log; tail -5 $WS/final.log`
Expected: green; skips only those named with an issue number. Then once with `DIALOGEDITOR_POE1_DIR` and `DIALOGEDITOR_POE2_DIR` set and `--filter "Category=GameData"`.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Fixtures/Canonical/README.md docs/game-data-tests.md
git commit -m "docs: canonical mods cover both games (#138)"
```
