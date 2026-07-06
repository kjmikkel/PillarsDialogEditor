# Dialog Text Tag Reference Window Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** A game-aware, searchable **Help ▸ Text Tag Reference…** window documenting the dialog text tag vocabulary (substitution tokens, rich-text markup, writing conventions), fed from an embedded `tags.json`.

**Architecture:** Mirrors the condition-reference stack: `tags.json` embedded in `DialogEditor.ViewModels` → `TagCatalogue` (singleton, same shape as `ConditionCatalogue`) → `TagReferenceViewModel` (game filter + search, pure logic) → `TagReferenceWindow` (non-modal, cached instance, Help menu). The catalogue deliberately lives in the ViewModels layer so the future autocomplete/validation gap can consume it.

**Tech Stack:** C# / .NET 8, Avalonia 11, CommunityToolkit.Mvvm, xUnit, System.Text.Json.

**Spec:** `docs/superpowers/specs/2026-07-05-tag-reference-window-design.md`

## Global Constraints

- Strict red/green TDD — failing test before implementation (CLAUDE.md).
- No user-visible text hard-coded in XAML or C#; all UI chrome strings go in `DialogEditor.Avalonia\Resources\Strings.axaml`. Tag entry content (names, descriptions, examples) is data from `tags.json`, English-only — same policy as condition descriptions.
- Every interactive control carries `ToolTip.Tip`; controls must be UIA-discoverable (`AutomationProperties.Name` where the control has no label).
- Every new `<Window>` has `Icon="avares://DialogEditor.Avalonia/Assets/app.ico"`.
- Every caught exception is logged via `AppLog.Error/Warn` except `OperationCanceledException` (swallowed silently); no bare `catch { }`.
- `DialogEditor.Tests` runs serially — do not change test parallelisation.
- Token vocabulary is engine-verified (spec § Authoritative sources); do not add, rename, or "fix" entries beyond what this plan specifies.

---

### Task 1: `TagEntry`, `TagCatalogue`, and `tags.json` (TDD)

**Files:**
- Create: `DialogEditor.ViewModels\Services\TagCatalogue.cs`
- Create: `DialogEditor.ViewModels\Resources\tags.json`
- Create: `data\tags.json` (verbatim copy of the above)
- Modify: `DialogEditor.ViewModels\DialogEditor.ViewModels.csproj` (add `<EmbeddedResource Include="Resources\tags.json" />` next to the existing `conditions.json` entry)
- Modify: `data\tags-poe1.md`, `data\tags-poe2.md` (one-line cross-reference header)
- Test: `DialogEditor.Tests\Services\TagCatalogueTests.cs`

**Interfaces:**
- Consumes: nothing from other tasks. Mirrors `ConditionCatalogue` (`DialogEditor.ViewModels\Services\ConditionCatalogue.cs`) — read it for the house pattern.
- Produces: `TagEntry` record (`Name, Kind, Games, Category, Description, Example, Count, Notes`) and `TagCatalogue` with `static TagCatalogue Instance`, `static void Configure(TagCatalogue)`, `IReadOnlyList<TagEntry> All`, `IReadOnlyList<TagEntry> ForGame(string gameId)`. Task 2's viewmodel depends on exactly these names.

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests\Services\TagCatalogueTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// The dialog-text tag vocabulary (tags.json) is engine-verified: token lists
/// come from both games' decompiled Conversation.cs (+ PoE2 ShipDuelManager.cs),
/// not from scanning shipped text. Count == 0 on a Token means engine-supported
/// but unused in shipped dialog. Lowercase variants ([player race]) are notes on
/// the base token, never separate entries.
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public class TagCatalogueTests
{
    private static TagCatalogue Cat => TagCatalogue.Instance;

    [Fact]
    public void LoadEmbedded_ContainsKnownEntries()
    {
        Assert.NotEmpty(Cat.All);

        var playerName = Cat.All.Single(e => e.Name == "[Player Name]");
        Assert.Equal("Token", playerName.Kind);
        Assert.Contains("poe1", playerName.Games);
        Assert.Contains("poe2", playerName.Games);

        var shipDuelPlayer = Cat.All.Single(e => e.Name == "[ShipDuel_Player]");
        Assert.Equal(new[] { "poe2" }, shipDuelPlayer.Games);
        Assert.Equal(0, shipDuelPlayer.Count);

        var ispeech = Cat.All.Single(e => e.Name == "<ispeech>…</ispeech>");
        Assert.Equal("Markup", ispeech.Kind);
        Assert.Equal(new[] { "poe2" }, ispeech.Games);
    }

    [Fact]
    public void ForGame_FiltersByGame()
    {
        var poe1 = Cat.ForGame("poe1");
        var poe2 = Cat.ForGame("poe2");

        Assert.DoesNotContain(poe1, e => e.Name.StartsWith("[ShipDuel_"));
        Assert.DoesNotContain(poe1, e => e.Kind == "Markup");
        Assert.Contains(poe2, e => e.Name.StartsWith("[ShipDuel_"));
        Assert.Contains(poe2, e => e.Kind == "Markup");
        Assert.Contains(poe1, e => e.Name == "[Player Name]");
    }

    [Fact]
    public void Entries_AreStructurallySound()
    {
        foreach (var e in Cat.All)
        {
            Assert.False(string.IsNullOrWhiteSpace(e.Name));
            Assert.Contains(e.Kind, new[] { "Token", "Markup", "Convention" });
            Assert.False(string.IsNullOrWhiteSpace(e.Category));
            Assert.False(string.IsNullOrWhiteSpace(e.Description));
            Assert.NotEmpty(e.Games);
            Assert.All(e.Games, g => Assert.Contains(g, new[] { "poe1", "poe2" }));
            Assert.True(e.Count >= 0);
        }
    }

    [Fact]
    public void LowercaseVariants_AreNotesNotEntries()
    {
        // PoE2's [player race]-style lowercase pairs are documented in Notes on
        // the base token; they must never appear as separate entries.
        Assert.DoesNotContain(Cat.All, e => e.Name.StartsWith("[player "));

        var race = Cat.All.Single(e => e.Name == "[Player Race]");
        Assert.NotNull(race.Notes);
        Assert.Contains("[player race]", race.Notes!);
    }
}
```

- [ ] **Step 2: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TagCatalogueTests"`
Expected: FAIL to compile — `'TagCatalogue' could not be found`.

