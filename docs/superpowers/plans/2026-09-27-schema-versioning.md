# Schema Version Validation & Migration Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Refuse `.dialogproject` / `ConversationPatch` / `.patchlist` files newer than this build reads, and chain older files forward through a JSON-level migration pipeline, in the editor, the Patch Manager and `dialog-patcher` (issues #62 + #104).

**Architecture:** A new `DialogEditor.Patch/Schema/` namespace holds `SchemaMigrator` (a registry of `vN → vN+1` steps that rewrite a `JsonObject`) and `UnsupportedSchemaVersionException`. The three serializers parse to `JsonNode`, migrate, then deserialize, so every existing load path gets the check. The view-model layer turns the exception into a localised message through one helper, `SchemaVersionMessages`. The CLI maps it to exit code 4.

**Tech Stack:** .NET 10, System.Text.Json (`JsonNode`), xUnit, Avalonia 11 (resources in `SharedStrings.axaml`), CommunityToolkit.Mvvm.

**Spec:** `docs/superpowers/specs/2026-09-27-schema-versioning-design.md`

## Global Constraints

- Current versions are **frozen**: `DialogProject.CurrentSchemaVersion = 1`, `ConversationPatch.CurrentSchemaVersion = 2`, `PatchList.CurrentSchemaVersion = 1`. Do not bump them.
- Strict red/green TDD: every behaviour gets a failing test first (CLAUDE.md).
- No user-visible text inline in XAML/C#. Editor and Patch Manager text goes in `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml` (or `DialogEditor.Avalonia/Resources/Strings.axaml` for editor-only keys). `dialog-patcher` is `[NotLocalised]` English and uses the exception's English `Message`.
- Every caught exception in production code is logged with `AppLog.Warn`/`AppLog.Error` before the status update (CLAUDE.md). No bare `catch { }`.
- Every new interactive control carries a `ToolTip`. (Only one control changes here: the Cancel button of the existing `ConfirmDialog` gets hidden in info mode.)
- Run tests headless: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. A bare `dotnet test` launches the GUI.
- Tests run serially (AppSettings/Loc are global). Every VM test calls `Loc.Configure(new StubStringProvider())` in its constructor.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`; reference `#62` / `#104`.
- CHANGELOG.md is frozen. Don't touch it.

## File Map

| File | Responsibility |
|---|---|
| Create `DialogEditor.Patch/Schema/SchemaFileKind.cs` | enum of the three versioned file kinds |
| Create `DialogEditor.Patch/Schema/UnsupportedSchemaVersionException.cs` | typed "too new" error with Kind / Found / Supported / ConversationName |
| Create `DialogEditor.Patch/Schema/ISchemaMigrationStep.cs` | one `vN → vN+1` JSON rewrite |
| Create `DialogEditor.Patch/Schema/Steps/ConversationPatchV1ToV2.cs` | the one real historical step (no-op) |
| Create `DialogEditor.Patch/Schema/SchemaMigrator.cs` | version read, refuse-newer, chain steps, nested patches, registry validation |
| Modify `DialogEditor.Patch/DialogProjectSerializer.cs`, `PatchSerializer.cs`, `PatchListSerializer.cs` | parse → migrate → deserialize |
| Modify `DialogEditor.Patch/Diff/DiffException.cs`, `Diff/ProjectVersionLoader.cs` | `DiffExceptionKind.UnsupportedSchema` |
| Create `DialogEditor.ViewModels/Services/SchemaVersionMessages.cs` | exception → localised message |
| Modify `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml` | message + format-name keys |
| Modify `DialogEditor.Avalonia/Resources/Strings.axaml` | `Status_DiffUnsupportedSchema` |
| Modify `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` | open / autosave / git-conflict / merge surfaces + `ShowUnsupportedFormat` seam |
| Modify `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` | map the new diff kind |
| Modify `DialogEditor.Avalonia.Shared/ConfirmDialog.axaml(.cs)` | info mode (Cancel hidden) |
| Modify `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` | wire `ShowUnsupportedFormat` |
| Modify `DialogEditor.ViewModels/ViewModels/PatchManagerViewModel.cs` | refuse on add; refuse a whole `.patchlist` |
| Modify `DialogEditor.PatchCli/PatcherCommand.cs` | exit 4 + help text |
| Modify `FORMAT.md` | project-format + versioning-policy section |
| Tests: `DialogEditor.Tests/Patch/Schema/*`, `DialogEditor.Tests/Patch/Schema/Fixtures/*`, VM / CLI / localisation tests listed per task | |

---

### Task 1: Schema core — `SchemaMigrator`, steps, exception

**Files:**
- Create: `DialogEditor.Patch/Schema/SchemaFileKind.cs`, `UnsupportedSchemaVersionException.cs`, `ISchemaMigrationStep.cs`, `Steps/ConversationPatchV1ToV2.cs`, `SchemaMigrator.cs`
- Test: `DialogEditor.Tests/Patch/Schema/SchemaMigratorTests.cs`

**Interfaces:**
- Produces:
  - `enum SchemaFileKind { Project, ConversationPatch, PatchList }`
  - `sealed class UnsupportedSchemaVersionException : InvalidDataException` with `SchemaFileKind Kind`, `int Found`, `int Supported`, `string? ConversationName`, ctor `(SchemaFileKind kind, int found, int supported, string? conversationName = null)`
  - `interface ISchemaMigrationStep { SchemaFileKind Kind { get; } int FromVersion { get; } void Apply(JsonObject root); }`
  - `sealed class SchemaMigrator(IReadOnlyList<ISchemaMigrationStep> steps, IReadOnlyDictionary<SchemaFileKind,int> currentVersions)`, with:
    - `static IReadOnlyList<ISchemaMigrationStep> RegisteredSteps`
    - `static IReadOnlyDictionary<SchemaFileKind,int> CurrentVersions`
    - `static SchemaMigrator Default`
    - `void Migrate(JsonObject root, SchemaFileKind kind)`
    - `static IReadOnlyList<string> FindRegistryProblems(IReadOnlyList<ISchemaMigrationStep> steps, IReadOnlyDictionary<SchemaFileKind,int> currentVersions)`

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Patch/Schema/SchemaMigratorTests.cs
using System.Text.Json.Nodes;
using DialogEditor.Patch;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Tests.Patch.Schema;

public class SchemaMigratorTests
{
    /// Test-only step: records that it ran by appending its FromVersion to "Trail".
    private sealed class TrailStep(SchemaFileKind kind, int from) : ISchemaMigrationStep
    {
        public SchemaFileKind Kind => kind;
        public int FromVersion => from;
        public void Apply(JsonObject root)
        {
            var trail = root["Trail"] as JsonArray ?? [];
            trail.Add(from);
            root["Trail"] = trail;
        }
    }

    private static readonly Dictionary<SchemaFileKind, int> Four = new()
    {
        [SchemaFileKind.Project] = 4, [SchemaFileKind.ConversationPatch] = 4, [SchemaFileKind.PatchList] = 4,
    };

    private static SchemaMigrator ChainOf(SchemaFileKind kind) =>
        new([new TrailStep(kind, 1), new TrailStep(kind, 2), new TrailStep(kind, 3)], Four);

    private static JsonObject Doc(int version) => new() { ["SchemaVersion"] = version };

