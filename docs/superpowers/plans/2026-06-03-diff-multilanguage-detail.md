# Multi-Language Before/After Detail Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Extend the diff window's before/after detail panel to show a selected node's text across the primary language plus every changed language, as stacked, labeled sections.

**Architecture:** A pure `NodeDiffDetailViewModel` produces an ordered list of `LanguageDiffSection`s (primary first, then alphabetical). `DiffViewModel` caches per-node, per-language text by reconstructing each diff side once per candidate language. A reusable `InlineDiffTextBlock` control renders each before/after pair via the existing `TextDiff`. Friendly language names come from a `LanguageNameResolver` (resource keys with raw-code fallback).

**Tech Stack:** C# 12 / .NET 8, CommunityToolkit.Mvvm, Avalonia 11.3.14, xUnit + `Avalonia.Headless.XUnit`. Reuses `DialogEditor.Patch.GitConflict.TextDiff`.

**Spec:** `docs/superpowers/specs/2026-06-03-diff-multilanguage-detail-design.md`

---

## File Structure

- **Create** `DialogEditor.ViewModels/Services/LanguageNameResolver.cs` — code → friendly name (resource keys + fallback).
- **Create** `DialogEditor.Tests/ViewModels/LanguageNameResolverTests.cs`.
- **Create** `DialogEditor.Avalonia/Controls/InlineDiffTextBlock.cs` — self-rendering before/after highlight control.
- **Create** `DialogEditor.Tests/Controls/InlineDiffTextBlockTests.cs`.
- **Modify** `DialogEditor.Avalonia/Resources/Strings.axaml` — language-name keys + primary marker.
- **Modify** `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs` — `ReconstructConversation(language)`, per-language caches.
- **Modify** `DialogEditor.ViewModels/ViewModels/NodeDiffDetailViewModel.cs` — `Sections` model (+ new `LanguageDiffSection`).
- **Modify** `DialogEditor.Avalonia/Views/DiffWindow.axaml(.cs)` — `ItemsControl` of sections; simplified code-behind.
- **Modify** `DialogEditor.Tests/ViewModels/{NodeDiffDetailViewModelTests,DiffViewModelTests}.cs`, `DialogEditor.Tests/Views/DiffWindowTests.cs`.
- **Modify** `Gaps.md`.

Tasks 1–4 are independent and green. **Task 5 is the atomic model swap** (VM + DiffViewModel + DiffWindow + their tests together — the shared contract cannot be split across green commits). Task 6 is docs + verification.

---

### Task 1: `LanguageNameResolver` and tests

**Files:**
- Create: `DialogEditor.Tests/ViewModels/LanguageNameResolverTests.cs`
- Create: `DialogEditor.ViewModels/Services/LanguageNameResolver.cs`

Mirrors `SpeakerNameService.Resolve`'s resolve-or-fallback shape. The code→resource-key map lives in code; the names live in resources (added in Task 2). `StubStringProvider` returns the key verbatim, so tests assert the key string for known codes and the raw code for unknown ones.

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public class LanguageNameResolverTests
{
    public LanguageNameResolverTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void Resolve_KnownCode_ReturnsLocalizedNameKey()
    {
        // Stub returns the key verbatim.
        Assert.Equal("Language_Name_fr", LanguageNameResolver.Resolve("fr"));
    }

    [Fact]
    public void Resolve_KnownCode_IsCaseInsensitive()
    {
        Assert.Equal("Language_Name_fr", LanguageNameResolver.Resolve("FR"));
    }

    [Fact]
    public void Resolve_HyphenatedKnownCode_MapsToSafeKey()
    {
        Assert.Equal("Language_Name_ptBR", LanguageNameResolver.Resolve("pt-BR"));
    }

    [Fact]
    public void Resolve_UnknownCode_ReturnsRawCode()
    {
        Assert.Equal("xx", LanguageNameResolver.Resolve("xx"));
    }
}
```

- [ ] **Step 2: Run, confirm fail**

```
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~LanguageNameResolverTests"
```
Expected: compile error — `LanguageNameResolver` does not exist.

- [ ] **Step 3: Implement**

```csharp
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// Resolves a game language code (e.g. "fr", "pt-BR") to a friendly, localized
/// name. Known PoE1/PoE2 codes map to resource keys; unknown codes fall back to
/// the raw code. Mirrors SpeakerNameService's resolve-or-fallback shape.
public static class LanguageNameResolver
{
    private static readonly IReadOnlyDictionary<string, string> KnownKeys =
        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["en"]    = "Language_Name_en",
            ["fr"]    = "Language_Name_fr",
            ["de"]    = "Language_Name_de",
            ["es"]    = "Language_Name_es",
            ["it"]    = "Language_Name_it",
            ["pl"]    = "Language_Name_pl",
            ["ru"]    = "Language_Name_ru",
            ["pt-BR"] = "Language_Name_ptBR",
            ["zh-CN"] = "Language_Name_zhCN",
            ["ko"]    = "Language_Name_ko",
            ["ja"]    = "Language_Name_ja",
        };

    /// Friendly name for a code, or the raw code if it is not a known language.
    public static string Resolve(string code) =>
        KnownKeys.TryGetValue(code, out var key) ? Loc.Get(key) : code;
}
```

- [ ] **Step 4: Run, confirm pass** (`...LanguageNameResolverTests`) — expect 4 passed.

- [ ] **Step 5: Commit**

```
git add DialogEditor.ViewModels/Services/LanguageNameResolver.cs DialogEditor.Tests/ViewModels/LanguageNameResolverTests.cs
git commit -m "feat: LanguageNameResolver — code to friendly name with raw-code fallback"
```

---

### Task 2: Localized strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

Add the language-name values the resolver keys point at, plus the primary marker. These must exist before Task 5's XAML references `Diff_Detail_PrimaryMarker`. Add near the other `Diff_Detail_*` keys (search `Diff_Detail_Header`), matching the file's `<sys:String x:Key="...">` convention.

- [ ] **Step 1: Add keys**

```xml
    <!-- Diff detail: language section labels -->
    <sys:String x:Key="Diff_Detail_PrimaryMarker">(primary)</sys:String>
    <sys:String x:Key="Language_Name_en">English</sys:String>
    <sys:String x:Key="Language_Name_fr">French</sys:String>
    <sys:String x:Key="Language_Name_de">German</sys:String>
    <sys:String x:Key="Language_Name_es">Spanish</sys:String>
    <sys:String x:Key="Language_Name_it">Italian</sys:String>
    <sys:String x:Key="Language_Name_pl">Polish</sys:String>
    <sys:String x:Key="Language_Name_ru">Russian</sys:String>
    <sys:String x:Key="Language_Name_ptBR">Portuguese (Brazil)</sys:String>
    <sys:String x:Key="Language_Name_zhCN">Chinese (Simplified)</sys:String>
    <sys:String x:Key="Language_Name_ko">Korean</sys:String>
    <sys:String x:Key="Language_Name_ja">Japanese</sys:String>
