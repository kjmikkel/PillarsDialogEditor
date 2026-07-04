# Plural-Aware Localisation Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** CLDR-category plural support (`Loc.FormatCount` + `_One`/`_Few`/`_Many`/`_Other`… key suffixes) and migration of all 24 naive-plural "`(s)`" strings, per `docs/superpowers/specs/2026-07-04-pluralisation-design.md`.

**Architecture:** Pure `PluralRules` (CLDR cardinal rules for en/de/fr/pl/ru/ar, en fallback) feeds `Loc.FormatCount`, which resolves `{key}_{Category}` → `{key}_Other` → `{key}` via a new `IStringProvider.TryGet`. English strings migrate to `_One`/`_Other` pairs; two-count strings split into pluralised fragments + a wrapper; a structural test bans the old "(s)" idiom.

**Tech Stack:** xUnit, `Loc`/`IStringProvider`/`AvaloniaStringProvider`/`StubStringProvider`, Avalonia resource dictionaries.

## Global Constraints

- No user-visible text hard-coded in XAML or C# — everything via `Strings.axaml`/`SharedStrings.axaml` keys.
- Strict red/green TDD for all new logic; observe each failing test before implementing.
- `CHANGELOG.md` is frozen — do not touch it.
- `FormatCount` convention: **count is always `{0}`**; extra args follow as `{1}`, `{2}`….
- Existing test assertions that expect a bare key may be updated **only** by appending the plural suffix the stub now produces (`_One` when the asserted count is 1, `_Other` otherwise) — no other assertion changes.
- `StubStringProvider` echoes keys and reports every key as present, so in VM tests `FormatCount("K", 2)` yields the literal string `"K_Other"` (no `{0}` in it to substitute).

---

### Task 1: `PluralRules` (pure, CLDR cardinal rules)

**Files:**
- Create: `DialogEditor.ViewModels/Services/PluralRules.cs`
- Test: `DialogEditor.Tests/Localisation/PluralRulesTests.cs` (**create**, new folder)

**Interfaces:**
- Produces (Task 2 relies on these exact names):
  - `enum PluralCategory { Zero, One, Two, Few, Many, Other }`
  - `static PluralCategory PluralRules.Category(string langTwoLetter, int n)`

- [x] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Localisation/PluralRulesTests.cs`:

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

// Reference cases from the CLDR cardinal plural rules (integers only).
public class PluralRulesTests
{
    [Theory]
    [InlineData("en", 1, PluralCategory.One)]
    [InlineData("en", 0, PluralCategory.Other)]
    [InlineData("en", 2, PluralCategory.Other)]
    [InlineData("de", 1, PluralCategory.One)]
    [InlineData("de", 5, PluralCategory.Other)]
    public void EnglishAndGerman_OneOther(string lang, int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category(lang, n));

    [Theory]
    [InlineData(0, PluralCategory.One)]   // French: 0 and 1 are both One
    [InlineData(1, PluralCategory.One)]
    [InlineData(2, PluralCategory.Other)]
    public void French_ZeroAndOneAreOne(int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category("fr", n));

    [Theory]
    [InlineData(1,  PluralCategory.One)]
    [InlineData(2,  PluralCategory.Few)]
    [InlineData(4,  PluralCategory.Few)]
    [InlineData(22, PluralCategory.Few)]   // 2..4 but NOT 12..14
    [InlineData(5,  PluralCategory.Many)]
    [InlineData(12, PluralCategory.Many)]  // teens are Many
    [InlineData(0,  PluralCategory.Many)]
    public void Polish(int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category("pl", n));

    [Theory]
    [InlineData(1,  PluralCategory.One)]
    [InlineData(21, PluralCategory.One)]   // ends in 1, not 11
    [InlineData(11, PluralCategory.Many)]
    [InlineData(2,  PluralCategory.Few)]
    [InlineData(3,  PluralCategory.Few)]
    [InlineData(5,  PluralCategory.Many)]
    [InlineData(14, PluralCategory.Many)]
    public void Russian(int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category("ru", n));

    [Theory]
    [InlineData(0,   PluralCategory.Zero)]
    [InlineData(1,   PluralCategory.One)]
    [InlineData(2,   PluralCategory.Two)]
    [InlineData(3,   PluralCategory.Few)]
    [InlineData(10,  PluralCategory.Few)]
    [InlineData(11,  PluralCategory.Many)]
    [InlineData(99,  PluralCategory.Many)]
    [InlineData(100, PluralCategory.Other)]
    public void Arabic(int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category("ar", n));

    [Theory]
    [InlineData("xx", 1, PluralCategory.One)]    // unknown language → en rule
    [InlineData("xx", 3, PluralCategory.Other)]
    [InlineData("EN", 1, PluralCategory.One)]    // case-insensitive
    public void UnknownOrUppercase_FallsBackToEnglishRule(string lang, int n, PluralCategory expected)
        => Assert.Equal(expected, PluralRules.Category(lang, n));
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~PluralRulesTests"`
Expected: **build failure** — `'PluralRules' could not be found`.