    [Fact]
    public void OlderFile_RunsEveryStepInOrder_AndEndsAtCurrent()
    {
        var root = Doc(1);
        ChainOf(SchemaFileKind.PatchList).Migrate(root, SchemaFileKind.PatchList);

        Assert.Equal([1, 2, 3], root["Trail"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(4, root["SchemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void FileInTheMiddle_RunsOnlyTheRemainingSteps()
    {
        var root = Doc(3);
        ChainOf(SchemaFileKind.PatchList).Migrate(root, SchemaFileKind.PatchList);

        Assert.Equal([3], root["Trail"]!.AsArray().Select(n => n!.GetValue<int>()));
    }

    [Fact]
    public void CurrentFile_IsUntouched()
    {
        var root = Doc(4);
        ChainOf(SchemaFileKind.PatchList).Migrate(root, SchemaFileKind.PatchList);

        Assert.Equal("""{"SchemaVersion":4}""", root.ToJsonString());
    }

    [Fact]
    public void StepsForOtherKinds_AreIgnored()
    {
        var root = Doc(3);
        new SchemaMigrator([new TrailStep(SchemaFileKind.Project, 3)], Four)
            .Migrate(root, SchemaFileKind.PatchList);

        Assert.Null(root["Trail"]);
        Assert.Equal(4, root["SchemaVersion"]!.GetValue<int>());
    }

    [Fact]
    public void NewerFile_Throws_WithKindAndVersions()
    {
        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() =>
            ChainOf(SchemaFileKind.PatchList).Migrate(Doc(5), SchemaFileKind.PatchList));

        Assert.Equal(SchemaFileKind.PatchList, ex.Kind);
        Assert.Equal(5, ex.Found);
        Assert.Equal(4, ex.Supported);
        Assert.Null(ex.ConversationName);
    }

    [Theory]
    [InlineData("""{}""")]
    [InlineData("""{"SchemaVersion":0}""")]
    [InlineData("""{"SchemaVersion":-3}""")]
    [InlineData("""{"SchemaVersion":"two"}""")]
    public void MissingOrInvalidVersion_IsRejectedAsInvalidData_NotMigrated(string json)
    {
        var ex = Assert.Throws<InvalidDataException>(() =>
            ChainOf(SchemaFileKind.PatchList).Migrate(JsonNode.Parse(json)!.AsObject(), SchemaFileKind.PatchList));

        Assert.IsNotType<UnsupportedSchemaVersionException>(ex);
    }

    [Fact]
    public void Project_MigratesEveryNestedPatch()
    {
        var root = new JsonObject
        {
            ["SchemaVersion"] = 4,
            ["Patches"] = new JsonObject { ["a"] = Doc(2), ["b"] = Doc(4) },
        };
        ChainOf(SchemaFileKind.ConversationPatch).Migrate(root, SchemaFileKind.Project);

        var a = root["Patches"]!["a"]!;
        Assert.Equal([2, 3], a["Trail"]!.AsArray().Select(n => n!.GetValue<int>()));
        Assert.Equal(4, a["SchemaVersion"]!.GetValue<int>());
        Assert.Null(root["Patches"]!["b"]!["Trail"]);
    }

    [Fact]
    public void Project_NewerNestedPatch_Throws_NamingTheConversation()
    {
        var root = new JsonObject
        {
            ["SchemaVersion"] = 4,
            ["Patches"] = new JsonObject { ["greeting"] = Doc(9) },
        };

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() =>
            ChainOf(SchemaFileKind.ConversationPatch).Migrate(root, SchemaFileKind.Project));

        Assert.Equal(SchemaFileKind.ConversationPatch, ex.Kind);
        Assert.Equal(9, ex.Found);
        Assert.Equal("greeting", ex.ConversationName);
    }

    [Fact]
    public void Project_NewerRoot_IsRefusedBeforeLookingAtPatches()
    {
        var root = new JsonObject { ["SchemaVersion"] = 5, ["Patches"] = "not even an object" };

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() =>
            ChainOf(SchemaFileKind.ConversationPatch).Migrate(root, SchemaFileKind.Project));

        Assert.Equal(SchemaFileKind.Project, ex.Kind);
    }

    // ── The real registry (#104 invariants) ──────────────────────────────

    [Fact]
    public void RealRegistry_HasExactlyOneStepPerVersion_NoGaps()
        => Assert.Empty(SchemaMigrator.FindRegistryProblems(SchemaMigrator.RegisteredSteps, SchemaMigrator.CurrentVersions));

    [Fact]
    public void RealCurrentVersions_MatchTheModelConstants()
    {
        Assert.Equal(DialogProject.CurrentSchemaVersion,     SchemaMigrator.CurrentVersions[SchemaFileKind.Project]);
        Assert.Equal(ConversationPatch.CurrentSchemaVersion, SchemaMigrator.CurrentVersions[SchemaFileKind.ConversationPatch]);
        Assert.Equal(PatchList.CurrentSchemaVersion,         SchemaMigrator.CurrentVersions[SchemaFileKind.PatchList]);
    }

    [Fact]
    public void FindRegistryProblems_ReportsAGap()
    {
        var problems = SchemaMigrator.FindRegistryProblems(
            [new TrailStep(SchemaFileKind.PatchList, 1), new TrailStep(SchemaFileKind.PatchList, 3)],
            new Dictionary<SchemaFileKind, int> { [SchemaFileKind.PatchList] = 4 });

        Assert.Contains(problems, p => p.Contains("PatchList") && p.Contains('2'));
    }

    [Fact]
    public void FindRegistryProblems_ReportsADuplicate_AndAStepAtOrPastCurrent()
    {
        var problems = SchemaMigrator.FindRegistryProblems(
            [new TrailStep(SchemaFileKind.PatchList, 1), new TrailStep(SchemaFileKind.PatchList, 1),
             new TrailStep(SchemaFileKind.PatchList, 2)],
            new Dictionary<SchemaFileKind, int> { [SchemaFileKind.PatchList] = 2 });

        Assert.Equal(2, problems.Count);   // duplicate v1, and v2 step with current = 2
    }

    [Fact]
    public void RealPatchStep_1To2_KeepsContent_AndSetsVersion2()
    {
        var root = new JsonObject
        {
            ["ConversationName"] = "c", ["SchemaVersion"] = 1,
            ["AddedNodes"] = new JsonArray(), ["DeletedNodeIds"] = new JsonArray(), ["ModifiedNodes"] = new JsonArray(),
        };
        var before = root.DeepClone();
        before["SchemaVersion"] = 2;

        SchemaMigrator.Default.Migrate(root, SchemaFileKind.ConversationPatch);

        Assert.True(JsonNode.DeepEquals(before, root));
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~SchemaMigratorTests"`
Expected: build FAILS with `The type or namespace name 'Schema' does not exist in the namespace 'DialogEditor.Patch'`.

- [ ] **Step 3: Write the implementation**

```csharp
// DialogEditor.Patch/Schema/SchemaFileKind.cs
namespace DialogEditor.Patch.Schema;

/// The three JSON file formats that carry a SchemaVersion (FORMAT.md, "Versioning").
/// A ConversationPatch also appears nested inside every .dialogproject.
public enum SchemaFileKind { Project, ConversationPatch, PatchList }
```

```csharp
// DialogEditor.Patch/Schema/UnsupportedSchemaVersionException.cs
namespace DialogEditor.Patch.Schema;

/// A file was written by a newer editor than this build (issue #62). Loading is refused
/// before anything is deserialised, so nothing can be dropped on save or half-applied.
/// Derives from InvalidDataException so generic "could not load" handlers still treat it
/// as a load failure; callers that can say "update the editor" catch it first.
/// Message is English (logs and the English-only dialog-patcher); the GUI builds its own
/// localised text from Kind / Found / Supported via SchemaVersionMessages.
public sealed class UnsupportedSchemaVersionException(
    SchemaFileKind kind, int found, int supported, string? conversationName = null)
    : InvalidDataException(BuildMessage(kind, found, supported, conversationName))
{
    public SchemaFileKind Kind             { get; } = kind;
    public int            Found            { get; } = found;
    public int            Supported        { get; } = supported;
    /// Set when the offender is a ConversationPatch nested inside a project.
    public string?        ConversationName { get; } = conversationName;

    private static string BuildMessage(SchemaFileKind kind, int found, int supported, string? conversation)
    {
        var what  = kind switch
        {
            SchemaFileKind.Project           => "project format",
            SchemaFileKind.ConversationPatch => "conversation patch format",
            _                                => "load-order format",
        };
        var where = conversation is null ? "" : $" (conversation '{conversation}')";
        return $"This file uses {what} {found}{where}, but this version reads up to {what} {supported}. " +
               "It was saved by a newer Pillars Dialog Editor; update the editor / dialog-patcher to read it.";
    }
}
```

```csharp
// DialogEditor.Patch/Schema/ISchemaMigrationStep.cs
using System.Text.Json.Nodes;

namespace DialogEditor.Patch.Schema;

/// Rewrites one file kind's raw JSON from FromVersion to FromVersion + 1 (issue #104).
/// Steps work on JSON rather than records because an old shape may no longer bind to
/// today's records: a renamed field would be silently dropped by the deserializer before
/// a post-deserialise step could move it.
/// Contract: pure (no I/O, deterministic) and scoped to its own document. Nested patches
/// inside a project are migrated separately by SchemaMigrator. Don't set SchemaVersion;
/// the migrator does that after Apply.
public interface ISchemaMigrationStep
{
    SchemaFileKind Kind        { get; }
    int            FromVersion { get; }
    void Apply(JsonObject root);
}
```

```csharp
// DialogEditor.Patch/Schema/Steps/ConversationPatchV1ToV2.cs
using System.Text.Json.Nodes;

namespace DialogEditor.Patch.Schema.Steps;

/// ConversationPatch v2 (commit 01b2f49) added Translations and NodeComments. Both
/// default to empty when absent, so a v1 patch needs no rewrite; the step exists so
/// the chain has no gap and FindRegistryProblems stays meaningful.
public sealed class ConversationPatchV1ToV2 : ISchemaMigrationStep
{
    public SchemaFileKind Kind        => SchemaFileKind.ConversationPatch;
    public int            FromVersion => 1;
    public void Apply(JsonObject root) { }
}
```

```csharp
// DialogEditor.Patch/Schema/SchemaMigrator.cs
using System.Text.Json.Nodes;
using DialogEditor.Patch.Schema.Steps;

namespace DialogEditor.Patch.Schema;

/// The single place older files are brought forward and newer files refused (issues
/// #62 + #104). The three serializers call Default.Migrate on the parsed JSON before
/// deserialising, so no load path can bypass it.
///
/// To bump a format: raise the model's CurrentSchemaVersion, add ONE step here from the
/// old version, and add a fixture file at the old version under
/// DialogEditor.Tests/Patch/Schema/Fixtures. The registry test fails until the step exists.
public sealed class SchemaMigrator(
    IReadOnlyList<ISchemaMigrationStep> steps,
    IReadOnlyDictionary<SchemaFileKind, int> currentVersions)
{
    private const string VersionKey = "SchemaVersion";
    private const string PatchesKey = "Patches";

    public static IReadOnlyList<ISchemaMigrationStep> RegisteredSteps { get; } =
    [
        new ConversationPatchV1ToV2(),
    ];

    public static IReadOnlyDictionary<SchemaFileKind, int> CurrentVersions { get; } =
        new Dictionary<SchemaFileKind, int>
        {
            [SchemaFileKind.Project]           = DialogProject.CurrentSchemaVersion,
            [SchemaFileKind.ConversationPatch] = ConversationPatch.CurrentSchemaVersion,
            [SchemaFileKind.PatchList]         = PatchList.CurrentSchemaVersion,
        };

    public static SchemaMigrator Default { get; } = new(RegisteredSteps, CurrentVersions);

    /// Brings <paramref name="root"/> up to the current version in place. Throws
    /// UnsupportedSchemaVersionException when it (or, for a project, any nested patch) is
    /// newer than supported, and InvalidDataException when SchemaVersion is missing or < 1.
    /// A project's own steps run before its nested patches, so a project step may
    /// restructure "Patches" first.
    public void Migrate(JsonObject root, SchemaFileKind kind)
    {
        MigrateOne(root, kind, conversationName: null);

        if (kind == SchemaFileKind.Project && root[PatchesKey] is JsonObject patches)
            foreach (var (name, patch) in patches)
                if (patch is JsonObject patchObject)
                    MigrateOne(patchObject, SchemaFileKind.ConversationPatch, name);
    }

    private void MigrateOne(JsonObject root, SchemaFileKind kind, string? conversationName)
    {
        var current = currentVersions[kind];
        var found   = ReadVersion(root, kind);

        if (found > current)
            throw new UnsupportedSchemaVersionException(kind, found, current, conversationName);

        for (var v = found; v < current; v++)
        {
            var step = steps.Single(s => s.Kind == kind && s.FromVersion == v);
            step.Apply(root);
            root[VersionKey] = v + 1;
        }
    }

    private static int ReadVersion(JsonObject root, SchemaFileKind kind)
    {
        // Every save writes SchemaVersion, so missing / non-integer / < 1 means the file was
        // hand-edited or damaged. Reject rather than guess which version it is.
        if (root[VersionKey] is JsonValue value && value.TryGetValue<int>(out var version) && version >= 1)
            return version;
        throw new InvalidDataException(
            $"Not a valid {kind} file: '{VersionKey}' is missing or is not a whole number of at least 1.");
    }

    /// Registry invariants (#104): for each kind, versions 1 .. current-1 each have exactly
    /// one step, and no step starts at or past current. Checked by a unit test, not at runtime.
    public static IReadOnlyList<string> FindRegistryProblems(
        IReadOnlyList<ISchemaMigrationStep> steps,
        IReadOnlyDictionary<SchemaFileKind, int> currentVersions)
    {
        var problems = new List<string>();
        foreach (var (kind, current) in currentVersions)
        {
            var ofKind = steps.Where(s => s.Kind == kind).ToList();
            for (var v = 1; v < current; v++)
            {
                var count = ofKind.Count(s => s.FromVersion == v);
                if (count == 0) problems.Add($"{kind}: no step from version {v}");
                if (count > 1)  problems.Add($"{kind}: {count} steps from version {v}");
            }
            foreach (var s in ofKind.Where(s => s.FromVersion < 1 || s.FromVersion >= current))
                problems.Add($"{kind}: step from version {s.FromVersion} is outside 1..{current - 1}");
        }
        return problems;
    }
}
```

- [ ] **Step 4: Run the tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~SchemaMigratorTests"`
Expected: all PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Patch/Schema DialogEditor.Tests/Patch/Schema/SchemaMigratorTests.cs
git commit -m "feat(schema): SchemaMigrator chains JSON-level steps and refuses newer files (#62, #104)"
```

---

### Task 2: Serializers route every load through the migrator

**Files:**
- Modify: `DialogEditor.Patch/DialogProjectSerializer.cs:19-21`, `DialogEditor.Patch/PatchSerializer.cs:19-21`, `DialogEditor.Patch/PatchListSerializer.cs:22-27`
- Create: `DialogEditor.Tests/Patch/Schema/Fixtures/project-v1-patch-v1.dialogproject`
- Modify: `DialogEditor.Tests/DialogEditor.Tests.csproj` (copy fixtures to output)
- Test: `DialogEditor.Tests/Patch/Schema/SerializerSchemaTests.cs`

**Interfaces:**
- Consumes: `SchemaMigrator.Default.Migrate(JsonObject, SchemaFileKind)`, `UnsupportedSchemaVersionException` (Task 1)
- Produces: `PatchListSerializer.Deserialize(string json)` (new public method). Every `Deserialize` / `LoadFromFile` now throws `UnsupportedSchemaVersionException` for newer files and `InvalidDataException` for missing / `< 1` versions.

- [ ] **Step 1: Add the fixture and copy-to-output**

`DialogEditor.Tests/Patch/Schema/Fixtures/project-v1-patch-v1.dialogproject`. This is a v1 project containing a v1 patch, the shape before ConversationPatch v2. Keep it forever; it's the "oldest file still opens" test.

```json
{
  "Name": "LegacyV1",
  "SchemaVersion": 1,
  "Patches": {
    "greeting": {
      "ConversationName": "greeting",
      "SchemaVersion": 1,
      "AddedNodes": [],
      "DeletedNodeIds": [ 7 ],
      "ModifiedNodes": []
    }
  }
}
```

In `DialogEditor.Tests/DialogEditor.Tests.csproj`, inside an `<ItemGroup>`:

```xml
<None Update="Patch\Schema\Fixtures\**\*" CopyToOutputDirectory="PreserveNewest" />
```

- [ ] **Step 2: Write the failing tests**

```csharp
// DialogEditor.Tests/Patch/Schema/SerializerSchemaTests.cs
using DialogEditor.Patch;
using DialogEditor.Patch.Schema;

namespace DialogEditor.Tests.Patch.Schema;

public class SerializerSchemaTests
{
    private static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Patch", "Schema", "Fixtures", name));

    [Fact]
    public void Project_NewerVersion_IsRefused()
    {
        var json = DialogProjectSerializer.Serialize(DialogProject.Empty("P"))
            .Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 2");

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => DialogProjectSerializer.Deserialize(json));
        Assert.Equal(SchemaFileKind.Project, ex.Kind);
    }

    [Fact]
    public void Project_NewerVersion_IsRefusedEvenWhenItsShapeWouldNotParse()
    {
        // A future format might change a field's type; the version check must win over a JSON type error.
        const string json = """{ "Name": 42, "SchemaVersion": 7, "Patches": [ "future shape" ] }""";

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => DialogProjectSerializer.Deserialize(json));
        Assert.Equal(7, ex.Found);
    }

    [Fact]
    public void Project_WithNewerNestedPatch_IsRefused_NamingTheConversation()
    {
        var project = DialogProject.Empty("P")
            .WithPatch(new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [7], []));
        var json = DialogProjectSerializer.Serialize(project)
            .Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 3");

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => DialogProjectSerializer.Deserialize(json));
        Assert.Equal(SchemaFileKind.ConversationPatch, ex.Kind);
        Assert.Equal("greeting", ex.ConversationName);
    }