```

- [ ] **Step 2: Build** — `dotnet build DialogEditor.Avalonia` — expect 0 errors.

- [ ] **Step 3: Commit**

```
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: localized language names and primary marker for diff detail"
```

---

### Task 3: `InlineDiffTextBlock` control and tests

**Files:**
- Create: `DialogEditor.Tests/Controls/InlineDiffTextBlockTests.cs`
- Create: `DialogEditor.Avalonia/Controls/InlineDiffTextBlock.cs`

A `TextBlock` subclass with `Before`/`After`/`ShowAfter` styled properties. On any change it re-renders its own `Inlines` from `TextDiff.Diff(Before, After)`: each instance shows one side — the *before* side (Common + before-only text, before-only highlighted) when `ShowAfter=false`, the *after* side when `ShowAfter=true`. Brushes match `GitConflictResolutionWindow`.

- [ ] **Step 1: Write the failing tests**

```csharp
using System.Linq;
using Avalonia.Controls.Documents;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Controls;

namespace DialogEditor.Tests.Controls;

public class InlineDiffTextBlockTests
{
    private static string Text(InlineDiffTextBlock c) =>
        string.Concat((c.Inlines ?? new InlineCollection()).OfType<Run>().Select(r => r.Text));

    [AvaloniaFact]
    public void BeforeSide_RendersCommonPlusBeforeOnlyText()
    {
        var c = new InlineDiffTextBlock { ShowAfter = false, Before = "hello world", After = "hello there" };
        Assert.Equal("hello world", Text(c));
    }

    [AvaloniaFact]
    public void AfterSide_RendersCommonPlusAfterOnlyText()
    {
        var c = new InlineDiffTextBlock { ShowAfter = true, Before = "hello world", After = "hello there" };
        Assert.Equal("hello there", Text(c));
    }

    [AvaloniaFact]
    public void IdenticalText_RendersThatTextOnBothSides()
    {
        var before = new InlineDiffTextBlock { ShowAfter = false, Before = "same", After = "same" };
        var after  = new InlineDiffTextBlock { ShowAfter = true,  Before = "same", After = "same" };
        Assert.Equal("same", Text(before));
        Assert.Equal("same", Text(after));
    }

    [AvaloniaFact]
    public void NullStrings_RenderEmpty()
    {
        var c = new InlineDiffTextBlock { ShowAfter = false };
        Assert.Equal("", Text(c));
    }
}
```

- [ ] **Step 2: Run, confirm fail** — `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~InlineDiffTextBlockTests"` — compile error (type missing).

- [ ] **Step 3: Implement**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using DialogEditor.Patch.GitConflict;

namespace DialogEditor.Avalonia.Controls;

/// A TextBlock that renders one side of a before/after text diff, highlighting
/// the changed run. Each instance shows the before side (ShowAfter=false) or the
/// after side (ShowAfter=true). Reuses TextDiff so the highlighting matches the
/// git-conflict resolution window.
public class InlineDiffTextBlock : TextBlock
{
    private static readonly IBrush CommonBrush = new SolidColorBrush(Color.Parse("#e8e8e8"));
    private static readonly IBrush BeforeBrush = new SolidColorBrush(Color.Parse("#9be39b"));
    private static readonly IBrush AfterBrush  = new SolidColorBrush(Color.Parse("#ff9c9c"));

    public static readonly StyledProperty<string?> BeforeProperty =
        AvaloniaProperty.Register<InlineDiffTextBlock, string?>(nameof(Before));
    public static readonly StyledProperty<string?> AfterProperty =
        AvaloniaProperty.Register<InlineDiffTextBlock, string?>(nameof(After));
    public static readonly StyledProperty<bool> ShowAfterProperty =
        AvaloniaProperty.Register<InlineDiffTextBlock, bool>(nameof(ShowAfter));

    public string? Before { get => GetValue(BeforeProperty); set => SetValue(BeforeProperty, value); }
    public string? After  { get => GetValue(AfterProperty);  set => SetValue(AfterProperty, value); }

    /// false → render the before side; true → render the after side.
    public bool ShowAfter { get => GetValue(ShowAfterProperty); set => SetValue(ShowAfterProperty, value); }