- [ ] **Step 3: Implement `TagCatalogue` and author `tags.json`**

Create `DialogEditor.ViewModels\Services\TagCatalogue.cs`:

```csharp
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace DialogEditor.ViewModels.Services;

/// One entry of the dialog-text tag vocabulary (tags.json).
/// Kind: "Token" (engine-substituted), "Markup" (Unity rich text), or
/// "Convention" (rendered literally; meaning is for the player).
/// Count is occurrences in shipped dialog text (2026-07-05 stringtable scan);
/// 0 on a Token means engine-supported but unused in shipped dialog.
public record TagEntry(
    string Name,
    string Kind,
    IReadOnlyList<string> Games,
    string Category,
    string Description,
    string? Example = null,
    int Count = 0,
    string? Notes = null);

/// The dialog-text tag vocabulary, engine-verified against both games'
/// decompiled Conversation.cs (+ PoE2 ShipDuelManager.cs) — see
/// docs/superpowers/specs/2026-07-05-tag-reference-window-design.md.
/// Mirrors ConditionCatalogue so the future token autocomplete/validation
/// feature can consume the same instance.
public sealed class TagCatalogue
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        DefaultIgnoreCondition      = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly IReadOnlyList<TagEntry> _entries;

    private TagCatalogue(IReadOnlyList<TagEntry> entries)
        => _entries = entries;

    public IReadOnlyList<TagEntry> All => _entries;

    public IReadOnlyList<TagEntry> ForGame(string gameId)
        => _entries
            .Where(e => e.Games.Any(g => string.Equals(g, gameId, StringComparison.OrdinalIgnoreCase)))
            .ToList();

    /// Load from the embedded tags.json resource shipped with the assembly.
    public static TagCatalogue LoadEmbedded()
    {
        var assembly     = Assembly.GetExecutingAssembly();
        var resourceName = assembly.GetManifestResourceNames()
            .FirstOrDefault(n => n.EndsWith("tags.json", StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("Embedded tags.json resource not found.");

        using var stream = assembly.GetManifestResourceStream(resourceName)!;
        var entries = JsonSerializer.Deserialize<List<TagEntry>>(stream, Options)
            ?? throw new InvalidOperationException("Failed to deserialise tags.json.");
        return new TagCatalogue(entries);
    }

    // ── Static instance (lazy-loaded once per process, matches ConditionCatalogue) ──
    private static TagCatalogue? _instance;

    public static TagCatalogue Instance
        => _instance ??= LoadEmbedded();

    public static void Configure(TagCatalogue catalogue)
        => _instance = catalogue;
}
```

Create `DialogEditor.ViewModels\Resources\tags.json` with exactly this content (engine-verified vocabulary — do not edit entries):

