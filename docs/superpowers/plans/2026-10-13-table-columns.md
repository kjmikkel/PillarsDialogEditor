# Table Columns That Scale With the Font — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** At 1.0× and 2.0× font scale no table header or cell overlaps its neighbour (issue 178).

**Architecture:** Each fixed table column width becomes a `ColumnWidth.*` resource token whose base value
`FontScaleApplier` multiplies by the font scale, exactly as it does `FontSize.*`. The width sits on each
header and row cell (`Width="{DynamicResource ColumnWidth.X}"`) in `Auto` columns, so header and rows
share one source. Long cells trim with an ellipsis and carry the full value as a tooltip; headers move
to `FontSize.Body`.

**Tech Stack:** Avalonia 11.3.22, xUnit + Avalonia.Headless (Skia), .NET 10.

**Spec:** `docs/superpowers/specs/2026-10-13-table-columns-design.md`

## Global Constraints

- Strict red/green TDD: every change starts from a failing test you watched fail.
- No hard-coded user-visible strings; tooltips bind to data, never literal text.
- Comments cite "issue 178" and say why (widths are font-scaled tokens shared by header and rows).
- Run tests with `dotnet test DialogEditor.Tests --filter "Category!=Gui"` (never bare `dotnet test`).
- New `.axaml`/`.cs` edits must keep each file's existing line endings; `git diff --check` must be clean.
- Commits end with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.
- The PR body notes: if overlapping/clipped table columns come back, revisit Avalonia's `DataGrid`.

## Review Focus

1. Live font-scale change with a table open: columns must re-widen without reopening (tokens are
   `DynamicResource`; Task 1's applier test plus Task 2's live-rescale check pin it).
2. Theme switch after a font-scale change: `ThemeApplier` swaps tokens and re-applies the scale; the
   column widths must come back scaled, not at base (Task 1 test).
3. A very long conversation name / commit subject: trimmed with "…" and a tooltip, never pushing
   later columns right (Task 2 fixtures use long values).
4. Empty tables (no rows): headers alone must still not clip (existing parameterless clipping cases).
5. Batch VO Import's button cells: the buttons must stay inside their column at 2.0× (Task 2
   clipping + alignment cover the nested grid).

---

### Task 1: `ColumnWidth.*` tokens scale with the font