    static InlineDiffTextBlock()
    {
        BeforeProperty.Changed.AddClassHandler<InlineDiffTextBlock>((c, _) => c.RenderDiff());
        AfterProperty.Changed.AddClassHandler<InlineDiffTextBlock>((c, _) => c.RenderDiff());
        ShowAfterProperty.Changed.AddClassHandler<InlineDiffTextBlock>((c, _) => c.RenderDiff());
    }

    private void RenderDiff()
    {
        var before = Before ?? "";
        var after  = After ?? "";
        var inlines = new InlineCollection();

        foreach (var span in TextDiff.Diff(before, after))
        {
            switch (span.Kind)
            {
                case DiffKind.Common:
                    inlines.Add(MakeRun(span.Text, CommonBrush));
                    break;
                case DiffKind.MineOnly:
                    if (!ShowAfter) inlines.Add(MakeRun(span.Text, BeforeBrush));
                    break;
                case DiffKind.TheirsOnly:
                    if (ShowAfter) inlines.Add(MakeRun(span.Text, AfterBrush));
                    break;
            }
        }

        Inlines = inlines;
    }

    private static Run MakeRun(string text, IBrush brush) => new(text) { Foreground = brush };
}
```

- [ ] **Step 4: Run, confirm pass** (`...InlineDiffTextBlockTests`) — expect 4 passed.

- [ ] **Step 5: Commit**

```
git add DialogEditor.Avalonia/Controls/InlineDiffTextBlock.cs DialogEditor.Tests/Controls/InlineDiffTextBlockTests.cs
git commit -m "feat: InlineDiffTextBlock — reusable before/after highlight control"
```

---

### Task 4: Parameterize `ReconstructConversation` by language

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`

Pure refactor: add a `language` parameter to `ReconstructConversation` (replacing its use of the `_language` field) so the VM can reconstruct any language. All three existing call sites pass `_language`, so behavior is unchanged — verified by the existing suite.

- [ ] **Step 1: Change the method signature and body**

In `ReconstructConversation`, change the signature to add `string language` and replace both `patch.Translations.GetValueOrDefault(_language)` uses with `...GetValueOrDefault(language)`:

```csharp
    private Conversation ReconstructConversation(
        string name, DialogProject? project, IGameDataProvider provider, string language)
    {
        var file = provider.FindConversation(name);

        if (project is not null && project.Patches.TryGetValue(name, out var patch))
        {
            if (file is not null)
            {
                var conv     = provider.LoadConversation(file);
                var baseSnap = ConversationSnapshotBuilder.Build(conv);
                var merged   = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                var translations = patch.Translations.GetValueOrDefault(language);
                return ConversationSnapshotBuilder.ToConversation(name, merged, translations);
            }
            else
            {
                var baseSnap = new ConversationEditSnapshot([]);
                var merged   = PatchApplier.Apply(baseSnap, patch, ignoreConflicts: true);
                var translations = patch.Translations.GetValueOrDefault(language);
                return ConversationSnapshotBuilder.ToConversation(name, merged, translations);
            }
        }
        else if (file is not null)
        {
            return provider.LoadConversation(file);
        }
        else
        {
            return new Conversation(name, [], new StringTable([]));
        }
    }
```

- [ ] **Step 2: Update the three call sites** to pass `_language`:

In `BuildDiffCanvas` (the right reconstruction):
```csharp
            Conversation rightConv = ReconstructConversation(name, _rightProject, _provider, _language);
```
In `BuildDiffCanvas` (the left/ghost reconstruction):
```csharp
                Conversation leftConv = ReconstructConversation(name, _leftProject, _provider, _language);
```
In `BuildAppliedPreviewCanvas`:
```csharp
            Conversation conv = ReconstructConversation(name, projected, _provider!, _language);
```

- [ ] **Step 3: Build + run the diff suites** (no behavior change expected):

```
dotnet build DialogEditor.ViewModels
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffViewModelTests"
```
Expected: build clean; all DiffViewModelTests pass.

- [ ] **Step 4: Commit**

```
git add DialogEditor.ViewModels/ViewModels/DiffViewModel.cs
git commit -m "refactor: ReconstructConversation takes an explicit language parameter"
```

---

### Task 5: Multi-language detail model, caches, and panel (atomic swap)

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDiffDetailViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/NodeDiffDetailViewModelTests.cs`
- Modify: `DialogEditor.ViewModels/ViewModels/DiffViewModel.cs`
- Modify: `DialogEditor.Tests/ViewModels/DiffViewModelTests.cs`
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml.cs`
- Modify: `DialogEditor.Tests/Views/DiffWindowTests.cs`

This task changes the `NodeDiffDetailViewModel` public surface from flat strings to a `Sections` list. Its three consumers (VM tests, `DiffViewModel`, `DiffWindow` code-behind + XAML) must change together — they will not compile otherwise. Do it as one commit. Follow the sub-steps in order; build only needs to be green at the end (Step 11).

- [ ] **Step 1: Rewrite `NodeDiffDetailViewModelTests.cs`** (replace the whole file)

