# Canonical Mods PR 3 (In-game Checklist) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A checklist a person follows to install both canonical mods into the real games and confirm, in play, every feature the automated tests can only check on disk. It also includes the small setup files Deadfire needs to reach the content at all.

**Architecture:** The checklist is a hand-written Markdown document. Tests keep it honest:
- every scenario step of both mods appears in it;
- every known issue a step carries is named next to that step;
- the Deadfire setup files match the fixture (conversation ID) and declare every global the fixture and the mod use.

**Tech Stack:** Markdown, xUnit, `System.Text.Json`.

**Spec:** `docs/superpowers/specs/2026-10-12-canonical-mods-design.md` §4 "In-game checklist" (amended for PR 3).

## Global Constraints

- Strict red/green TDD for the tests; the documents are content.
- Never run bare `dotnet test`: always `--filter "Category!=Gui"`.
- The repo is public. No local paths, no game data, and nothing copied from shipped files. The setup files are written from the format alone, with invented IDs and names.
- Write as "I". CHANGELOG is frozen.
- Facts about the games come from the decompiled code (memory: `reference_decompiled_game_code`). Anything the code does not settle is marked "confirm on the first run", not guessed.

## Review Focus

1. **Coverage that can't drift.** A step added to either scenario without a checklist row must fail a test.
2. **The setup must not overwrite anything the game ships.** Every copied file is new (`canonical*` names only), and clean-up lists each one.
3. **Known issues are recorded, not failed.** Each row whose step has a `KnownIssue` says what the issue predicts the tester will see.
4. **Commands work as typed.** PoE1 matches conversations by substring, so the edited conversation is started by its full path (spec §4).
5. **Globals.** Deadfire throws on undeclared globals, so the bundle must list every global the fixture and the PoE2 mod name. A test derives that list from the files.

---

### Task 1: Deadfire in-game setup files

**Files:**
- Create: `DialogEditor.Tests/Fixtures/CanonicalMod/poe2-ingame/PillarsOfEternityII_Data/exported/design/manifests/canonical_conversations.manifest`
- Create: `DialogEditor.Tests/Fixtures/CanonicalMod/poe2-ingame/PillarsOfEternityII_Data/exported/design/globalvariables/canonical.globalvariablesbundle`
- Create: `DialogEditor.Tests/CanonicalMod/InGameSetupTests.cs`