    [Fact]
    public void Project_MissingVersion_IsInvalidData()
        => Assert.Throws<InvalidDataException>(() =>
            DialogProjectSerializer.Deserialize("""{ "Name": "P", "Patches": {} }"""));

    [Fact]
    public void Project_CurrentVersion_RoundTripsByteIdentical()
    {
        var project = DialogProject.Empty("P")
            .WithPatch(new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [], [7], []))
            .WithNewConversation("brand_new");
        var json = DialogProjectSerializer.Serialize(project);

        Assert.Equal(json, DialogProjectSerializer.Serialize(DialogProjectSerializer.Deserialize(json)));
    }

    [Fact]
    public void Fixture_V1ProjectWithV1Patch_OpensAtCurrentVersions()
    {
        var project = DialogProjectSerializer.Deserialize(Fixture("project-v1-patch-v1.dialogproject"));

        Assert.Equal("LegacyV1", project.Name);
        Assert.Equal(DialogProject.CurrentSchemaVersion, project.SchemaVersion);
        var patch = project.Patches["greeting"];
        Assert.Equal(ConversationPatch.CurrentSchemaVersion, patch.SchemaVersion);
        Assert.Equal([7], patch.DeletedNodeIds);
        Assert.Empty(patch.Translations);
    }

    [Fact]
    public void Patch_NewerVersion_IsRefused()
    {
        var json = PatchSerializer.Serialize(new ConversationPatch("c", ConversationPatch.CurrentSchemaVersion, [], [], []))
            .Replace("\"SchemaVersion\": 2", "\"SchemaVersion\": 3");

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => PatchSerializer.Deserialize(json));
        Assert.Equal(SchemaFileKind.ConversationPatch, ex.Kind);
    }

    [Fact]
    public void Patch_V1_IsMigratedToCurrent()
    {
        var json = PatchSerializer.Serialize(new ConversationPatch("c", 1, [], [3], []));

        Assert.Equal(ConversationPatch.CurrentSchemaVersion, PatchSerializer.Deserialize(json).SchemaVersion);
    }

    [Fact]
    public void PatchList_NewerVersion_IsRefused()
    {
        const string json = """{ "SchemaVersion": 2, "GameFolder": "", "Entries": [] }""";

        var ex = Assert.Throws<UnsupportedSchemaVersionException>(() => PatchListSerializer.Deserialize(json));
        Assert.Equal(SchemaFileKind.PatchList, ex.Kind);
    }

    [Fact]
    public void PatchList_LoadFromFile_UsesTheSameCheck()
    {
        var path = Path.Combine(Path.GetTempPath(), $"schema_{Guid.NewGuid():N}.patchlist");
        File.WriteAllText(path, """{ "SchemaVersion": 2, "GameFolder": "", "Entries": [] }""");
        try
        {
            Assert.Throws<UnsupportedSchemaVersionException>(() => PatchListSerializer.LoadFromFile(path));
        }
        finally { try { File.Delete(path); } catch (Exception) { /* best-effort */ } }
    }
}
```

- [ ] **Step 3: Run the tests to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~SerializerSchemaTests"`
Expected: build FAILS on `PatchListSerializer.Deserialize` (it doesn't exist yet). After you add a stub, the refusal tests FAIL because newer files deserialise silently.

- [ ] **Step 4: Implement parse → migrate → deserialize**

In each serializer, add `using System.Text.Json.Nodes;` and `using DialogEditor.Patch.Schema;`, then:

```csharp
// DialogProjectSerializer.cs — replace Deserialize
/// Parses, refuses newer / migrates older (SchemaMigrator, issues #62 + #104), then binds.
public static DialogProject Deserialize(string json)
    => Migrated(json, SchemaFileKind.Project).Deserialize<DialogProject>(Options)
       ?? throw new InvalidOperationException("Deserialised project was null.");

private static JsonObject Migrated(string json, SchemaFileKind kind)
{
    var root = JsonNode.Parse(json) as JsonObject
               ?? throw new InvalidDataException("Project file is not a JSON object.");
    SchemaMigrator.Default.Migrate(root, kind);
    return root;
}
```

```csharp
// PatchSerializer.cs — replace Deserialize
public static ConversationPatch Deserialize(string json)
{
    var root = JsonNode.Parse(json) as JsonObject
               ?? throw new InvalidDataException("Patch file is not a JSON object.");
    SchemaMigrator.Default.Migrate(root, SchemaFileKind.ConversationPatch);
    return root.Deserialize<ConversationPatch>(Options)
           ?? throw new InvalidOperationException("Deserialised patch was null.");
}
```

```csharp
// PatchListSerializer.cs — replace LoadFromFile, add Deserialize
public static PatchList LoadFromFile(string path) => Deserialize(File.ReadAllText(path));

public static PatchList Deserialize(string json)
{
    var root = JsonNode.Parse(json) as JsonObject
               ?? throw new InvalidDataException("Load-order file is not a JSON object.");
    SchemaMigrator.Default.Migrate(root, SchemaFileKind.PatchList);
    return root.Deserialize<PatchList>(Options)
           ?? throw new InvalidOperationException("Deserialised PatchList was null.");
}
```

(`JsonNode.Deserialize<T>(options)` is the `JsonSerializer` extension on `JsonNode`, available since .NET 6.)

- [ ] **Step 5: Run the new tests, then the whole headless suite**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~SerializerSchemaTests"`. Expected: PASS.
Run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. Expected: PASS.
If an existing test fails because its hand-written JSON has no `SchemaVersion`, add `"SchemaVersion": <current>` to that JSON. Real saves always write the field, so such a fixture never represented a real file. Do **not** relax the migrator.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Patch/*Serializer.cs DialogEditor.Tests/Patch/Schema DialogEditor.Tests/DialogEditor.Tests.csproj
git commit -m "feat(schema): every serializer loads through SchemaMigrator; v1 fixture (#62, #104)"
```

---

### Task 3: Localised message helper + resources

**Files:**
- Create: `DialogEditor.ViewModels/Services/SchemaVersionMessages.cs`
- Modify: `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml` (new section at the end, before `</ResourceDictionary>`)
- Test: `DialogEditor.Tests/Localisation/SchemaVersionMessageTests.cs`

**Interfaces:**
- Consumes: `UnsupportedSchemaVersionException` (Task 1)
- Produces: `static string SchemaVersionMessages.TooNew(UnsupportedSchemaVersionException ex, string fileName)` and `static string SchemaVersionMessages.Title` (dialog title)

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/Localisation/SchemaVersionMessageTests.cs
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Patch.Schema;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

public class SchemaVersionMessageTests
{
    [Fact]
    public void TopLevel_UsesTheTopLevelTemplate()
    {
        Loc.Configure(new StubStringProvider());
        var text = SchemaVersionMessages.TooNew(new UnsupportedSchemaVersionException(SchemaFileKind.Project, 2, 1), "mod.dialogproject");
        Assert.StartsWith("Schema_TooNew", text);
        Assert.DoesNotContain("Schema_TooNewInConversation", text);
    }

    [Fact]
    public void Nested_UsesTheConversationTemplate()
    {
        Loc.Configure(new StubStringProvider());
        var text = SchemaVersionMessages.TooNew(
            new UnsupportedSchemaVersionException(SchemaFileKind.ConversationPatch, 3, 2, "greeting"), "mod.dialogproject");
        Assert.StartsWith("Schema_TooNewInConversation", text);
    }
}

public class SchemaVersionMessageResourceEndToEndTests
{
    public SchemaVersionMessageResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void TopLevel_RendersFromSharedStrings()
        => Assert.Equal(
            "'mod.dialogproject' was saved by a newer version of Pillars Dialog Editor (project format 2). " +
            "This version reads up to project format 1. Update to the latest version to open it.",
            SchemaVersionMessages.TooNew(new UnsupportedSchemaVersionException(SchemaFileKind.Project, 2, 1), "mod.dialogproject"));

    [AvaloniaFact]
    public void Nested_RendersTheConversationName()
        => Assert.Contains("'greeting'",
            SchemaVersionMessages.TooNew(
                new UnsupportedSchemaVersionException(SchemaFileKind.ConversationPatch, 3, 2, "greeting"), "mod.dialogpack"));

    [AvaloniaFact]
    public void EveryKind_HasAFormatName()
    {
        foreach (var kind in Enum.GetValues<SchemaFileKind>())
            Assert.DoesNotContain("Schema_Format_",
                SchemaVersionMessages.TooNew(new UnsupportedSchemaVersionException(kind, 9, 1), "f"));
    }

    [AvaloniaFact]
    public void Title_RendersFromSharedStrings()
        => Assert.Equal("Newer file format", SchemaVersionMessages.Title);
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~SchemaVersionMessage"`
Expected: build FAILS (`SchemaVersionMessages` doesn't exist).

- [ ] **Step 3: Implement**

`SharedStrings.axaml`, new section:

```xml
    <!-- ─── File-format version checks (issues #62, #104) — editor and Patch Manager ── -->
    <sys:String x:Key="Schema_TooNew_Title">Newer file format</sys:String>
    <!-- {0} file name, {1} format name, {2} version in the file, {3} highest version this build reads -->
    <sys:String x:Key="Schema_TooNew">'{0}' was saved by a newer version of Pillars Dialog Editor ({1} {2}). This version reads up to {1} {3}. Update to the latest version to open it.</sys:String>
    <!-- {4} conversation name -->
    <sys:String x:Key="Schema_TooNewInConversation">'{0}' contains conversation '{4}' saved by a newer version of Pillars Dialog Editor ({1} {2}). This version reads up to {1} {3}. Update to the latest version to open it.</sys:String>
    <sys:String x:Key="Schema_Format_Project">project format</sys:String>
    <sys:String x:Key="Schema_Format_ConversationPatch">conversation patch format</sys:String>
    <sys:String x:Key="Schema_Format_PatchList">load-order format</sys:String>
```

```csharp
// DialogEditor.ViewModels/Services/SchemaVersionMessages.cs
using DialogEditor.Patch.Schema;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// Turns UnsupportedSchemaVersionException into the localised "update to open it" text.
/// It's shared by the editor and the Patch Manager so both word it identically.
/// DialogEditor.Patch can't reach Loc, which is why this lives here rather than on the exception.
public static class SchemaVersionMessages
{
    public static string Title => Loc.Get("Schema_TooNew_Title");

    public static string TooNew(UnsupportedSchemaVersionException ex, string fileName)
    {
        var format = Loc.Get($"Schema_Format_{ex.Kind}");
        return ex.ConversationName is null
            ? Loc.Format("Schema_TooNew", fileName, format, ex.Found, ex.Supported)
            : Loc.Format("Schema_TooNewInConversation", fileName, format, ex.Found, ex.Supported, ex.ConversationName);
    }
}
```

Note: `Loc.Get($"Schema_Format_{ex.Kind}")` is a computed key. If `NoHardcodedUiStringsInCodeTests` or a key-existence guard rejects computed keys, replace it with an explicit `switch` over `SchemaFileKind`, one `Loc.Get("Schema_Format_…")` literal per arm.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~SchemaVersionMessage|FullyQualifiedName~Localisation"`
Expected: PASS (including the existing localisation guards).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/SchemaVersionMessages.cs DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml DialogEditor.Tests/Localisation/SchemaVersionMessageTests.cs
git commit -m "feat(schema): localised 'newer file format' message (#62)"
```

---

### Task 4: `dialog-patcher` exit code 4

**Files:**
- Modify: `DialogEditor.PatchCli/PatcherCommand.cs` (help text exit-code table; the load loop's `catch` around line 180)
- Test: `DialogEditor.Tests/PatchCli/PatcherCommandTests.cs`

**Interfaces:**
- Consumes: `UnsupportedSchemaVersionException` (Task 1); the serializers throw it (Task 2)
- Produces: exit code `4` = unsupported file format

- [ ] **Step 1: Write the failing tests** (append to `PatcherCommandTests`)

```csharp
    private string SaveNewerProject(string name)
    {
        var path = SaveProject(FakePoe2Game.ExternalVoMod(name, "x"));
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99"));
        return path;
    }

    [Fact]
    public void NewerProject_ExitsWith4_NamesTheFile_AndWritesNothing()
    {
        var before = _game.SnapshotGameData();
        var newer  = SaveNewerProject("Future");

        var (code, _, err) = Run(_game.Root, SaveProject(FakePoe2Game.ExternalVoMod("A", "a")), newer);

        Assert.Equal(4, code);
        Assert.Contains("Future.dialogproject", err);
        Assert.Contains("newer", err);
        Assert.Equal(before, _game.SnapshotGameData());
        Assert.False(Directory.Exists(Path.Combine(_game.Root, "PillarsDialogPatcher")));
    }

    [Fact]
    public void NewerPack_ExitsWith4()
    {
        var project = SaveNewerProject("FuturePack");
        var pack    = Path.Combine(_projDir, "FuturePack.dialogpack");
        using (var zip = System.IO.Compression.ZipFile.Open(pack, System.IO.Compression.ZipArchiveMode.Create))
            zip.CreateEntryFromFile(project, "project.dialogproject");

        Assert.Equal(4, Run(_game.Root, pack).Code);
    }

    [Fact]
    public void Help_DocumentsExitCode4()
        => Assert.Contains("4   ", Run("--help").Out);
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherCommandTests"`
Expected: the new tests FAIL (code is `2`; help has no `4`).

- [ ] **Step 3: Implement**

In the per-path load loop, add a catch **before** the existing `catch (Exception ex)`:

```csharp
            catch (UnsupportedSchemaVersionException ex)
            {
                // Refused in the load phase, before PatchInstaller, so nothing is written (issue #62).
                AppLog.Warn($"dialog-patcher: '{path}' needs a newer patcher: {ex.Message}");
                Error($"Could not load '{path}': {ex.Message}");
                CleanupTempDirs(tempDirs);
                return 4;
            }
```

Add `using DialogEditor.Patch.Schema;`. Then extend the exit-code table in `Help`:

```
        3   Files the patcher manages were changed outside it. Re-run with
            --accept-current-files, or with --restore.
        4   A project or pack uses a newer file format than this patcher reads.
            Nothing was changed. Update dialog-patcher.
```

Check `Error(...)` writes to `stderr` (it does for the existing load failure). The first test asserts on `err`.

- [ ] **Step 4: Run to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatcherCommandTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.PatchCli/PatcherCommand.cs DialogEditor.Tests/PatchCli/PatcherCommandTests.cs
git commit -m "feat(patcher): exit 4 for a newer file format, before any write (#62)"
```

---

### Task 5: Patch Manager refuses newer mods and load orders

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/PatchManagerViewModel.cs` (`AddEntries`, `LoadEntry`, `LoadFromFile`)
- Test: `DialogEditor.Tests/ViewModels/PatchManagerViewModelSchemaTests.cs`

**Interfaces:**
- Consumes: `UnsupportedSchemaVersionException`, `SchemaVersionMessages.TooNew` (Tasks 1, 3), `PatchListSerializer` (Task 2)
- Produces: nothing new. Behaviour: a newer mod is **not added** (status shows the message). A `.patchlist` whose own version or any entry is newer is **refused whole**: the previous `Entries` and `GameFolder` are kept, and the status names the offending file.

Design note for the implementer: today `LoadEntry` turns every failure into an error entry, and `Apply` silently skips unloaded entries. For a newer format that would apply the rest of the stack without the newer mod, which is the half-apply #62 exists to stop. So `LoadEntry` must **rethrow** `UnsupportedSchemaVersionException` (after deleting its temp dir), and its callers decide.

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/ViewModels/PatchManagerViewModelSchemaTests.cs
using System.IO.Compression;
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class PatchManagerViewModelSchemaTests : IDisposable
{
    private readonly string _dir = Directory.CreateTempSubdirectory().FullName;

    public PatchManagerViewModelSchemaTests() => Loc.Configure(new StubStringProvider());
    public void Dispose() { try { Directory.Delete(_dir, true); } catch (Exception) { /* best-effort */ } }

    private string Project(string name, int version = 1)
    {
        var path = Path.Combine(_dir, name + ".dialogproject");
        DialogProjectSerializer.SaveToFile(path, FakePoe2Game.ExternalVoMod(name, "x"));
        if (version != 1)
            File.WriteAllText(path, File.ReadAllText(path).Replace("\"SchemaVersion\": 1", $"\"SchemaVersion\": {version}"));
        return path;
    }

    private string Pack(string projectPath)
    {
        var pack = Path.ChangeExtension(projectPath, ".dialogpack");
        using var zip = ZipFile.Open(pack, ZipArchiveMode.Create);
        zip.CreateEntryFromFile(projectPath, "project.dialogproject");
        return pack;
    }

    private static string DialogPackTempRoot =>
        Path.Combine(Path.GetTempPath(), "PillarsDialogEditor", "dialogpack");

    private static int TempPackDirCount() =>
        Directory.Exists(DialogPackTempRoot) ? Directory.GetDirectories(DialogPackTempRoot).Length : 0;

    private static PatchManagerViewModel Vm(params string[] picked) =>
        new(new StubFolderPicker(), new StubFilePicker(multiResult: picked));

    [Fact]
    public async Task AddEntries_NewerProject_IsNotAdded_AndStatusExplains()
    {
        var vm = Vm(Project("Ok"), Project("Future", version: 99));

        await vm.AddEntriesCommand.ExecuteAsync(null);

        Assert.Equal(["Ok"], vm.Entries.Select(e => e.ProjectName));
        Assert.StartsWith("Schema_TooNew", vm.StatusText);
    }

    [Fact]
    public async Task AddEntries_NewerPack_IsNotAdded_AndItsTempDirIsDeleted()
    {
        var before = TempPackDirCount();
        var vm = Vm(Pack(Project("Future", version: 99)));

        await vm.AddEntriesCommand.ExecuteAsync(null);

        Assert.Empty(vm.Entries);
        Assert.Equal(before, TempPackDirCount());
    }

    [Fact]
    public void LoadFromFile_ListWithANewerEntry_IsRefusedWhole_KeepingTheCurrentOrder()
    {
        var vm = Vm();
        vm.GameFolder = "kept";
        vm.Entries.Add(new PatchEntryViewModel("Existing", DialogProject.Empty("Existing")));
        var listPath = Path.Combine(_dir, "order.patchlist");
        var list = PatchList.Empty().WithGameFolder("other")
            .WithEntry(PatchListSerializer.BuildEntry(listPath, Project("Ok")))
            .WithEntry(PatchListSerializer.BuildEntry(listPath, Project("Future", version: 99)));
        PatchListSerializer.SaveToFile(listPath, list);

        vm.LoadFromFile(listPath);

        Assert.Equal(["Existing"], vm.Entries.Select(e => e.ProjectName));
        Assert.Equal("kept", vm.GameFolder);
        Assert.StartsWith("Schema_TooNew", vm.StatusText);
    }

    [Fact]
    public void LoadFromFile_NewerListItself_IsRefused()
    {
        var vm = Vm();
        var listPath = Path.Combine(_dir, "future.patchlist");
        File.WriteAllText(listPath, """{ "SchemaVersion": 99, "GameFolder": "", "Entries": [] }""");

        vm.LoadFromFile(listPath);

        Assert.StartsWith("Schema_TooNew", vm.StatusText);
    }

    [Fact]
    public void LoadFromFile_CurrentList_StillLoads()
    {
        var vm = Vm();
        var listPath = Path.Combine(_dir, "ok.patchlist");
        PatchListSerializer.SaveToFile(listPath,
            PatchList.Empty().WithEntry(PatchListSerializer.BuildEntry(listPath, Project("Ok"))));

        vm.LoadFromFile(listPath);

        Assert.Equal(["Ok"], vm.Entries.Select(e => e.ProjectName));
    }
}
```

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatchManagerViewModelSchemaTests"`
Expected: FAIL. Newer entries are added as error entries, and the list replaces the current order.