```csharp
using System.Collections.Generic;
using System.Linq;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDiffDetailViewModelTests
{
    public NodeDiffDetailViewModelTests() => Loc.Configure(new StubStringProvider());

    private static IReadOnlyDictionary<string, (string Default, string Female)> Map(
        params (string Lang, string Default, string Female)[] items) =>
        items.ToDictionary(i => i.Lang, i => (i.Default, i.Female));

    private static readonly IReadOnlyDictionary<string, (string Default, string Female)> Empty = Map();

    [Fact]
    public void Primary_IsAlwaysPresent_EvenWhenUnchanged()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", ""), ("fr", "x", "")),
            Map(("en", "a", ""), ("fr", "y", "")));

        Assert.Equal(2, vm.Sections.Count);
        var en = vm.Sections.Single(s => s.LanguageCode == "en");
        Assert.True(en.IsPrimary);
        Assert.Contains(vm.Sections, s => s.LanguageCode == "fr");
    }

    [Fact]
    public void NonPrimary_Unchanged_IsExcluded()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", ""), ("de", "d", "")),
            Map(("en", "b", ""), ("de", "d", "")));

        Assert.Single(vm.Sections);
        Assert.Equal("en", vm.Sections[0].LanguageCode);
    }

    [Fact]
    public void Sections_OrderedPrimaryFirstThenAlphabetical()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "fr",
            Map(("en", "1", ""), ("de", "2", ""), ("fr", "3", "")),
            Map(("en", "x", ""), ("de", "y", ""), ("fr", "z", "")));

        Assert.Equal(new[] { "fr", "de", "en" }, vm.Sections.Select(s => s.LanguageCode).ToArray());
    }

    [Fact]
    public void StructuralOnly_WhenNoLanguageDiffers()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", "")), Map(("en", "a", "")));

        Assert.True(vm.IsStructuralOnly);
        Assert.False(vm.ShowSections);
        Assert.Empty(vm.Sections);
    }

    [Fact]
    public void Added_PlaceholderBefore_PerSection()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Added, "en",
            Empty, Map(("en", "hi", ""), ("fr", "salut", "")));

        var en = vm.Sections.Single(s => s.LanguageCode == "en");
        Assert.Equal("Diff_Detail_NodeAdded", en.DefaultBefore);
        Assert.Equal("hi", en.DefaultAfter);
        var fr = vm.Sections.Single(s => s.LanguageCode == "fr");
        Assert.Equal("Diff_Detail_NodeAdded", fr.DefaultBefore);
        Assert.Equal("salut", fr.DefaultAfter);
    }

    [Fact]
    public void Removed_PlaceholderAfter_PerSection()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Removed, "en",
            Map(("en", "bye", ""), ("fr", "adieu", "")), Empty);

        var en = vm.Sections.Single(s => s.LanguageCode == "en");
        Assert.Equal("bye", en.DefaultBefore);
        Assert.Equal("Diff_Detail_NodeRemoved", en.DefaultAfter);
    }

    [Fact]
    public void FemaleRow_Visibility_IsPerSection()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", "fa"), ("de", "d", "")),
            Map(("en", "b", "fb"), ("de", "e", "")));

        Assert.True(vm.Sections.Single(s => s.LanguageCode == "en").HasFemaleRow);
        Assert.False(vm.Sections.Single(s => s.LanguageCode == "de").HasFemaleRow);
    }

    [Fact]
    public void Section_LanguageName_IsResolved()
    {
        var vm = new NodeDiffDetailViewModel(1, DiffStatus.Changed, "en",
            Map(("en", "a", "")), Map(("en", "b", "")));

        // Stub returns the resource key verbatim.
        Assert.Equal("Language_Name_en", vm.Sections[0].LanguageName);
    }

    [Fact]
    public void HeaderText_UsesLocFormatKey()
    {
        var vm = new NodeDiffDetailViewModel(7, DiffStatus.Changed, "en",
            Map(("en", "a", "")), Map(("en", "b", "")));

        Assert.Equal("Diff_Detail_Header", vm.HeaderText);
    }
}
```

- [ ] **Step 2: Run, confirm fail** — `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NodeDiffDetailViewModelTests"` — compile error (new constructor/`Sections` missing).

- [ ] **Step 3: Rewrite `NodeDiffDetailViewModel.cs`** (replace the whole file)

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// One language's before/after text for a diff node, with placeholder
/// substitution for added/removed nodes and per-language female-row visibility.
public sealed class LanguageDiffSection
{
    public string LanguageCode  { get; }
    public string LanguageName  { get; }
    public bool   IsPrimary     { get; }
    public string DefaultBefore { get; }
    public string DefaultAfter  { get; }
    public string FemaleBefore  { get; }
    public string FemaleAfter   { get; }
    public bool   HasFemaleRow  { get; }

    public LanguageDiffSection(
        string code, bool isPrimary, DiffStatus kind,
        (string Default, string Female) left, (string Default, string Female) right)
    {
        LanguageCode = code;
        LanguageName = LanguageNameResolver.Resolve(code);
        IsPrimary    = isPrimary;

        DefaultBefore = kind == DiffStatus.Added   ? Loc.Get("Diff_Detail_NodeAdded")   : left.Default;
        DefaultAfter  = kind == DiffStatus.Removed ? Loc.Get("Diff_Detail_NodeRemoved") : right.Default;

        HasFemaleRow  = !string.IsNullOrEmpty(left.Female) || !string.IsNullOrEmpty(right.Female);
        FemaleBefore  = kind == DiffStatus.Added   ? Loc.Get("Diff_Detail_NodeAdded")   : left.Female;
        FemaleAfter   = kind == DiffStatus.Removed ? Loc.Get("Diff_Detail_NodeRemoved") : right.Female;
    }
}

/// Before/after text detail for one selected diff node, across the primary
/// language plus every language whose text changed. Pure presentation logic;
/// the view renders each section's before/after via InlineDiffTextBlock.
public sealed class NodeDiffDetailViewModel
{
    public int        NodeId { get; }
    public DiffStatus Kind   { get; }