```json
[
  { "name": "[Player Name]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Player",
    "description": "The Watcher's name.",
    "example": "\"I'm [Player Name], captain of [Player Ship].\"", "count": 456 },
  { "name": "[Player Race]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Player",
    "description": "The Watcher's race (e.g. \"elf\").",
    "example": "What is this, [Player Race]-creature?", "count": 91,
    "notes": "Deadfire also replaces the lowercase form [player race] with the lower-cased value for mid-sentence use; PoE1 has no lowercase pairs — matching is exact-case." },
  { "name": "[Player Subrace]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Player",
    "description": "The Watcher's subrace (e.g. \"wood elf\").", "count": 1,
    "notes": "Deadfire also replaces the lowercase form [player subrace]; PoE1 does not." },
  { "name": "[Player Class]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Player",
    "description": "The Watcher's class (gendered form in PoE1).",
    "example": "\"This one's the softest [player class] in Deadfire.\"", "count": 1,
    "notes": "Deadfire also replaces the lowercase form [player class]; PoE1 does not. The single shipped use is the lowercase variant." },
  { "name": "[Player Culture]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Player",
    "description": "The Watcher's culture (e.g. \"Aedyr\").",
    "example": "\"Then you will also share my name when you take this back to [Player Culture]?\"", "count": 7,
    "notes": "Deadfire also replaces the lowercase form [player culture]; PoE1 does not." },
  { "name": "[Player Background]", "kind": "Token", "games": ["poe2"], "category": "Player",
    "description": "The Watcher's background (e.g. \"aristocrat\").", "count": 0,
    "notes": "Deadfire also replaces the lowercase form [player background]." },
  { "name": "[Player Deity]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Player",
    "description": "The priest player's chosen deity.", "count": 3 },
  { "name": "[Player Paladin Order]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Player",
    "description": "The paladin player's order (gendered form).", "count": 0 },
  { "name": "[Player Ship]", "kind": "Token", "games": ["poe2"], "category": "Player",
    "description": "The player's ship name.",
    "example": "\"I'm [Player Name], captain of [Player Ship].\"", "count": 188 },
  { "name": "[Player Animal Companion]", "kind": "Token", "games": ["poe2"], "category": "Player",
    "description": "The ranger player's animal companion's name.",
    "example": "\"[Player Animal Companion] and I don't mind trudging through the hatchery.\"", "count": 33 },

  { "name": "[Specified n]", "kind": "Token", "games": ["poe1", "poe2"], "category": "CharacterReference",
    "description": "The character bound to the conversation's Specified Speaker slot n (script-selected, e.g. a specific companion).",
    "example": "[Specified 0] takes a firm grip on the rope and descends.", "count": 2091,
    "notes": "Shipped text uses n = 0–5 (Deadfire) and 0–1 (PoE1); any bound slot works." },
  { "name": "[SkillCheck n]", "kind": "Token", "games": ["poe1", "poe2"], "category": "CharacterReference",
    "description": "The party member selected by skill-check slot n (highest relevant skill).",
    "example": "Partway through, [SkillCheck 0] starts to lag behind.", "count": 238,
    "notes": "Shipped text uses n = 0–1 (Deadfire) and 0–3 (PoE1)." },
  { "name": "[Slot n]", "kind": "Token", "games": ["poe1", "poe2"], "category": "CharacterReference",
    "description": "The character in party slot n (0–5). PoE1 party banter uses this heavily; the Deadfire engine supports it equally.", "count": 717 },

  { "name": "[ShipDuel_Player]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The player captain's name during a ship duel.", "count": 0 },
  { "name": "[ShipDuel_PlayerShip]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The player ship's name during a ship duel.", "count": 22 },
  { "name": "[ShipDuel_Opponent]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The enemy captain's name during a ship duel.", "count": 4 },
  { "name": "[ShipDuel_OpponentShip]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The enemy ship's name during a ship duel.",
    "example": "The crew of [ShipDuel_OpponentShip] spring into action.", "count": 26 },
  { "name": "[ShipDuel_SurrenderCost]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The cost the opponent demands for surrender.",
    "example": "For your surrender, [ShipDuel_Opponent] demands: [ShipDuel_SurrenderCost]", "count": 2 },
  { "name": "[ShipDuel_CloseToBoardCost]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The cost/distance to close for boarding.", "count": 2 },
  { "name": "[ShipDuel_PlayerFullSailDist]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The distance the player ship moves at full sail.", "count": 1 },
  { "name": "[ShipDuel_PlayerHalfSailDist]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The distance the player ship moves at half sail.", "count": 1 },
  { "name": "[ShipDuel_FleeChance]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The chance (percentage) that fleeing succeeds.", "count": 0 },
  { "name": "[ShipDuel_BraceChance]", "kind": "Token", "games": ["poe2"], "category": "ShipDuel",
    "description": "The chance (percentage) that bracing prevents injuries.", "count": 1 },

  { "name": "[Interaction Ability]", "kind": "Token", "games": ["poe2"], "category": "Other",
    "description": "Display name of the ability granted by a scripted interaction.", "count": 0 },
  { "name": "[NPCBacker]", "kind": "Token", "games": ["poe1", "poe2"], "category": "Other",
    "description": "Backer NPC description text.", "count": 0 },
  { "name": "[God_Boon]", "kind": "Token", "games": ["poe2"], "category": "Other",
    "description": "Name of the god's boon being granted.", "count": 0 },

  { "name": "<i>…</i>", "kind": "Markup", "games": ["poe2"], "category": "Markup",
    "description": "Italics — narration emphasis, foreign words, thoughts. Pairs must be closed.", "count": 820 },
  { "name": "<ispeech>…</ispeech>", "kind": "Markup", "games": ["poe2"], "category": "Markup",
    "description": "\"Inner speech\" styling — telepathy, soul-voices, gods.",
    "example": "<ispeech>Show. Them.</ispeech>", "count": 490 },
  { "name": "<xg>…</xg>", "kind": "Markup", "games": ["poe2"], "category": "Markup",
    "description": "Dialect styling for single words.",
    "example": "I'd <xg>nae</xg> be surprised.", "count": 17 },
  { "name": "<color=\"…\">…</color>", "kind": "Markup", "games": ["poe2"], "category": "Markup",
    "description": "Text colour.",
    "example": "<color=\"red\">(Required - Incomplete)</color>", "count": 7 },
  { "name": "<link=\"glossary://…\">…</link>", "kind": "Markup", "games": ["poe2"], "category": "Markup",
    "description": "Hover link to a glossary entry (ship actions, deities).",
    "example": "We're <link=\"glossary://GlossaryEntry_Action_LieAhull\">lying ahull</link>.", "count": 30 },
  { "name": "<link=\"stringtooltip://…\">…</link>", "kind": "Markup", "games": ["poe2"], "category": "Markup",
    "description": "Hover tooltip from a tooltip stringtable (untranslated speeches, cyclopedia notes).", "count": 10 },
  { "name": "<link=\"neutralvalue://…\">…</link>", "kind": "Markup", "games": ["poe2"], "category": "Markup",
    "description": "Hover translation of an in-world-language line; the text before the colon names the language.",
    "example": "[Vailian] <link=\"neutralvalue://Vailian: Do you speak Vailian, sir?\">\"Perla Vailian, fentre?\"</link>", "count": 80,
    "notes": "A few shipped lines are missing the closing quote on the attribute — parse leniently." },
  { "name": "<sprite=\"Inline\" name=\"…\" tint=1>", "kind": "Markup", "games": ["poe2"], "category": "Markup",
    "description": "Inline icon (ship-combat action buttons); self-closing.", "count": 25 },

  { "name": "Stage directions", "kind": "Convention", "games": ["poe1", "poe2"], "category": "Convention",
    "description": "A bracketed action in a player response is a non-spoken option, rendered literally: [Say nothing.], [Attack], [Leave]. It may stand alone or prefix speech. Free text, not a fixed vocabulary (~1,300 distinct values in shipped data).",
    "example": "[Lie] \"He kept the koīki for himself and paid for my silence.\"", "count": 0 },
  { "name": "Language markers", "kind": "Convention", "games": ["poe2"], "category": "Convention",
    "description": "A leading bracket names the in-world language of the quoted line, usually paired with a neutralvalue:// hover translation. Seen for Vailian, Huana, Rauataian, Engwithan, Ixamitl, Eld Aedyran, Ordhjóma, Lembur.",
    "example": "[Vailian] \"Perla Vailian, fentre?\"", "count": 0 },
  { "name": "VO / chatter annotations", "kind": "Convention", "games": ["poe1"], "category": "Convention",
    "description": "PoE1 companion chatter describes non-verbal audio in brackets — subtitle placeholders for barks, not dialog options.",
    "example": "[Pained grunt]", "count": 0 },
  { "name": "Skill & disposition labels", "kind": "Convention", "games": ["poe1", "poe2"], "category": "Convention",
    "description": "Option labels like [Diplomacy] or [Honest] shown in-game are built by the engine from the node's check data — they are never written in dialog text. Don't type them.", "count": 0 }
]
```