- [ ] **Step 3: Implement**

In `LoadEntry`, add a catch **before** `catch (Exception ex)` that deletes the temp dir and rethrows. Pull the existing temp-dir cleanup into a helper so both catches share it:

```csharp
        catch (OperationCanceledException) { throw; }
        catch (UnsupportedSchemaVersionException)
        {
            // Not an error entry: Apply skips unloaded entries, which would half-apply the
            // stack without this mod (issue #62). Callers refuse it instead.
            DeleteTempDir(tempDir);
            throw;
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load '{path}'", ex);
            // An error entry owns no temp dir, so nothing would ever delete it.
            DeleteTempDir(tempDir);
            return new PatchEntryViewModel(path, ex.Message);
        }
    }

    private static void DeleteTempDir(string? tempDir)
    {
        if (tempDir is null) return;
        try { Directory.Delete(tempDir, recursive: true); }
        catch (Exception cleanupEx) { AppLog.Warn($"PatchManager: failed to delete temp dir '{tempDir}': {cleanupEx.Message}"); }
    }
```

In `AddEntries`:

```csharp
            try
            {
                Entries.Add(LoadEntry(path));
            }
            catch (UnsupportedSchemaVersionException ex)
            {
                AppLog.Warn($"PatchManager: refused '{path}': {ex.Message}");
                StatusText = SchemaVersionMessages.TooNew(ex, Path.GetFileName(path));
            }
```