- [x] **Step 3: Implement**

Create `DialogEditor.ViewModels/Services/PluralRules.cs`:

```csharp
namespace DialogEditor.ViewModels.Services;

/// <summary>CLDR plural category of a cardinal count.</summary>
public enum PluralCategory { Zero, One, Two, Few, Many, Other }

/// <summary>
/// CLDR cardinal plural rules, integer counts only (every pluralised UI string
/// counts discrete things). Multi-form languages (pl/ru/ar) ship now as the
/// reference implementations a future translation plugs into — the mechanism
/// is proven by tests, not hoped generalisable. Unknown languages use the
/// English rule; Loc.FormatCount's _Other fallback absorbs the rest.
/// Rules source: CLDR cardinal rules (see unicode.org CLDR plural rules chart).
/// </summary>
public static class PluralRules
{
    public static PluralCategory Category(string langTwoLetter, int n)
        => langTwoLetter.ToLowerInvariant() switch
        {
            "fr" => n is 0 or 1 ? PluralCategory.One : PluralCategory.Other,
            "pl" => Polish(n),
            "ru" => Russian(n),
            "ar" => Arabic(n),
            _    => n == 1 ? PluralCategory.One : PluralCategory.Other, // en, de, …
        };

    private static PluralCategory Polish(int n)
    {
        if (n == 1) return PluralCategory.One;
        if (n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) return PluralCategory.Few;
        return PluralCategory.Many;
    }

    private static PluralCategory Russian(int n)
    {
        if (n % 10 == 1 && n % 100 != 11) return PluralCategory.One;
        if (n % 10 is >= 2 and <= 4 && n % 100 is < 12 or > 14) return PluralCategory.Few;
        return PluralCategory.Many;
    }

    private static PluralCategory Arabic(int n) => n switch
    {
        0 => PluralCategory.Zero,
        1 => PluralCategory.One,
        2 => PluralCategory.Two,
        _ when n % 100 is >= 3 and <= 10  => PluralCategory.Few,
        _ when n % 100 is >= 11 and <= 99 => PluralCategory.Many,
        _ => PluralCategory.Other,
    };
}
```

- [x] **Step 4: Run tests to verify they pass**

Run: `dotnet test --nologo --filter "FullyQualifiedName~PluralRulesTests"` → PASS (28 cases).

- [x] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/PluralRules.cs DialogEditor.Tests/Localisation/PluralRulesTests.cs
git commit -m "feat(l10n): CLDR cardinal plural rules (en/de/fr/pl/ru/ar)

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: `IStringProvider.TryGet` + `Loc.FormatCount`

**Files:**
- Modify: `DialogEditor.ViewModels/Services/IStringProvider.cs`
- Modify: `DialogEditor.Avalonia.Shared/Services/AvaloniaStringProvider.cs`
- Modify: `DialogEditor.Tests/Helpers/StubStringProvider.cs`
- Modify: `DialogEditor.ViewModels/Resources/Loc.cs`
- Test: `DialogEditor.Tests/Localisation/LocFormatCountTests.cs` (**create**)

**Interfaces:**
- Consumes: Task 1 `PluralRules.Category`, `PluralCategory`.
- Produces:
  - `bool IStringProvider.TryGet(string key, out string value)`
  - `static string Loc.FormatCount(string key, int count, params object[] extraArgs)`

- [x] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/Localisation/LocFormatCountTests.cs`:

```csharp
using System.Globalization;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