Add the embedded resource to `DialogEditor.ViewModels\DialogEditor.ViewModels.csproj`, next to the existing entries:

```xml
    <EmbeddedResource Include="Resources\tags.json" />
```

Copy the file to the repo-root data folder (source copy, like `data\conditions.json`):

```powershell
Copy-Item "DialogEditor.ViewModels\Resources\tags.json" "data\tags.json"
```

Add this line to the end of the intro paragraph block (before the first `---`) of **both** `data\tags-poe1.md` and `data\tags-poe2.md`:

```markdown
Machine-readable copy: `data/tags.json` (embedded in the app for the in-app
tag reference window; keep the two in sync when either changes).
```

- [ ] **Step 4: Run the new tests, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TagCatalogueTests"`
Expected: PASS (4).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1908 = 1904 existing + 4 new).

- [ ] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/Services/TagCatalogue.cs DialogEditor.ViewModels/Resources/tags.json DialogEditor.ViewModels/DialogEditor.ViewModels.csproj data/tags.json data/tags-poe1.md data/tags-poe2.md DialogEditor.Tests/Services/TagCatalogueTests.cs
git commit -m @'
feat(tags): embedded tag vocabulary catalogue (tags.json + TagCatalogue)

Engine-verified dialog-text tag vocabulary as an embedded resource,
mirroring the ConditionCatalogue pattern. Lives in the ViewModels layer
so future token autocomplete/validation can reuse it.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 2: `TagReferenceViewModel` (TDD)

**Files:**
- Create: `DialogEditor.ViewModels\ViewModels\TagReferenceViewModel.cs`
- Test: `DialogEditor.Tests\ViewModels\TagReferenceViewModelTests.cs`

**Interfaces:**
- Consumes: `TagCatalogue.Instance`, `TagEntry` (Task 1); `Loc.Get(string)` (`DialogEditor.ViewModels.Resources`).
- Produces: `TagGameFilter` enum, `TagGameOption` record, `TagRowViewModel`, `TagGroupViewModel`, and `TagReferenceViewModel(string activeGameId, TagCatalogue? catalogue = null)` with `GameOptions`, `SelectedGame`, `SearchText`, `TokenGroups`, `MarkupRows`, `ConventionRows`, `HasTokens`, `HasMarkup`, `HasConventions`, `HasNoResults`. Task 3's command and Task 4's XAML bind to exactly these names.

- [ ] **Step 1: Write the failing tests**

Create `DialogEditor.Tests\ViewModels\TagReferenceViewModelTests.cs`:

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

/// Game-aware, searchable view over TagCatalogue. StubStringProvider echoes
/// keys, so localised headers/badges assert against the resource key itself.
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public class TagReferenceViewModelTests
{
    public TagReferenceViewModelTests() => Loc.Configure(new StubStringProvider());

    private static TagReferenceViewModel MakeVm(string gameId = "") => new(gameId);

    private static IEnumerable<TagRowViewModel> AllTokenRows(TagReferenceViewModel vm)
        => vm.TokenGroups.SelectMany(g => g.Rows);

    [Fact]
    public void InitialGame_FollowsActiveGame_DefaultsToPoE2()
    {
        Assert.Equal(TagGameFilter.PoE1, MakeVm("poe1").SelectedGame.Value);
        Assert.Equal(TagGameFilter.PoE2, MakeVm("poe2").SelectedGame.Value);
        Assert.Equal(TagGameFilter.PoE2, MakeVm().SelectedGame.Value);
    }

    [Fact]
    public void PoE1_HidesShipDuelTokensAndMarkup()
    {
        var vm = MakeVm("poe1");

        Assert.DoesNotContain(vm.TokenGroups, g => g.Header == "TagCategory_ShipDuel");
        Assert.False(vm.HasMarkup);
        Assert.True(vm.HasTokens);
        Assert.True(vm.HasConventions);   // stage directions + VO annotations
    }

    [Fact]
    public void Both_ShowsUnionWithGameBadges()
    {
        var vm = MakeVm();
        vm.SelectedGame = vm.GameOptions.Single(o => o.Value == TagGameFilter.Both);

        Assert.Contains(vm.TokenGroups, g => g.Header == "TagCategory_ShipDuel");
        var playerName = AllTokenRows(vm).Single(r => r.Name == "[Player Name]");
        Assert.True(playerName.ShowBadge);
        Assert.Equal("TagRef_BadgePoE1 · TagRef_BadgePoE2", playerName.GamesBadge);
    }

    [Fact]
    public void Search_FiltersNameAndDescription_CaseInsensitive()
    {
        var vm = MakeVm("poe2");

        vm.SearchText = "ISPEECH";
        Assert.Empty(vm.TokenGroups);
        Assert.Equal("<ispeech>…</ispeech>", Assert.Single(vm.MarkupRows).Name);

        vm.SearchText = "watcher";   // matches token descriptions only
        Assert.Contains(AllTokenRows(vm), r => r.Name == "[Player Name]");
        Assert.False(vm.HasMarkup);

        vm.SearchText = "";
        Assert.True(vm.HasMarkup);
        Assert.True(vm.HasTokens);
    }

    [Fact]
    public void TokenGroups_AppearInFixedCategoryOrder()
    {
        var vm = MakeVm("poe2");
        Assert.Equal(
            new[] { "TagCategory_Player", "TagCategory_CharacterReference", "TagCategory_ShipDuel", "TagCategory_Other" },
            vm.TokenGroups.Select(g => g.Header).ToArray());
    }

    [Fact]
    public void NoResults_WhenSearchMatchesNothing()
    {
        var vm = MakeVm("poe2");
        vm.SearchText = "zzz_no_such_tag";
        Assert.True(vm.HasNoResults);
    }

    [Fact]
    public void EngineOnlyBadge_OnZeroCountTokensOnly()
    {
        var vm = MakeVm("poe2");
        Assert.True(AllTokenRows(vm).Single(r => r.Name == "[ShipDuel_Player]").IsEngineOnly);
        Assert.False(AllTokenRows(vm).Single(r => r.Name == "[Player Name]").IsEngineOnly);
        // Conventions have count 0 but are not "engine-only" tokens.
        Assert.DoesNotContain(vm.ConventionRows, r => r.IsEngineOnly);
    }
}
```

- [ ] **Step 2: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TagReferenceViewModelTests"`
Expected: FAIL to compile — `'TagReferenceViewModel' could not be found`.