`LoadFromFile`: build the new entries into a local list first, and only commit them to `Entries` / `GameFolder` / `_patchlistPath` when every entry loaded without a schema refusal. Dispose the temp dirs of the entries already loaded if one is refused:

```csharp
    public void LoadFromFile(string path)
    {
        var loaded = new List<PatchEntryViewModel>();
        string? refusedFile = null;
        try
        {
            var list = PatchListSerializer.LoadFromFile(path);
            foreach (var entry in list.Entries)
            {
                var resolved = PatchListSerializer.ResolvePath(path, entry);
                refusedFile = resolved;
                loaded.Add(LoadEntry(resolved));
            }

            _patchlistPath = path;
            GameFolder     = list.GameFolder;
            Entries.Clear();
            foreach (var e in loaded) Entries.Add(e);
        }
        catch (UnsupportedSchemaVersionException ex)
        {
            // One newer mod refuses the whole load order, so it's never applied without it (#62, #79).
            foreach (var e in loaded) DeleteTempDir(e.TempDir);
            var offender = Path.GetFileName(refusedFile ?? path);
            AppLog.Warn($"PatchManager: refused load order '{path}' ({offender}): {ex.Message}");
            StatusText = SchemaVersionMessages.TooNew(ex, offender);
        }
        catch (Exception ex)
        {
            foreach (var e in loaded) DeleteTempDir(e.TempDir);
            AppLog.Error($"Failed to load load order '{path}'", ex);
            StatusText = Loc.Format("PatchManager_LoadError", path, ex.Message);
        }
    }
```

