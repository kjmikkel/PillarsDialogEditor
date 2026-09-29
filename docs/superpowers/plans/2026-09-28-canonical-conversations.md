# Canonical Test Conversations Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** One hand-written canonical conversation per game, checked in as a miniature game install, that exercises every construct the editor reads or writes — with coverage, clean-load and round-trip tests over it.

**Architecture:** Each fixture lives under `DialogEditor.Tests/Fixtures/Canonical/{poe1,poe2}/` in the exact on-disk layout of a real install, so the running app can point its game folder straight at it. Tests copy the tree to a temp folder (`FakePoe1Game`, `FakePoe2Game.Canonical()`) and drive the real providers, parsers and serializers. The comparison logic already written for the #117 shipped-data fidelity tests is extracted into a shared helper and reused, so both suites judge "round-trips intact" identically.

**Tech Stack:** .NET 10, xunit v2, System.Xml.Linq, System.Text.Json.Nodes.

**Spec:** GitHub issue #122 (kjmikkel/PillarsDialogEditor) plus the design approved in the session of 2026-09-28 (mini-install layout; reuse #117 checks; PoE1 byte/structure check only via the game's own model when `DIALOGEDITOR_POE1_DIR` is set).

## Global Constraints

- Strict red/green TDD (CLAUDE.md): the failing test exists and is seen failing before the thing it tests.
- Fixtures are **authored from the documented formats**, never copied from game data. Invented text, invented NPC GUID, invented script parameters. The public repo must contain nothing from a game install. Well-known engine GUIDs (player `b1a8e901-0000-0000-0000-000000000000`, PoE2 narrator `6a99a109-0000-0000-0000-000000000000`, PoE1 narrator `00000000-0000-0000-0000-000000000000`) are format constants and are allowed.
- `.wem` files in the fixture are a few placeholder bytes, not audio.
- Test cleanup may swallow exceptions (`catch (Exception) { /* best-effort */ }`); nothing else may.
- Run tests headless: `dotnet test DialogEditor.Tests --filter "Category!=Gui"` (bare `dotnet test` launches the GUI).
- Test parallelization stays disabled (AppLog/AppSettings statics); the clean-load test relies on it.
- No BOM on new files. CHANGELOG.md is frozen — do not touch it.
- Commit messages reference `#122` and end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Deviations during execution

- **PoE1 has no link conditions and uses different enum names.** The decompiled `OEIFormats`
  model shows that `DialogueLink` has no `Conditionals` and that QuestionNodeTextDisplay takes
  `ShowOnce`/`ShowAlways`/`ShowNever`. So the PoE1 fixture puts its nested condition on node 1,
  uses the game's own names, and the coverage test asserts both points. Filed the editor bugs this
  turned up: #139, #140 (RandomWeight is an int in both games), #141.
- **PoE1 stringtables** carry `<Language>` and `<GenderNeutralText>` per entry and have no
  `<NextEntryID>`. The fixture follows that shape.
- **PoE2 node −200** comes last in `Nodes`, as shipped. The bundle was generated with a
  throwaway script so property order stays consistent.
- **App verification** used a temp copy of each fixture, never the source tree. It found #142
  (selecting a linked node marks the conversation modified).

## File Structure

| File | Responsibility |
|---|---|
| `DialogEditor.Tests/Helpers/RoundTripChecks.cs` (new) | Shared comparison functions extracted from `ShippedConversationRoundTripTests` |
| `DialogEditor.Tests/GameData/ShippedConversationRoundTripTests.cs` (modify) | Uses `RoundTripChecks` instead of private copies |
| `DialogEditor.Tests/Helpers/CanonicalFixture.cs` (new) | Locates the checked-in fixture tree and copies it to a temp folder |
| `DialogEditor.Tests/Helpers/FakePoe1Game.cs` (new) | Temp PoE1 install populated from the canonical fixture |
| `DialogEditor.Tests/Helpers/FakePoe2Game.cs` (modify) | Adds `FakePoe2Game.Canonical()` |
| `DialogEditor.Tests/Fixtures/Canonical/poe1/...` (new) | PoE1 canonical conversation + en/de stringtables |
| `DialogEditor.Tests/Fixtures/Canonical/poe2/...` (new) | PoE2 canonical bundle + en/de stringtables + `.wem` placeholders |
| `DialogEditor.Tests/Fixtures/Canonical/README.md` (new) | Node ID → construct, per game; how to open in the app |
| `DialogEditor.Tests/GameData/CanonicalConversationTests.cs` (new) | Coverage, clean-load and round-trip tests |
| `DialogEditor.Tests/DialogEditor.Tests.csproj` (modify) | Copy `Fixtures\Canonical\**` to output |
| `docs/game-data-tests.md`, `.claude/skills/running-the-app/SKILL.md` (modify) | Point at the canonical fixtures |

---

### Task 1: Extract the #117 comparison helpers (refactor under green)

**Files:**
- Create: `DialogEditor.Tests/Helpers/RoundTripChecks.cs`
- Modify: `DialogEditor.Tests/GameData/ShippedConversationRoundTripTests.cs`

**Interfaces:**
- Produces (all `public static` on `internal static class DialogEditor.Tests.Helpers.RoundTripChecks`):
  - `ConversationEditSnapshot UnchangedSnapshot(IGameDataProvider provider, ConversationFile file)`
  - `string? ModelDifference(IReadOnlyList<ConversationNode> before, IReadOnlyList<ConversationNode> after)`
  - `string? JsonDifference(string originalJson, string savedJson)` — null when `JsonNode.DeepEquals`, else the first differing node
  - `string? FirstLineDifference(string a, string b)`
  - `List<(int Id, string? DefaultText, string? FemaleText)> StringTableEntries(string xml)`
  - `XmlSerializer Poe1GameSerializer(string installDir)`
  - `string Poe1GameCanonical(XmlSerializer gameSerializer, string xml)` — deserialize + reserialize through the game model

- [ ] **Step 1: Confirm the baseline builds and the fast suite is green**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: PASS (GameData tests report as skipped unless the env vars are set).