- [ ] **Step 3: Implement the viewmodel**

Create `DialogEditor.ViewModels\ViewModels\TagReferenceViewModel.cs`:

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public enum TagGameFilter { PoE1, PoE2, Both }

/// A game-filter choice with its localised label (ComboBox item).
public sealed record TagGameOption(TagGameFilter Value, string Label)
{
    public override string ToString() => Label;
}

/// One tag entry prepared for display.
public sealed class TagRowViewModel(TagEntry entry, bool showBadge)
{
    public string  Name        => entry.Name;
    public string  Description => entry.Description;
    public string? Example     => entry.Example;
    public bool    HasExample  => !string.IsNullOrEmpty(entry.Example);
    public string? Notes       => entry.Notes;
    public bool    HasNotes    => !string.IsNullOrEmpty(entry.Notes);

    /// Engine-supported token that never occurs in shipped dialog (count 0).
    public bool   IsEngineOnly    => entry.Kind == "Token" && entry.Count == 0;
    public string EngineOnlyLabel => Loc.Get("TagRef_EngineOnly");

    /// Game badges are shown only in the "Both" filter, where entries mix.
    public bool   ShowBadge  { get; } = showBadge;
    public string GamesBadge => string.Join(" · ", entry.Games.Select(g =>
        Loc.Get(string.Equals(g, "poe1", StringComparison.OrdinalIgnoreCase)
            ? "TagRef_BadgePoE1" : "TagRef_BadgePoE2")));
}

/// A category of token rows with its localised header.
public sealed class TagGroupViewModel(string header, IReadOnlyList<TagRowViewModel> rows)
{
    public string Header { get; } = header;
    public IReadOnlyList<TagRowViewModel> Rows { get; } = rows;
}

/// Game-aware, searchable view over the tag vocabulary (TagCatalogue).
/// Pure logic — the window binds to the computed collections.
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public sealed partial class TagReferenceViewModel : ObservableObject
{
    // Fixed display order for token categories.
    private static readonly string[] CategoryOrder =
        ["Player", "CharacterReference", "ShipDuel", "Other"];

    private readonly TagCatalogue _catalogue;

    public IReadOnlyList<TagGameOption> GameOptions { get; }

    [ObservableProperty] private TagGameOption _selectedGame;
    [ObservableProperty] private string        _searchText = string.Empty;

    public TagReferenceViewModel(string activeGameId, TagCatalogue? catalogue = null)
    {
        _catalogue = catalogue ?? TagCatalogue.Instance;
        GameOptions =
        [
            new(TagGameFilter.PoE1, Loc.Get("TagRef_GamePoE1")),
            new(TagGameFilter.PoE2, Loc.Get("TagRef_GamePoE2")),
            new(TagGameFilter.Both, Loc.Get("TagRef_GameBoth")),
        ];
        // Follow the open game folder; PoE2 is the default vocabulary otherwise.
        var initial = string.Equals(activeGameId, "poe1", StringComparison.OrdinalIgnoreCase)
            ? TagGameFilter.PoE1 : TagGameFilter.PoE2;
        _selectedGame = GameOptions.First(o => o.Value == initial);
    }

    partial void OnSelectedGameChanged(TagGameOption value) => Refresh();
    partial void OnSearchTextChanged(string value)          => Refresh();

    private void Refresh()
    {
        OnPropertyChanged(nameof(TokenGroups));
        OnPropertyChanged(nameof(MarkupRows));
        OnPropertyChanged(nameof(ConventionRows));
        OnPropertyChanged(nameof(HasTokens));
        OnPropertyChanged(nameof(HasMarkup));
        OnPropertyChanged(nameof(HasConventions));
        OnPropertyChanged(nameof(HasNoResults));
    }

    private bool ShowBadges => SelectedGame.Value == TagGameFilter.Both;

    private bool MatchesGame(TagEntry e) => SelectedGame.Value switch
    {
        TagGameFilter.PoE1 => e.Games.Any(g => string.Equals(g, "poe1", StringComparison.OrdinalIgnoreCase)),
        TagGameFilter.PoE2 => e.Games.Any(g => string.Equals(g, "poe2", StringComparison.OrdinalIgnoreCase)),
        _                  => true,
    };

    private bool MatchesSearch(TagEntry e)
        => string.IsNullOrWhiteSpace(SearchText)
           || e.Name.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
           || e.Description.Contains(SearchText, StringComparison.OrdinalIgnoreCase);

    private IEnumerable<TagEntry> Visible(string kind)
        => _catalogue.All.Where(e => e.Kind == kind && MatchesGame(e) && MatchesSearch(e));

    public IReadOnlyList<TagGroupViewModel> TokenGroups =>
        CategoryOrder
            .Select(c => new TagGroupViewModel(
                Loc.Get($"TagCategory_{c}"),
                Visible("Token").Where(e => e.Category == c)
                    .Select(e => new TagRowViewModel(e, ShowBadges)).ToList()))
            .Where(g => g.Rows.Count > 0)
            .ToList();

    public IReadOnlyList<TagRowViewModel> MarkupRows =>
        Visible("Markup").Select(e => new TagRowViewModel(e, ShowBadges)).ToList();

    public IReadOnlyList<TagRowViewModel> ConventionRows =>
        Visible("Convention").Select(e => new TagRowViewModel(e, ShowBadges)).ToList();

    public bool HasTokens      => TokenGroups.Count > 0;
    public bool HasMarkup      => MarkupRows.Count > 0;
    public bool HasConventions => ConventionRows.Count > 0;
    public bool HasNoResults   => !HasTokens && !HasMarkup && !HasConventions;
}
```

- [ ] **Step 4: Run the new tests, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TagReferenceViewModelTests"`
Expected: PASS (7).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1915 = 1908 + 7 new).

