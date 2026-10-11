# Canonical Mods — PR 1 (harness + PoE2 mod) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A generated, checked-in PoE2 canonical mod (project + `_vo/` + `.dialogpack` + feature README) and tests that apply it through `dialog-patcher`, the Patch Manager and Test Patch (F5), then verify every feature, three-way agreement, untouched data, #116 property sets, VO placement and reversibility.

**Architecture:** A test-side `CanonicalModSession` drives the real view models (`MainWindowViewModel`, `ConversationViewModel`, `NodeDetailViewModel`, `ConditionEditorViewModel`, `ScriptEditorViewModel`) against a temp copy of `Fixtures/Canonical/poe2`. A `ScenarioStep` list pairs each edit with its own expectation. `CanonicalModBuilder` runs the steps, saves through `SaveProject`, packs with `VoPackExporter`, and renders the feature README; golden tests compare that output with the checked-in copy. Apply tests read the written game files back into a `ModView` and run every step's expectation against it.

**Tech Stack:** .NET 10, xUnit (serial), CommunityToolkit.Mvvm view models, `System.IO.Compression`.

**Spec:** `docs/superpowers/specs/2026-10-12-canonical-mods-design.md` (sections 1–4; this plan is PR 1 only — PoE1 is PR 2, the in-game checklist PR 3).

## Global Constraints

- TDD: every task starts with a failing test (CLAUDE.md).
- Test project only, plus checked-in fixtures/docs. No production code changes in this PR; a production bug the mod exposes is **filed** (`gh issue list --state all --search …` first) and the failing check gets `Skip = "#NNN"` — never fixed inline.
- All mod content is invented: text, GUIDs, global-variable names, `.wem` bytes. Nothing from game data. The repository is public; no absolute local paths in issues.
- `DialogEditor.Tests` runs serially; every test that touches `AppSettings` sets `AppSettings.SettingsPathOverride` to a temp file and clears it in `Dispose`.
- Test cleanup may swallow exceptions (`catch (Exception) { /* best-effort */ }`).
- Run headless with `dotnet test DialogEditor.Tests --filter "Category!=Gui"`; never bare `dotnet test` (it launches the GUI tests).
- Regenerate env var: `DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1`. A regenerating run rewrites the files **and fails**.
- Fixture IDs from `DialogEditor.Tests/Fixtures/Canonical/README.md`: NPC GUID `c0a1c0a1-0000-4000-8000-000000000122`, narrator `6a99a109-0000-0000-0000-000000000000`, player `b1a8e901-0000-0000-0000-000000000000`. Verify each against the fixture's bundle before use.

## Review Focus

1. **Nondeterministic output** (layout positions, dictionary order, timestamps): two builds in one process must be byte-identical — test in Task 7.
2. **Re-applying the same pack** must give the same game files (the installer restores originals first) — test in Task 8.
3. **Install path with spaces** — the temp game root contains a space — set in Task 1's session.
4. **A mod language the install lacks** (`de` removed from the fake install): apply succeeds, `en` verifies, nothing is written for `de` — test in Task 8.
5. **Stale pack vs. project** (someone regenerates the project but not the pack): the golden test compares the pack's `project.dialogproject` entry with the checked-in project — Task 7.

---

### Task 1: Harness skeleton — session, steps, ModView, one end-to-end step

**Files:**
- Create: `DialogEditor.Tests/CanonicalMod/CanonicalIds.cs`
- Create: `DialogEditor.Tests/CanonicalMod/CanonicalModSession.cs`
- Create: `DialogEditor.Tests/CanonicalMod/ScenarioStep.cs`
- Create: `DialogEditor.Tests/CanonicalMod/ModView.cs`
- Create: `DialogEditor.Tests/CanonicalMod/CanonicalModBuilder.cs`
- Create: `DialogEditor.Tests/CanonicalMod/Poe2Scenario.cs`
- Create: `DialogEditor.Tests/CanonicalMod/BuiltPoe2Mod.cs` (xUnit class fixture)
- Test: `DialogEditor.Tests/CanonicalMod/Poe2ScenarioStepTests.cs`

**Interfaces:**
- Produces:
  - `record ScenarioStep(string Tag, string Description, Func<CanonicalModSession, Task> Edit, Action<ModView> Expect)`
  - `sealed class CanonicalModSession : IDisposable` — `static Task<CanonicalModSession> OpenAsync(string game, string projectName)`; `string GameRoot`, `string ProjectPath`, `MainWindowViewModel Vm`; `void Open(string conversation)`; `Task SwitchLanguageAsync(string lang)`; `NodeViewModel Node(int id)`; `NodeViewModel AddNode(int expectedId, bool playerChoice, SpeakerCategory category, string speaker, string listener, double x, double y)`; `void Link(int from, int to)`; `void Unlink(int from, int to)`; `ConnectionViewModel LinkOf(int from, int to)`; `void Delete(int id)`; `void Save()`.
  - `sealed class ModView` — `static ModView FromGame(IGameDataProvider provider, IEnumerable<string> conversations, IEnumerable<string> languages)`; `ConversationNode Node(string conv, int id)`; `bool Has(string conv, int id)`; `StringEntry? Text(string conv, string lang, int id)`.
  - `static class CanonicalModBuilder` — `static Task<string> BuildAsync(string game, IReadOnlyList<ScenarioStep> steps, string outDir)` returns the project path under `outDir` (project file + `_vo/`).
  - `static class Poe2Scenario` — `static IReadOnlyList<ScenarioStep> Steps { get; }`; `const string Edited = "canonical"`; `const string NewConversation = "canonical_mod_new"`.
  - `sealed class BuiltPoe2Mod : IDisposable` — builds once per test class: `string ProjectPath`, `ModView Applied` (the project installed with `PatchInstaller.Install` into a fresh fixture copy, read back).

- [ ] **Step 1: Write the failing test**

`Poe2ScenarioStepTests.cs`:

```csharp
using DialogEditor.Tests.CanonicalMod;

namespace DialogEditor.Tests.CanonicalMod;

/// Issue 138: every scenario step's own expectation, checked against the PoE2 canonical
/// mod after the patcher has written it into a fresh copy of the canonical install. One
/// test case per step, so a failure names the feature.
[Trait("Category", "Canonical")]
public class Poe2ScenarioStepTests(BuiltPoe2Mod mod) : IClassFixture<BuiltPoe2Mod>
{
    public static TheoryData<string> Tags => [.. Poe2Scenario.Steps.Select(s => s.Tag)];

    [Theory]
    [MemberData(nameof(Tags))]
    public void Step_HoldsAfterInstall(string tag) =>
        Poe2Scenario.Steps.Single(s => s.Tag == tag).Expect(mod.Applied);

    [Fact]
    public void Tags_AreUnique() =>
        Assert.Equal(Poe2Scenario.Steps.Count, Poe2Scenario.Steps.Select(s => s.Tag).Distinct().Count());
}
```

and the first step in `Poe2Scenario.cs`:

```csharp
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
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2ScenarioStepTests"`
Expected: build FAILS — `CanonicalModSession`, `ModView`, `BuiltPoe2Mod` … do not exist.

- [ ] **Step 3: Implement the harness**

`CanonicalIds.cs`:

```csharp
namespace DialogEditor.Tests.CanonicalMod;

/// GUIDs from the hand-written canonical fixture (Fixtures/Canonical/README.md). Invented
/// except the player and narrator, which are engine constants.
internal static class CanonicalIds
{
    public const string Npc      = "c0a1c0a1-0000-4000-8000-000000000122";
    public const string Narrator = "6a99a109-0000-0000-0000-000000000000";
    public const string Player   = "b1a8e901-0000-0000-0000-000000000000";
}
```

`ScenarioStep.cs`:

```csharp
namespace DialogEditor.Tests.CanonicalMod;

/// One feature of the canonical mod (issue 138): the edit that produces it through the real
/// view models, and the expectation that proves it reached the game files. Expectations
/// describe the FINAL state, so a later step must never undo an earlier step's feature.
public sealed record ScenarioStep(
    string Tag,
    string Description,
    Func<CanonicalModSession, Task> Edit,
    Action<ModView> Expect);
```

`CanonicalModSession.cs` — drives `MainWindowViewModel` the way the existing VM tests do (reflection for `LoadDirectory`, `LoadProjectAsync`, `OnConversationSelected`, `SaveProject`; these are the GUI's own code paths):

```csharp
using System.Reflection;
using DialogEditor.Core.Models;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.CanonicalMod;

/// A real editor session over a temp copy of a canonical fixture (issue 138). Every edit
/// goes through the view models the GUI uses; only the Avalonia views are skipped.
public sealed class CanonicalModSession : IDisposable
{
    private const BindingFlags Private = BindingFlags.NonPublic | BindingFlags.Instance;

    // A space in the root on purpose: install paths with spaces are the norm on Windows.
    public string Root         { get; } = Path.Combine(Path.GetTempPath(), $"canonical mod {Guid.NewGuid():N}");
    public string GameRoot     => Path.Combine(Root, "game");
    public string ProjectPath  { get; private set; } = "";
    public MainWindowViewModel Vm { get; }
    private readonly string _settingsPath;

    private CanonicalModSession()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Root, "settings.json");
        Directory.CreateDirectory(Root);
        AppSettings.SettingsPathOverride = _settingsPath;
        Vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
    }

    public static async Task<CanonicalModSession> OpenAsync(string game, string projectName)
    {
        var s = new CanonicalModSession();
        CanonicalFixture.CopyTo(game, s.GameRoot);
        s.Invoke("LoadDirectory", s.GameRoot);

        s.ProjectPath = Path.Combine(s.Root, "project", projectName + ".dialogproject");
        DialogProjectSerializer.SaveToFile(s.ProjectPath, DialogProject.Empty(projectName));
        await (Task)s.Invoke("LoadProjectAsync", s.ProjectPath, false)!;
        return s;
    }

    public void Open(string conversation)
    {
        var provider = (DialogEditor.Core.GameData.IGameDataProvider)typeof(MainWindowViewModel).GetField("_provider", Private)!.GetValue(Vm)!;
        var file = provider.FindConversation(conversation) ?? throw new InvalidOperationException($"no conversation '{conversation}'");
        Invoke("OnConversationSelected", file);
    }

    /// Saves, then switches the text language the way the toolbar does (the conversation reloads).
    public Task SwitchLanguageAsync(string lang)
    {
        Save();
        Vm.SelectedLanguage = lang;
        return Task.CompletedTask;
    }

    public NodeViewModel Node(int id) => Vm.Canvas.Nodes.Single(n => n.NodeId == id);

    /// Adds a node at a fixed position (layouts are saved, so positions must be deterministic)
    /// and asserts the editor allocated the id the scenario expects.
    public NodeViewModel AddNode(int expectedId, bool playerChoice, SpeakerCategory category,
                                 string speaker, string listener, double x, double y)
    {
        var id = Vm.Canvas.NextNodeId();
        Assert.Equal(expectedId, id);
        var node = new NodeViewModel(
            new ConversationNode(id, playerChoice, category, speaker, listener, [], [], [], "Conversation", "None"),
            new StringEntry(id, "", ""));
        Vm.Canvas.AddNode(node, new LayoutPoint(x, y));
        return node;
    }

    public void Link(int from, int to)   => Vm.Canvas.AddConnection(Node(from).Output, Node(to).Input);
    public void Unlink(int from, int to) => Vm.Canvas.DeleteConnection(LinkOf(from, to));
    public ConnectionViewModel LinkOf(int from, int to) =>
        Vm.Canvas.Connections.Single(c => c.Source.Owner == Node(from) && c.Target.Owner == Node(to));
    public void Delete(int id) => Vm.Canvas.DeleteNode(Node(id));

    /// The GUI's Save Project path: folds the canvas into the project, writes the file.
    public void Save() => Invoke("SaveProject");

    private object? Invoke(string method, params object?[] args) =>
        typeof(MainWindowViewModel).GetMethod(method, Private)!.Invoke(Vm, args);

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ }
    }
}
```

If `NodeViewModel`'s or `ConversationNode`'s constructor differs from the above, read `DialogEditor.ViewModels/ViewModels/NodeViewModel.cs:285` and `ConversationViewModel.AddConnectedNode` (`ConversationViewModel.cs:463`) and build the node the same way `AddConnectedNode` does.

`ModView.cs`:

```csharp
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;

namespace DialogEditor.Tests.CanonicalMod;

/// The game files as the editor's own parser reads them back, per conversation and language.
public sealed class ModView
{
    private readonly Dictionary<string, IReadOnlyList<ConversationNode>> _nodes = new();
    private readonly Dictionary<(string, string), StringTable> _text = new();

    public static ModView FromGame(IGameDataProvider provider, IEnumerable<string> conversations, IEnumerable<string> languages)
    {
        var view  = new ModView();
        var langs = languages.ToList();
        foreach (var conv in conversations)
        {
            var file = provider.FindConversation(conv) ?? throw new InvalidOperationException($"'{conv}' not installed");
            foreach (var lang in langs)
            {
                provider.Language = lang;
                var c = provider.LoadConversation(file);
                view._nodes[conv]         = c.Nodes;
                view._text[(conv, lang)]  = c.Strings;
            }
        }
        return view;
    }

    public bool Has(string conv, int id) => _nodes[conv].Any(n => n.NodeId == id);
    public ConversationNode Node(string conv, int id) => _nodes[conv].Single(n => n.NodeId == id);
    public StringEntry? Text(string conv, string lang, int id) => _text[(conv, lang)].Get(id);
}
```

`CanonicalModBuilder.cs`:

```csharp
namespace DialogEditor.Tests.CanonicalMod;

/// Runs a scenario in a real editor session and copies the saved project (and its _vo/) to outDir.
public static class CanonicalModBuilder
{
    public const string ProjectName = "canonical_mod";

    public static async Task<string> BuildAsync(string game, IReadOnlyList<ScenarioStep> steps, string outDir)
    {
        using var session = await CanonicalModSession.OpenAsync(game, ProjectName);
        foreach (var step in steps) await step.Edit(session);
        session.Save();

        Directory.CreateDirectory(outDir);
        var projectDir = Path.GetDirectoryName(session.ProjectPath)!;
        foreach (var file in Directory.EnumerateFiles(projectDir, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(outDir, Path.GetRelativePath(projectDir, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest, overwrite: true);
        }
        return Path.Combine(outDir, ProjectName + ".dialogproject");
    }
}
```

`BuiltPoe2Mod.cs`:

```csharp
using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Patch.Install;

namespace DialogEditor.Tests.CanonicalMod;

/// Builds the PoE2 mod once per test class and installs it with the patcher's own routine
/// into a fresh copy of the canonical install.
public sealed class BuiltPoe2Mod : IDisposable
{
    public string Root        { get; } = Path.Combine(Path.GetTempPath(), $"canonical built {Guid.NewGuid():N}");
    public string ProjectPath { get; }
    public string GameRoot    => Path.Combine(Root, "game");
    public ModView Applied    { get; }

    public BuiltPoe2Mod()
    {
        ProjectPath = CanonicalModBuilder.BuildAsync("poe2", Poe2Scenario.Steps, Path.Combine(Root, "mod")).GetAwaiter().GetResult();
        CanonicalFixture.CopyTo("poe2", GameRoot);
        var provider = new Poe2GameDataProvider(GameRoot);
        var project  = DialogProjectSerializer.LoadFromFile(ProjectPath);
        var voDir    = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
        var result   = PatchInstaller.Install(provider, GameRoot,
            [new InstallEntry(project, Directory.Exists(voDir) ? voDir : null)], new InstallOptions());
        Assert.IsType<InstallResult.Applied>(result);
        Applied = ModView.FromGame(provider, Poe2Scenario.Conversations, ["en", "de"]);
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ } }
}
```

Add `public static IReadOnlyList<string> Conversations { get; } = [Edited];` to `Poe2Scenario` (Task 6 adds `NewConversation`). Check `InstallEntry`'s real constructor in `DialogEditor.Patch/Install/InstallModels.cs` and adapt the call.

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2ScenarioStepTests"`
Expected: PASS (2 tests). If `nodes.add-npc-line` fails because the editor allocates a different id than 11, read `NodeIdAllocator` and adjust the expected id — the README must reflect what the editor really does.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod
git commit -m "test: canonical mod harness — session, steps, ModView, first step (#138)"
```

---

### Task 2: Structural steps — nodes, kept types, links

**Files:**
- Modify: `DialogEditor.Tests/CanonicalMod/Poe2Scenario.cs` (append steps)
- Test: `Poe2ScenarioStepTests` (data-driven; no new test code)

**Interfaces:**
- Consumes: `CanonicalModSession` (Task 1), `ScenarioStep`, `ModView`.

- [ ] **Step 1: Write the failing tests (append these steps; their expectations are the tests)**

```csharp
new("nodes.add-player-choice", "New player choice 12 under 0",
    async s => { s.Open(Edited);
        var n = s.AddNode(12, playerChoice: true, SpeakerCategory.Player, CanonicalIds.Player, CanonicalIds.Npc, 300, 400);
        n.DefaultText = "Ask about the canonical mod.";
        s.Link(0, 12); s.Save(); await Task.CompletedTask; },
    v => { Assert.True(v.Node(Edited, 12).IsPlayerChoice);
           Assert.Contains(v.Node(Edited, 0).Links, l => l.ToNodeId == 12); }),

new("nodes.add-script", "New script node 13 after 12",
    async s => { s.Open(Edited);
        s.AddNode(13, playerChoice: false, SpeakerCategory.Script, "", "", 600, 400);
        s.Link(12, 13); s.Save(); await Task.CompletedTask; },
    v => { Assert.Equal(SpeakerCategory.Script, v.Node(Edited, 13).SpeakerCategory);
           Assert.Contains(v.Node(Edited, 12).Links, l => l.ToNodeId == 13); }),

new("nodes.convert-player-to-npc", "Player choice 3 converted to an NPC line",
    async s => { s.Open(Edited);
        var n = s.Node(3); n.IsPlayerChoice = false; n.SpeakerCategory = SpeakerCategory.Npc;
        n.SpeakerGuid = CanonicalIds.Npc; n.ListenerGuid = CanonicalIds.Player;
        s.Save(); await Task.CompletedTask; },
    v => { Assert.False(v.Node(Edited, 3).IsPlayerChoice);
           Assert.Equal(CanonicalIds.Npc, v.Node(Edited, 3).SpeakerGuid); }),

new("nodes.delete", "Node 9 deleted; bank 5 keeps child 8",
    async s => { s.Open(Edited); s.Delete(9); s.Save(); await Task.CompletedTask; },
    v => { Assert.False(v.Has(Edited, 9)); Assert.True(v.Has(Edited, 8)); }),

new("kept.bank-and-trigger", "Bank 5 and trigger 6 get new comments; their bank/trigger fields survive",
    async s => { s.Open(Edited);
        s.Node(5).Comments = "Bank edited by the canonical mod.";
        s.Node(6).Comments = "Trigger edited by the canonical mod.";
        s.Save(); await Task.CompletedTask; },
    v => { Assert.Equal("Bank edited by the canonical mod.", v.Node(Edited, 5).Comments);
           Assert.Equal("Trigger edited by the canonical mod.", v.Node(Edited, 6).Comments);
           // The type-specific fields are checked on the raw bundle in Task 9 (the editor model
           // does not carry ChildNodeIDs / trigger targets); here: the types are kept.
           Assert.Equal(SpeakerCategory.Script, v.Node(Edited, 5).SpeakerCategory); }),

new("links.remove", "Link 2→5 removed",
    async s => { s.Open(Edited); s.Unlink(2, 5); s.Save(); await Task.CompletedTask; },
    v => Assert.DoesNotContain(v.Node(Edited, 2).Links, l => l.ToNodeId == 5)),

new("links.weight", "Link 0→2 re-weighted to 3",
    async s => { s.Open(Edited); s.LinkOf(0, 2).RandomWeight = 3; s.Save(); await Task.CompletedTask; },
    v => Assert.Equal(3, v.Node(Edited, 0).Links.Single(l => l.ToNodeId == 2).RandomWeight)),

new("links.display-values", "0→1 ShowNever, 0→2 ShowOnce, 0→12 ShowAlways",
    async s => { s.Open(Edited);
        s.LinkOf(0, 1).QuestionNodeTextDisplay  = "ShowNever";
        s.LinkOf(0, 2).QuestionNodeTextDisplay  = "ShowOnce";
        s.LinkOf(0, 12).QuestionNodeTextDisplay = "ShowAlways";
        s.Save(); await Task.CompletedTask; },
    v => { var links = v.Node(Edited, 0).Links;
           Assert.Equal("ShowNever",  links.Single(l => l.ToNodeId == 1).QuestionNodeTextDisplay);
           Assert.Equal("ShowOnce",   links.Single(l => l.ToNodeId == 2).QuestionNodeTextDisplay);
           Assert.Equal("ShowAlways", links.Single(l => l.ToNodeId == 12).QuestionNodeTextDisplay); }),
```

Check the node-map in `Fixtures/Canonical/README.md` before each step: the fixture's existing values must differ from what the step sets, or the step proves nothing (e.g. 0→2 is `ShowAlways` in the fixture, so setting `ShowOnce` is a real change).

- [ ] **Step 2: Run tests to verify the new ones fail first**

Temporarily comment out each new step's `Edit` body (keep `Expect`), run:
`dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2ScenarioStepTests"`
Expected: each new tag FAILS on its own assertion (proves the expectation detects the change). Restore the bodies.

- [ ] **Step 3: Run tests to verify they pass**

Same command. Expected: PASS for all tags. A failure that is a product bug: file an issue (search first), then mark that tag skipped by giving `ScenarioStep` an optional `string? KnownIssue = null` field and in `Step_HoldsAfterInstall` call `Assert.Skip`-equivalent — in xUnit v2 use a separate `[Theory(Skip = "#NNN")]` test for that tag and remove it from `Tags`. Record the issue in the step's description.

- [ ] **Step 4: Commit**

```bash
git commit -am "test: canonical mod — node, kept-type and link steps (#138)"
```

---

### Task 3: Conditions and scripts steps

**Files:**
- Modify: `DialogEditor.Tests/CanonicalMod/CanonicalModSession.cs` (add `EditConditions`, `EditScripts`, `Catalogue` helpers)
- Modify: `DialogEditor.Tests/CanonicalMod/Poe2Scenario.cs`

**Interfaces:**
- Produces (session): `void EditConditions(NodeViewModel node, IReadOnlyList<ConditionNode> conditions)`; `void EditLinkConditions(int from, int to, IReadOnlyList<ConditionNode> conditions)`; `void EditScripts(NodeViewModel node, IReadOnlyList<ScriptCall> scripts)`; `static string ConditionName(string method)` / `static string ScriptName(string method)` — the catalogue's `ReflectionFullName` for a PoE2 method, throwing if absent (an unknown function would be written with a bad hash).

- [ ] **Step 1: Add the session helpers**

```csharp
/// Opens the condition editor on an existing tree and confirms it: the same tree → rows → tree
/// round trip the dialog performs, so nesting, negation and operators go through the editor's code.
public void EditConditions(NodeViewModel node, IReadOnlyList<ConditionNode> conditions) =>
    new ConditionEditorViewModel("canonical", conditions, c => node.Conditions = c, "poe2").ConfirmCommand.Execute(null);

public void EditLinkConditions(int from, int to, IReadOnlyList<ConditionNode> conditions)
{
    var link = LinkOf(from, to);
    new ConditionEditorViewModel("canonical", conditions, c => link.Conditions = c, "poe2").ConfirmCommand.Execute(null);
}

public void EditScripts(NodeViewModel node, IReadOnlyList<ScriptCall> scripts) =>
    new ScriptEditorViewModel("canonical", scripts, s => node.Scripts = s, "poe2").ConfirmCommand.Execute(null);

public static string ConditionName(string method) =>
    ConditionCatalogue.Instance.ForGame("poe2").First(e => e.ReflectionFullName.Contains(" " + method + "(")).ReflectionFullName;

public static string ScriptName(string method) =>
    ScriptCatalogue.Instance.ForGame("poe2").First(e => e.ReflectionFullName.Contains(" " + method + "(")).ReflectionFullName;
```

Read `ScriptEditorViewModel.cs` for the confirm command's name and `ScriptCatalogue`'s real type/name before writing; adapt.

- [ ] **Step 2: Write the failing steps**

```csharp
new("conditions.node-nested", "Node 12: A and (B or not C), with a multi-parameter call",
    async s => { s.Open(Edited);
        var a = new ConditionLeaf(CanonicalModSession.ConditionName("IsGlobalValue"), ["cm_flag_a", "EqualTo", "1"], false, "And");
        var b = new ConditionLeaf(CanonicalModSession.ConditionName("IsGlobalValue"), ["cm_flag_b", "GreaterThan", "2"], false, "Or");
        var c = new ConditionLeaf(CanonicalModSession.ConditionName("IsGlobalValue"), ["cm_flag_c", "EqualTo", "0"], true,  "And");
        s.EditConditions(s.Node(12), [a, new ConditionBranch([b, c], false, "And")]);
        s.Save(); await Task.CompletedTask; },
    v => { var conds = v.Node(Edited, 12).Conditions;
           Assert.Equal(2, conds.Count);
           var branch = Assert.IsType<ConditionBranch>(conds[1]);
           Assert.True(((ConditionLeaf)branch.Components[1]).Not);
           Assert.Equal(["cm_flag_b", "GreaterThan", "2"], ((ConditionLeaf)branch.Components[0]).Parameters); }),

new("conditions.link", "Link 1→4 gets a negated condition (PoE2 links carry conditions)",
    async s => { s.Open(Edited);
        s.EditLinkConditions(1, 4, [new ConditionLeaf(CanonicalModSession.ConditionName("IsGlobalValue"), ["cm_flag_link", "EqualTo", "1"], true, "And")]);
        s.Save(); await Task.CompletedTask; },
    v => { var leaf = Assert.IsType<ConditionLeaf>(Assert.Single(v.Node(Edited, 1).Links.Single(l => l.ToNodeId == 4).Conditions));
           Assert.True(leaf.Not); }),

new("conditions.clear", "Node 2's condition removed entirely",
    async s => { s.Open(Edited); s.EditConditions(s.Node(2), []); s.Save(); await Task.CompletedTask; },
    v => Assert.Empty(v.Node(Edited, 2).Conditions)),

new("scripts.add-all-categories", "Node 13 gets OnEnter, OnExit and OnUpdate scripts",
    async s => { s.Open(Edited);
        var set = CanonicalModSession.ScriptName("SetGlobalValue");
        s.EditScripts(s.Node(13), [
            new ScriptCall(set, ["cm_enter", "1"],  ScriptCategory.Enter),
            new ScriptCall(set, ["cm_exit", "2"],   ScriptCategory.Exit),
            new ScriptCall(set, ["cm_update", "3"], ScriptCategory.Update)]);
        s.Save(); await Task.CompletedTask; },
    v => Assert.Equal([ScriptCategory.Enter, ScriptCategory.Exit, ScriptCategory.Update],
                      v.Node(Edited, 13).Scripts.Select(x => x.Category).Order())),

new("scripts.keep-per-script-conditional", "Node 4 gains a script; its existing script keeps its own conditional (#115)",
    async s => { s.Open(Edited);
        var n = s.Node(4);
        s.EditScripts(n, [.. n.Scripts, new ScriptCall(CanonicalModSession.ScriptName("SetGlobalValue"), ["cm_extra", "1"], ScriptCategory.Exit)]);
        s.Save(); await Task.CompletedTask; },
    v => Assert.Contains(v.Node(Edited, 4).Scripts, x => x.Parameters.SequenceEqual(new[] { "cm_extra", "1" }))
    /* the conditional itself is checked on the raw bundle in Task 9 */),

new("scripts.remove", "Node 0's OnEnter script removed",
    async s => { s.Open(Edited); s.EditScripts(s.Node(0), []); s.Save(); await Task.CompletedTask; },
    v => Assert.Empty(v.Node(Edited, 0).Scripts)),
```

Names (`IsGlobalValue`, `SetGlobalValue`) are engine function names from the condition/script catalogue, not game data; if either is not in the PoE2 catalogue, pick another multi-parameter entry and keep the rest unchanged.

- [ ] **Step 3: Verify red, then green** — as Task 2 Steps 2–3.

- [ ] **Step 4: Commit**

```bash
git commit -am "test: canonical mod — condition and script steps (#138)"
```

---

### Task 4: Properties and text steps (including `de`)

**Files:**
- Modify: `DialogEditor.Tests/CanonicalMod/Poe2Scenario.cs`

- [ ] **Step 1: Write the failing steps**

```csharp
new("props.display-and-persistence", "Every DisplayType and Persistence value across nodes 11, 12, 13, 0",
    async s => { s.Open(Edited);
        s.Node(11).DisplayType = "Hidden";       s.Node(11).Persistence = "OnceEver";
        s.Node(12).DisplayType = "Conversation"; s.Node(12).Persistence = "OncePerConversation";
        s.Node(13).DisplayType = "Bark";         s.Node(13).Persistence = "MarkAsRead";
        s.Node(0).DisplayType  = "Overlay";      s.Node(0).Persistence  = "None";
        s.Save(); await Task.CompletedTask; },
    v => { Assert.Equal(("Hidden", "OnceEver"),              (v.Node(Edited, 11).DisplayType, v.Node(Edited, 11).Persistence));
           Assert.Equal(("Conversation", "OncePerConversation"), (v.Node(Edited, 12).DisplayType, v.Node(Edited, 12).Persistence));
           Assert.Equal(("Bark", "MarkAsRead"),            (v.Node(Edited, 13).DisplayType, v.Node(Edited, 13).Persistence));
           Assert.Equal(("Overlay", "None"),               (v.Node(Edited, 0).DisplayType,  v.Node(Edited, 0).Persistence)); }),

new("props.speaker-listener", "Node 10's speaker becomes the narrator, listener the NPC",
    async s => { s.Open(Edited); s.Node(10).SpeakerGuid = CanonicalIds.Narrator; s.Node(10).ListenerGuid = CanonicalIds.Npc;
        s.Save(); await Task.CompletedTask; },
    v => Assert.Equal((CanonicalIds.Narrator, CanonicalIds.Npc), (v.Node(Edited, 10).SpeakerGuid, v.Node(Edited, 10).ListenerGuid))),

new("props.comments", "Node 11 gets a comment",
    async s => { s.Open(Edited); s.Node(11).Comments = "Comment written by the canonical mod."; s.Save(); await Task.CompletedTask; },
    v => Assert.Equal("Comment written by the canonical mod.", v.Node(Edited, 11).Comments)),

new("props.hide-speaker", "HideSpeaker turned on for 11 and off for 10",
    async s => { s.Open(Edited); s.Node(11).HideSpeaker = true; s.Node(10).HideSpeaker = false; s.Save(); await Task.CompletedTask; },
    v => { Assert.True(v.Node(Edited, 11).HideSpeaker); Assert.False(v.Node(Edited, 10).HideSpeaker); }),

new("text.edit-with-female", "Node 0's text and female variant edited",
    async s => { s.Open(Edited); s.Node(0).DefaultText = "Welcome back, [Player Name]. The mod changed this line.";
        s.Node(0).FemaleText = "Welcome back, [Player Name]. The mod changed this line, too."; s.Save(); await Task.CompletedTask; },
    v => { Assert.Equal("Welcome back, [Player Name]. The mod changed this line.", v.Text(Edited, "en", 0)!.DefaultText);
           Assert.Equal("Welcome back, [Player Name]. The mod changed this line, too.", v.Text(Edited, "en", 0)!.FemaleText); }),

new("text.markup", "Node 12's text uses <i> markup",
    async s => { s.Open(Edited); s.Node(12).DefaultText = "Ask about the <i>canonical</i> mod."; s.Save(); await Task.CompletedTask; },
    v => Assert.Equal("Ask about the <i>canonical</i> mod.", v.Text(Edited, "en", 12)!.DefaultText)),

new("text.second-language", "German text for new node 11 and edited node 0",
    async s => { s.Open(Edited); await s.SwitchLanguageAsync("de");
        s.Node(11).DefaultText = "Eine neue Schlusszeile aus dem Kanon-Mod.";
        s.Node(0).DefaultText  = "Willkommen zurück, [Player Name]. Der Mod hat diese Zeile geändert.";
        s.Save(); await s.SwitchLanguageAsync("en"); },
    v => { Assert.Equal("Eine neue Schlusszeile aus dem Kanon-Mod.", v.Text(Edited, "de", 11)!.DefaultText);
           Assert.Equal("Willkommen zurück, [Player Name]. Der Mod hat diese Zeile geändert.", v.Text(Edited, "de", 0)!.DefaultText);
           Assert.Equal("A new closing line, written by the canonical mod.", v.Text(Edited, "en", 11)!.DefaultText); }),
```

Read `Fixtures/Canonical/README.md` first: node 0 must not already be `Overlay`, node 10 must have `HideSpeaker` on (it does — PoE2 row), etc. Each assignment must be a change.

- [ ] **Step 2: Verify red, then green** — as Task 2 Steps 2–3. If `text.second-language` fails because the switch drops or misfiles text, that is a product bug: file and skip.

- [ ] **Step 3: Commit**

```bash
git commit -am "test: canonical mod — property and text steps (#138)"
```

---

### Task 5: VO steps

**Files:**
- Modify: `DialogEditor.Tests/CanonicalMod/CanonicalModSession.cs` (add `ImportVoAsync`)
- Modify: `DialogEditor.Tests/CanonicalMod/Poe2Scenario.cs`
- Create: `DialogEditor.Tests/Fixtures/CanonicalMod/poe2-inputs/canonical_mod_0011.wem` (invented bytes)

**Interfaces:**
- Produces (session): `Task ImportVoAsync(int nodeId, string sourceWem)` — selects the node, wires `Detail.Importer = new VoImporter()` and `Detail.ShowImportDialog = _ => Task.FromResult<VoImportDialogResult?>(new(sourceWem, null))`, runs `Detail.ImportVoCommand`.

- [ ] **Step 1: Create the input and helper**

Create the `.wem` with invented placeholder bytes (as the #122 fixture does — four bytes, not audio):

```bash
printf 'CMOD' > DialogEditor.Tests/Fixtures/CanonicalMod/poe2-inputs/canonical_mod_0011.wem
```

Ensure the test project copies it (`DialogEditor.Tests.csproj` — add next to the existing `Fixtures\Canonical\**\*` line):

```xml
<None Update="Fixtures\CanonicalMod\**\*" CopyToOutputDirectory="PreserveNewest" />
```

```csharp
public async Task ImportVoAsync(int nodeId, string sourceWem)
{
    Vm.Canvas.SelectNode(Node(nodeId));
    Vm.Detail.Importer         = new DialogEditor.Avalonia.Audio.VoImporter();
    Vm.Detail.ShowImportDialog = _ => Task.FromResult<VoImportDialogResult?>(new VoImportDialogResult(sourceWem, null));
    await Vm.Detail.ImportVoCommand.ExecuteAsync(null);
}
```

- [ ] **Step 2: Write the failing steps**

```csharp
new("vo.import", "Narrator line 11 gets an imported .wem (HasVO)",
    async s => { s.Open(Edited);
        s.Node(11).SpeakerGuid = CanonicalIds.Narrator;   // the narrator has a VO folder
        await s.ImportVoAsync(11, CanonicalModInputs.Poe2Wem); s.Save(); },
    v => Assert.True(v.Node(Edited, 11).HasVO)
    /* placement of the file is checked in Task 9 */),

new("vo.alias", "Node 12 aliases node 7's VO (ExternalVO)",
    async s => { s.Open(Edited); s.Node(12).ExternalVO = "narrator/canonical_0007"; s.Save(); await Task.CompletedTask; },
    v => Assert.Equal("narrator/canonical_0007", v.Node(Edited, 12).ExternalVO)),

new("vo.off", "Node 7's HasVO switched off",
    async s => { s.Open(Edited); s.Node(7).HasVO = false; s.Save(); await Task.CompletedTask; },
    v => Assert.False(v.Node(Edited, 7).HasVO)),
```

Note `vo.import` re-sets node 11's speaker after `props.speaker-listener`; keep the README description truthful. `CanonicalModInputs.Poe2Wem` = `Path.Combine(AppContext.BaseDirectory, "Fixtures", "CanonicalMod", "poe2-inputs", "canonical_mod_0011.wem")` in a small static class in the same folder. Node 12 is a player choice: if the editor refuses ExternalVO on a player node, move the alias to node 11's sibling NPC node and say so in the description.

- [ ] **Step 3: Verify red, then green** — as Task 2 Steps 2–3.

- [ ] **Step 4: Commit**

```bash
git add -A DialogEditor.Tests
git commit -m "test: canonical mod — VO import, alias and HasVO steps (#138)"
```

---

### Task 6: New-conversation steps

**Files:**
- Modify: `DialogEditor.Tests/CanonicalMod/CanonicalModSession.cs` (add `NewConversationAsync`)
- Modify: `DialogEditor.Tests/CanonicalMod/Poe2Scenario.cs` (steps; `Conversations` gains `NewConversation`)

- [ ] **Step 1: Add the helper**

```csharp
public async Task NewConversationAsync(string name)
{
    Save();
    Vm.RequestConversationName = () => Task.FromResult<string?>(name);
    await Vm.NewConversationCommand.ExecuteAsync(null);
}
```

- [ ] **Step 2: Write the failing step**

```csharp
new("conversation.new", "New conversation canonical_mod_new: NPC → two choices (one conditioned) → end, en + de",
    async s => {
        await s.NewConversationAsync(NewConversation);
        var start = s.Vm.Canvas.Nodes.Single();              // the editor seeds one node; read NewConversation's code to confirm
        start.SpeakerGuid = CanonicalIds.Npc; start.DefaultText = "Hello from a conversation the mod created.";
        var a = s.AddNode(s.Vm.Canvas.NextNodeId(), true, SpeakerCategory.Player, CanonicalIds.Player, CanonicalIds.Npc, 300, 0);
        a.DefaultText = "First choice.";
        var b = s.AddNode(s.Vm.Canvas.NextNodeId(), true, SpeakerCategory.Player, CanonicalIds.Player, CanonicalIds.Npc, 300, 200);
        b.DefaultText = "Second choice, only when cm_flag_a is set.";
        s.EditConditions(b, [new ConditionLeaf(CanonicalModSession.ConditionName("IsGlobalValue"), ["cm_flag_a", "EqualTo", "1"], false, "And")]);
        var end = s.AddNode(s.Vm.Canvas.NextNodeId(), false, SpeakerCategory.Npc, CanonicalIds.Npc, CanonicalIds.Player, 600, 100);
        end.DefaultText = "Goodbye.";
        s.Link(start.NodeId, a.NodeId); s.Link(start.NodeId, b.NodeId); s.Link(a.NodeId, end.NodeId); s.Link(b.NodeId, end.NodeId);
        s.Save();
        await s.SwitchLanguageAsync("de");
        s.Node(start.NodeId).DefaultText = "Hallo aus einem Gespräch, das der Mod erstellt hat.";
        s.Save(); await s.SwitchLanguageAsync("en"); },
    v => { var nodes = Enumerable.Range(0, 8).Where(id => v.Has(NewConversation, id)).ToList();
           Assert.Equal(4, nodes.Count);
           Assert.Contains(nodes, id => v.Node(NewConversation, id).Conditions.Count == 1);
           Assert.Contains(nodes, id => v.Text(NewConversation, "de", id)?.DefaultText == "Hallo aus einem Gespräch, das der Mod erstellt hat."); }),
```

Set `Conversations = [Edited, NewConversation]`. Read `MainWindowViewModel.NewConversation` (around line 1141) to confirm what the new canvas starts with (one root node vs. none) and adapt the first lines.

- [ ] **Step 3: Verify red, then green** — as Task 2 Steps 2–3.

- [ ] **Step 4: Commit**

```bash
git commit -am "test: canonical mod — new conversation step (#138)"
```

---

### Task 7: Pack, generated README, golden files, regeneration

**Files:**
- Modify: `DialogEditor.Tests/CanonicalMod/CanonicalModBuilder.cs` (add `BuildAllAsync` → project, `_vo/`, `.dialogpack`, `README.md`)
- Create: `DialogEditor.Tests/CanonicalMod/CanonicalModGoldenTests.cs`
- Create (generated, checked in): `DialogEditor.Tests/Fixtures/CanonicalMod/poe2/{canonical_mod.dialogproject,_vo/…,canonical_mod.dialogpack,README.md}`
- Modify: `DialogEditor.Tests/Fixtures/Canonical/README.md` (*Known gaps*: #139, #141 fixed; point at the canonical mod)

**Interfaces:**
- Produces: `static Task BuildAllAsync(string game, IReadOnlyList<ScenarioStep> steps, string outDir)`; `static string GoldenDir(string game)` (source tree path, found by walking up to `DialogEditor.slnx`).

- [ ] **Step 1: Write the failing golden tests**

```csharp
namespace DialogEditor.Tests.CanonicalMod;

/// The checked-in PoE2 canonical mod must equal a fresh build. If the editor's output
/// changed on purpose, rerun with DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1: that rewrites
/// the files AND fails, so a regeneration is always a reviewed diff, never a silent pass.
[Trait("Category", "Canonical")]
public class CanonicalModGoldenTests
{
    private const string RegenerateVar = "DIALOGEDITOR_REGENERATE_CANONICAL_MOD";

    [Fact]
    public async Task Poe2_CheckedInMod_MatchesAFreshBuild()
    {
        var fresh = Path.Combine(Path.GetTempPath(), $"canonical golden {Guid.NewGuid():N}");
        try
        {
            await CanonicalModBuilder.BuildAllAsync("poe2", Poe2Scenario.Steps, fresh);
            var golden = CanonicalModBuilder.GoldenDir("poe2");

            if (Environment.GetEnvironmentVariable(RegenerateVar) == "1")
            {
                if (Directory.Exists(golden)) Directory.Delete(golden, true);
                CopyTree(fresh, golden);
                Assert.Fail($"Regenerated {golden}. Review the diff, commit it, and rerun without {RegenerateVar}.");
            }

            Assert.Null(GoldenDiff.First(golden, fresh));
        }
        finally { try { Directory.Delete(fresh, true); } catch (Exception) { /* best-effort */ } }
    }

    [Fact]
    public async Task Poe2_TwoBuilds_AreIdentical()   // Review Focus 1
    {
        var a = Path.Combine(Path.GetTempPath(), $"canonical a {Guid.NewGuid():N}");
        var b = Path.Combine(Path.GetTempPath(), $"canonical b {Guid.NewGuid():N}");
        try
        {
            await CanonicalModBuilder.BuildAllAsync("poe2", Poe2Scenario.Steps, a);
            await CanonicalModBuilder.BuildAllAsync("poe2", Poe2Scenario.Steps, b);
            Assert.Null(GoldenDiff.First(a, b));
        }
        finally { foreach (var d in new[] { a, b }) try { Directory.Delete(d, true); } catch (Exception) { /* best-effort */ } }
    }

    [Fact]
    public void Poe2_PackCarriesTheCheckedInProject()   // Review Focus 5
    {
        var golden = CanonicalModBuilder.GoldenDir("poe2");
        using var zip = System.IO.Compression.ZipFile.OpenRead(Path.Combine(golden, "canonical_mod.dialogpack"));
        using var reader = new StreamReader(zip.GetEntry("project.dialogproject")!.Open());
        Assert.Equal(File.ReadAllText(Path.Combine(golden, "canonical_mod.dialogproject")), reader.ReadToEnd());
    }

    private static void CopyTree(string from, string to)
    {
        foreach (var f in Directory.EnumerateFiles(from, "*", SearchOption.AllDirectories))
        {
            var d = Path.Combine(to, Path.GetRelativePath(from, f));
            Directory.CreateDirectory(Path.GetDirectoryName(d)!);
            File.Copy(f, d, true);
        }
    }
}
```

`GoldenDiff.First(a, b)` (new file `GoldenDiff.cs`): compares the file lists; for `.dialogpack` compares zip entry names and per-entry bytes (ignoring zip metadata); for text files reports the first differing line as `"<relative path>:<line>: expected … / actual …"`; returns `null` when equal.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalModGoldenTests"`
Expected: build FAILS (`BuildAllAsync`, `GoldenDir`, `GoldenDiff` missing).

- [ ] **Step 3: Implement `BuildAllAsync`, `GoldenDir`, `GoldenDiff`, README rendering**

`BuildAllAsync` = `BuildAsync` + `await VoPackExporter.ExportAsync(projectPath, Path.Combine(outDir, "canonical_mod.dialogpack"))` + `File.WriteAllText(Path.Combine(outDir, "README.md"), RenderReadme(game, steps))`.

`RenderReadme` writes a fixed header (what the mod is, that it is generated, how to regenerate, that all content is invented, how to install it on a copy of `Fixtures/Canonical/<game>`) followed by one table row per step: `| \`{Tag}\` | {Description} |`. Use `\n` line endings explicitly so the output is identical on every OS.

- [ ] **Step 4: Generate, review, and check in**

```bash
DIALOGEDITOR_REGENERATE_CANONICAL_MOD=1 dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Poe2_CheckedInMod_MatchesAFreshBuild"
```

Expected: FAIL with "Regenerated …". Read the generated `README.md` and `canonical_mod.dialogproject` in full: every step's edit visible, no absolute paths, no machine names, nothing from game data. Then:

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalModGoldenTests"
```

Expected: PASS (3 tests).

- [ ] **Step 5: Refresh the canonical README's Known gaps**

In `DialogEditor.Tests/Fixtures/Canonical/README.md`, replace the *Known gaps* list: #139 and #141 are fixed (PRs #156, #157), #129 fixed (#154); say that edited-file coverage now lives in `Fixtures/CanonicalMod/` (#138).

- [ ] **Step 6: Commit**

```bash
git add -A DialogEditor.Tests
git commit -m "test: generated PoE2 canonical mod with golden checks (#138)"
```

---

### Task 8: Delivery paths — dialog-patcher, Patch Manager, Test Patch

**Files:**
- Create: `DialogEditor.Tests/CanonicalMod/DeliveryPaths.cs`
- Create: `DialogEditor.Tests/CanonicalMod/CanonicalModApplyTests.cs`

**Interfaces:**
- Produces: `static class DeliveryPaths` — `static string FreshInstall(string game)` (temp copy, space in path); `static void ViaPatcher(string gameRoot, string pack)`; `static Task ViaPatchManagerAsync(string gameRoot, string pack)`; `static Task ViaTestPatchAsync(string gameRoot, string projectPath)`; `static SortedDictionary<string, string> GameFiles(string gameRoot)` (relative path → SHA-256, excluding the patcher's backup folder and the editor's backup/journal folders).

- [ ] **Step 1: Write the failing tests**

```csharp
[Trait("Category", "Canonical")]
public class CanonicalModApplyTests
{
    private static string Pack    => Path.Combine(CanonicalModBuilder.GoldenDir("poe2"), "canonical_mod.dialogpack");
    private static string Project => Path.Combine(CanonicalModBuilder.GoldenDir("poe2"), "canonical_mod.dialogproject");

    public static TheoryData<string> Paths => ["patcher", "patchmanager", "testpatch"];

    private static async Task<string> ApplyAsync(string path)
    {
        var root = DeliveryPaths.FreshInstall("poe2");
        switch (path)
        {
            case "patcher":      DeliveryPaths.ViaPatcher(root, Pack); break;
            case "patchmanager": await DeliveryPaths.ViaPatchManagerAsync(root, Pack); break;
            default:             await DeliveryPaths.ViaTestPatchAsync(root, Project); break;
        }
        return root;
    }

    [Theory]
    [MemberData(nameof(Paths))]
    public async Task EveryStep_HoldsAfter(string path)
    {
        var root = await ApplyAsync(path);
        var view = ModView.FromGame(new Poe2GameDataProvider(root), Poe2Scenario.Conversations, ["en", "de"]);
        var failures = new List<string>();
        foreach (var step in Poe2Scenario.Steps)
            try { step.Expect(view); } catch (Exception ex) { failures.Add($"{step.Tag}: {ex.Message}"); }
        Assert.Empty(failures);
    }

    [Fact]
    public async Task AllThreePaths_WriteIdenticalGameFiles()
    {
        var files = new List<SortedDictionary<string, string>>();
        foreach (var path in new[] { "patcher", "patchmanager", "testpatch" })
            files.Add(DeliveryPaths.GameFiles(await ApplyAsync(path)));
        Assert.Equal(files[0], files[1]);
        Assert.Equal(files[0], files[2]);
    }

    [Fact]
    public async Task ApplyingTwice_GivesTheSameFiles()   // Review Focus 2
    {
        var root = DeliveryPaths.FreshInstall("poe2");
        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);
        var once = DeliveryPaths.GameFiles(root);
        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);
        Assert.Equal(once, DeliveryPaths.GameFiles(root));
    }

    [Fact]
    public async Task MissingModLanguage_IsSkipped_AndEnglishStillApplies()   // Review Focus 4
    {
        var root = DeliveryPaths.FreshInstall("poe2");
        Directory.Delete(Path.Combine(root, "PillarsOfEternityII_Data", "exported", "localized", "de"), true);
        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);
        var view = ModView.FromGame(new Poe2GameDataProvider(root), Poe2Scenario.Conversations, ["en"]);
        Assert.Equal("A new closing line, written by the canonical mod.", view.Text(Poe2Scenario.Edited, "en", 11)!.DefaultText);
        Assert.False(Directory.Exists(Path.Combine(root, "PillarsOfEternityII_Data", "exported", "localized", "de")));
    }
}
```

Each test must delete its fresh install in a `finally` (wrap with a small `using var install = new TempInstall("poe2")` helper in `DeliveryPaths.cs` if cleaner).

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalModApplyTests"`
Expected: build FAILS (`DeliveryPaths` missing).