**Files:**
- Create: `DialogEditor.Avalonia.Shared/Theming/ColumnWidthTokens.cs`
- Modify: `DialogEditor.Avalonia.Shared/Theming/FontScaleApplier.cs` (the `Apply` loop)
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml` (new `x:Double` block after the font sizes)
- Test: `DialogEditor.Tests/Theming/ColumnWidthTokenTests.cs`

**Interfaces:**
- Produces: `ColumnWidthTokens.BaseValues : IReadOnlyDictionary<string,double>` with the keys below;
  resources of the same names in `Tokens.axaml`.

Initial keys (base = today's width; Task 3+ may raise a value, never invent a key without adding it here):

| Key | Base | Used by |
|---|---|---|
| `ColumnWidth.Conversation` | 160 | Find in Project, Blame |
| `ColumnWidth.NodeId` | 70 | Find in Project (50), Blame (60), VO Validation (70) |
| `ColumnWidth.Field` | 110 | Find in Project |
| `ColumnWidth.Language` | 90 | Find in Project |
| `ColumnWidth.Author` | 140 | Blame (130), History (140) |
| `ColumnWidth.Date` | 110 | Blame, History |
| `ColumnWidth.CommitSha` | 90 | Blame, History |
| `ColumnWidth.Origin` | 70 | Speaker Lines |
| `ColumnWidth.SpeakerConversation` | 180 | Speaker Lines |
| `ColumnWidth.NodeLabel` | 90 | Speaker Lines |
| `ColumnWidth.Count` | 50 | Rep/Disposition Balance |
| `ColumnWidth.Share` | 70 | Rep/Disposition Balance |
| `ColumnWidth.Flag` | 110 | Rep/Disposition Balance |
| `ColumnWidth.BatchVo.Conversation` | 140 | Batch VO Import |
| `ColumnWidth.BatchVo.Node` | 60 | Batch VO Import |
| `ColumnWidth.BatchVo.Status` | 50 | Batch VO Import |
| `ColumnWidth.BatchVo.File` | 220 | Batch VO Import (two columns) |
| `ColumnWidth.BatchVo.Result` | 50 | Batch VO Import |

Ruling for the executor: where two windows used different widths for the same meaning, the token takes
the larger (NodeId 70, Author 140).

- [ ] **Step 1: Write the failing tests**

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;

namespace DialogEditor.Tests.Theming;

/// <summary>Issue 178: table column widths are tokens that scale with the font scale.</summary>
public class ColumnWidthTokenTests
{
    [AvaloniaFact]
    public void TokensAxaml_DeclaresEveryBaseValue()
    {
        foreach (var (key, value) in ColumnWidthTokens.BaseValues)
        {
            Assert.True(Application.Current!.TryGetResource(key, null, out var r), key);
            Assert.Equal(value, (double)r!);
        }
    }

    [AvaloniaFact]
    public void Apply_ScalesEveryColumnWidth()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            foreach (var (key, value) in ColumnWidthTokens.BaseValues)
            {
                Application.Current!.TryGetResource(key, null, out var r);
                Assert.Equal(value * 2.0, (double)r!);
            }
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void ThemeSwitch_KeepsColumnWidthsScaled()
    {
        // Review focus 2: ThemeApplier swaps token dictionaries, then re-applies the font scale.
        try
        {
            new FontScaleApplier().Apply(2.0);
            new ThemeApplier().Apply(ThemeService.Current.ActiveThemeId);
            Application.Current!.TryGetResource("ColumnWidth.Conversation", null, out var r);
            Assert.Equal(ColumnWidthTokens.BaseValues["ColumnWidth.Conversation"] * 2.0, (double)r!);
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }
}
```

(Executor: check `ThemeApplier`/`ThemeService` for the exact apply call and active-theme property names and
use those; the assertion is the point.)

- [ ] **Step 2: Run, expect FAIL** — `dotnet test DialogEditor.Tests --filter "Category!=Gui&FullyQualifiedName~ColumnWidthTokenTests"` → compile error: `ColumnWidthTokens` does not exist.

- [ ] **Step 3: Implement**

`ColumnWidthTokens.cs`, mirroring `FontSizeTokens.cs`:

```csharp
namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Unscaled table column widths in DIPs (issue 178). Tables are a header grid plus a row grid per
/// item; both read the same ColumnWidth.* token, and FontScaleApplier multiplies it by the font
/// scale, so columns grow with their text instead of overlapping at large scales. Single source
/// of truth for FontScaleApplier and ColumnWidthTokenTests; Tokens.axaml declares the same values.
/// See docs/superpowers/specs/2026-10-13-table-columns-design.md.
/// </summary>
public static class ColumnWidthTokens
{
    public static readonly IReadOnlyDictionary<string, double> BaseValues = new Dictionary<string, double>
    {
        ["ColumnWidth.Conversation"]         = 160,
        ["ColumnWidth.NodeId"]               = 70,
        ["ColumnWidth.Field"]                = 110,
        ["ColumnWidth.Language"]             = 90,
        ["ColumnWidth.Author"]               = 140,
        ["ColumnWidth.Date"]                 = 110,
        ["ColumnWidth.CommitSha"]            = 90,
        ["ColumnWidth.Origin"]               = 70,
        ["ColumnWidth.SpeakerConversation"]  = 180,
        ["ColumnWidth.NodeLabel"]            = 90,
        ["ColumnWidth.Count"]                = 50,
        ["ColumnWidth.Share"]                = 70,
        ["ColumnWidth.Flag"]                 = 110,
        ["ColumnWidth.BatchVo.Conversation"] = 140,
        ["ColumnWidth.BatchVo.Node"]         = 60,
        ["ColumnWidth.BatchVo.Status"]       = 50,
        ["ColumnWidth.BatchVo.File"]         = 220,
        ["ColumnWidth.BatchVo.Result"]       = 50,
    };
}
```