public class LocFormatCountTests : IDisposable
{
    private sealed class MapProvider(Dictionary<string, string> map) : IStringProvider
    {
        public string Get(string key) => map.TryGetValue(key, out var v) ? v : $"[{key}]";
        public bool TryGet(string key, out string value)
        {
            if (map.TryGetValue(key, out var v)) { value = v; return true; }
            value = string.Empty; return false;
        }
    }

    private readonly CultureInfo _originalCulture = CultureInfo.CurrentUICulture;

    public void Dispose() => CultureInfo.CurrentUICulture = _originalCulture;

    private static void UseEnglish() => CultureInfo.CurrentUICulture = new CultureInfo("en-US");

    [Fact]
    public void FormatCount_PicksCategorySuffix()
    {
        UseEnglish();
        Loc.Configure(new MapProvider(new()
        {
            ["X_One"]   = "1 match",
            ["X_Other"] = "{0} matches",
        }));
        Assert.Equal("1 match",   Loc.FormatCount("X", 1));
        Assert.Equal("3 matches", Loc.FormatCount("X", 3));
    }

    [Fact]
    public void FormatCount_MissingCategory_FallsBackToOther()
    {
        // Polish Few requested, only _Other provided — a partially translated
        // overlay must not crash.
        CultureInfo.CurrentUICulture = new CultureInfo("pl-PL");
        Loc.Configure(new MapProvider(new() { ["X_Other"] = "{0} plików" }));
        Assert.Equal("2 plików", Loc.FormatCount("X", 2));   // Few → falls to Other
    }

    [Fact]
    public void FormatCount_NoSuffixedKeys_FallsBackToBareKey()
    {
        UseEnglish();
        Loc.Configure(new MapProvider(new() { ["X"] = "{0} legacy" }));
        Assert.Equal("7 legacy", Loc.FormatCount("X", 7));
    }

    [Fact]
    public void FormatCount_ExtraArgsFollowCount()
    {
        UseEnglish();
        Loc.Configure(new MapProvider(new()
        {
            ["X_Other"] = "Exported {0} conversations to {1}.",
        }));
        Assert.Equal("Exported 4 conversations to out.csv.",
            Loc.FormatCount("X", 4, "out.csv"));
    }