    public IReadOnlyList<LanguageDiffSection> Sections { get; }
    public bool   IsStructuralOnly { get; }
    public bool   ShowSections => !IsStructuralOnly;
    public string HeaderText   => Loc.Format("Diff_Detail_Header", NodeId);

    public NodeDiffDetailViewModel(
        int nodeId, DiffStatus kind, string primaryLanguage,
        IReadOnlyDictionary<string, (string Default, string Female)> leftByLang,
        IReadOnlyDictionary<string, (string Default, string Female)> rightByLang)
    {
        NodeId = nodeId;
        Kind   = kind;

        var codes = new HashSet<string>(StringComparer.Ordinal) { primaryLanguage };
        foreach (var k in leftByLang.Keys)  codes.Add(k);
        foreach (var k in rightByLang.Keys) codes.Add(k);

        (string Default, string Female) Left(string c)  => leftByLang.GetValueOrDefault(c, ("", ""));
        (string Default, string Female) Right(string c) => rightByLang.GetValueOrDefault(c, ("", ""));

        bool Differs(string c)
        {
            var l = Left(c);
            var r = Right(c);
            return !string.Equals(l.Default, r.Default, StringComparison.Ordinal)
                || !string.Equals(l.Female,  r.Female,  StringComparison.Ordinal);
        }

        IsStructuralOnly = kind == DiffStatus.Changed && !codes.Any(Differs);

        Sections = IsStructuralOnly
            ? []
            : codes
                .Where(c => c == primaryLanguage || Differs(c))
                .OrderBy(c => c == primaryLanguage ? 0 : 1)
                .ThenBy(c => c, StringComparer.Ordinal)
                .Select(c => new LanguageDiffSection(c, c == primaryLanguage, kind, Left(c), Right(c)))
                .ToList();
    }
}
```

- [ ] **Step 4: Change `DiffViewModel` cache types and selection wiring**

Change the two cache fields:
```csharp
    private readonly Dictionary<int, Dictionary<string, (string Default, string Female)>> _leftTextById  = new();
    private readonly Dictionary<int, Dictionary<string, (string Default, string Female)>> _rightTextById = new();
```
Add a shared empty map near the fields:
```csharp
    private static readonly IReadOnlyDictionary<string, (string Default, string Female)> EmptyLangMap =
        new Dictionary<string, (string Default, string Female)>();
```
Replace `UpdateSelectedNodeDetail` with:
```csharp
    private void UpdateSelectedNodeDetail()
    {
        var node = DiffCanvas?.SelectedNode;
        if (CanvasMode != CanvasMode.Changes || node is null
            || node.DiffStatus == DiffStatus.Unchanged)
        {
            SelectedNodeDetail = null;
            return;
        }

        var leftByLang  = _leftTextById.GetValueOrDefault(node.NodeId)  ?? EmptyLangMap;
        var rightByLang = _rightTextById.GetValueOrDefault(node.NodeId) ?? EmptyLangMap;
        SelectedNodeDetail = new NodeDiffDetailViewModel(
            node.NodeId, node.DiffStatus, _language, leftByLang, rightByLang);
    }
```

- [ ] **Step 5: Replace the cache-filling in `BuildDiffCanvas` with a per-language pass**

In `BuildDiffCanvas`, **remove** the old `_rightTextById.Clear(); foreach (var n in rightConv.Nodes) { ... }` block and the `_leftTextById[...] = ...` line inside the left-reconstruction `try` (the ghost-injection loop stays). After the left-reconstruction `try/catch` block, and before `DiffCanvas = vm;`, insert:

```csharp
            // ── Per-language before/after text caches for the detail panel ──
            var candidateLangs = new HashSet<string>(StringComparer.Ordinal) { _language };
            foreach (var k in _leftProject?.Patches.GetValueOrDefault(name)?.Translations.Keys
                              ?? Enumerable.Empty<string>())
                candidateLangs.Add(k);
            foreach (var k in _rightProject?.Patches.GetValueOrDefault(name)?.Translations.Keys
                              ?? Enumerable.Empty<string>())
                candidateLangs.Add(k);
            BuildTextCaches(name, candidateLangs);
```

The left-reconstruction `try` block should now read (note: no `_leftTextById` fill inside it anymore — only ghosting):
```csharp
            try
            {
                Conversation leftConv = ReconstructConversation(name, _leftProject, _provider, _language);

                if (removedSet.Count > 0)
                {
                    foreach (var leftNode in leftConv.Nodes)
                    {
                        if (!removedSet.Contains(leftNode.NodeId)) continue;
                        if (vm.Nodes.Any(n => n.NodeId == leftNode.NodeId)) continue;

                        var entry   = leftConv.Strings.Get(leftNode.NodeId);
                        var ghost   = new NodeViewModel(leftNode, entry);
                        ghost.OnSelected   = n => vm.SelectedNode = n;
                        ghost.Input.Owner  = ghost;
                        ghost.Output.Owner = ghost;
                        ghost.DiffStatus   = DiffStatus.Removed;
                        vm.Nodes.Add(ghost);
                    }
                }
            }
            catch (Exception ex)
            {
                AppLog.Warn($"DiffViewModel: could not reconstruct left conversation for '{name}': {ex.Message}");
            }