`FontScaleApplier.Apply`, after the font-size loop:

```csharp
        // Table column widths scale with the text they hold (issue 178).
        foreach (var (key, baseValue) in ColumnWidthTokens.BaseValues)
            dict[key] = baseValue * scale;
```

`Tokens.axaml`: a commented block of `<x:Double x:Key="ColumnWidth.…">…</x:Double>` with the same values.

- [ ] **Step 4: Run, expect PASS** (same command; 3 passed).

- [ ] **Step 5: Commit** — `feat: table column widths are font-scaled tokens (#178)`.

---

### Task 2: Table checks (red) — populated tables, alignment, header size, tooltips

**Files:**
- Create: `DialogEditor.Tests/Theming/TableWindowFixtures.cs` (populated window factories)
- Create: `DialogEditor.Tests/Theming/TableColumnsTests.cs`
- Modify: `DialogEditor.Tests/Theming/NoClippedTextTests.cs` (remove `TrackedByIssue178`; add fixtures to `Populated`)

**Interfaces:**
- Produces: `TableWindowFixtures.All : IReadOnlyDictionary<string, Func<Window>>` keyed
  `FindInProject`, `Blame`, `History`, `SpeakerLines`, `RepDisposition`, `BatchVoImport`, `VoValidation`.

- [ ] **Step 1: Write the fixtures.** Each factory builds the window with one or two rows, reusing the
  setup already in these tests (copy the helper code, do not reference test classes):
  - `FindInProject` — `FindInProjectWindowTests.ProjectWith` + `ProjectFindViewModel(...)`, then run a search
    that matches (as that test does) so `Results` has a row.
  - `Blame` — `BlameWindowTests.FakeGit` + `OneNodePorcelain` + `MakeVm`.
  - `History` — `HistoryWindowTests.MakeVm` with one commit line.
  - `SpeakerLines` — `SpeakerLineBrowserWindowTests` setup.
  - `RepDisposition` — `RepDispositionBalanceViewModelTests` setup at line 31 with an open snapshot that has
    one reputation and one disposition change.
  - `BatchVoImport` — `BatchVoImportViewModelTests.MakeRow()` (one found, one missing row) and `StubImporter`.
  - `VoValidation` — `VoValidationViewModelTests` setup with one node result and one orphan.
  Use long values (a 60-character conversation name / commit subject) wherever the setup lets you choose
  text (review focus 3).

- [ ] **Step 2: Write `TableColumnsTests`**