(`refusedFile` stays `null` when the `.patchlist` itself is newer, so the list's own name is shown.) Add `using DialogEditor.Patch.Schema;` and `using DialogEditor.ViewModels.Services;` if they're missing.

- [ ] **Step 4: Run to verify they pass, plus the existing Patch Manager tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PatchManager"`
Expected: PASS, including `PatchManagerTests` and the #98 `.dialogpack` reload tests.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/PatchManagerViewModel.cs DialogEditor.Tests/ViewModels/PatchManagerViewModelSchemaTests.cs
git commit -m "feat(patch-manager): refuse newer mods and whole newer load orders (#62)"
```

---

### Task 6: Editor surfaces — open, autosave, git conflict, merge, diff

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs` (`LoadProjectAsync` ~970-1020, `LoadConflictedProjectAsync` ~1028-1040, merge ~1625-1645; new seam near `ReportError` at line 116)
- Modify: `DialogEditor.Patch/Diff/DiffException.cs`, `DialogEditor.Patch/Diff/ProjectVersionLoader.cs:17-18`
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs:326-331`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (`Status_DiffUnsupportedSchema`)
- Modify: `DialogEditor.Avalonia.Shared/ConfirmDialog.axaml.cs` (+ `.axaml` if the Cancel button has no `x:Name`) — info mode
- Modify: `DialogEditor.Avalonia/Views/MainWindow.axaml.cs` (wire the seam next to `vm.ReportError = …`)
- Test: `DialogEditor.Tests/ViewModels/MainWindowViewModelSchemaTests.cs`, `DialogEditor.Tests/Patch/Diff/ProjectVersionLoaderSchemaTests.cs`

**Interfaces:**
- Consumes: Tasks 1–3
- Produces:
  - `public Func<string, string, Task>? ShowUnsupportedFormat { get; set; }` on `MainWindowViewModel` (title, message). The host shows a one-button modal.
  - `DiffExceptionKind.UnsupportedSchema`
  - `ConfirmDialog(string title, string message, string confirmText, IReadOnlyList<string>? details, bool showCancel = true)`

Design note: `ReportError` opens the crash/"report an issue" dialog. A newer file isn't a bug, so it gets its own seam instead (spec deviation, agreed in review).

- [ ] **Step 1: Write the failing tests**

```csharp
// DialogEditor.Tests/ViewModels/MainWindowViewModelSchemaTests.cs
using DialogEditor.Patch;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class MainWindowViewModelSchemaTests : IDisposable
{
    private readonly string _settingsPath = Path.Combine(Path.GetTempPath(), $"mwvm_schema_{Guid.NewGuid():N}.json");
    private readonly string _dir          = Directory.CreateTempSubdirectory().FullName;
    private string ProjectPath => Path.Combine(_dir, "p.dialogproject");

    public MainWindowViewModelSchemaTests()
    {
        Loc.Configure(new StubStringProvider());
        AppSettings.SettingsPathOverride = _settingsPath;
    }

    public void Dispose()
    {
        AppSettings.SettingsPathOverride = null;
        try { if (File.Exists(_settingsPath)) File.Delete(_settingsPath); } catch (Exception) { /* best-effort */ }
        try { Directory.Delete(_dir, true); } catch (Exception) { /* best-effort */ }
    }

    private static MainWindowViewModel MakeVm(string? openResult = null) =>
        new(new StubDispatcher(), new StubFolderPicker(), new StubFilePicker(openResult: openResult));

    private static Task Load(MainWindowViewModel vm, string path) =>
        (Task)typeof(MainWindowViewModel).GetMethod("LoadProjectAsync",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .Invoke(vm, [path, false])!;

    private static void WriteVersion(string path, string name, int version)
    {
        DialogProjectSerializer.SaveToFile(path, DialogProject.Empty(name));
        File.WriteAllText(path, File.ReadAllText(path).Replace("\"SchemaVersion\": 1", $"\"SchemaVersion\": {version}"));
    }

    [Fact]
    public async Task Open_NewerProject_IsRefused_WithMessage_NotACrashReport()
    {
        var other = Path.Combine(_dir, "current.dialogproject");
        DialogProjectSerializer.SaveToFile(other, DialogProject.Empty("Current"));
        WriteVersion(ProjectPath, "Future", 99);
        var vm = MakeVm();
        await Load(vm, other);
        string? shown = null; Exception? reported = null;
        vm.ShowUnsupportedFormat = (_, msg) => { shown = msg; return Task.CompletedTask; };
        vm.ReportError = ex => reported = ex;

        await Load(vm, ProjectPath);

        Assert.Equal("Current", vm.CurrentProjectName);   // previous project untouched
        Assert.StartsWith("Schema_TooNew", vm.StatusText);
        Assert.StartsWith("Schema_TooNew", shown);
        Assert.Null(reported);
    }

    [Fact]
    public async Task Open_NewerAutosaveSidecar_IsKept_AndTheSavedFileLoads()
    {
        DialogProjectSerializer.SaveToFile(ProjectPath, DialogProject.Empty("Saved"));
        var sidecar = AutosaveRecovery.SidecarPath(ProjectPath);
        WriteVersion(sidecar, "Recovered", 99);
        File.SetLastWriteTimeUtc(ProjectPath, DateTime.UtcNow.AddMinutes(-10));
        File.SetLastWriteTimeUtc(sidecar,     DateTime.UtcNow.AddMinutes(-1));
        var vm = MakeVm();
        vm.ConfirmRestoreAutosave = _ => Task.FromResult(true);

        await Load(vm, ProjectPath);

        Assert.Equal("Saved", vm.CurrentProjectName);
        Assert.True(File.Exists(sidecar));   // someone's work, not corruption
    }

    [Fact]
    public async Task Open_OlderPatchInside_IsNotMarkedModified()
    {
        var project = DialogProject.Empty("Old")
            .WithPatch(new ConversationPatch("greeting", 1, [], [7], []));
        DialogProjectSerializer.SaveToFile(ProjectPath, project);
        var vm = MakeVm();

        await Load(vm, ProjectPath);

        Assert.Equal("Old", vm.CurrentProjectName);
        Assert.False(vm.IsModified);
    }

    [Fact]
    public async Task Open_GitConflictWithANewerSide_ShowsTheSchemaMessage()
    {
        var mine = DialogProjectSerializer.Serialize(DialogProject.Empty("P"));
        var theirs = mine.Replace("\"SchemaVersion\": 1", "\"SchemaVersion\": 99");
        File.WriteAllText(ProjectPath, $"<<<<<<< HEAD\n{mine}\n=======\n{theirs}\n>>>>>>> other\n");
        var vm = MakeVm();

        await Load(vm, ProjectPath);

        Assert.StartsWith("Schema_TooNew", vm.StatusText);
    }
}
```

Check `GitConflictMarkers.SplitSides` accepts that whole-file conflict shape (see `GitConflictMarkersTests` for the exact format it expects) and adjust the fixture text to match. The point of the test is a conflicted file whose "theirs" side is newer.

Add a merge test in the same class, following `MainWindowViewModelTests`' existing merge-projects test for how it sets `_projectPath` and triggers `MergeProjectsCommand`. Assert that merging a newer project leaves `CurrentProjectName`, the on-disk project file and `IsModified` unchanged, and that `StatusText` starts with `Schema_TooNew`.

```csharp
// DialogEditor.Tests/Patch/Diff/ProjectVersionLoaderSchemaTests.cs
using DialogEditor.Patch;
using DialogEditor.Patch.Diff;

namespace DialogEditor.Tests.Patch.Diff;

public class ProjectVersionLoaderSchemaTests
{
    [Fact]
    public void NewerWorkingCopy_ThrowsUnsupportedSchema()
    {
        var path = Path.Combine(Path.GetTempPath(), $"pvl_{Guid.NewGuid():N}.dialogproject");
        File.WriteAllText(path, """{ "Name": "P", "SchemaVersion": 99, "Patches": {} }""");
        try
        {
            var ex = Assert.Throws<DiffException>(() =>
                new ProjectVersionLoader(new ThrowingGitRunner()).Load(new DiffEndpoint.WorkingCopy(), path));
            Assert.Equal(DiffExceptionKind.UnsupportedSchema, ex.Kind);
        }
        finally { try { File.Delete(path); } catch (Exception) { /* best-effort */ } }
    }

    private sealed class ThrowingGitRunner : IGitRunner
    {
        public GitResult Run(string workingDirectory, params string[] args) => throw new InvalidOperationException();
    }
}
```

Match `IGitRunner`'s real signature and the `DiffEndpoint.WorkingCopy` constructor (see `ProjectVersionLoaderTests`). If a fake git runner already exists in `DialogEditor.Tests/Helpers`, reuse it.

- [ ] **Step 2: Run to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModelSchemaTests|FullyQualifiedName~ProjectVersionLoaderSchemaTests"`
Expected: build FAILS (`ShowUnsupportedFormat`, `DiffExceptionKind.UnsupportedSchema` don't exist).

- [ ] **Step 3: Implement**

`DiffException.cs`: append `UnsupportedSchema` to the enum.

`ProjectVersionLoader.Load`:

```csharp
        try { return DialogProjectSerializer.Deserialize(json); }
        catch (UnsupportedSchemaVersionException ex)
        {
            throw new DiffException($"Project uses a newer file format: {ex.Message}", DiffExceptionKind.UnsupportedSchema);
        }
        catch (Exception ex) { throw new DiffException($"Could not parse project: {ex.Message}", DiffExceptionKind.ParseFailed); }
```

`DiffViewModel` switch: `DiffExceptionKind.UnsupportedSchema => Loc.Format("Status_DiffUnsupportedSchema", endpointLabel),`
`Strings.axaml`, next to `Status_DiffParseError`:

```xml
    <sys:String x:Key="Status_DiffUnsupportedSchema">Can't compare the '{0}' version: it was saved by a newer version of Pillars Dialog Editor. Update to the latest version to compare it.</sys:String>
```

`MainWindowViewModel`, next to `ReportError`:

```csharp
    /// Set by the host view: shows a one-button message (title, text). Used for files saved by a
    /// newer editor (issue #62). That's not a bug, so it must not go through ReportError's crash report.
    public Func<string, string, Task>? ShowUnsupportedFormat { get; set; }

    private async Task RefuseNewerFormatAsync(UnsupportedSchemaVersionException ex, string path)
    {
        AppLog.Warn($"Refused '{path}': {ex.Message}");
        StatusText = SchemaVersionMessages.TooNew(ex, Path.GetFileName(path));
        if (ShowUnsupportedFormat is not null)
            await ShowUnsupportedFormat(SchemaVersionMessages.Title, StatusText);
    }
```

In `LoadProjectAsync`:
- In the autosave-restore `try`, add a catch before `catch (Exception ex)`:

  ```csharp
                  catch (UnsupportedSchemaVersionException ex)
                  {
                      // A newer editor's unsaved work: keep the sidecar (not corrupt), load the saved file.
                      AppLog.Warn($"Autosave sidecar for '{path}' is from a newer version, kept: {ex.Message}");
                  }
  ```
- In the main open `try`, add before `catch (Exception ex)`:

  ```csharp
          catch (UnsupportedSchemaVersionException ex)
          {
              await RefuseNewerFormatAsync(ex, path);
          }
  ```

In `LoadConflictedProjectAsync`, before its `catch (Exception ex)`:

```csharp
        catch (UnsupportedSchemaVersionException ex)
        {
            await RefuseNewerFormatAsync(ex, path);
            return;
        }
```

In the merge command, before `catch (Exception ex)`:

```csharp
        catch (UnsupportedSchemaVersionException ex)
        {
            await RefuseNewerFormatAsync(ex, _projectPath!);
        }
```

The merge loads every file before `SetProject` / `SaveToFile`, so the open project is untouched. `MergeProjects` is already `async Task`. Pass the offending merge path rather than `_projectPath` by tracking the current `path` in a local declared outside the `foreach`.

`ConfirmDialog.axaml.cs`: add `bool showCancel = true` as a last constructor parameter, then `CancelButton.IsVisible = showCancel;`. The Cancel button already has its `ToolTip`, and hiding it adds no control.

`MainWindow.axaml.cs`, next to `vm.ReportError = …`:

```csharp
        vm.ShowUnsupportedFormat = (title, message) =>
            new ConfirmDialog(title, message, Loc.Get("ImportWarnings_Ok"), details: null, showCancel: false)
                .ShowAsync(this);
```

Reuse an existing `OK` key if one fits your review. Otherwise add `Schema_TooNew_Ok` = `OK` to `SharedStrings.axaml` and use that. The Confirm button's ToolTip comes from `ConfirmDialog.axaml`; check it reads sensibly for a one-button message, and if it says "confirm", pass a neutral tooltip through the same key pattern.

- [ ] **Step 4: Run to verify they pass, plus the existing editor and diff tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~MainWindowViewModel|FullyQualifiedName~Diff|FullyQualifiedName~Autosave"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels DialogEditor.Patch/Diff DialogEditor.Avalonia DialogEditor.Avalonia.Shared DialogEditor.Tests
git commit -m "feat(editor): refuse newer project formats on open, merge, conflict and diff; keep newer autosaves (#62)"
```

---

### Task 7: `FORMAT.md` — the 1.0 project format and versioning policy

**Files:**
- Modify: `FORMAT.md` (new top-level section **before** `# .dialogpack format`; add a line for exit 4 under `## Applying a .dialogpack` if that section lists exit codes)

- [ ] **Step 1: Write the section**

Content (edit field lists against `DialogProject.cs` / `ConversationPatch.cs` / `PatchList.cs` as they are on the branch):

```markdown
# Project file formats and versioning

Pillars Dialog Editor's own files are UTF-8 JSON with PascalCase property names. Each carries a
`SchemaVersion`. As of 1.0 these formats are **stable**: files are shared between authors and
merged in git, so a version only changes under the policy below.

| File | Format | Current `SchemaVersion` |
|---|---|---|
| `.dialogproject` | `DialogProject` | 1 |
| (inside `Patches`) | `ConversationPatch`, one per conversation | 2 |
| `.patchlist` | `PatchList` (Patch Manager load order) | 1 |

## `.dialogproject`

| Field | Required | Meaning |
|---|---|---|
| `Name` | yes | Project name. |
| `SchemaVersion` | yes | Project format version. |
| `Patches` | yes | Object keyed by conversation name; each value is a `ConversationPatch`. |
| `Layouts` | no | Canvas node positions per conversation (editor metadata). |
| `NewConversations` | no | Conversations the project creates rather than modifies. |
| `Annotations` | no | Canvas annotations (editor metadata; never written to game files). |
| `IgnoredDuplicates` | no | Duplicate-line allowlist (editor metadata). |

## `ConversationPatch`

`ConversationName`, `SchemaVersion`, `AddedNodes`, `DeletedNodeIds`, `ModifiedNodes`
(field changes stored as JSON-encoded `From` / `To` strings, plus link and condition/script
changes), and optional `Translations` (keyed by language code) and `NodeComments`. A patch's
`SchemaVersion` is independent of its project's.

## `.patchlist`

`SchemaVersion`, `GameFolder`, and `Entries` (each with `RelativePath`, resolved against the
`.patchlist`'s folder first, and `AbsolutePath` as a fallback).

## Versioning policy

- **Bump** a format's `SchemaVersion` when an older reader would misread or silently drop data:
  a renamed, removed or re-typed field, a changed meaning, or a new *required* field.
- **Don't bump** for a new *optional* field that older readers can safely ignore.
- Every bump ships **one migration step** (`DialogEditor.Patch/Schema/SchemaMigrator.cs`) that
  rewrites the raw JSON from the old version to the new one, and **a fixture file at the old
  version** under `DialogEditor.Tests/Patch/Schema/Fixtures`. Steps chain, so a file several
  versions behind is brought forward one step at a time.
- An older file is migrated in memory when it is opened; the new version is written on the next save.
- A **newer** file is refused, before anything is read or written: the editor and Patch Manager
  explain that a newer version is needed, and `dialog-patcher` exits with code **4**.
```

- [ ] **Step 2: Commit**

```bash
git add FORMAT.md
git commit -m "docs(format): document the 1.0 project formats and versioning policy (#62, #104)"
```

---

### Task 8: Full verification

- [ ] **Step 1:** `dotnet build` for the solution. Expected: 0 errors and no new warnings.
- [ ] **Step 2:** `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. Expected: all PASS.
- [ ] **Step 3:** GUI check with the `running-the-app` skill: open a hand-edited `.dialogproject` with `"SchemaVersion": 99`. The one-button "Newer file format" dialog appears, the status bar shows the message, and no crash-report window opens. In the Patch Manager, add the same file: it's not added and the status explains why.
- [ ] **Step 4:** `git grep -n "SchemaVersion" -- "*.cs" ":!DialogEditor.Tests"`. Confirm no production code reads `SchemaVersion` outside `SchemaMigrator` and the backup manifest (`PatcherManifest` has its own check from #76 and stays as is).