    [Fact]
    public void FormatCount_PolishFew_UsesFewKeyWhenPresent()
    {
        CultureInfo.CurrentUICulture = new CultureInfo("pl-PL");
        Loc.Configure(new MapProvider(new()
        {
            ["X_Few"]   = "{0} pliki",
            ["X_Other"] = "{0} plików",
        }));
        Assert.Equal("3 pliki", Loc.FormatCount("X", 3));
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~LocFormatCountTests"`
Expected: **build failure** — `'IStringProvider' does not contain a definition for 'TryGet'` / `'Loc' does not contain a definition for 'FormatCount'`.

- [x] **Step 3: Implement**

`IStringProvider.cs` — add below `Get`:

```csharp
    /// <summary>True and the value when <paramref name="key"/> exists; false
    /// otherwise. Unlike Get, never substitutes a "[key]" placeholder — used by
    /// Loc.FormatCount's plural-suffix fallback chain.</summary>
    bool TryGet(string key, out string value);
```

`AvaloniaStringProvider.cs` — add:

```csharp
    public bool TryGet(string key, out string value)
    {
        if (Application.Current is { } app &&
            app.TryGetResource(key, null, out var raw) && raw is string s)
        {
            value = s;
            return true;
        }
        value = string.Empty;
        return false;
    }
```

`StubStringProvider.cs` — becomes:

```csharp
public sealed class StubStringProvider : IStringProvider
{
    public string Get(string key) => key;

    // Every key "exists" and echoes itself, so FormatCount(key, n) in VM tests
    // deterministically yields "key_One" / "key_Other" etc.
    public bool TryGet(string key, out string value) { value = key; return true; }
}
```

`Loc.cs` — add below `Format`:

```csharp
    /// <summary>
    /// Plural-aware lookup: resolves "{key}_{CldrCategory}" for the current UI
    /// language (set by CoreLocale.SetCulture), falling back to "{key}_Other",
    /// then bare "{key}" (unmigrated legacy). Count is always {0}; extraArgs
    /// follow as {1}… . See docs/superpowers/specs/2026-07-04-pluralisation-design.md.
    /// </summary>
    public static string FormatCount(string key, int count, params object[] extraArgs)
    {
        var lang     = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        var category = PluralRules.Category(lang, count);

        if (!Provider.TryGet($"{key}_{category}", out var value)
            && !Provider.TryGet($"{key}_Other", out value))
            value = Provider.Get(key);

        var args = new object[extraArgs.Length + 1];
        args[0] = count;
        extraArgs.CopyTo(args, 1);
        return string.Format(value, args);
    }
```

(`Loc.cs` already has `using DialogEditor.ViewModels.Services;` for `IStringProvider` — `PluralRules` resolves from the same namespace.)

- [x] **Step 4: Run tests, then full suite**

Run: `dotnet test --nologo --filter "FullyQualifiedName~LocFormatCountTests"` → PASS (5 tests).
Run: `dotnet test --nologo` → all pass (nothing calls FormatCount yet; TryGet additions compile everywhere — if any other `IStringProvider` implementation exists, the build error names it; give it the same TryGet shape).

- [x] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/IStringProvider.cs DialogEditor.Avalonia.Shared/Services/AvaloniaStringProvider.cs DialogEditor.Tests/Helpers/StubStringProvider.cs DialogEditor.ViewModels/Resources/Loc.cs DialogEditor.Tests/Localisation/LocFormatCountTests.cs
git commit -m "feat(l10n): Loc.FormatCount with plural-suffix fallback chain

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Migrate the simple-count strings (18 keys)

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`
- Modify: `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`
- Modify (call sites): `FindReplaceViewModel.cs`, `BatchReplaceViewModel.cs` (skipped-string only), `DiffViewModel.cs`, `MainWindowViewModel.cs`, `ExportConversationsViewModel.cs`, `VoValidationViewModel.cs`, `NodeDetailViewModel.cs`, `PatchManagerViewModel.cs` (all under `DialogEditor.ViewModels/ViewModels/`)
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml` (dangling-warning header)
- Test: existing suites (assertion suffix updates only, per Global Constraints)

**Interfaces:**
- Consumes: Task 2 `Loc.FormatCount`.
- Produces: the `_One`/`_Other` key pairs below (Task 5's guard test relies on the old values being gone).

- [x] **Step 1: Replace the 16 single-count string definitions**

In the dictionaries, replace each single `<sys:String>` with a `_One`/`_Other` pair (delete the old key — the bare-key fallback is for future stragglers, not dead keys). New values (count is `{0}`; renamed args noted):

`Strings.axaml`:

```xml
    <sys:String x:Key="FindReplace_Matches_One">1 match</sys:String>
    <sys:String x:Key="FindReplace_Matches_Other">{0} matches</sys:String>
    <sys:String x:Key="FindReplace_Replaced_One">Replaced 1 match</sys:String>
    <sys:String x:Key="FindReplace_Replaced_Other">Replaced {0} matches</sys:String>
    <sys:String x:Key="BatchReplace_StatusSkipped_One">1 open conversation skipped — save it first</sys:String>
    <sys:String x:Key="BatchReplace_StatusSkipped_Other">{0} open conversations skipped — save them first</sys:String>
    <sys:String x:Key="Status_DiffComputed_One">1 changed conversation</sys:String>
    <sys:String x:Key="Status_DiffComputed_Other">{0} changed conversations</sys:String>
    <sys:String x:Key="Status_BroughtIn_One">Brought in 1 change to your copy.</sys:String>
    <sys:String x:Key="Status_BroughtIn_Other">Brought in {0} changes to your copy.</sys:String>
    <sys:String x:Key="Diff_DanglingWarning_One">1 link may not lead anywhere</sys:String>
    <sys:String x:Key="Diff_DanglingWarning_Other">{0} links may not lead anywhere</sys:String>
    <!-- {1} = project name (count moved to {0} for FormatCount) -->
    <sys:String x:Key="Status_ProjectOpened_One">Opened project '{1}' (1 patch)</sys:String>
    <sys:String x:Key="Status_ProjectOpened_Other">Opened project '{1}' ({0} patches)</sys:String>
    <sys:String x:Key="Status_ProjectGitConflictDetected_One">'{1}' has 1 unresolved git merge conflict. Resolve it to open the project.</sys:String>
    <sys:String x:Key="Status_ProjectGitConflictDetected_Other">'{1}' has {0} unresolved git merge conflicts. Resolve them to open the project.</sys:String>
    <sys:String x:Key="Status_ProjectGitConflictPending_One">'{1}' has 1 unresolved git merge conflict. Click Resolve to fix it.</sys:String>
    <sys:String x:Key="Status_ProjectGitConflictPending_Other">'{1}' has {0} unresolved git merge conflicts. Click Resolve to fix them.</sys:String>
    <sys:String x:Key="Status_MergeComplete_One">Merged 1 project into '{1}'</sys:String>
    <sys:String x:Key="Status_MergeComplete_Other">Merged {0} projects into '{1}'</sys:String>
    <sys:String x:Key="Status_ExportConversationsSaved_One">Exported 1 conversation to {1}.</sys:String>
    <sys:String x:Key="Status_ExportConversationsSaved_Other">Exported {0} conversations to {1}.</sys:String>
    <sys:String x:Key="VoValidation_CleanUpConfirm_One">Delete 1 orphaned file from the _vo folder? The editor cannot undo this — re-import the source audio to restore a file.</sys:String>
    <sys:String x:Key="VoValidation_CleanUpConfirm_Other">Delete {0} orphaned files from the _vo folder? The editor cannot undo this — re-import the source audio to restore a file.</sys:String>
    <sys:String x:Key="VoValidation_CleanedUp_One">Deleted 1 orphaned file.</sys:String>
    <sys:String x:Key="VoValidation_CleanedUp_Other">Deleted {0} orphaned files.</sys:String>
    <sys:String x:Key="VoValidation_CleanUpPartial_One">Deleted 1 file; {1} could not be deleted — see the log.</sys:String>
    <sys:String x:Key="VoValidation_CleanUpPartial_Other">Deleted {0} files; {1} could not be deleted — see the log.</sys:String>
    <sys:String x:Key="NodeDetail_AliasSharedCount_One">Also used by 1 other node</sys:String>
    <sys:String x:Key="NodeDetail_AliasSharedCount_Other">Also used by {0} other nodes</sys:String>
    <!-- {1} = raw alias path (count moved to {0} for FormatCount) -->
    <sys:String x:Key="AliasImport_Message_One">This node plays a shared recording ({1}), also used by 1 other node. Importing will replace that recording for both.</sys:String>
    <sys:String x:Key="AliasImport_Message_Other">This node plays a shared recording ({1}), also used by {0} other nodes. Importing will replace that recording for all of them.</sys:String>
```

`SharedStrings.axaml`:

```xml
    <sys:String x:Key="PatchManager_ConflictsFound_One">1 conflict detected — reorder so the version you want is lowest in the list.</sys:String>
    <sys:String x:Key="PatchManager_ConflictsFound_Other">{0} conflicts detected — reorder so the version you want is lowest in the list.</sys:String>
    <sys:String x:Key="PatchManager_ApplySuccess_One">Applied 1 patch to {1}.</sys:String>
    <sys:String x:Key="PatchManager_ApplySuccess_Other">Applied {0} patches to {1}.</sys:String>
```

- [x] **Step 2: Update the call sites**

For each key, find the `Loc.Format("<key>", …)` call in the file listed and transform (`grep -rn 'Loc.Format("<key>"' DialogEditor.ViewModels DialogEditor.Avalonia`):

| Key | Old call shape | New call |
|---|---|---|
| `FindReplace_Matches` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `FindReplace_Replaced` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `BatchReplace_StatusSkipped` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `Status_DiffComputed` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `Status_BroughtIn` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `Status_ProjectOpened` | `Loc.Format(k, name, n)` | `Loc.FormatCount(k, n, name)` |
| `Status_ProjectGitConflictDetected` | `Loc.Format(k, name, n)` | `Loc.FormatCount(k, n, name)` |
| `Status_ProjectGitConflictPending` | `Loc.Format(k, name, n)` | `Loc.FormatCount(k, n, name)` |
| `Status_MergeComplete` | `Loc.Format(k, n, name)` | `Loc.FormatCount(k, n, name)` |
| `Status_ExportConversationsSaved` | `Loc.Format(k, n, path)` | `Loc.FormatCount(k, n, path)` |
| `VoValidation_CleanUpConfirm` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `VoValidation_CleanedUp` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `VoValidation_CleanUpPartial` | `Loc.Format(k, deleted, failed)` | `Loc.FormatCount(k, deleted, failed)` |
| `NodeDetail_AliasSharedCount` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `AliasImport_Message` (in `AliasImportConfirmDialog.axaml.cs`) | `Loc.Format(k, path, n)` | `Loc.FormatCount(k, n, path)` |
| `PatchManager_ConflictsFound` | `Loc.Format(k, n)` | `Loc.FormatCount(k, n)` |
| `PatchManager_ApplySuccess` | `Loc.Format(k, n, target)` | `Loc.FormatCount(k, n, target)` |

Verify each old call's actual argument order in place before transforming — the table's "old call shape" is the expected shape; if a site differs, keep its semantics and only ensure the count lands as the second argument (`FormatCount`'s `count`).

`Diff_DanglingWarning` has **no C# call site** — it is bound in `DiffWindow.axaml:136` via `StringFormat`. Replace the binding with a ViewModel property:

In `DiffViewModel.cs`, add next to the `DanglingLinks` collection (find `DanglingLinks` first; hook wherever it is (re)populated — raise the notification in the same place its own change notifications / related recomputes happen):

```csharp
    /// XAML previously formatted DanglingLinks.Count via StringFormat, which
    /// cannot pluralise; the text lives here now so FormatCount can.
    public string DanglingWarningText => Loc.FormatCount("Diff_DanglingWarning", DanglingLinks.Count);
```

with `OnPropertyChanged(nameof(DanglingWarningText));` raised wherever `DanglingLinks` is rebuilt/cleared. In `DiffWindow.axaml:136` replace:

```xml
                <TextBlock Text="{Binding DanglingLinks.Count, StringFormat={StaticResource Diff_DanglingWarning}}"
```

with:

```xml
                <TextBlock Text="{Binding DanglingWarningText}"
```

- [x] **Step 3: Build, fix key-suffix assertion breaks, full suite**

Run: `dotnet build && dotnet test --nologo`
Expected: build succeeds; some tests fail asserting bare keys (e.g. expecting `"NodeDetail_AliasSharedCount"` where the stub now yields `"NodeDetail_AliasSharedCount_Other"`). Fix per Global Constraints (append the correct suffix for the asserted count), re-run to green.

- [x] **Step 4: Commit**

```bash
git add -A DialogEditor.ViewModels DialogEditor.Avalonia DialogEditor.Avalonia.Shared DialogEditor.Tests
git commit -m "feat(l10n): migrate single-count strings to plural categories

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Two-count composites, bare suffixes, non-count stragglers

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`, `DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml`
- Modify: `DialogEditor.ViewModels/ViewModels/BatchReplaceViewModel.cs`, `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Modify: `DialogEditor.Avalonia/Views/ImportWarningsDialog.axaml.cs`
- Test: existing suites (suffix-assertion updates only)

**Interfaces:**
- Consumes: Task 2 `Loc.FormatCount`.
- Produces: fragment/wrapper keys below.

- [x] **Step 1: Two-count strings (BatchReplace)**

Replace `BatchReplace_StatusMatches` and `BatchReplace_StatusApplied` definitions with:

```xml
    <!-- Composed two-count status lines: wrapper takes two pre-pluralised
         fragments. Translators: translate fragments with their own plural
         forms; the wrapper only arranges them. -->
    <sys:String x:Key="BatchReplace_MatchCount_One">1 match</sys:String>
    <sys:String x:Key="BatchReplace_MatchCount_Other">{0} matches</sys:String>
    <sys:String x:Key="BatchReplace_ReplacementCount_One">1 replacement</sys:String>
    <sys:String x:Key="BatchReplace_ReplacementCount_Other">{0} replacements</sys:String>
    <sys:String x:Key="BatchReplace_ConversationCount_One">1 conversation</sys:String>
    <sys:String x:Key="BatchReplace_ConversationCount_Other">{0} conversations</sys:String>
    <sys:String x:Key="BatchReplace_StatusMatches">{0} across {1}</sys:String>
    <sys:String x:Key="BatchReplace_StatusApplied">Applied {0} across {1}</sys:String>
```

In `BatchReplaceViewModel.cs`, the two `Loc.Format` calls become compositions:

```csharp
Loc.Format("BatchReplace_StatusMatches",
    Loc.FormatCount("BatchReplace_MatchCount", matches),
    Loc.FormatCount("BatchReplace_ConversationCount", conversations))
```

```csharp
Loc.Format("BatchReplace_StatusApplied",
    Loc.FormatCount("BatchReplace_ReplacementCount", replacements),
    Loc.FormatCount("BatchReplace_ConversationCount", conversations))
```

(match the local variable names at each site).

- [x] **Step 2: Bare noun suffixes**

`NodeDetail_NotesWord` (value "note(s)", used as `$"{n} {Loc.Get("NodeDetail_NotesWord")}"` at `NodeDetailViewModel.cs:527`): replace key with

```xml
    <sys:String x:Key="NodeDetail_NotesCount_One">1 note</sys:String>
    <sys:String x:Key="NodeDetail_NotesCount_Other">{0} notes</sys:String>
```

and the call site with `Loc.FormatCount("NodeDetail_NotesCount", n)`.

`ImportWarnings_OccurrenceSuffix` (code-behind builds `$"<<{w.Construct}>> — {w.Count} {suffix}"` in `ImportWarningsDialog.axaml.cs:18-21`): replace key with

```xml
    <sys:String x:Key="ImportWarnings_OccurrenceCount_One">1 occurrence</sys:String>
    <sys:String x:Key="ImportWarnings_OccurrenceCount_Other">{0} occurrences</sys:String>
```

and the code-behind with (add `using DialogEditor.ViewModels.Resources;` if missing; Loc is configured at app startup before any window):

```csharp
        WarningsList.ItemsSource = warnings
            .Select(w => $"<<{w.Construct}>> — {Loc.FormatCount("ImportWarnings_OccurrenceCount", w.Count)}")
```

(keep the rest of the statement — ordering/ToList — as it is; delete the old `suffix` lookup lines.)

- [x] **Step 3: Non-count stragglers**

- `PatchManager_AddProjects` ("Add project(s)…") is a button label/file-picker title, no count exists — reword the value to `Add projects…` (key and both usages unchanged).
- `ToolTip_LinkConditions_Some` — zero references anywhere (verified 2026-07-04): delete the key.

- [x] **Step 4: Build, fix suffix assertions, full suite, commit**

Run: `dotnet build && dotnet test --nologo` → green after mechanical suffix fixes.

```bash
git add -A DialogEditor.ViewModels DialogEditor.Avalonia DialogEditor.Avalonia.Shared DialogEditor.Tests
git commit -m "feat(l10n): pluralise composed counts and noun suffixes; drop dead key

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 5: Guard rail, workflow pin, docs

**Files:**
- Create: `DialogEditor.Tests/Localisation/NoNaivePluralTests.cs`
- Modify: `DialogEditor.Tests/Services/UiStringImportServiceTests.cs` (one pinning test)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml` (translator notes header)
- Modify: `Gaps.md` (localisation item 6)

**Interfaces:**
- Consumes: Tasks 3–4 migration must be complete (the guard test fails otherwise).

- [x] **Step 1: Write the guard test (fails only if migration missed something)**

Create `DialogEditor.Tests/Localisation/NoNaivePluralTests.cs`, mirroring the file-scanning pattern of `DialogEditor.Tests/Accessibility/AutomationNameTests.cs` — copy that file's solution-root discovery helper verbatim, then:

```csharp
    // The "(s)"/"(es)" idiom cannot be translated into languages with more
    // than two plural forms. Pluralised strings use _One/_Few/_Many/_Other…
    // key pairs resolved by Loc.FormatCount (see 2026-07-04 pluralisation spec).
    [Theory]
    [InlineData("DialogEditor.Avalonia/Resources/Strings.axaml")]
    [InlineData("DialogEditor.Avalonia.Shared/Resources/SharedStrings.axaml")]
    public void StringValues_NeverUseNaivePluralSuffix(string relativePath)
    {
        var text     = File.ReadAllText(Path.Combine(SolutionRoot, relativePath));
        var offenders = System.Text.RegularExpressions.Regex
            .Matches(text, @"x:Key=""([^""]+)""[^<]*\((?:e?s)\)")
            .Select(m => m.Groups[1].Value)
            .ToList();
        Assert.Empty(offenders);
    }
```

Run: `dotnet test --nologo --filter "FullyQualifiedName~NoNaivePluralTests"` → PASS if Tasks 3–4 were complete (if it fails, the failure lists the missed keys — migrate them the same way before proceeding).

- [x] **Step 2: Pin translator-added plural rows in CSV import**

Append to `UiStringImportServiceTests`:

```csharp
    [Fact]
    public void Import_AcceptsPluralCategoryRows_AbsentFromEnglishSource()
    {
        // A Polish translator adds _Few/_Many rows that English never ships.
        var csv = WriteCsv("""
            Key,Source,Translation,File
            X_One,1 file,1 plik,Strings.axaml
            X_Few,,{0} pliki,Strings.axaml
            X_Many,,{0} plików,Strings.axaml
            """);
        UiStringImportService.Import(csv, "pl", _tempDir);
        var overlay = ReadOverlay(Path.Combine(_tempDir, "Strings.pl.axaml"));
        Assert.Equal("{0} pliki",  overlay["X_Few"]);
        Assert.Equal("{0} plików", overlay["X_Many"]);
    }
```

Run: `dotnet test --nologo --filter "FullyQualifiedName~UiStringImportServiceTests"` → expected PASS (the importer already writes all translated rows; if it fails, the importer filters unknown keys — remove that filter, this test is the spec).

- [x] **Step 3: Translator notes + Gaps.md**

In `Strings.axaml`'s translator-notes comment block at the top, append:

```xml
    <!-- Plurals: keys ending _One/_Few/_Many/_Other (etc.) are CLDR plural
         forms selected by count at runtime. Translate each form your language
         uses; add rows for categories your language needs that English lacks
         (e.g. Polish _Few/_Many) — the CSV import accepts them. {0} is always
         the count. -->
```

In `Gaps.md`, UI Localisation Readiness item 6, replace the item text:

```markdown
6. **Pluralisation ✓ implemented (2026-07-04):** `Loc.FormatCount` resolves CLDR
   plural-category key suffixes (`_One`/`_Few`/`_Many`/`_Other`…) with rules shipped
   for en/de/fr/pl/ru/ar (en fallback for unknown languages) and a fallback chain
   `_Category` → `_Other` → bare key. All 24 naive "(s)" strings migrated
   (`NoNaivePluralTests` bans the idiom); two-count lines compose pre-pluralised
   fragments; CSV import accepts translator-added category rows (pinned by test).
   Spec: docs/superpowers/specs/2026-07-04-pluralisation-design.md.
```

- [x] **Step 4: Full suite, commit**

Run: `dotnet build && dotnet test --nologo` → all pass.

```bash
git add DialogEditor.Tests/Localisation/NoNaivePluralTests.cs DialogEditor.Tests/Services/UiStringImportServiceTests.cs "DialogEditor.Avalonia/Resources/Strings.axaml" Gaps.md
git commit -m "test(l10n): ban naive plurals; pin translator-added category rows

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

- [x] **Step 5: Verification (2026-07-04)** — two-part, replacing the eyeball-only pass:

  1. `PluralResourceEndToEndTests` (headless, real `App` resources through the real
     `AvaloniaStringProvider`) pins the checklist strings: "1 match"/"3 matches",
     composed "2 matches across 1 conversation" / "1 match across 5 conversations",
     "1 note"/"4 notes", "Opened project 'Foo' (1 patch)"/"(3 patches)", and the
     SharedStrings key "Applied 1 patch to Foo." — 10 cases, all green.
  2. Live run: `dotnet run --project DialogEditor.Avalonia` — main window screenshot
     shows the status bar rendering "Opened project 'MyMod' (3 patches)" from the
     migrated keys; app boots cleanly with no resource errors.

- [x] **Step 6: Report results** — no failures; verification committed as the test above.