```

- [ ] **Step 6: Add the cache-building helpers** (place near `BuildDiffCanvas`)

```csharp
    private void BuildTextCaches(string name, IEnumerable<string> languages)
    {
        _leftTextById.Clear();
        _rightTextById.Clear();
        if (_provider is null) return;

        foreach (var lang in languages)
        {
            FillLangCache(_rightTextById, name, _rightProject, lang);
            FillLangCache(_leftTextById,  name, _leftProject,  lang);
        }
    }

    private void FillLangCache(
        Dictionary<int, Dictionary<string, (string Default, string Female)>> cache,
        string name, DialogProject? project, string lang)
    {
        try
        {
            var conv = ReconstructConversation(name, project, _provider!, lang);
            foreach (var n in conv.Nodes)
            {
                var e = conv.Strings.Get(n.NodeId);
                if (!cache.TryGetValue(n.NodeId, out var byLang))
                    cache[n.NodeId] = byLang = new Dictionary<string, (string Default, string Female)>(StringComparer.Ordinal);
                byLang[lang] = (e?.DefaultText ?? "", e?.FemaleText ?? "");
            }
        }
        catch (Exception ex)
        {
            AppLog.Warn($"DiffViewModel: text cache for '{name}' [{lang}] failed: {ex.Message}");
        }
    }
```

Confirm `using System.Linq;` is present at the top of `DiffViewModel.cs` (it is). The early-return and error paths already call `_leftTextById.Clear(); _rightTextById.Clear();` — leave those as-is (they still compile against the new dictionary type).

- [ ] **Step 7: Update the affected `DiffViewModelTests`**

The integration tests `SelectingAddedNode_PopulatesDetail_WithPlaceholderBefore`, `SelectingChangedNode_PopulatesBothSides_FromTheirReconstructions`, and `SelectingRemovedGhostNode_PopulatesDetail_WithRemovedPlaceholder` assert on the removed flat properties. Update their assertions to read the primary section. Replace each test's assertion block as follows.

In `SelectingAddedNode_PopulatesDetail_WithPlaceholderBefore`, replace the final assertions with:
```csharp
        Assert.NotNull(vm.SelectedNodeDetail);
        var sec = vm.SelectedNodeDetail!.Sections.Single(s => s.LanguageCode == "en");
        Assert.Equal(DiffStatus.Added, vm.SelectedNodeDetail.Kind);
        Assert.Equal("Diff_Detail_NodeAdded", sec.DefaultBefore);
        Assert.Equal(node2.DefaultText, sec.DefaultAfter);
```

In `SelectingChangedNode_PopulatesBothSides_FromTheirReconstructions`, replace the final assertions with:
```csharp
        Assert.NotNull(vm.SelectedNodeDetail);
        var sec = vm.SelectedNodeDetail!.Sections.Single(s => s.LanguageCode == "en");
        Assert.Equal(DiffStatus.Changed, vm.SelectedNodeDetail.Kind);
        Assert.Equal("old text", sec.DefaultBefore);
        Assert.Equal("new text", sec.DefaultAfter);
        Assert.Equal(node1.DefaultText, sec.DefaultAfter);
```

In `SelectingRemovedGhostNode_PopulatesDetail_WithRemovedPlaceholder`, replace the flat-property assertions with (keep the rest of the test's setup and the ghost-node selection):
```csharp
        Assert.NotNull(vm.SelectedNodeDetail);
        var sec = vm.SelectedNodeDetail!.Sections.Single(s => s.LanguageCode == "en");
        Assert.Equal(DiffStatus.Removed, vm.SelectedNodeDetail.Kind);
        Assert.Equal("removed line", sec.DefaultBefore);
        Assert.Equal("Diff_Detail_NodeRemoved", sec.DefaultAfter);
```

(If any of these tests reference `vm.SelectedNodeDetail.DefaultBefore`/`.DefaultAfter` elsewhere, replace those reads with `sec.DefaultBefore`/`sec.DefaultAfter`. The `AppliedPreviewMode_SuppressesSelectedNodeDetail`, `Clearing*`, and `SelectingUnchangedNode_LeavesDetailNull` tests assert `SelectedNodeDetail` is null and need no change.)

- [ ] **Step 8: Add a multi-language integration test + helper to `DiffViewModelTests`**

Add this helper next to `PatchWithText`:
```csharp
    // Builds a patch whose nodes carry per-language text via Translations.
    private static ConversationPatch PatchMultiLang(
        string convName,
        IReadOnlyList<int> nodeIds,
        IReadOnlyDictionary<string, IReadOnlyList<(int Id, string Text)>> byLang)
    {
        var snapNodes = nodeIds.Select(NodeT).ToList();
        var translations = byLang.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<NodeTranslation>)kv.Value
                .Select(t => new NodeTranslation(t.Id, t.Text, "")).ToList());
        return new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, snapNodes, [], [])
        {
            Translations = translations
        };
    }
```
Add the test:
```csharp
    [Fact]
    public void SelectingNodeChangedInTwoLanguages_YieldsSectionPerLanguage()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));

        var disk = DialogProject.Empty("p").WithPatch(PatchMultiLang(convName, [1],
            new Dictionary<string, IReadOnlyList<(int, string)>>
            {
                ["en"] = [(1, "old en")],
                ["fr"] = [(1, "vieux fr")],
            }));
        var refp = DialogProject.Empty("p").WithPatch(PatchMultiLang(convName, [1],
            new Dictionary<string, IReadOnlyList<(int, string)>>
            {
                ["en"] = [(1, "new en")],
                ["fr"] = [(1, "neuf fr")],
            }));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp), branchOutput: "main\n");

        var vm = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        vm.Selected = vm.Changes.Single(c => c.Name == convName);
        vm.DiffCanvas!.SelectedNode = vm.DiffCanvas.Nodes.First(n => n.NodeId == 1);

        var codes = vm.SelectedNodeDetail!.Sections.Select(s => s.LanguageCode).ToList();
        Assert.Contains("en", codes);
        Assert.Contains("fr", codes);
        var fr = vm.SelectedNodeDetail.Sections.Single(s => s.LanguageCode == "fr");
        Assert.Equal("vieux fr", fr.DefaultBefore);
        Assert.Equal("neuf fr",  fr.DefaultAfter);
    }