- [ ] **Step 2: Create `RoundTripChecks.cs`** by moving the bodies verbatim from `ShippedConversationRoundTripTests` (`UnchangedSnapshot`, `ModelDifference`, both `Describe` overloads, `FirstLineDifference`, `Line`, `FirstNodeDifference`, `Entries`, `Poe1GameSerializer`) and adding the two thin wrappers:

```csharp
using System.Reflection;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using System.Xml.Serialization;
using DialogEditor.Core.Editing;
using DialogEditor.Core.GameData;
using DialogEditor.Core.Models;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Helpers;

/// <summary>
/// What "saved unchanged and came back intact" means, shared by the shipped-data fidelity
/// tests (issue 117) and the canonical-conversation tests (issue 122), so both judge alike.
/// Each check returns null when the two sides agree, else a short description of the first
/// difference.
/// </summary>
internal static class RoundTripChecks
{
    // What an unchanged save writes: the conversation exactly as the editor loads it.
    public static ConversationEditSnapshot UnchangedSnapshot(IGameDataProvider provider, ConversationFile file) =>
        ConversationSnapshotBuilder.Build(provider.LoadConversation(file));

    public static string? JsonDifference(string originalJson, string savedJson) =>
        JsonNode.DeepEquals(JsonNode.Parse(originalJson), JsonNode.Parse(savedJson))
            ? null
            : FirstNodeDifference(originalJson, savedJson);

    public static string Poe1GameCanonical(XmlSerializer gameSerializer, string xml)
    {
        using var reader = new StringReader(xml);
        var data = gameSerializer.Deserialize(reader);
        using var writer = new StringWriter();
        gameSerializer.Serialize(writer, data);
        return writer.ToString();
    }

    // ModelDifference, Describe (x2), FirstLineDifference, Line, FirstNodeDifference,
    // StringTableEntries (renamed from Entries, keep its comment), Poe1GameSerializer:
    // moved verbatim from ShippedConversationRoundTripTests, made public static
    // (Describe/Line/FirstNodeDifference stay private).
}
```

(`ConversationSnapshotBuilder` lives in `DialogEditor.Patch` — keep whatever `using` the original file needed for it.)

- [ ] **Step 3: Point `ShippedConversationRoundTripTests` at the helper**: delete the moved private members, add `using DialogEditor.Tests.Helpers;` and `using static DialogEditor.Tests.Helpers.RoundTripChecks;`, replace `Entries(` with `StringTableEntries(`, replace the inline `JsonNode.DeepEquals(...) ? null : FirstNodeDifference(...)` with `JsonDifference(original, saved)`, and replace the local `Canonical` function with `Poe1GameCanonical(gameSerializer, …)`. Remove now-unused `using`s.