```csharp
/// <summary>
/// Issue 178: each table's header cells line up with the row cells beneath them, headers are at least
/// the row's text size, and a cell that trims its text offers the full value as a tooltip.
/// </summary>
public class TableColumnsTests
{
    public TableColumnsTests() => Loc.Configure(new StubStringProvider());

    public static IEnumerable<object[]> Cases() =>
        from name in TableWindowFixtures.All.Keys
        from scale in new[] { 1.0, 2.0 }
        select new object[] { name, scale };

    [AvaloniaTheory, MemberData(nameof(Cases))]
    public void HeaderCells_LineUpWithRowCells(string name, double scale) =>
        WithWindow(name, scale, w =>
        {
            foreach (var (header, row) in HeaderRowPairs(w))
                foreach (var h in header.Children.Where(c => c.IsEffectivelyVisible))
                {
                    var r = row.Children.First(c => Grid.GetColumn(c) == Grid.GetColumn(h));
                    var hx = h.TranslatePoint(default, w)!.Value.X;
                    var rx = r.TranslatePoint(default, w)!.Value.X;
                    Assert.True(Math.Abs(hx - rx) <= 0.5,
                        $"{name} at {scale}x: column {Grid.GetColumn(h)} header at x={hx:0.#}, row at x={rx:0.#}");
                }
        });

    [AvaloniaTheory, MemberData(nameof(Cases))]
    public void Headers_AreAtLeastRowSize(string name, double scale) =>
        WithWindow(name, scale, w =>
        {
            foreach (var (header, row) in HeaderRowPairs(w))
            {
                var rowSize = row.GetVisualDescendants().OfType<TextBlock>().Max(t => t.FontSize);
                foreach (var h in header.Children.OfType<TextBlock>())
                    Assert.True(h.FontSize >= rowSize - 0.01, $"{name}: header \"{h.Text}\" {h.FontSize} < row {rowSize}");
            }
        });

    [AvaloniaTheory, MemberData(nameof(Cases))]
    public void TrimmedCells_HaveTooltips(string name, double scale) =>
        WithWindow(name, scale, w =>
        {
            var bare = w.GetVisualDescendants().OfType<TextBlock>()
                .Where(t => t.TextTrimming != TextTrimming.None && t.FindAncestorOfType<ListBoxItem>() is not null
                            || t.TextTrimming != TextTrimming.None && t.FindAncestorOfType<ContentPresenter>()?.Parent is ItemsControl)
                .Where(t => ToolTip.GetTip(t) is not string { Length: > 0 })
                .Select(t => t.Text).ToList();
            Assert.True(bare.Count == 0, $"{name}: trimmed cells without a tooltip: {string.Join(", ", bare)}");
        });

    [AvaloniaFact]
    public void LiveRescale_WidensOpenTable()
    {
        // Review focus 1: a scale change reaches an already-open table.
        WithWindow("FindInProject", 1.0, w =>
        {
            var (header, _) = HeaderRowPairs(w).First();
            var before = header.Children[0].Bounds.Width;
            new FontScaleApplier().Apply(2.0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(before * 2.0, header.Children[0].Bounds.Width, 0.5);
        });
    }

    // A header grid is the Grid just before an ItemsControl in the same panel, holding ≥2 TextBlocks;
    // its row grid is the first Grid inside the first realised item.
    private static IEnumerable<(Grid Header, Grid Row)> HeaderRowPairs(Window w) =>
        from items in w.GetVisualDescendants().OfType<ItemsControl>()
        where items is not ComboBox && items.IsEffectivelyVisible && items.GetVisualParent() is Panel
        let siblings = ((Panel)items.GetVisualParent()!).Children
        let header = siblings.OfType<Grid>().FirstOrDefault(g => g.Children.OfType<TextBlock>().Count() >= 2)
        let row = items.GetVisualDescendants().OfType<Grid>().FirstOrDefault(g => g.Children.Count >= 2)
        where header is not null && row is not null
        select (header, row);

    private static void WithWindow(string name, double scale, Action<Window> check)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => null;
        var w = TableWindowFixtures.All[name]();
        try
        {
            new FontScaleApplier().Apply(scale);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            check(w);
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }
}
```