```

- [ ] **Step 9: Rewrite the `DiffWindow.axaml` detail panel**

Add the controls namespace to the root `<Window ...>` element:
```xml
        xmlns:controls="clr-namespace:DialogEditor.Avalonia.Controls"
```
Replace the entire `<Border ... x:Name="DetailPanel"> ... </Border>` block (the before/after detail panel) with:
```xml
                <!-- ── Before/after detail panel (per changed language) ── -->
                <Border DockPanel.Dock="Bottom"
                        x:Name="DetailPanel"
                        Background="#1e1e1e"
                        BorderBrush="#444" BorderThickness="1" CornerRadius="4"
                        Padding="8" Margin="0,6,0,0"
                        IsVisible="{Binding SelectedNodeDetail, Converter={StaticResource IsNotNull}}"
                        ToolTip.Tip="{StaticResource ToolTip_Diff_Detail}">
                    <DockPanel DataContext="{Binding SelectedNodeDetail}">
                        <TextBlock DockPanel.Dock="Top" Text="{Binding HeaderText}"
                                   Foreground="#ddd" FontWeight="Bold" FontSize="12"/>
                        <TextBlock DockPanel.Dock="Top"
                                   Text="{StaticResource Diff_Detail_StructuralOnly}"
                                   Foreground="#e0a030" FontSize="11" TextWrapping="Wrap"
                                   IsVisible="{Binding IsStructuralOnly}"/>
                        <ScrollViewer MaxHeight="180" IsVisible="{Binding ShowSections}">
                            <ItemsControl x:Name="SectionsList" ItemsSource="{Binding Sections}">
                                <ItemsControl.ItemTemplate>
                                    <DataTemplate>
                                        <StackPanel Spacing="2" Margin="0,6,0,0">
                                            <StackPanel Orientation="Horizontal" Spacing="4">
                                                <TextBlock Text="{Binding LanguageName}"
                                                           Foreground="#bbb" FontWeight="SemiBold" FontSize="11"/>
                                                <TextBlock Text="{StaticResource Diff_Detail_PrimaryMarker}"
                                                           Foreground="#666" FontSize="10"
                                                           IsVisible="{Binding IsPrimary}"/>
                                            </StackPanel>

                                            <TextBlock Text="{StaticResource Diff_Detail_DefaultTextLabel}"
                                                       Foreground="#888" FontSize="10"/>
                                            <Grid ColumnDefinitions="Auto,*">
                                                <TextBlock Grid.Column="0" Text="{StaticResource Diff_Detail_BeforeLabel}"
                                                           Foreground="#888" FontSize="11" Margin="0,0,6,0"/>
                                                <controls:InlineDiffTextBlock Grid.Column="1"
                                                           Before="{Binding DefaultBefore}" After="{Binding DefaultAfter}"
                                                           ShowAfter="False" Foreground="#ddd" FontSize="12" TextWrapping="Wrap"/>
                                            </Grid>
                                            <Grid ColumnDefinitions="Auto,*">
                                                <TextBlock Grid.Column="0" Text="{StaticResource Diff_Detail_AfterLabel}"
                                                           Foreground="#888" FontSize="11" Margin="0,0,6,0"/>
                                                <controls:InlineDiffTextBlock Grid.Column="1"
                                                           Before="{Binding DefaultBefore}" After="{Binding DefaultAfter}"
                                                           ShowAfter="True" Foreground="#ddd" FontSize="12" TextWrapping="Wrap"/>
                                            </Grid>

                                            <StackPanel Spacing="2" IsVisible="{Binding HasFemaleRow}">
                                                <TextBlock Text="{StaticResource Diff_Detail_FemaleTextLabel}"
                                                           Foreground="#888" FontSize="10"/>
                                                <Grid ColumnDefinitions="Auto,*">
                                                    <TextBlock Grid.Column="0" Text="{StaticResource Diff_Detail_BeforeLabel}"
                                                               Foreground="#888" FontSize="11" Margin="0,0,6,0"/>
                                                    <controls:InlineDiffTextBlock Grid.Column="1"
                                                               Before="{Binding FemaleBefore}" After="{Binding FemaleAfter}"
                                                               ShowAfter="False" Foreground="#ddd" FontSize="12" TextWrapping="Wrap"/>
                                                </Grid>
                                                <Grid ColumnDefinitions="Auto,*">
                                                    <TextBlock Grid.Column="0" Text="{StaticResource Diff_Detail_AfterLabel}"
                                                               Foreground="#888" FontSize="11" Margin="0,0,6,0"/>
                                                    <controls:InlineDiffTextBlock Grid.Column="1"
                                                               Before="{Binding FemaleBefore}" After="{Binding FemaleAfter}"
                                                               ShowAfter="True" Foreground="#ddd" FontSize="12" TextWrapping="Wrap"/>
                                                </Grid>
                                            </StackPanel>
                                        </StackPanel>
                                    </DataTemplate>
                                </ItemsControl.ItemTemplate>
                            </ItemsControl>
                        </ScrollViewer>
                    </DockPanel>
                </Border>