**Interfaces:**
- Produces: the folder `Fixtures/CanonicalMod/poe2-ingame/`, laid out as an install overlay (Task 2's checklist tells the tester to copy it). The existing `Fixtures\CanonicalMod\**\*` copy rule already copies it to the test output.

- [ ] **Step 1: Write the tests**

```csharp
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138, spec §4: the files the in-game run copies into a real Deadfire install so the
/// game can reach the canonical conversation. They are hand-written, so these tests tie
/// them to the fixture and the mod they serve.
[Trait("Category", "Canonical")]
public class InGameSetupTests
{
    private static string Overlay => Path.Combine(AppContext.BaseDirectory, "Fixtures", "CanonicalMod", "poe2-ingame",
                                                  "PillarsOfEternityII_Data", "exported", "design");
    private static string Manifest => Path.Combine(Overlay, "manifests", "canonical_conversations.manifest");
    private static string Globals  => Path.Combine(Overlay, "globalvariables", "canonical.globalvariablesbundle");
    private static string FixtureBundle => Path.Combine(CanonicalFixture.SourceDir("poe2"),
        "PillarsOfEternityII_Data", "exported", "design", "conversations", "canonical.conversationbundle");

    // ConversationDataManager loads design/manifests/*conversations.manifest and resolves a
    // typed name by the entry's file name; LoadConversationData finds the bundle by ID.
    [Fact]
    public void Manifest_RegistersTheCanonicalBundle_UnderItsOwnId()
    {
        Assert.Matches(new Regex("conversations\\.manifest$"), Path.GetFileName(Manifest));
        var entry = Assert.Single(JsonNode.Parse(File.ReadAllText(Manifest))!["Entries"]!.AsArray())!;
        var id    = JsonNode.Parse(File.ReadAllText(FixtureBundle))!["Conversations"]![0]!["ID"]!.GetValue<string>();
        Assert.Equal(id, entry["ID"]!.GetValue<string>());
        Assert.Equal("/conversations/canonical.conversationbundle", entry["Filename"]!.GetValue<string>());
        Assert.StartsWith("OEIFormats.Tools.ManifestData+ConversationManifestEntry", entry["$type"]!.GetValue<string>());
    }

    /// Every global the fixture and the PoE2 mod read or write: the first parameter of each
    /// *GlobalValue call, found in the raw JSON so the check shares no code with the editor.
    private static SortedSet<string> GlobalsUsed()
    {
        var used = new SortedSet<string>(StringComparer.Ordinal);
        var sources = new[] { File.ReadAllText(FixtureBundle),
                              File.ReadAllText(CanonicalGame.Poe2.Project) };
        foreach (var json in sources)
            foreach (var call in JsonNode.Parse(json)!.DescendantObjects()
                         .Where(o => o["FullName"]?.GetValue<string>().Contains("GlobalValue(") == true))
                used.Add(call["Parameters"]![0]!.GetValue<string>());
        return used;
    }

    [Fact]
    public void GlobalsBundle_DeclaresEveryGlobalTheFixtureAndTheModUse()
    {
        var declared = JsonNode.Parse(File.ReadAllText(Globals))!["GlobalVariablesSets"]![0]!["GlobalVariables"]!.AsArray()
            .Select(g => g!["Tag"]!.GetValue<string>()).ToHashSet(StringComparer.Ordinal);
        var used = GlobalsUsed();
        Assert.NotEmpty(used);
        Assert.Empty(used.Where(g => !declared.Contains(g)));
    }
}
```

`DescendantObjects()` is a small private static extension in the same file that walks a `JsonNode` tree and yields every `JsonObject`. Write it, don't search for one.

- [ ] **Step 2: Run them, and watch them fail** because the files don't exist.

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~InGameSetupTests"`
Expected: 2 failed (`FileNotFoundException` / `DirectoryNotFoundException`).

- [ ] **Step 3: Write the two files**

Manifest (the format comes from `OEIFormats.Tools.ManifestData`):

```json
{
  "Entries": [
    {
      "$type": "OEIFormats.Tools.ManifestData+ConversationManifestEntry, OEIFormats",
      "DisplayType": 0,
      "IncludesPlayer": true,
      "ID": "c0a1c0a1-0000-4000-8000-00000000c122",
      "Filename": "/conversations/canonical.conversationbundle"
    }
  ]
}
```

Globals bundle (the format comes from `GlobalVariablesBundle.LoadJson`, which reads `GlobalVariablesSets[0].GlobalVariables` and needs no hash). It holds one `{ "Tag": "<name>", "InitialValue": 0 }` per global in `GlobalsUsed()`, sorted by name: every `canonical_*` and `cm_*` global the fixture and the mod name. Print the set with a throwaway run if needed; don't type it from memory.

- [ ] **Step 4: Run the tests** — Expected: 2 passed.
- [ ] **Step 5: Commit** `test: Deadfire in-game setup files for the canonical mod (#138)`

---

### Task 2: The checklist and its coverage test

**Files:**
- Create: `docs/canonical-mod-checklist.md`
- Create: `DialogEditor.Tests/CanonicalMod/ChecklistTests.cs`

**Interfaces:**
- Consumes: `CanonicalGame.Poe1/Poe2.Steps` (tags, `KnownIssue`), Task 1's overlay folder.

- [ ] **Step 1: Write the test**

```csharp
namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138, spec §4: the in-game checklist has one row per scenario step of each mod, and
/// names the known issue a step carries, so the checklist can't drift from the mods.
[Trait("Category", "Canonical")]
public class ChecklistTests
{
    private static string Checklist()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx"))) dir = dir.Parent;
        return File.ReadAllText(Path.Combine(dir!.FullName, "docs", "canonical-mod-checklist.md"));
    }

    /// The section of the checklist for one game: from its "## <title>" heading to the next "## ".
    private static string Section(string game)
    {
        var text  = Checklist();
        var title = game == "poe1" ? "## Pillars of Eternity" : "## Deadfire";
        var start = text.IndexOf(title + "\n", StringComparison.Ordinal);
        Assert.True(start >= 0, $"no '{title}' section");
        var end = text.IndexOf("\n## ", start + title.Length, StringComparison.Ordinal);
        return end < 0 ? text[start..] : text[start..end];
    }

    public static TheoryData<string> Games => ["poe1", "poe2"];

    [Theory]
    [MemberData(nameof(Games))]
    public void EveryStep_HasARow_NamingItsKnownIssue(string gameId)
    {
        var rows = Section(gameId).Split('\n').Where(l => l.StartsWith("- [ ]")).ToList();
        var problems = new List<string>();
        foreach (var step in CanonicalGame.For(gameId).Steps)
        {
            var row = rows.SingleOrDefault(r => r.Contains($"`{step.Tag}`"));
            if (row is null) problems.Add($"{step.Tag}: no checkbox row");
            else if (step.KnownIssue is not null && !row.Contains(step.KnownIssue)) problems.Add($"{step.Tag}: row doesn't name {step.KnownIssue}");
        }
        Assert.True(problems.Count == 0, string.Join("\n", problems));
    }
}
```

Reading the file with `"\n"` assumes LF in the working copy. Normalise first: `.Replace("\r\n", "\n")` inside `Checklist()`.

- [ ] **Step 2: Run it, and watch it fail** because the file doesn't exist.

- [ ] **Step 3: Write `docs/canonical-mod-checklist.md`.** It has these sections, in this order:

1. **Intro.** What the checklist is for: features the tests can only check on disk. Link to #138 and to both mods' READMEs.
2. **Before you start (both games).**
   - Use a throwaway save.
   - Back up the save folder.
   - Note the game build.
   - Turn on the game's output log, and say where Unity writes it on Windows (in generic terms, no user paths).
3. **`## Pillars of Eternity`.**
   - **Setup.** Copy `DialogEditor.Tests/Fixtures/Canonical/poe1/PillarsOfEternity_Data` into the install's game folder (it only adds files). Then apply `Fixtures/CanonicalMod/poe1/canonical_mod.dialogpack` with `dialog-patcher <game folder> <pack>`.
   - **In game.** Load the save and open the console. Run `iroll20s`, then the PoE1 start command from spec §4.
   - **One `- [ ]` row per PoE1 step,** with the tag in backticks. Each row says how to see the feature (which choice to pick, which `SetGlobalValue` / `PrintGlobal` to type) and what to expect. Rows with a `KnownIssue` give the issue number and what it predicts. Derive each row from the step's edit and the fixture's node map, in play order. Steps you can't see in play (for example `kept.bank-and-trigger`, Comments) say "not visible in play: checked by the automated tests" and still get a row.
   - **Issues the tests skip outside a step's `KnownIssue`.** The rows also name these, with what each predicts in play:
     - #161 on `nodes.delete`: the bank still lists node 9;
     - #164 on PoE1 `links.display-values`: the new link 0→12 behaves as ShowOnce;
     - #162 on `conversation.new`.

     Add a test case for this, `RowsNameTheIssuesTheRawAndGameChecksSkip`. It holds a small table of (game, tag, issue) and asserts each row contains that issue.
   - **New conversation.** It is started with `StartConversationFacingListener oei_player canonical_mod_new 1`. The `1` is because #162 means it has no node 0. Also note that start node 0 is expected to fail until #162 is fixed.
4. **`## Deadfire`.** The same structure as PoE1, with these differences:
   - Setup copies the Deadfire fixture and `Fixtures/CanonicalMod/poe2-ingame/` before applying the pack.
   - The start command is `StartConversation oei_player canonical 0`.
   - The PoE2-only rows: link conditions, voice-over, and HideSpeaker. The VO files are 4 placeholder bytes, so expect silence and record what the log says.
   - Calls new in the mod: record whether the game runs them, since the editor writes them without function hashes.
   - `canonical_mod_new` can't be reached in Deadfire because of #162 (no manifest entry, no ID). Record that.
5. **What to record.** Note:
   - how the game presents the conversation when its speaker is absent;
   - output-log errors mentioning `canonical`.
6. **Results.** A table: game, date, build, result, issues filed. It starts with one empty row per game.
7. **Clean up.**
   - Run Remove all mods in the Patch Manager, or `dialog-patcher --remove-all <game folder>` if that flag exists. Check `PatcherCommand` for the real flag before writing it.
   - Delete every copied setup file by name (list them).
   - Verify the game files in the launcher.

- [ ] **Step 4: Run the test** — Expected: 2 passed (poe1, poe2).
- [ ] **Step 5: Commit** `docs: in-game checklist for the canonical mods (#138)`

---

### Task 3: Links and the full test run

**Files:**
- Modify: `DialogEditor.Tests/Fixtures/Canonical/README.md`: in "Known gaps", the sentence about the canonical mods also points at `docs/canonical-mod-checklist.md`.
- Modify: `docs/game-data-tests.md`: one sentence after the canonical-mods paragraph that points at the checklist.

- [ ] **Step 1: Edit both docs.**
- [ ] **Step 2: Full suite.** Run `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. Expected: green, with only issue-named or opt-in skips.
- [ ] **Step 3: Commit** `docs: link the canonical-mod checklist (#138)`

After merge, the run itself is done by hand, once per game. Results go into the checklist's table in a follow-up commit, and failures are filed as issues.