- [ ] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/TagReferenceViewModel.cs DialogEditor.Tests/ViewModels/TagReferenceViewModelTests.cs
git commit -m @'
feat(tags): game-aware searchable TagReferenceViewModel

Filters TagCatalogue by game (auto from the open game folder, PoE2
default, Both with badges) and by name/description search; tokens
grouped by category in fixed order.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 3: `MainWindowViewModel` command + delegate (TDD)

**Files:**
- Modify: `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs` (next to the existing `ShowAbout` / `AboutCommand` block, ~line 1505)
- Test: `DialogEditor.Tests\ViewModels\MainWindowViewModelTagReferenceTests.cs`

**Interfaces:**
- Consumes: `TagReferenceViewModel(string activeGameId)` (Task 2); `_activeGameId` field (exists, `MainWindowViewModel.cs:59`).
- Produces: `public Action<TagReferenceViewModel>? ShowTagReference { get; set; }` and generated `TagReferenceCommand` (from `[RelayCommand] private void TagReference()`). Task 4's menu item and window wiring bind to these.

- [ ] **Step 1: Write the failing test**

Create `DialogEditor.Tests\ViewModels\MainWindowViewModelTagReferenceTests.cs`:

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

/// Help > Text Tag Reference: the command hands the UI layer a viewmodel
/// primed with the active game (PoE2 default when no game folder is open).
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public class MainWindowViewModelTagReferenceTests : IDisposable
{
    private readonly string _settingsPath;

    public MainWindowViewModelTagReferenceTests()
    {
        Loc.Configure(new StubStringProvider());
        _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_tagref_{Guid.NewGuid():N}.json");
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
    }

    [Fact]
    public void TagReferenceCommand_ShowsViewModel_DefaultingToPoE2()
    {
        var vm = new MainWindowViewModel(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker());
        TagReferenceViewModel? shown = null;
        vm.ShowTagReference = t => shown = t;

        vm.TagReferenceCommand.Execute(null);

        Assert.NotNull(shown);
        Assert.Equal(TagGameFilter.PoE2, shown!.SelectedGame.Value);   // no game folder open
    }
}
```

- [ ] **Step 2: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelTagReferenceTests"`
Expected: FAIL to compile — `'MainWindowViewModel' does not contain a definition for 'ShowTagReference'`.

- [ ] **Step 3: Add delegate and command**

In `DialogEditor.ViewModels\ViewModels\MainWindowViewModel.cs`, directly after the `About()` command (`=> ShowAbout?.Invoke(...)`, ~line 1513), add:

```csharp
    /// Set by the UI layer to open the dialog-text tag reference window.
    public Action<TagReferenceViewModel>? ShowTagReference { get; set; }

    [RelayCommand]
    private void TagReference()
        => ShowTagReference?.Invoke(new TagReferenceViewModel(_activeGameId));
```

- [ ] **Step 4: Run the new test, then the full suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelTagReferenceTests"`
Expected: PASS (1).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1916 = 1915 + 1 new).

- [ ] **Step 5: Commit**

```powershell
git add DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/MainWindowViewModelTagReferenceTests.cs
git commit -m @'
feat(tags): TagReference command hands the UI a game-primed viewmodel

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 4: Window, strings, menu item, wiring

**Files:**
- Create: `DialogEditor.Avalonia\Views\TagReferenceWindow.axaml`
- Create: `DialogEditor.Avalonia\Views\TagReferenceWindow.axaml.cs`
- Modify: `DialogEditor.Avalonia\Resources\Strings.axaml` (new keys, next to the `Menu_Changelog` block)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml` (Help menu, after the `Menu_StartGuidedTour` item, ~line 201)
- Modify: `DialogEditor.Avalonia\Views\MainWindow.axaml.cs` (window field ~line 30; delegate wiring next to `vm.ShowChangelog`, ~line 74)

**Interfaces:**
- Consumes: `TagReferenceViewModel` and its properties (Task 2), `ShowTagReference` / `TagReferenceCommand` (Task 3).
- Produces: nothing downstream — this completes the feature.

No new viewmodel logic, so no new unit tests; the structural suites (automation names, string coverage) and a build cover this task, plus GUI verification in Step 5.

- [ ] **Step 1: Add the strings**

In `DialogEditor.Avalonia\Resources\Strings.axaml`, next to the `Menu_Changelog` key block, add:

```xml
    <!-- Dialog text tag reference (Help menu) — entries themselves are data from tags.json -->
    <sys:String x:Key="Menu_TagReference">Text Tag Reference…</sys:String>
    <sys:String x:Key="ToolTip_Menu_TagReference">Open a searchable reference of the tags used in dialog text: substitution tokens like [Player Name], text markup like &lt;i&gt;, and the bracket writing conventions.</sys:String>
    <sys:String x:Key="TagRef_Title">Dialog Text Tag Reference</sys:String>
    <sys:String x:Key="TagRef_SearchWatermark">Search tags…</sys:String>
    <sys:String x:Key="ToolTip_TagRef_Search">Filter the reference by tag name or description text.</sys:String>
    <sys:String x:Key="ToolTip_TagRef_GameSelector">Choose which game's tag vocabulary to show. The two games differ: ship-duel tokens and text markup are Deadfire-only.</sys:String>
    <sys:String x:Key="TagRef_GamePoE1">Pillars of Eternity</sys:String>
    <sys:String x:Key="TagRef_GamePoE2">Deadfire</sys:String>
    <sys:String x:Key="TagRef_GameBoth">Both games</sys:String>
    <sys:String x:Key="TagRef_BadgePoE1">PoE1</sys:String>
    <sys:String x:Key="TagRef_BadgePoE2">PoE2</sys:String>
    <sys:String x:Key="TagRef_SectionTokens">Substitution tokens</sys:String>
    <sys:String x:Key="TagRef_SectionMarkup">Rich-text markup</sys:String>
    <sys:String x:Key="TagRef_SectionConventions">Writing conventions</sys:String>
    <sys:String x:Key="TagCategory_Player">Player</sys:String>
    <sys:String x:Key="TagCategory_CharacterReference">Character references</sys:String>
    <sys:String x:Key="TagCategory_ShipDuel">Ship duels</sys:String>
    <sys:String x:Key="TagCategory_Other">Other</sys:String>
    <sys:String x:Key="TagRef_EngineOnly">engine-supported, unused in shipped dialog</sys:String>
    <sys:String x:Key="TagRef_NoResults">No tags match your search.</sys:String>
```

- [ ] **Step 2: Create the window**

Create `DialogEditor.Avalonia\Views\TagReferenceWindow.axaml`:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.TagReferenceWindow"
        Title="{DynamicResource TagRef_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="600" Height="680" MinWidth="440" MinHeight="360" CanResize="True"
        ShowInTaskbar="False">
    <Window.Resources>
        <!-- One shared row template for all three sections. -->
        <DataTemplate x:Key="TagRowTemplate">
            <StackPanel Margin="0,0,0,10">
                <StackPanel Orientation="Horizontal" Spacing="8">
                    <TextBlock Text="{Binding Name}" FontWeight="Bold"
                               FontFamily="Consolas,Courier New,monospace"
                               Foreground="{DynamicResource Brush.Text.Emphasis}"
                               FontSize="{DynamicResource FontSize.Body}"/>
                    <TextBlock Text="{Binding GamesBadge}" IsVisible="{Binding ShowBadge}"
                               VerticalAlignment="Center"
                               Foreground="{DynamicResource Brush.Text.Tertiary}"
                               FontSize="{DynamicResource FontSize.Label}"/>
                    <TextBlock Text="{Binding EngineOnlyLabel}" IsVisible="{Binding IsEngineOnly}"
                               VerticalAlignment="Center"
                               Foreground="{DynamicResource Brush.Text.Link.Subtle}"
                               FontSize="{DynamicResource FontSize.Label}"/>
                </StackPanel>
                <TextBlock Text="{Binding Description}" TextWrapping="Wrap"
                           Foreground="{DynamicResource Brush.Text.Tertiary}"
                           FontSize="{DynamicResource FontSize.Body}"/>
                <TextBlock Text="{Binding Example}" IsVisible="{Binding HasExample}"
                           FontStyle="Italic" TextWrapping="Wrap" Margin="8,2,0,0"
                           Foreground="{DynamicResource Brush.Text.Tertiary}"
                           FontSize="{DynamicResource FontSize.Body}"/>
                <TextBlock Text="{Binding Notes}" IsVisible="{Binding HasNotes}"
                           TextWrapping="Wrap" Margin="8,2,0,0"
                           Foreground="{DynamicResource Brush.Text.Tertiary}"
                           FontSize="{DynamicResource FontSize.Label}"/>
            </StackPanel>
        </DataTemplate>
    </Window.Resources>

    <DockPanel Margin="14,12">
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal" Spacing="8" Margin="0,0,0,10">
            <TextBox Width="280"
                     Text="{Binding SearchText}"
                     Watermark="{DynamicResource TagRef_SearchWatermark}"
                     ToolTip.Tip="{DynamicResource ToolTip_TagRef_Search}"
                     AutomationProperties.Name="{DynamicResource TagRef_SearchWatermark}"/>
            <ComboBox ItemsSource="{Binding GameOptions}"
                      SelectedItem="{Binding SelectedGame}"
                      ToolTip.Tip="{DynamicResource ToolTip_TagRef_GameSelector}"
                      AutomationProperties.Name="{DynamicResource ToolTip_TagRef_GameSelector}"/>
        </StackPanel>

        <TextBlock DockPanel.Dock="Top" IsVisible="{Binding HasNoResults}"
                   Text="{DynamicResource TagRef_NoResults}" TextWrapping="Wrap"
                   Foreground="{DynamicResource Brush.Text.Tertiary}"
                   FontSize="{DynamicResource FontSize.Medium}"/>

        <ScrollViewer>
            <StackPanel>
                <TextBlock Text="{DynamicResource TagRef_SectionTokens}" IsVisible="{Binding HasTokens}"
                           FontWeight="Bold" FontSize="{DynamicResource FontSize.Subtitle}"
                           Foreground="{DynamicResource Brush.Text.Emphasis}" Margin="0,0,0,6"/>
                <ItemsControl ItemsSource="{Binding TokenGroups}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel Margin="0,0,0,8">
                                <TextBlock Text="{Binding Header}" FontWeight="Bold"
                                           Foreground="{DynamicResource Brush.Text.Link.Subtle}"
                                           FontSize="{DynamicResource FontSize.Label}" Margin="0,0,0,4"/>
                                <ItemsControl ItemsSource="{Binding Rows}"
                                              ItemTemplate="{StaticResource TagRowTemplate}" Margin="6,0,0,0"/>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <TextBlock Text="{DynamicResource TagRef_SectionMarkup}" IsVisible="{Binding HasMarkup}"
                           FontWeight="Bold" FontSize="{DynamicResource FontSize.Subtitle}"
                           Foreground="{DynamicResource Brush.Text.Emphasis}" Margin="0,8,0,6"/>
                <ItemsControl ItemsSource="{Binding MarkupRows}"
                              ItemTemplate="{StaticResource TagRowTemplate}" Margin="6,0,0,0"/>

                <TextBlock Text="{DynamicResource TagRef_SectionConventions}" IsVisible="{Binding HasConventions}"
                           FontWeight="Bold" FontSize="{DynamicResource FontSize.Subtitle}"
                           Foreground="{DynamicResource Brush.Text.Emphasis}" Margin="0,8,0,6"/>
                <ItemsControl ItemsSource="{Binding ConventionRows}"
                              ItemTemplate="{StaticResource TagRowTemplate}" Margin="6,0,0,0"/>
            </StackPanel>
        </ScrollViewer>
    </DockPanel>