```

- [ ] **Step 10: Simplify `DiffWindow.axaml.cs`** (replace the whole file)

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class DiffWindow : Window
{
    private DiffHelpWindow? _helpWindow;

    public DiffWindow() => InitializeComponent();

    public DiffWindow(DiffViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        if (_helpWindow is null || !_helpWindow.IsVisible)
            _helpWindow = new DiffHelpWindow();
        _helpWindow.Show();
        _helpWindow.Activate();
    }

    private void UndoBringIn_Click(object? sender, RoutedEventArgs e)
        => (DataContext as DiffViewModel)?.RequestUndoApply?.Invoke();
}
```

- [ ] **Step 11: Add a sections-count headless test to `DiffWindowTests`**

Add a multi-language helper (same shape as Task 5 Step 8) and a test. Add `using Avalonia.Controls;` if not present (it is).

```csharp
    private static ConversationPatch PatchMultiLang(
        string convName, IReadOnlyList<int> nodeIds,
        IReadOnlyDictionary<string, IReadOnlyList<(int Id, string Text)>> byLang)
    {
        var snapNodes = nodeIds.Select(NodeT).ToList();
        var translations = byLang.ToDictionary(
            kv => kv.Key,
            kv => (IReadOnlyList<NodeTranslation>)kv.Value
                .Select(t => new NodeTranslation(t.Id, t.Text, "")).ToList());
        return new ConversationPatch(convName, ConversationPatch.CurrentSchemaVersion, snapNodes, [], [])
        {
            Translations = translations
        };
    }

    [AvaloniaFact]
    public void DetailPanel_ShowsOneSectionPerChangedLanguage()
    {
        var convName = "greeting";
        var file     = new ConversationFile(convName, "", "/fake/greeting.conversation", "/fake/greeting.stringtable");
        var provider = new StubProvider(file, new ConversationEditSnapshot([]));

        var disk = DialogProject.Empty("p").WithPatch(PatchMultiLang(convName, [1],
            new Dictionary<string, IReadOnlyList<(int, string)>>
            { ["en"] = [(1, "old en")], ["fr"] = [(1, "vieux")] }));
        var refp = DialogProject.Empty("p").WithPatch(PatchMultiLang(convName, [1],
            new Dictionary<string, IReadOnlyList<(int, string)>>
            { ["en"] = [(1, "new en")], ["fr"] = [(1, "neuf")] }));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));

        var vm     = new DiffViewModel(git, new StubDispatcher(), path, provider, "en");
        var window = new DiffWindow(vm);
        window.Show();

        vm.Selected = vm.Changes.Single(c => c.Name == convName);
        vm.DiffCanvas!.SelectedNode = vm.DiffCanvas.Nodes.First(n => n.NodeId == 1);

        Assert.Equal(2, window.FindControl<ItemsControl>("SectionsList")!.ItemCount);
    }
```
Confirm the test file's usings include `DialogEditor.Core.Editing`, `DialogEditor.Core.Models`, `DialogEditor.Core.GameData`, `System.Linq`, `System.Collections.Generic` (add any missing).

- [ ] **Step 12: Build and run the full suite**

```
dotnet build
dotnet test DialogEditor.Tests
```
Expected: build clean; all tests pass. Fix compile errors (e.g. any lingering reference to the removed flat properties) before committing.

- [ ] **Step 13: Commit**

```
git add DialogEditor.ViewModels/ViewModels/NodeDiffDetailViewModel.cs DialogEditor.ViewModels/ViewModels/DiffViewModel.cs DialogEditor.Avalonia/Views/DiffWindow.axaml DialogEditor.Avalonia/Views/DiffWindow.axaml.cs DialogEditor.Tests/ViewModels/NodeDiffDetailViewModelTests.cs DialogEditor.Tests/ViewModels/DiffViewModelTests.cs DialogEditor.Tests/Views/DiffWindowTests.cs
git commit -m "feat: multi-language before/after detail — stacked language sections"
```

---

### Task 6: Update Gaps.md and full verification

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Update Gaps.md**

In the "Diff viewing" paragraph, find the sentence added previously:
> Multi-language before/after (showing every changed language at once) remains a deferred follow-up — the panel currently uses the diff window's selected language.

Replace it with:
> Multi-language before/after is implemented: the detail panel shows the node's text for the primary language plus every language whose text changed, as stacked sections (primary first, then alphabetical) with friendly language names and inline highlighting. Languages with no change (other than the primary anchor) are omitted.

If the exact sentence is not found, search for "Multi-language before/after" and report before editing.

- [ ] **Step 2: Full verification**

```
dotnet test DialogEditor.Tests
dotnet build
```
Expected: all tests pass; `Build succeeded. 0 Error(s)`. If anything fails, stop and report — do not commit.

- [ ] **Step 3: Commit**

```
git add Gaps.md
git commit -m "docs: record multi-language before/after detail as implemented"
```

---

## Verification Checklist

1. `dotnet test DialogEditor.Tests` — all pass (suite runs serially by design).
2. `dotnet build` — 0 errors.
3. **Manual:** open the diff window with a game folder loaded, pick two endpoints that differ; select a node changed in one language — one section, the primary, labeled with its friendly name and "(primary)".
4. **Manual:** select a node whose text changed in two languages — two stacked sections, primary first, each with its own before/after highlight.
5. **Manual:** a node changed only in a non-primary language — the primary section appears (as an unchanged anchor) plus the changed language; an unrelated unchanged language does not appear.
6. **Manual:** a structural-only change (no language text differs) — the "structural only" hint, no sections.
7. **Manual:** Applied-Preview mode — panel still hidden.
8. **Manual:** a node with female text in a language shows that section's Female rows; a section without female text does not.