- [ ] **Step 3: Implement `DeliveryPaths`**

- `ViaPatcher`: `var code = PatcherCommand.Run([gameRoot, pack], new StringWriter(), err = new StringWriter()); Assert.Equal(0, code);` — check `PatcherCommandTests` for the exact argument order and any required flags.
- `ViaPatchManagerAsync`: `new PatchManagerViewModel(new StubFolderPicker(), new StubFilePicker(multiResult: [pack])) { GameFolder = gameRoot }`, `await vm.AddEntriesCommand.ExecuteAsync(null)`, `await vm.ApplyCommand.ExecuteAsync(null)`; assert no entry has `LoadError` and the status is a success (`PatchManagerViewModelInstallTests` shows the pattern).
- `ViaTestPatchAsync`: a `CanonicalModSession`-style VM over `gameRoot` (reuse `CanonicalModSession`'s reflection: `LoadDirectory(gameRoot)`, `LoadProjectAsync(copyOfProject, false)` where the project and its `_vo/` are first copied next to each other in a temp dir), then `await vm.TestPatchCommand.ExecuteAsync(null)`. Add a `CanonicalModSession.OpenExistingAsync(string gameRoot, string projectPath)` overload rather than duplicating reflection.
- `GameFiles`: hash every file under `PillarsOfEternityII_Data`, keyed by relative path with `/` separators; exclude the `PillarsDialogPatcher` folder and any editor backup/journal folder (read `PatcherBackupStore` and the Test Patch backup code for their names).

- [ ] **Step 4: Run tests to verify they pass**

Same command. Expected: PASS. A mismatch between paths is the most likely real bug this PR finds: diff the two file sets, identify the field, file an issue (search first), and skip **only** the identical-files assertion for that pair with `Skip = "#NNN"`.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod
git commit -m "test: apply the canonical mod through patcher, Patch Manager and Test Patch (#138)"
```

---

### Task 9: Raw-file checks — untouched data, #116 properties, carried fields, VO placement, reversibility

**Files:**
- Create: `DialogEditor.Tests/CanonicalMod/CanonicalModRawChecks.cs` (tests)

- [ ] **Step 1: Write the failing tests**

Fixture field names (read from `Fixtures/Canonical/poe2/…/canonical.conversationbundle`): bank 5 has `ChildNodeIDs` `[8, 9]` and `BankNodePlayType` `2`; trigger 6 has `ConversationGuid` and `StartNodeID`; scripts are `{ "Data": …, "Conditional": … }`; the conversation has `ConversationScriptNode` and a node with `NodeID` −200.

```csharp
using System.Text.Json.Nodes;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Checks on the written files themselves, for what the editor model does not carry
/// (issue 138, spec §3): untouched data, #116 property sets, bank/trigger fields, #115
/// per-script conditionals, VO placement, and that both undo routes restore the fixture.
[Trait("Category", "Canonical")]
public class CanonicalModRawChecks
{
    private static string Pack    => Path.Combine(CanonicalModBuilder.GoldenDir("poe2"), "canonical_mod.dialogpack");
    private static string Project => Path.Combine(CanonicalModBuilder.GoldenDir("poe2"), "canonical_mod.dialogproject");
    private static string Fixture => CanonicalFixture.SourceDir("poe2");

    private static string BundlePath(string root, string conv) =>
        Path.Combine(root, "PillarsOfEternityII_Data", "exported", "design", "conversations", conv + ".conversationbundle");
    private static JsonObject Conversation(string root, string conv) =>
        JsonNode.Parse(File.ReadAllText(BundlePath(root, conv)))!["Conversations"]![0]!.AsObject();
    private static JsonObject NodeOf(JsonObject conv, int id) =>
        conv["Nodes"]!.AsArray().Single(n => n!["NodeID"]!.GetValue<int>() == id)!.AsObject();
    private static string TypeOf(JsonNode node) => node["$type"]!.GetValue<string>().Split(',')[0];

    private static async Task<string> InstalledAsync()
    {
        var root = DeliveryPaths.FreshInstall("poe2");
        await DeliveryPaths.ViaPatchManagerAsync(root, Pack);
        return root;
    }

    [Fact]
    public async Task NodeMinus200_AndUntouchedFiles_AreUnchanged()
    {
        var root   = await InstalledAsync();
        var before = DeliveryPaths.GameFiles(DeliveryPaths.FreshInstall("poe2"));
        var after  = DeliveryPaths.GameFiles(root);
        static bool ModOwned(string rel) =>
            rel.Contains("/canonical.") || rel.Contains("/canonical_mod_new.") || rel.Contains("/narrator/canonical_0011");
        foreach (var (rel, hash) in before.Where(kv => !ModOwned(kv.Key)))
            Assert.True(after.TryGetValue(rel, out var h) && h == hash, $"{rel} changed");

        var fixture = Conversation(Fixture, "canonical");
        var written = Conversation(root, "canonical");
        Assert.True(JsonNode.DeepEquals(fixture["ConversationScriptNode"], written["ConversationScriptNode"]));
        Assert.True(JsonNode.DeepEquals(NodeOf(fixture, -200), NodeOf(written, -200)));
    }

    [Fact]
    public async Task EveryWrittenNode_CarriesOnlyPropertiesItsTypeHasInTheFixture()   // #116
    {
        var oracle = Conversation(Fixture, "canonical")["Nodes"]!.AsArray()
            .GroupBy(n => TypeOf(n!))
            .ToDictionary(g => g.Key, g => g.SelectMany(n => n!.AsObject().Select(kv => kv.Key)).ToHashSet());
        var root = await InstalledAsync();
        var problems = new List<string>();
        foreach (var conv in Poe2Scenario.Conversations)
            foreach (var node in Conversation(root, conv)["Nodes"]!.AsArray())
            {
                var type = TypeOf(node!);
                if (!oracle.TryGetValue(type, out var allowed)) { problems.Add($"{conv}: {type} not in the fixture"); continue; }
                foreach (var key in node!.AsObject().Select(kv => kv.Key).Where(k => !allowed.Contains(k)))
                    problems.Add($"{conv} node {node["NodeID"]}: {type} has '{key}'");
            }
        Assert.Empty(problems);
    }

    [Fact]
    public async Task BankChildren_PlayType_AndTriggerTarget_Survive()
    {
        var root    = await InstalledAsync();
        var fixture = Conversation(Fixture, "canonical");
        var written = Conversation(root, "canonical");
        Assert.Equal([8], NodeOf(written, 5)["ChildNodeIDs"]!.AsArray().Select(n => n!.GetValue<int>()));   // 9 was deleted
        Assert.Equal(NodeOf(fixture, 5)["BankNodePlayType"]!.GetValue<int>(), NodeOf(written, 5)["BankNodePlayType"]!.GetValue<int>());
        foreach (var key in new[] { "ConversationGuid", "StartNodeID" })
            Assert.True(JsonNode.DeepEquals(NodeOf(fixture, 6)[key], NodeOf(written, 6)[key]), key);
    }

    [Fact]
    public async Task Node4_ExistingScript_KeepsItsConditional()   // #115
    {
        var root     = await InstalledAsync();
        var original = NodeOf(Conversation(Fixture, "canonical"), 4)["OnEnterScripts"]![0]!;
        var kept     = NodeOf(Conversation(root, "canonical"), 4)["OnEnterScripts"]!.AsArray()
            .Single(sc => JsonNode.DeepEquals(sc!["Data"], original["Data"]));
        Assert.True(JsonNode.DeepEquals(original["Conditional"], kept!["Conditional"]));
    }

    [Fact]
    public async Task ImportedVo_IsWhereTheGameLooks_AndTheAliasResolves()
    {
        var root   = await InstalledAsync();
        var voices = Path.Combine(root, "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Assert.Equal(File.ReadAllBytes(CanonicalModInputs.Poe2Wem), File.ReadAllBytes(Path.Combine(voices, "narrator", "canonical_0011.wem")));
        Assert.True(File.Exists(Path.Combine(voices, "narrator", "canonical_0007.wem")));   // node 12's alias target
    }

    [Fact]
    public async Task RemoveAllMods_RestoresTheFixtureExactly()
    {
        var root = await InstalledAsync();
        await DeliveryPaths.RemoveAllModsAsync(root);
        Assert.Equal(DeliveryPaths.GameFiles(DeliveryPaths.FreshInstall("poe2")), DeliveryPaths.GameFiles(root));
    }

    [Fact]
    public async Task TestPatchRestore_RestoresTheFixtureExactly()
    {
        var root = DeliveryPaths.FreshInstall("poe2");
        await DeliveryPaths.ViaTestPatchAndRestoreAsync(root, Project);
        Assert.Equal(DeliveryPaths.GameFiles(DeliveryPaths.FreshInstall("poe2")), DeliveryPaths.GameFiles(root));
    }
}
```

Add to `DeliveryPaths` (Task 8's file): `static Task RemoveAllModsAsync(string gameRoot)` — a `PatchManagerViewModel` with `GameFolder = gameRoot` and `ConfirmRemoveAllMods = () => Task.FromResult(true)`, then `await vm.RemoveAllModsCommand.ExecuteAsync(null)`; and `static Task ViaTestPatchAndRestoreAsync(string gameRoot, string projectPath)` — the Task 8 Test Patch session, then `RestoreConversationCommand` on the same VM (answer any confirmation hook it exposes with yes; `MainWindowViewModelTestPatchTests` around line 153 shows the call). Track every `FreshInstall` root and delete it in the test class's `Dispose`. The exact `.wem` name the importer writes for node 11 (`canonical_0011.wem`) follows `<conversation>_<id:0000>.wem` (`ConversationViewModel.IsNodeIdReservedByVo`); confirm against the generated `_vo/` from Task 7.

- [ ] **Step 2: Run to verify they fail**

Expected: build FAILS (`RemoveAllModsAsync`, `ViaTestPatchAndRestoreAsync` missing). Run:
 `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalModRawChecks"`.

- [ ] **Step 3: Implement until green; file-and-skip real bugs**

Expected failures that are product bugs (not harness bugs): file with `gh issue create` after `gh issue list --state all --search "<keywords>"`, then `Skip = "#NNN"` on that one test.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Tests/CanonicalMod
git commit -m "test: canonical mod raw-file, VO and reversibility checks (#138)"
```

---

### Task 10: Full verification and PR

- [ ] **Step 1: Full headless suite (with installs if available)**

```bash
dotnet build && dotnet test --no-build --filter "Category!=Gui"
```

Expected: all pass; skipped tests are only the canonical-mod checks carrying `#NNN`, plus `GameData` tests when no install is configured.

- [ ] **Step 2: Hygiene checks**

```bash
git grep -n "C:\\\\Users\|/c/Users" -- DialogEditor.Tests/Fixtures/CanonicalMod docs
```

Expected: no matches (no local paths in checked-in output).

- [ ] **Step 3: Open the PR** (`Closes` nothing — #138 stays open for PR 2 and PR 3; reference it as `Part of #138`), list every filed bug and its skipped check, then enable Auto-fix on the PR.