(Executor: fix usings/compile details; the `TrimmedCells` predicate may be simplified to "inside an
ItemsControl item container" — keep the meaning.)

- [ ] **Step 3: `NoClippedTextTests`** — delete `TrackedByIssue178` and its filter; add
  `foreach (var (k, f) in TableWindowFixtures.All) Populated[k] = f;` (or list them in the initializer).

- [ ] **Step 4: Run, expect FAIL** — `dotnet test DialogEditor.Tests --filter "Category!=Gui&(FullyQualifiedName~TableColumnsTests|FullyQualifiedName~NoClippedTextTests)" > $WS/t2.txt`.
  Expected failures: clipping in the six previously exempt headers and in populated rows at 2.0×; header size
  in every table with `col-header`/`Caption` headers; trimmed cells without tooltips; `LiveRescale` (fixed widths
  don't change). Record the list in the ledger; it is the to-do list for Tasks 3–6. Alignment may already pass
  (fixed widths are equal) — that is fine, it guards the conversion.

- [ ] **Step 5: Commit** (tests only, red) — `test: table columns must line up, fit and explain trimmed cells (#178)`.
  Note in the commit body that the suite is intentionally red until Task 6.

---

### Task 3: Find in Project, Speaker Lines, Rep/Disposition Balance

**Files:** `DialogEditor.Avalonia/Views/FindInProjectWindow.axaml`, `SpeakerLineBrowserWindow.axaml`,
`RepDispositionBalanceWindow.axaml`.

For each table, in both the header grid and the row template grid:

- [ ] **Step 1:** Replace each fixed width in `ColumnDefinitions` with `Auto` (star columns stay `*`), e.g.
  Find in Project `160,50,110,90,*` → `Auto,Auto,Auto,Auto,*`.
- [ ] **Step 2:** Give every cell in those columns `Width="{DynamicResource ColumnWidth.X}"` per the Task 1
  table; header and row cell in the same column get the same token and the same `Margin`. Add a right
  padding so adjacent columns don't touch: `Padding` is not on TextBlock, so use `Margin="0,0,8,0"` on
  both header and row cell (same on both keeps alignment).
- [ ] **Step 3:** Change the `col-header` style's `FontSize` to `{DynamicResource FontSize.Body}`.
- [ ] **Step 4:** Every row cell that can exceed its width gets `TextTrimming="CharacterEllipsis"` and
  `ToolTip.Tip="{Binding <same path as Text>}"`. Fixed-format cells (counts, share %) need neither.
- [ ] **Step 5:** Add one comment above each header grid:
  `<!-- Issue 178: column widths are ColumnWidth.* tokens that scale with the font; header and row cells share them, so they stay aligned. -->`
- [ ] **Step 6:** Run Task 2's command; the cases for these three windows pass. If a header still clips at 1.0×,
  raise its base in `ColumnWidthTokens` and `Tokens.axaml` together (ledger a ruling with the new value).
- [ ] **Step 7: Commit** — `fix: Find in Project, Speaker Lines and Rep/Disposition columns scale with the font (#178)`.

### Task 4: Attribution (Blame) and History

**Files:** `BlameWindow.axaml`, `HistoryWindow.axaml`. Same steps 1–7 as Task 3. Blame's headers set their
size inline: change it to `FontSize.Body`. History has no header grid; its row cells still take tokens, trim
and get tooltips. Date cells use `TextTrimming` + tooltip only if the formatted date can exceed the width at
1.0×; the clipping test decides. Commit: `fix: Attribution and History columns scale with the font (#178)`.

### Task 5: Batch VO Import and VO Validation

**Files:** `BatchVoImportDialog.axaml`, `VoValidationWindow.axaml`. Same steps as Task 3, except:
- Batch VO Import's two VO/alias columns contain a nested `Grid` (text + buttons): the width token goes on
  the nested grid and on the matching header cell (review focus 5).
- VO Validation's node cell `Width="70"` → `Width="{DynamicResource ColumnWidth.NodeId}"`.
Commit: `fix: Batch VO Import and VO Validation columns scale with the font (#178)`.

### Task 6: Flow Analytics, full suite, captures

- [ ] **Step 1:** Run `NoClippedTextTests` for `FlowAnalyticsWindow` at 2.0×, populated with the
  `FlowAnalyticsViewModelTests` setup (add a `FlowAnalytics` fixture to `TableWindowFixtures` first, red
  if it clips). Fix what still overlaps: stat columns wrap (`TextWrapping="Wrap"` already on the values —
  check the labels), and the "Playthrough stats" heading row `*,Auto,Auto` must not let the checkbox cover the
  heading (heading `TextTrimming` + tooltip, or let the row wrap). Commit if changed.
- [ ] **Step 2:** Full suite: `dotnet test DialogEditor.Tests --filter "Category!=Gui"` → all pass.
- [ ] **Step 3:** Captures at 1.0× and 2.0× with `tools/ui-automation/Capture-Windows.ps1` for the surfaces in
  the spec; read each image; no overlap. Build a before/after sheet (no local paths/usernames) for the PR.
- [ ] **Step 4:** `git diff --check main...HEAD` clean.