- [ ] **Step 4: Run the fast suite, then the GameData suite against the local installs**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"` → PASS.
Then, if the installs exist locally (see memory: GOG installs on D:):
```powershell
$env:DIALOGEDITOR_POE1_DIR = 'D:\Program Files (x86)\GOG Galaxy\Games\PillarsOfEternity'
$env:DIALOGEDITOR_POE2_DIR = 'D:\Program Files (x86)\GOG Galaxy\Games\Pillars of Eternity II Deadfire'
dotnet test DialogEditor.Tests --filter "Category=GameData"
```
Expected: the six `ShippedConversationRoundTripTests` PASS, as before the refactor.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/Helpers/RoundTripChecks.cs DialogEditor.Tests/GameData/ShippedConversationRoundTripTests.cs
git commit -m "test: share the round-trip checks from the shipped-data tests (#122)"
```

---

### Task 2: PoE1 canonical fixture + `FakePoe1Game` + coverage and clean-load tests

**Files:**
- Create: `DialogEditor.Tests/Helpers/CanonicalFixture.cs`
- Create: `DialogEditor.Tests/Helpers/FakePoe1Game.cs`
- Create: `DialogEditor.Tests/GameData/CanonicalConversationTests.cs`
- Create: `DialogEditor.Tests/Fixtures/Canonical/poe1/PillarsOfEternity_Data/data/conversations/canonical/canonical.conversation`
- Create: `DialogEditor.Tests/Fixtures/Canonical/poe1/PillarsOfEternity_Data/data/localized/en/text/conversations/canonical/canonical.stringtable`
- Create: `DialogEditor.Tests/Fixtures/Canonical/poe1/PillarsOfEternity_Data/data/localized/de/text/conversations/canonical/canonical.stringtable`
- Modify: `DialogEditor.Tests/DialogEditor.Tests.csproj`

**Interfaces:**
- Produces:
  - `static class CanonicalFixture { string SourceDir(string game); void CopyTo(string game, string destRoot); }` — `game` is `"poe1"` or `"poe2"`
  - `sealed class FakePoe1Game : IDisposable { string Root; IGameDataProvider Provider; string ConvPath(); string StPath(string lang); ConversationFile File; }` — `File` is the single enumerated canonical conversation
  - `CanonicalConversationTests` (class, `[Trait("Category","Canonical")]`)

- [ ] **Step 1: Add the copy rule to the csproj** (under the existing schema-fixtures `ItemGroup`):

```xml
    <!-- Canonical conversations (issue #122): each is a miniature game install. -->
    <None Update="Fixtures\Canonical\**\*" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Write `CanonicalFixture.cs` and `FakePoe1Game.cs`**

```csharp
namespace DialogEditor.Tests.Helpers;

/// The checked-in canonical conversations (issue 122), each laid out as a miniature game
/// install under Fixtures/Canonical/{poe1,poe2}. See Fixtures/Canonical/README.md.
internal static class CanonicalFixture
{
    public static string SourceDir(string game) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Canonical", game);

    /// Copies the fixture's install tree (not the README) into <paramref name="destRoot"/>.
    public static void CopyTo(string game, string destRoot)
    {
        var source = SourceDir(game);
        if (!Directory.Exists(source))
            throw new DirectoryNotFoundException($"Canonical fixture not copied to output: {source}");
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var dest = Path.Combine(destRoot, Path.GetRelativePath(source, file));
            Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
            File.Copy(file, dest);
        }
    }
}
```

```csharp
using DialogEditor.Core.GameData;

namespace DialogEditor.Tests.Helpers;

/// A throwaway PoE1 install holding the canonical conversation (issue 122) in "en" and "de".
public sealed class FakePoe1Game : IDisposable
{
    public string Root { get; } = Path.Combine(Path.GetTempPath(), $"fakepoe1_{Guid.NewGuid():N}");
    public string DataRoot => Path.Combine(Root, "PillarsOfEternity_Data", "data");
    public string ConvPath() => Path.Combine(DataRoot, "conversations", "canonical", "canonical.conversation");
    public string StPath(string lang) =>
        Path.Combine(DataRoot, "localized", lang, "text", "conversations", "canonical", "canonical.stringtable");

    public FakePoe1Game() => CanonicalFixture.CopyTo("poe1", Root);

    public IGameDataProvider Provider => new Poe1GameDataProvider(Root);
    public ConversationFile File => Provider.EnumerateConversations().Single();

    public void Dispose() { try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ } }
}
```

- [ ] **Step 3: Write the failing coverage and clean-load tests** in `CanonicalConversationTests.cs`:

```csharp
using System.Xml.Linq;
using DialogEditor.Core.Logging;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.GameData;

/// <summary>
/// The canonical conversations (issue 122): one hand-written conversation per game that
/// exercises every construct the editor reads or writes. Fixtures/Canonical/README.md maps
/// each construct to its node ID. These tests keep the fixture honest (every construct is
/// really there), show it loads cleanly, and run the #117 round-trip checks over it.
/// </summary>
[Trait("Category", "Canonical")]
public class CanonicalConversationTests
{
    private static readonly XNamespace Xsi = "http://www.w3.org/2001/XMLSchema-instance";

    // ── PoE1: coverage ──────────────────────────────────────────────────────────

    [Fact]
    public void Poe1_Fixture_CoversEveryConstruct()
    {
        using var game = new FakePoe1Game();
        var doc   = XDocument.Load(game.ConvPath());
        var nodes = doc.Descendants("FlowChartNode").ToList();
        var links = doc.Descendants("FlowChartLink").ToList();
        string Type(XElement e) => (string?)e.Attribute(Xsi + "type") ?? "";

        Assert.Equal(["BankNode", "PlayerResponseNode", "ScriptNode", "TalkNode", "TriggerConversationNode"],
            nodes.Select(Type).Distinct().Order());
        Assert.Equal(["Bark", "Conversation", "Hidden", "Overlay"],
            nodes.Select(n => (string?)n.Element("DisplayType")).OfType<string>().Distinct().Order());
        Assert.Equal(["MarkAsRead", "None", "OnceEver", "OncePerConversation"],
            nodes.Select(n => (string?)n.Element("Persistence")).OfType<string>().Distinct().Order());
        Assert.Contains(nodes, n => (string?)n.Element("BankNodePlayType") == "PlayRandom");

        // Links: optional elements present and absent (#112); non-default values; conditions.
        Assert.Contains(links, l => l.Element("RandomWeight") is null && l.Element("QuestionNodeTextDisplay") is null);
        Assert.Contains(links, l => (string?)l.Element("RandomWeight") is { } w && w != "1");
        Assert.Contains(links, l => (string?)l.Element("QuestionNodeTextDisplay") == "Always");
        Assert.Contains(links, l => (string?)l.Element("QuestionNodeTextDisplay") == "Never");
        Assert.Contains(links, l => l.Element("Conditionals")?.Element("Components")?.HasElements == true);

        // Conditions: a nested expression holding both operators, and a negated call.
        Assert.Contains(doc.Descendants("ExpressionComponent"), e =>
            Type(e) == "ConditionalExpression"
            && e.Element("Components")!.Elements().Any(c => Type(c) == "ConditionalExpression"));
        var ops = doc.Descendants("ExpressionComponent").Select(e => (string?)e.Element("Operator")).ToHashSet();
        Assert.Contains("And", ops);
        Assert.Contains("Or", ops);
        Assert.Contains(doc.Descendants("ExpressionComponent"), e => (string?)e.Element("Not") == "true");

        // Scripts: all three lists used; ScriptCall written bare, as the game requires (#111).
        foreach (var list in new[] { "OnEnterScripts", "OnExitScripts", "OnUpdateScripts" })
            Assert.Contains(nodes, n => n.Element(list)?.HasElements == true);
        Assert.All(doc.Descendants("ScriptCall"), s => Assert.Null(s.Attribute(Xsi + "type")));

        AssertTextCoverage(game.Provider, game.File, ["en", "de"]);
    }

    // ── PoE1: loads cleanly ─────────────────────────────────────────────────────

    [Fact]
    public void Poe1_Fixture_LoadsWithoutWarnings()
    {
        using var game = new FakePoe1Game();
        AssertLoadsCleanly(game.Provider, game.File);
    }

    // ── Shared assertions ───────────────────────────────────────────────────────

    /// The node every fixture leaves without a stringtable entry, on purpose (README).
    private const int MissingStringNodeId = 9;

    private static void AssertTextCoverage(
        Core.GameData.IGameDataProvider provider, Core.GameData.ConversationFile file, string[] languages)
    {
        Assert.Equal(languages.Order(), provider.AvailableLanguages.Order());
        foreach (var lang in languages)
        {
            var entries = RoundTripChecks.StringTableEntries(File.ReadAllText(provider.GetStringTablePath(file, lang)));
            Assert.DoesNotContain(entries, e => e.Id == MissingStringNodeId);
            Assert.Contains(entries, e => !string.IsNullOrEmpty(e.FemaleText));
            Assert.Contains(entries, e => e.DefaultText?.Contains("[Player Name]") == true);
            Assert.Contains(entries, e => e.DefaultText?.Contains("<i>") == true);   // stored escaped as &lt;i&gt;
        }
    }

    /// Loads in every language with no WARN/ERROR logged, every link and bank child points
    /// at a node that exists, and every text-bearing node except the deliberate one has text.
    private static void AssertLoadsCleanly(
        Core.GameData.IGameDataProvider provider, Core.GameData.ConversationFile file)
    {
        foreach (var lang in provider.AvailableLanguages)
        {
            provider.Language = lang;
            var logBefore = LogLength();
            var conv = provider.LoadConversation(file);
            var logged = NewLogText(logBefore);
            Assert.DoesNotContain("WARN", logged);
            Assert.DoesNotContain("ERROR", logged);

            var ids = conv.Nodes.Select(n => n.NodeId).ToHashSet();
            Assert.All(conv.Nodes.SelectMany(n => n.Links), l => Assert.Contains(l.ToNodeId, ids));
            Assert.All(conv.Nodes.Where(n => n.SpeakerCategory != SpeakerCategory.Script && n.NodeId != MissingStringNodeId),
                n => Assert.False(string.IsNullOrEmpty(conv.Strings.Get(n.NodeId)?.DefaultText),
                                  $"{lang}: node {n.NodeId} has no text"));
            Assert.Null(conv.Strings.Get(MissingStringNodeId));
        }
    }

    private static long LogLength() =>
        File.Exists(AppLog.LogPath) ? new FileInfo(AppLog.LogPath).Length : 0;

    private static string NewLogText(long from)
    {
        if (!File.Exists(AppLog.LogPath)) return "";
        using var s = new FileStream(AppLog.LogPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
        s.Seek(Math.Min(from, s.Length), SeekOrigin.Begin);
        return new StreamReader(s).ReadToEnd();
    }
}
```

Notes for the implementer:
- `IGameDataProvider.Language` is settable; `FakePoe1Game.Provider` returns a new instance per call, so the helper takes the provider once and switches its language.
- `StringEntry(int Id, string DefaultText, string FemaleText)` — `conv.Strings.Get(id)?.DefaultText` is correct.
- A ScriptNode/BankNode/TriggerConversationNode classifies as `SpeakerCategory.Script` — those carry no text in the fixture.

- [ ] **Step 4: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalConversationTests"`
Expected: FAIL — `DirectoryNotFoundException: Canonical fixture not copied to output`.

- [ ] **Step 5: Author the PoE1 conversation** at `Fixtures/Canonical/poe1/PillarsOfEternity_Data/data/conversations/canonical/canonical.conversation`.

Document shape (as shipped): `<?xml version="1.0" encoding="utf-8"?>`, root `<ConversationData xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">`, children in this order: `<NextNodeID>11</NextNodeID>`, `<Nodes>…</Nodes>`, `<Bookmarks />`, `<ClassExtender><ExtendedProperties /></ClassExtender>`, `<ConversationType>Conversation</ConversationType>`, `<Description>Canonical test conversation (issue 122).</Description>`, `<SceneLocation /> <SceneTime />`, `<CharacterMappings />`. Two-space indent.

Every **dialogue node** (TalkNode, PlayerResponseNode, ScriptNode, TriggerConversationNode) has exactly these children in this order:

```xml
<FlowChartNode xsi:type="TalkNode">
  <NodeID>0</NodeID>
  <Comments />
  <PackageID>1</PackageID>
  <ContainerNodeID>-1</ContainerNodeID>
  <Links>…</Links>                       <!-- or <Links /> -->
  <ClassExtender><ExtendedProperties /></ClassExtender>
  <Conditionals><Operator>And</Operator><Components>…</Components></Conditionals>
  <OnEnterScripts /> <OnExitScripts /> <OnUpdateScripts />
  <NotSkippable>false</NotSkippable>
  <IsQuestionNode>false</IsQuestionNode>
  <IsTempText>false</IsTempText>
  <PlayVOAs3DSound>false</PlayVOAs3DSound>
  <PlayType>Normal</PlayType>
  <Persistence>None</Persistence>
  <NoPlayRandomWeight>0</NoPlayRandomWeight>
  <DisplayType>Conversation</DisplayType>
  <VOFilename /> <VoiceType />
  <ExcludedSpeakerClasses /> <ExcludedListenerClasses />
  <IncludedSpeakerClasses /> <IncludedListenerClasses />
  <!-- TalkNode only: --> <ActorDirection /> <SpeakerGuid>…</SpeakerGuid> <ListenerGuid>…</ListenerGuid>
  <!-- TriggerConversationNode only: --> <ConversationFilename>…</ConversationFilename> <StartNodeID>0</StartNodeID>
</FlowChartNode>
```

A **BankNode** stops after `<OnUpdateScripts />` and adds `<BankNodePlayType>PlayRandom</BankNodePlayType><ChildNodeIDs><int>8</int><int>9</int></ChildNodeIDs>`.

A **full link**: `<FlowChartLink xsi:type="DialogueLink"><FromNodeID/><ToNodeID/><PointsToGhost>false</PointsToGhost><ClassExtender><ExtendedProperties /></ClassExtender><RandomWeight>1</RandomWeight><PlayQuestionNodeVO>true</PlayQuestionNodeVO><QuestionNodeTextDisplay>ShowOnce</QuestionNodeTextDisplay></FlowChartLink>`. A link with conditions puts `<Conditionals><Operator>And</Operator><Components>…</Components></Conditionals>` after `<ClassExtender>`. A **short link** (#112) omits `RandomWeight` and `QuestionNodeTextDisplay`.

A **condition call**: `<ExpressionComponent xsi:type="ConditionalCall"><Data><FullName>Boolean IsGlobalValue(String, Operator, Int32)</FullName><Parameters><string>canonical_flag</string><string>EqualTo</string><string>1</string></Parameters></Data><Not>false</Not><Operator>And</Operator></ExpressionComponent>`. A **nested expression**: `<ExpressionComponent xsi:type="ConditionalExpression"><Operator>Or</Operator><Components>…</Components></ExpressionComponent>` (no `Not`). A **script**: `<ScriptCall><Data><FullName>Void SetGlobalValue(String, Int32)</FullName><Parameters><string>canonical_seen</string><string>1</string></Parameters></Data></ScriptCall>` — no `xsi:type`.

GUIDs: player `b1a8e901-0000-0000-0000-000000000000`; narrator `00000000-0000-0000-0000-000000000000`; the NPC `c0a1c0a1-0000-4000-8000-000000000122` (invented).

Nodes (use invented global names prefixed `canonical_`):

| ID | Type | Container | Speaker | DisplayType / Persistence / PlayType | Links (→ target: link details) | Other |
|---|---|---|---|---|---|---|
| 0 | TalkNode | -1 | NPC | Conversation / None / Normal | →1 full; →2 full `RandomWeight 2`, `QuestionNodeTextDisplay Always`; →3 full `Never` | OnEnter: SetGlobalValue(canonical_seen, 1) |
| 1 | PlayerResponseNode | -1 | — | Conversation / OnceEver / Normal | →4 **short** link with conditions: `Or`-expression{ call A (`And`), expression `And`{ call B, call C `Not=true` } } | IsQuestionNode false |
| 2 | PlayerResponseNode | -1 | — | Conversation / OncePerConversation | →5 full | node conditions: one call, `Operator Or` |
| 3 | PlayerResponseNode | -1 | — | Conversation / MarkAsRead | →6 full | node conditions: one call `Not=true` |
| 4 | ScriptNode | -1 | — | Hidden / None | →7 full | OnEnter, OnExit **and** OnUpdate each one ScriptCall (different parameters) |
| 5 | BankNode | -1 | — | (none) | →10 full | PlayRandom, ChildNodeIDs 8, 9 |
| 6 | TriggerConversationNode | -1 | — | Conversation / None | none | ConversationFilename `Assets/Data/Conversations/Canonical/canonical.conversation`, StartNodeID 0 |
| 7 | TalkNode | -1 | narrator | Bark / None / Random | →10 full | NoPlayRandomWeight 1 |
| 8 | TalkNode | 5 | NPC | Overlay / None | none | |
| 9 | TalkNode | 5 | NPC | Conversation / None | none | **no stringtable entry** |
| 10 | TalkNode | -1 | NPC | Conversation / None | none | Comments `End of the canonical conversation.`, ActorDirection `Looks up.` |

The parser reads the root `<Conditionals><Components>`; node conditions go inside `Components`.

- [ ] **Step 6: Author the PoE1 stringtables** (en and de, same IDs, invented text). Shape:

```xml
<?xml version="1.0" encoding="utf-8"?>
<StringTableFile xmlns:xsi="http://www.w3.org/2001/XMLSchema-instance" xmlns:xsd="http://www.w3.org/2001/XMLSchema">
  <Name>conversations\canonical\canonical</Name>
  <NextEntryID>11</NextEntryID>
  <EntryCount>7</EntryCount>
  <Entries>
    <Entry>
      <ID>0</ID>
      <DefaultText>Greetings, [Player Name]. This is the canonical conversation.</DefaultText>
      <FemaleText>Greetings, [Player Name]. This is the canonical conversation, my lady.</FemaleText>
    </Entry>
    …
  </Entries>
</StringTableFile>
```

Entries for IDs 0, 1, 2, 3, 7, 8, 10 (text-bearing nodes except 9). Node 7's text contains `&lt;i&gt;quietly&lt;/i&gt;`. Every other `FemaleText` is `<FemaleText />`. German file: same structure, German invented text (keep `[Player Name]` and the `&lt;i&gt;` markup; give entry 0 a female variant too).

- [ ] **Step 7: Run to verify green**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalConversationTests"`
Expected: both PoE1 tests PASS. If the clean-load test fails on a real warning, fix the fixture — not the test — unless the warning is an editor bug (then see Task 4 Step 4 for the skip-and-file procedure).

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.Tests/DialogEditor.Tests.csproj DialogEditor.Tests/Helpers/CanonicalFixture.cs DialogEditor.Tests/Helpers/FakePoe1Game.cs DialogEditor.Tests/GameData/CanonicalConversationTests.cs DialogEditor.Tests/Fixtures/Canonical/poe1
git commit -m "test: canonical PoE1 conversation and FakePoe1Game (#122)"
```

---

### Task 3: PoE2 canonical fixture + `FakePoe2Game.Canonical()` + coverage and clean-load tests

**Files:**
- Modify: `DialogEditor.Tests/Helpers/FakePoe2Game.cs`
- Modify: `DialogEditor.Tests/GameData/CanonicalConversationTests.cs`
- Create: `DialogEditor.Tests/Fixtures/Canonical/poe2/PillarsOfEternityII_Data/exported/design/conversations/canonical.conversationbundle`
- Create: `…/poe2/PillarsOfEternityII_Data/exported/localized/{en,de}/text/conversations/canonical.stringtable`
- Create: `…/poe2/PillarsOfEternityII_Data/StreamingAssets/Audio/Windows/Voices/English(US)/narrator/canonical_0007.wem` and `canonical_0007_fem.wem`

**Interfaces:**
- Consumes: `CanonicalFixture.CopyTo`, `AssertTextCoverage`, `AssertLoadsCleanly`, `MissingStringNodeId` (Task 2)
- Produces: `public static FakePoe2Game FakePoe2Game.Canonical()`; with it, `ConvPath("canonical")`, `StPath(lang, "canonical")`, `VoDir` work unchanged.

- [ ] **Step 1: Add the factory to `FakePoe2Game`**

```csharp
    /// The canonical PoE2 conversation (issue 122) instead of the one-node test_conv:
    /// "canonical" in "en" and "de", with its .wem files. See Fixtures/Canonical/README.md.
    public static FakePoe2Game Canonical() => new(canonical: true);

    private FakePoe2Game(bool canonical) => CanonicalFixture.CopyTo("poe2", Root);

    public ConversationFile CanonicalFile =>
        Provider.EnumerateConversations().Single(f => f.Name == "canonical");
```

(`Root` is an auto-property initializer, so it is set before the private constructor body runs. Add `using DialogEditor.Core.GameData;` if missing — it is already there.)

- [ ] **Step 2: Write the failing PoE2 tests** (append to `CanonicalConversationTests`):

```csharp
    // ── PoE2: coverage ──────────────────────────────────────────────────────────

    [Fact]
    public void Poe2_Fixture_CoversEveryConstruct()
    {
        using var game = FakePoe2Game.Canonical();
        var conv  = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(game.ConvPath("canonical")))!["Conversations"]![0]!;
        var nodes = conv["Nodes"]!.AsArray().Select(n => n!).ToList();
        var links = nodes.SelectMany(n => n["Links"]!.AsArray().Select(l => l!)).ToList();
        string Type(System.Text.Json.Nodes.JsonNode n) =>
            n["$type"]!.GetValue<string>().Split(',')[0].Split('.')[^1];
        IEnumerable<System.Text.Json.Nodes.JsonNode> Components(System.Text.Json.Nodes.JsonNode? c) =>
            c?["Components"]?.AsArray().Select(x => x!).SelectMany(x => Components(x).Prepend(x)) ?? [];

        Assert.NotNull(conv["ConversationScriptNode"]);                       // #115
        Assert.Contains(nodes, n => n["NodeID"]!.GetValue<int>() == -200 && Type(n) == "ScriptNode");
        Assert.Equal(["BankNode", "PlayerResponseNode", "ScriptNode", "TalkNode", "TriggerConversationNode"],
            nodes.Select(Type).Distinct().Order());
        Assert.Equal([0, 1, 2, 3], nodes.Where(n => n["NodeID"]!.GetValue<int>() >= 0)
            .Select(n => n["DisplayType"]?.GetValue<int>()).OfType<int>().Distinct().Order());   // #114
        Assert.Equal([0, 1, 2, 3], nodes.Where(n => n["NodeID"]!.GetValue<int>() >= 0)
            .Select(n => n["Persistence"]?.GetValue<int>()).OfType<int>().Distinct().Order());

        Assert.Contains(links, l => l["RandomWeight"]!.GetValue<double>() != 1);
        Assert.Equal([0, 1, 2], links.Select(l => l["QuestionNodeTextDisplay"]!.GetValue<int>()).Distinct().Order());
        Assert.Contains(links, l => l["Conditionals"]!["Components"]!.AsArray().Count > 0);

        var allConds = nodes.SelectMany(n => Components(n["Conditionals"]))
            .Concat(links.SelectMany(l => Components(l["Conditionals"]))).ToList();
        Assert.Contains(allConds, c => Type(c) == "ConditionalExpression"
                                       && Components(c).Any(x => Type(x) == "ConditionalExpression"));
        Assert.Contains(allConds, c => c["Operator"]!.GetValue<int>() == 1);
        Assert.Contains(allConds, c => c["Not"]?.GetValue<bool>() == true);

        foreach (var list in new[] { "OnEnterScripts", "OnExitScripts", "OnUpdateScripts" })
            Assert.Contains(nodes, n => n[list]!.AsArray().Count > 0);
        var scripts = nodes.SelectMany(n => new[] { "OnEnterScripts", "OnExitScripts", "OnUpdateScripts" }
            .SelectMany(k => n[k]!.AsArray().Select(s => s!))).ToList();
        Assert.Contains(scripts, s => s["Conditional"]?["Components"]?.AsArray().Count > 0);   // #115
        Assert.All(scripts, s => Assert.NotNull(s["Data"]!["FunctionHash"]));

        // VO: a node with its own VO file (and female variant), and an alias to it.
        Assert.Contains(nodes, n => n["HasVO"]?.GetValue<bool>() == true);
        var alias = nodes.Select(n => n["ExternalVO"]?.GetValue<string>()).Single(v => !string.IsNullOrEmpty(v))!;
        Assert.True(File.Exists(Path.Combine(game.VoDir, alias + ".wem")));
        Assert.True(File.Exists(Path.Combine(game.VoDir, alias + "_fem.wem")));

        AssertTextCoverage(game.Provider, game.CanonicalFile, ["de", "en"]);
    }

    [Fact]
    public void Poe2_Fixture_LoadsWithoutWarnings()
    {
        using var game = FakePoe2Game.Canonical();
        AssertLoadsCleanly(game.Provider, game.CanonicalFile);
    }
```

- [ ] **Step 3: Run to verify red**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalConversationTests.Poe2"`
Expected: FAIL — `DirectoryNotFoundException: Canonical fixture not copied to output: …poe2`.

- [ ] **Step 4: Author the PoE2 bundle** `canonical.conversationbundle`. Tab-indented JSON, starting `{"Conversations": [` then a newline and `{` (as shipped). Conversation object keys in order: `"ID"` (invented GUID `c0a1c0a1-0000-4000-8000-00000000c122`), `"Filename": "Conversations/Canonical/canonical.conversation"`, `"ConversationScriptNode"` (the -200 shape below), `"ConversationType": 0`, `"CharacterMappings": [{"Guid": "c0a1c0a1-0000-4000-8000-000000000122", "InstanceTag": ""}, {"Guid": "6a99a109-0000-0000-0000-000000000000", "InstanceTag": ""}]`, `"Nodes": [...]`.

Property order per type (copy exactly; the save must reproduce identical JSON):

- **TalkNode**: `$type` (`"OEIFormats.FlowCharts.Conversations.TalkNode, OEIFormats"`), `EmotionType ""`, `EmotionStrength 0.5`, `PersistEmotion true`, `EmotionDelay 0.0`, `SpeakerGuid`, `ListenerGuid`, `ExternalVO`, `HasVO`, `NotSkippable`, `IsQuestionNode`, `HideSpeaker`, `IsTempText`, `PlayVOAs3DSound`, `PlayType`, `Persistence`, `NoPlayRandomWeight`, `VOPositioning`, `DisplayType`, `NodeID`, `ContainerNodeID`, `Links`, `ClassExtender` (`{"ExtendedProperties": ["SpeakerAnimation,0","ListenerAnimation,0","DoNotClearText,False","FocusedSpeaker,","VOEventOverride,None"]}`), `Conditionals`, `OnEnterScripts`, `OnExitScripts`, `OnUpdateScripts`.
- **PlayerResponseNode**: `$type`, then `NotSkippable` … `DisplayType` (same run as TalkNode, no speaker/VO keys), `NodeID` … `OnUpdateScripts`.
- **ScriptNode**: `$type`, `RequiresValidChildNode false`, then as PlayerResponseNode.
- **TriggerConversationNode**: `$type`, `ConversationGuid` (the conversation's own ID), `StartNodeID 0`, then as PlayerResponseNode.
- **BankNode**: `$type` `"OEIFormats.FlowCharts.BankNode, OEIFormats"`, `BankNodePlayType 2` (PlayRandom), `Persistence`, `ChildNodeIDs [8, 9]`, `NodeID`, `ContainerNodeID`, `Links`, `ClassExtender {"ExtendedProperties": []}`, `Conditionals`, `OnEnterScripts`, `OnExitScripts`, `OnUpdateScripts`.
- **Link**: `$type` (`"OEIFormats.FlowCharts.Conversations.DialogueLink, OEIFormats"`), `RandomWeight`, `PlayQuestionNodeVO true`, `QuestionNodeTextDisplay`, `FromNodeID`, `ToNodeID`, `PointsToGhost false`, `Conditionals`, `ClassExtender {"ExtendedProperties": []}`.
- **Condition call**: `$type "OEIFormats.FlowCharts.ConditionalCall, OEIFormats"`, `Data {FullName, Parameters, Flags "", UnrealCall "", FunctionHash <int>, ParameterHash <int>}`, `Not`, `Operator`. **Expression**: `$type "OEIFormats.FlowCharts.ConditionalExpression, OEIFormats"`, `Operator`, `Components`.
- **Script**: `{"Data": {FullName, Parameters, Flags "", UnrealCall "", FunctionHash <int>, ParameterHash <int>}, "Conditional": {"Operator": 0, "Components": [...]}}`.
- **-200 node** (both as `ConversationScriptNode` and as a `ScriptNode` entry at the start of `Nodes`): ScriptNode shape with `NodeID -200`, `ContainerNodeID -1`, `Links []`, `ClassExtender {"ExtendedProperties": []}`. Give the in-`Nodes` copy an `OnEnterScripts` entry so hidden-node preservation is meaningful.

Hash values are invented integers (the editor never computes them). Enum ints: DisplayType Hidden 0 / Conversation 1 / Bark 2 / Overlay 3; Persistence None 0 / OnceEver 1 / OncePerConversation 2 / MarkAsRead 3; QuestionNodeTextDisplay ShowOnce 0 / Always 1 / Never 2; Operator And 0 / Or 1.

Nodes: the same eleven as PoE1 (IDs 0–10, same types, containers, links, DisplayType/Persistence, conditions — link 1→4 carries the nested condition, and PoE2 links are never "short"). Differences from PoE1:
- Narrator GUID is `6a99a109-0000-0000-0000-000000000000`.
- Node 4 (ScriptNode): its OnEnter script carries a non-empty `Conditional` (one call).
- Node 7 (narrator Bark): `HasVO true`, `ExternalVO ""`, `PlayType 1`, `NoPlayRandomWeight 1`.
- Node 8 (Overlay): `HasVO false`, `ExternalVO "narrator/canonical_0007"` (alias to node 7's file).
- Node 10: `HideSpeaker true`.

- [ ] **Step 5: Author the PoE2 stringtables** (`localized/en/…/canonical.stringtable`, `localized/de/…`): same shape and IDs as PoE1, `<Name>conversations\canonical</Name>`. Node 7 has a female variant here too (it is the VO node with `_fem.wem`).

- [ ] **Step 6: Add the two `.wem` placeholders** under `Voices/English(US)/narrator/` — a few bytes each, e.g.:

```powershell
$vo = 'DialogEditor.Tests/Fixtures/Canonical/poe2/PillarsOfEternityII_Data/StreamingAssets/Audio/Windows/Voices/English(US)/narrator'
New-Item -ItemType Directory -Force $vo | Out-Null
[IO.File]::WriteAllBytes("$vo/canonical_0007.wem", [byte[]](0x52,0x49,0x46,0x46))
[IO.File]::WriteAllBytes("$vo/canonical_0007_fem.wem", [byte[]](0x52,0x49,0x46,0x46))
```

Then check `git check-ignore -v` on both files; if `.gitignore` matches `*.wem`, add a negation `!DialogEditor.Tests/Fixtures/Canonical/**/*.wem`.

- [ ] **Step 7: Run to verify green**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalConversationTests"` → all four PASS.
Also: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FakePoe2Game|FullyQualifiedName~PatchInstaller|FullyQualifiedName~UnreadableStringTable"` → PASS (existing `FakePoe2Game` users unaffected).

- [ ] **Step 8: Commit**

```bash
git add .gitignore DialogEditor.Tests/Helpers/FakePoe2Game.cs DialogEditor.Tests/GameData/CanonicalConversationTests.cs DialogEditor.Tests/Fixtures/Canonical/poe2
git commit -m "test: canonical PoE2 conversation and FakePoe2Game.Canonical (#122)"
```

---

### Task 4: Round-trip tests over both fixtures

**Files:**
- Modify: `DialogEditor.Tests/GameData/CanonicalConversationTests.cs`

**Interfaces:**
- Consumes: `RoundTripChecks.*` (Task 1), `FakePoe1Game` (Task 2), `FakePoe2Game.Canonical()` (Task 3), `GameInstallFactAttribute` (existing).

- [ ] **Step 1: Write the round-trip tests**

```csharp
    // ── Round-trip: load → unchanged save → load (the #117 checks) ──────────────

    [Fact]
    public void Poe1_UnchangedSave_KeepsTheEditorModel()
    {
        using var game = new FakePoe1Game();
        var original = File.ReadAllText(game.ConvPath());
        var saved = Poe1ConversationSerializer.Serialize(original, RoundTripChecks.UnchangedSnapshot(game.Provider, game.File));
        Assert.Null(RoundTripChecks.ModelDifference(Poe1ConversationParser.ParseXml(original), Poe1ConversationParser.ParseXml(saved)));
    }

    /// PoE1's file-level check needs the game's own XmlSerializer model, which only an
    /// install has (docs/game-data-tests.md), so it runs only when DIALOGEDITOR_POE1_DIR is set.
    [GameInstallFact(GameInstallFactAttribute.Poe1Variable)]
    [Trait("Category", "GameData")]
    public void Poe1_UnchangedSave_IsEquivalentInTheGamesOwnModel()
    {
        using var game = new FakePoe1Game();
        var serializer = RoundTripChecks.Poe1GameSerializer(GameInstallFactAttribute.InstallDir(GameInstallFactAttribute.Poe1Variable));
        var original = File.ReadAllText(game.ConvPath());
        var saved = Poe1ConversationSerializer.Serialize(original, RoundTripChecks.UnchangedSnapshot(game.Provider, game.File));
        Assert.Null(RoundTripChecks.FirstLineDifference(
            RoundTripChecks.Poe1GameCanonical(serializer, original), RoundTripChecks.Poe1GameCanonical(serializer, saved)));
    }

    [Fact]
    public void Poe2_UnchangedSave_KeepsTheEditorModel()
    {
        using var game = FakePoe2Game.Canonical();
        var original = File.ReadAllText(game.ConvPath("canonical"));
        var saved = Poe2ConversationSerializer.Serialize(original, RoundTripChecks.UnchangedSnapshot(game.Provider, game.CanonicalFile));
        Assert.Null(RoundTripChecks.ModelDifference(Poe2ConversationParser.ParseJson(original), Poe2ConversationParser.ParseJson(saved)));
    }

    [Fact]
    public void Poe2_UnchangedSave_IsIdenticalJson()
    {
        using var game = FakePoe2Game.Canonical();
        var original = File.ReadAllText(game.ConvPath("canonical"));
        var saved = Poe2ConversationSerializer.Serialize(original, RoundTripChecks.UnchangedSnapshot(game.Provider, game.CanonicalFile));
        Assert.Null(RoundTripChecks.JsonDifference(original, saved));
    }

    [Theory]
    [InlineData("poe1", "en")] [InlineData("poe1", "de")]
    [InlineData("poe2", "en")] [InlineData("poe2", "de")]
    public void UnchangedStringTable_KeepsEveryEntry(string gameId, string lang)
    {
        using var poe1 = gameId == "poe1" ? new FakePoe1Game() : null;
        using var poe2 = gameId == "poe2" ? FakePoe2Game.Canonical() : null;
        var path = poe1?.StPath(lang) ?? poe2!.StPath(lang, "canonical");

        var original = File.ReadAllText(path);
        var entries  = RoundTripChecks.StringTableEntries(original);
        var saved    = StringTableSerializer.SerializeTranslations(original,
            entries.Select(e => new NodeTranslation(e.Id, e.DefaultText, e.FemaleText)));
        Assert.Equal(entries, RoundTripChecks.StringTableEntries(saved));
    }
```

Add `using DialogEditor.Core.Parsing;` and `using DialogEditor.Core.Serialization;`. Remove `Category=Canonical` from the GameData test if xunit reports both traits confusingly — `Category=GameData` must be present so CI filters it out.

- [ ] **Step 2: Run them**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalConversationTests"`
Expected: PASS for all, with the GameData one skipped when the env var is unset. (These are characterization tests over code that #111–#116 already fixed; the "red" step for this feature was Tasks 2–3.)

- [ ] **Step 3: Run the PoE1 game-model check locally**

```powershell
$env:DIALOGEDITOR_POE1_DIR = 'D:\Program Files (x86)\GOG Galaxy\Games\PillarsOfEternity'
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanonicalConversationTests"
```
Expected: PASS.

- [ ] **Step 4: If any round-trip test fails**, decide first whether the fixture is wrong (a property order or shape that shipped files never use — fix the fixture) or the editor is wrong (a shape shipped files do use — a real bug). For a real bug: file it with `gh issue create` (no local paths in the body), then mark the test `[Fact(Skip = "Exposes #NNN: <one line>")]` and note it in the README's "Known gaps" section. Do not fix production code in this branch.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/GameData/CanonicalConversationTests.cs
git commit -m "test: round-trip the canonical conversations through an unchanged save (#122)"
```

---

### Task 5: README, docs, and verification in the running app

**Files:**
- Create: `DialogEditor.Tests/Fixtures/Canonical/README.md`
- Modify: `docs/game-data-tests.md`
- Modify: `.claude/skills/running-the-app/SKILL.md`

- [ ] **Step 1: Write the README.** Sections: what the fixtures are and why (issue #122); "Written from the documented formats, not copied from game data"; how to open one in the app (set the game folder to `…/Fixtures/Canonical/poe1` or `…/poe2`, open conversation `canonical`); one table per game mapping **node ID → constructs** (from the Task 2/3 tables, plus the links, conditions, scripts, text variants, VO files and the -200 node); "Known gaps" (issues from Task 4 Step 4, or "none"); "Changing the fixture" (keep the tables and `CanonicalConversationTests` in step; the coverage test fails if a construct disappears).

- [ ] **Step 2: Cross-link it.** In `docs/game-data-tests.md` add a short section: for a small, deterministic, CI-safe baseline use the canonical conversations (`CanonicalConversationTests`, README link); they run on every `dotnet test` except the PoE1 game-model check. In `.claude/skills/running-the-app/SKILL.md`, next to where the game folder is chosen, add: "For a deterministic conversation covering every construct, point the game folder at `DialogEditor.Tests/Fixtures/Canonical/poe1` or `…/poe2` and open `canonical` (map in that folder's README)."

- [ ] **Step 3: Full fast suite**

Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`
Expected: PASS.

- [ ] **Step 4: Verify in the running app** (REQUIRED SUB-SKILL: `running-the-app`). For each of `poe1` and `poe2`: point the game folder at the fixture copy **in the source tree**, open `canonical`, screenshot the canvas, confirm all 11 nodes render, the status bar/log shows no warnings, and (PoE2) node 7 shows VO found. Switch language to `de` once and confirm German text. Do not save.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/Fixtures/Canonical/README.md docs/game-data-tests.md .claude/skills/running-the-app/SKILL.md
git commit -m "docs: map the canonical conversations and point the tooling at them (#122)"
```