</Window>
```

Create `DialogEditor.Avalonia\Views\TagReferenceWindow.axaml.cs`:

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

/// Non-modal, searchable reference of the dialog-text tag vocabulary
/// (tags.json via TagCatalogue). Opened from Help > Text Tag Reference;
/// MainWindow keeps one cached instance so reopening focuses it.
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public partial class TagReferenceWindow : Window
{
    // Designer/runtime-loader constructor.
    public TagReferenceWindow() : this(new TagReferenceViewModel(string.Empty)) { }

    public TagReferenceWindow(TagReferenceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
```

- [ ] **Step 3: Menu item and wiring**

In `DialogEditor.Avalonia\Views\MainWindow.axaml`, inside the Help menu directly after the `Menu_StartGuidedTour` `<MenuItem>` (~line 201) and before the existing `<Separator/>`, add:

```xml
                        <MenuItem Header="{DynamicResource Menu_TagReference}"
                                  Command="{Binding TagReferenceCommand}"
                                  ToolTip.Tip="{DynamicResource ToolTip_Menu_TagReference}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_Menu_TagReference}"/>
```

In `DialogEditor.Avalonia\Views\MainWindow.axaml.cs`:

**(a)** Add a field next to the other cached window fields (~line 30):

```csharp
    private TagReferenceWindow?    _tagReferenceWindow;
```

**(b)** Add the delegate wiring next to `vm.ShowChangelog` (~line 74):

```csharp
        vm.ShowTagReference = tagVm =>
        {
            // Cached instance: reopening the menu item focuses the open window.
            if (_tagReferenceWindow is { IsVisible: true })
            {
                _tagReferenceWindow.Activate();
                return;
            }
            _tagReferenceWindow = new TagReferenceWindow(tagVm);
            _tagReferenceWindow.Closed += (_, _) => _tagReferenceWindow = null;
            _tagReferenceWindow.Show();
            _tagReferenceWindow.Activate();
        };
```

- [ ] **Step 4: Build and run the full suite**

Run: `dotnet build`
Expected: Build succeeded (pre-existing warnings only; no new warnings).

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1916; structural suites — automation names, string coverage — pick up the new window and keys).

- [ ] **Step 5: GUI verification**

Use the `running-the-app` skill (`tools/ui-automation/DriveApp.ps1`) to verify end-to-end:
1. Launch the app; open **Help** — a menu item named "Text Tag Reference…" exists (UIA name from its Header).
2. Invoke it — a window titled "Dialog Text Tag Reference" opens showing the three sections.
3. Type `ship` in the search box — ship-duel tokens remain, unrelated entries disappear.
4. Switch the game selector to "Pillars of Eternity" — the ship-duel group and the Rich-text markup section disappear.
5. Invoke the menu item again while the window is open — the existing window is focused (no second window).
6. Take a screenshot for the report.

- [ ] **Step 6: Commit**

```powershell
git add DialogEditor.Avalonia/Views/TagReferenceWindow.axaml DialogEditor.Avalonia/Views/TagReferenceWindow.axaml.cs DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Views/MainWindow.axaml DialogEditor.Avalonia/Views/MainWindow.axaml.cs
git commit -m @'
feat(tags): Help menu Text Tag Reference window

Non-modal cached-instance window over TagReferenceViewModel: search box,
game selector, grouped token/markup/convention sections.

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```

---

### Task 5: Close the gap entry

**Files:**
- Modify: `Gaps.md` (the `### Dialog text tag reference window` entry)

**Interfaces:** none — documentation only.

- [ ] **Step 1: Mark the gap implemented**

Replace the entry heading and body with:

```markdown
### ~~Dialog text tag reference window~~ ✓ Implemented (2026-07-05)
**Help ▸ Text Tag Reference…** opens a non-modal, searchable window over the
engine-verified tag vocabulary (`tags.json`, embedded via `TagCatalogue` —
the `ConditionCatalogue` pattern): substitution tokens grouped by category,
rich-text markup, and writing conventions, filtered by game (auto from the
open game folder, PoE2 default, "Both" with per-entry badges). Engine-only
tokens (count 0 in shipped dialog) are badged as such. The catalogue lives in
`DialogEditor.ViewModels` so token autocomplete/validation (separate gap) can
consume it. Human-readable docs: `data/tags-poe1.md` / `data/tags-poe2.md`.
Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md.
```

- [ ] **Step 2: Full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (1916).

- [ ] **Step 3: Commit**

```powershell
git add Gaps.md
git commit -m @'
docs(gaps): mark tag reference window implemented

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>
'@
```
