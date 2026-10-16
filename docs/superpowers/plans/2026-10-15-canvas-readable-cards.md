# Readable Canvas Cards Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Conversation-canvas cards a writer can read at 100%: 12 px text, 240 px wide, growing to four lines, laid out by their real height. Font scale magnifies the canvas, so cards look the same at every scale.

**Architecture:** A new `CardMetrics` class in `DialogEditor.Core` is the single source of card dimensions. `AutoLayoutService` uses its height estimate, the node template reads the same numbers through `Canvas.*` tokens that `FontScaleApplier` never touches, and a headless render test keeps the estimate and the template in step. `ConversationView` multiplies its zoom by `FontScaleService.Current.Scale` instead of letting text grow inside fixed cards.

**Tech Stack:** .NET 10, Avalonia 11.3.22, NodifyAvalonia 6.6.0, xUnit with `Avalonia.Headless.XUnit` (Skia text measurement, see `DialogEditor.Tests/AvaloniaTestApp.cs`).

**Spec:** `docs/superpowers/specs/2026-10-15-canvas-readable-cards-design.md` (approved). Mockup: `docs/superpowers/mockups/2026-10-15-canvas-look-and-feel.html`.

## Global Constraints

- **TDD:** red/green, strictly (`CLAUDE.md`). Every task starts with a failing test, and you watch it fail before implementing.
- **No hard-coded user-visible text:** put it in resources. This plan adds no new strings. If you find you need one, it goes in `DialogEditor.Avalonia/Resources/Strings.axaml`.
- **UI rules:** every interactive control keeps its `ToolTip` and its UIA name. Ports keep their current automation behaviour.
- **Exceptions:** every caught exception in production code is logged with `AppLog.Warn`/`AppLog.Error`.
- **Colours** go through `Palette.*.axaml` primitives and `Tokens.axaml` brushes, never literal colours in views.
- **Reasoning in comments:** put the "why" in comments in the `.cs`/`.axaml` files (project convention), not only in the spec.
- **Tests:**
  - Headless run: `dotnet test DialogEditor.Tests --filter "Category!=Gui"`. A bare `dotnet test` launches the GUI.
  - The suite runs serially by design.
  - Tests that call `new FontScaleApplier().Apply(x)` or `ThemeApplier` must reset in `finally`, as `ColumnWidthTokenTests` does.
- **Canvas values:** these are copied verbatim from the spec, and every task uses them.
  - card width 240;
  - fonts: header 12 (bold), body 12, footer 10;
  - line heights: body 16, header 16, footer 13;
  - padding: header 3 × 6, body 4 × 6, footer 2 × 6;
  - 1 px card border;
  - body 1–4 lines;
  - gaps: column 100, row 28;
  - a four-line card must stay under 130, the old row pitch.
- **Branch:** `feat/82-readable-cards`. The spec and mockup are already committed there. Commit after every task, ending the message with `Co-Authored-By: Claude Opus 5.5 <noreply@anthropic.com>`.

## Review Focus

1. **A line with no spaces**, such as a long `SetGlobalValue(...)` script line or a URL, must wrap or trim inside the card, never widen it or spill out. Pinned in Task 5 (`CardRender_NoSpaceLine_StaysInsideCard`).
2. **An empty or whitespace-only text** (shown as the #84 placeholder) gives a one-line card, not a zero-height body that the next card overlaps. Pinned in Task 1 (`EstimateHeight_EmptyAndWhitespace_IsOneLine`) and Task 5 (`CardRender_EmptyText_MatchesOneLineEstimate`).
3. **Switching font scale while a conversation is open** keeps the user's place and doesn't compound: 1.0 → 2.0 → 1.0 returns to the original zoom. Pinned in Task 7 (`FontScaleRoundTrip_RestoresZoom`).
4. **A layout saved before this change**, at the old 130 px pitch, must not overlap with the new cards. Pinned in Task 2 (`FourLineCard_FitsOldRowPitch`) and Task 5 (`CardRender_FourLinesAtMost113`).
5. **Theme switch at font scale 2.0:** the `Canvas.*` tokens must stay at their base values after `ThemeApplier` re-applies the font scale, or the canvas would be scaled twice (font tokens × zoom). Pinned in Task 4 (`ThemeSwitch_LeavesCanvasTokensUnscaled`).

---

### Task 1: `CardMetrics`, the single source of card size

**Files:**
- Create: `DialogEditor.Core/Layout/CardMetrics.cs`
- Test: `DialogEditor.Tests/Layout/CardMetricsTests.cs`

**Interfaces:**
- Produces:
  - `public static class CardMetrics` in namespace `DialogEditor.Core.Layout`.
  - Constants (`public const double`): `Width = 240`, `HeaderFontSize = 12`, `BodyFontSize = 12`, `FooterFontSize = 10`, `HeaderLineHeight = 16`, `BodyLineHeight = 16`, `FooterLineHeight = 13`, `MaxBodyLines = 4` (an `int` const), `ColumnGap = 100`, `RowGap = 28`, `BorderThickness = 1`.
  - Derived values (`public static double`): `HeaderHeight`, `FooterHeight`, `MaxHeight`.
  - `public static int EstimateBodyLines(string? text)`.
  - `public static double EstimateHeight(string? text)`.

- [ ] **Step 1: Write the failing tests**

```csharp
using DialogEditor.Core.Layout;

namespace DialogEditor.Tests.Layout;

/// Issue 82: one source of truth for a canvas card's size, shared by the node template
/// (through the Canvas.* tokens) and AutoLayoutService.
public class CardMetricsTests
{
    [Fact]
    public void HeaderAndFooterHeights_AreLineHeightPlusPadding()
    {
        Assert.Equal(16 + 2 * 3, CardMetrics.HeaderHeight);
        Assert.Equal(13 + 2 * 2, CardMetrics.FooterHeight);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void EstimateHeight_EmptyAndWhitespace_IsOneLine(string? text)
    {
        Assert.Equal(1, CardMetrics.EstimateBodyLines(text));
        Assert.Equal(CardMetrics.EstimateHeight("x"), CardMetrics.EstimateHeight(text));
    }

    [Fact]
    public void EstimateHeight_ShortLine_IsOneLine() =>
        Assert.Equal(1, CardMetrics.EstimateBodyLines("Tell me about the storm."));

    [Fact]
    public void EstimateHeight_VeryLongText_CapsAtFourLines()
    {
        var text = string.Concat(Enumerable.Repeat("A long line of dialogue. ", 40));
        Assert.Equal(CardMetrics.MaxBodyLines, CardMetrics.EstimateBodyLines(text));
        Assert.Equal(CardMetrics.MaxHeight, CardMetrics.EstimateHeight(text));
    }

    [Fact]
    public void EstimateHeight_NeverDecreasesAsTextGrows()
    {
        var previous = 0.0;
        for (var n = 0; n <= 400; n += 7)
        {
            var h = CardMetrics.EstimateHeight(new string('a', n));
            Assert.True(h >= previous, $"height fell from {previous} to {h} at {n} characters");
            previous = h;
        }
    }

    [Fact]
    public void FourLineCard_FitsOldRowPitch() =>
        // Layouts saved before issue 82 used a 110 + 20 = 130 row pitch. A full card must fit
        // inside it so those layouts don't overlap with the new cards.
        Assert.True(CardMetrics.MaxHeight < 130, $"MaxHeight {CardMetrics.MaxHeight} >= 130");
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CardMetricsTests"`
Expected: build error `CS0103: The name 'CardMetrics' does not exist`.

- [ ] **Step 3: Implement**

```csharp
namespace DialogEditor.Core.Layout;

/// <summary>
/// Issue 82: the conversation-canvas card's dimensions, in canvas units (px at 100% zoom and
/// font scale 1.0). This is the single source of truth: Tokens.axaml declares the same values
/// as Canvas.* tokens for the node template, and AutoLayoutService places cards using
/// <see cref="EstimateHeight"/>. CanvasCardRenderTests renders the real template and fails if
/// the two drift apart. Font scale never changes these numbers: it magnifies the whole canvas
/// instead (ConversationView), so layouts and line breaks are the same at every scale.
/// See docs/superpowers/specs/2026-10-15-canvas-readable-cards-design.md.
/// </summary>
public static class CardMetrics
{
    public const double Width            = 240;
    public const double BorderThickness  = 1;
    public const double HeaderFontSize   = 12;
    public const double BodyFontSize     = 12;
    public const double FooterFontSize   = 10;
    public const double HeaderLineHeight = 16;
    public const double BodyLineHeight   = 16;
    public const double FooterLineHeight = 13;
    public const int    MaxBodyLines     = 4;
    public const double ColumnGap        = 100;
    public const double RowGap           = 28;

    private const double HeaderPaddingY = 3, BodyPaddingY = 4, FooterPaddingY = 2, PaddingX = 6;

    // Deliberately wider than the body font's real average glyph: an estimate that is too tall
    // only adds space, one that is too short overlaps the next card. CanvasCardRenderTests
    // checks that it never under-counts the lines the template actually renders.
    private const double AverageCharWidth = 6.4;

    private const double BodyTextWidth = Width - 2 * BorderThickness - 2 * PaddingX;

    public static double HeaderHeight => HeaderLineHeight + 2 * HeaderPaddingY;
    public static double FooterHeight => FooterLineHeight + 2 * FooterPaddingY;
    public static double MaxHeight    => HeightFor(MaxBodyLines);

    public static int EstimateBodyLines(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return 1;   // the #84 placeholder is one line
        var perLine = Math.Max(1, (int)Math.Floor(BodyTextWidth / AverageCharWidth));
        var lines = 0;
        foreach (var paragraph in text.Split('\n'))
            lines += Math.Max(1, (int)Math.Ceiling(paragraph.TrimEnd('\r').Length / (double)perLine));
        return Math.Clamp(lines, 1, MaxBodyLines);
    }

    public static double EstimateHeight(string? text) => HeightFor(EstimateBodyLines(text));

    private static double HeightFor(int bodyLines) =>
        2 * BorderThickness + HeaderHeight + bodyLines * BodyLineHeight + 2 * BodyPaddingY + FooterHeight;
}
```

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CardMetricsTests"`
Expected: PASS (7 tests). `MaxHeight` = 2 + 22 + 64 + 8 + 17 = 113.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Layout/CardMetrics.cs DialogEditor.Tests/Layout/CardMetricsTests.cs
git commit -m "feat: CardMetrics, one source of canvas card size (#82)"
```

---

### Task 2: Auto-layout places cards by their height

**Files:**
- Modify: `DialogEditor.Core/Layout/AutoLayoutService.cs` (whole file, ~120 lines)
- Modify: `DialogEditor.Tests/Layout/AutoLayoutServiceTests.cs` (its constants `NodeWidth = 220`, `NodeHeight = 110`, `HGap`, `VGap` describe the old layout)
- Test: `DialogEditor.Tests/Layout/AutoLayoutServiceTests.cs`. `DialogEditor.Tests/Services/AutoLayoutServiceTests.cs` must keep passing unchanged.

**Interfaces:**
- Consumes: `CardMetrics.Width`, `.ColumnGap`, `.RowGap`, `.MaxHeight` (Task 1).
- Produces:
  - `AutoLayoutService.Apply(IReadOnlyList<ConversationNode> nodes, Func<int, double> heightOf, Action<int, double, double> setLocation)`.
  - The existing `Apply(nodes, setLocation)` stays as an overload that passes `_ => CardMetrics.MaxHeight`.

- [ ] **Step 1: Write the failing tests** (add to `DialogEditor.Tests/Layout/AutoLayoutServiceTests.cs`, and replace the four old constants with the ones below)

```csharp
    private const double Width  = CardMetrics.Width;
    private const double RowGap = CardMetrics.RowGap;

    private static Dictionary<int, (double X, double Y)> Layout(
        IReadOnlyList<ConversationNode> nodes, Func<int, double> heightOf)
    {
        var p = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(nodes, heightOf, (id, x, y) => p[id] = (x, y));
        return p;
    }

    [Fact]
    public void Apply_ColumnPitch_IsCardWidthPlusColumnGap()
    {
        var p = Layout(new[] { Node(0, 1), Node(1) }, _ => 50);
        Assert.Equal(CardMetrics.Width + CardMetrics.ColumnGap, p[1].X - p[0].X);
    }

    [Fact]
    public void Apply_Siblings_SitRowGapBelowThePreviousCardsBottom()
    {
        // Node 1 is tall, node 2 short: node 2 starts RowGap below node 1's bottom edge,
        // not one fixed pitch below its top.
        var heights = new Dictionary<int, double> { [0] = 40, [1] = 113, [2] = 40 };
        var p = Layout(new[] { Node(0, 1, 2), Node(1), Node(2) }, id => heights[id]);
        var (upper, lower) = p[1].Y < p[2].Y ? (1, 2) : (2, 1);
        Assert.Equal(p[upper].Y + heights[upper] + RowGap, p[lower].Y, 3);
    }

    [Fact]
    public void Apply_NoTwoCardsOverlap_ForMixedHeights()
    {
        // A branching conversation with several roots and a back-edge.
        var nodes = new[] { Node(0, 1, 2, 3), Node(1, 4), Node(2, 4, 5), Node(3), Node(4, 0), Node(5), Node(6, 5) };
        var rnd = new Random(82);
        var heights = nodes.ToDictionary(n => n.NodeId, _ => 41 + rnd.Next(0, 73));
        var p = Layout(nodes, id => heights[id]);
        foreach (var a in nodes)
            foreach (var b in nodes.Where(b => b.NodeId > a.NodeId))
            {
                var (pa, pb) = (p[a.NodeId], p[b.NodeId]);
                var overlapX = pa.X < pb.X + Width && pb.X < pa.X + Width;
                var overlapY = pa.Y < pb.Y + heights[b.NodeId] && pb.Y < pa.Y + heights[a.NodeId];
                Assert.False(overlapX && overlapY, $"nodes {a.NodeId} and {b.NodeId} overlap");
            }
    }

    [Fact]
    public void Apply_TwoArgumentOverload_UsesMaxHeight()
    {
        var p = new Dictionary<int, (double X, double Y)>();
        AutoLayoutService.Apply(new[] { Node(0, 1, 2), Node(1), Node(2) }, (id, x, y) => p[id] = (x, y));
        Assert.Equal(CardMetrics.MaxHeight + RowGap, Math.Abs(p[1].Y - p[2].Y), 3);
    }
```

Also update the existing `Apply_TwoNodesInSameLayer_VerticallySpaced` to assert `yDiff >= CardMetrics.MaxHeight + RowGap`.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AutoLayoutServiceTests"`
Expected: build error. `Apply` has no overload taking 3 arguments.

- [ ] **Step 3: Implement.** In `AutoLayoutService.cs`:
  - Delete the four constants.
  - Add the overloads.
  - Replace the fixed `pitch` with per-node heights.

  The full new `Apply`:

```csharp
    /// <summary>Lays out with every card at its largest size. For callers without the text.</summary>
    public static void Apply(IReadOnlyList<ConversationNode> nodes, Action<int, double, double> setLocation)
        => Apply(nodes, _ => CardMetrics.MaxHeight, setLocation);

    /// <summary>
    /// Layered layout (issue 82): column pitch is the card width plus <see cref="CardMetrics.ColumnGap"/>.
    /// Within a column each card sits <see cref="CardMetrics.RowGap"/> below the previous card's
    /// bottom edge, using <paramref name="heightOf"/>, so tall and short cards neither overlap nor
    /// leave fixed-pitch holes. Callers pass <c>CardMetrics.EstimateHeight(text)</c>.
    /// A full card (CardMetrics.MaxHeight) is under the old 130 px pitch, so layouts saved
    /// before this change still don't overlap.
    /// </summary>
    public static void Apply(
        IReadOnlyList<ConversationNode> nodes,
        Func<int, double> heightOf,
        Action<int, double, double> setLocation)
    {
        var layers = AssignLayers(nodes);
        var byLayer = layers.GroupBy(kv => kv.Value, kv => kv.Key).OrderBy(g => g.Key).ToList();

        var inputOrder = new Dictionary<int, int>();
        for (var i = 0; i < nodes.Count; i++) inputOrder[nodes[i].NodeId] = i;

        var parents = nodes
            .SelectMany(n => n.Links
                .Where(l => layers.TryGetValue(l.ToNodeId, out var childLayer) && childLayer > layers[n.NodeId])
                .Select(l => (Child: l.ToNodeId, Parent: n.NodeId)))
            .GroupBy(p => p.Child)
            .ToDictionary(g => g.Key, g => g.Select(p => p.Parent).ToList());

        var assignedY = new Dictionary<int, double>();

        foreach (var layer in byLayer)
        {
            var nodeIds = layer.ToList();

            if (layer.Key == 0)
            {
                // Roots have no parent to align to: stack them, centred on the origin.
                // As before, the first and last roots' tops sit symmetrically about 0 (a single
                // root lands at exactly 0).
                var total = nodeIds.Sum(heightOf) + CardMetrics.RowGap * (nodeIds.Count - 1);
                var y0 = -(total - heightOf(nodeIds[^1])) / 2.0;
                foreach (var id in nodeIds)
                {
                    assignedY[id] = y0;
                    y0 += heightOf(id) + CardMetrics.RowGap;
                }
                continue;
            }

            double Desired(int id) =>
                parents.TryGetValue(id, out var ps) && ps.Count > 0 ? ps.Average(p => assignedY[p]) : 0.0;

            // Anchor the topmost node at its desired row, then push each next one down only as far
            // as needed to clear the previous card's bottom edge plus the row gap.
            var nextFree = double.NegativeInfinity;
            foreach (var id in nodeIds.OrderBy(Desired).ThenBy(id => inputOrder[id]))
            {
                var y = Math.Max(Desired(id), nextFree);
                assignedY[id] = y;
                nextFree = y + heightOf(id) + CardMetrics.RowGap;
            }
        }

        foreach (var layer in byLayer)
        {
            var x = layer.Key * (CardMetrics.Width + CardMetrics.ColumnGap);
            foreach (var id in layer) setLocation(id, x, assignedY[id]);
        }
    }
```

The roots-centring formula must keep `Apply_SingleNode_PlacedAtOrigin` (Y = 0) passing. If centring several roots fails an existing test in `Services/AutoLayoutServiceTests.cs`, read that test and keep its expectation: it describes behaviour callers rely on.

- [ ] **Step 4: Run all layout tests and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~AutoLayoutService|FullyQualifiedName~CardMetrics"`
Expected: PASS, including every pre-existing test in both `AutoLayoutServiceTests` files.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Core/Layout/AutoLayoutService.cs DialogEditor.Tests/Layout/AutoLayoutServiceTests.cs
git commit -m "feat: auto-layout places canvas cards by their height (#82)"
```

---

### Task 3: Both layout callers pass estimated heights

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs:375` (the `AutoLayoutService.Apply` call in `Load`)
- Modify: `DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs:1274` (the import path)
- Test: `DialogEditor.Tests/ViewModels/ConversationViewModelLayoutTests.cs` (create)

**Interfaces:**
- Consumes: `AutoLayoutService.Apply(nodes, heightOf, setLocation)` (Task 2) and `CardMetrics.EstimateHeight(string?)` (Task 1).

- [ ] **Step 1: Write the failing test**

```csharp
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;

namespace DialogEditor.Tests.ViewModels;

/// Issue 82: Load lays out cards by their estimated height, not one fixed size.
public class ConversationViewModelLayoutTests
{
    [Fact]
    public void Load_StacksSiblingsByEstimatedHeight()
    {
        static ConversationNode N(int id, params int[] to) =>
            new(id, false, SpeakerCategory.Npc, "", "", to.Select(t => new NodeLink(id, t, [])).ToList(), [], [], "Conversation", "None");
        var longText = string.Concat(Enumerable.Repeat("A long line of dialogue. ", 20));
        var conv = new Conversation("c", [N(0, 1, 2), N(1), N(2)],
            new StringTable([new StringEntry(0, "Hi", ""), new StringEntry(1, longText, ""), new StringEntry(2, "Bye", "")]));

        var vm = new ConversationViewModel(new StubDispatcher());
        vm.Load(conv);

        var n1 = vm.Nodes.Single(n => n.NodeId == 1);
        var n2 = vm.Nodes.Single(n => n.NodeId == 2);
        var (upper, lower) = n1.Location.Y < n2.Location.Y ? (n1, n2) : (n2, n1);
        Assert.Equal(upper.Location.Y + CardMetrics.EstimateHeight(upper.DefaultText) + CardMetrics.RowGap,
                     lower.Location.Y, 3);
    }
}
```

(If `Conversation` / `StringTable` constructors differ from this, copy the construction from `TableWindowFixtures.FindInProject`, which builds the same objects.)

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewModelLayoutTests"`
Expected: FAIL. The siblings are spaced by `MaxHeight + RowGap` (the two-argument overload), not by the short card's estimate.

- [ ] **Step 3: Implement**

In `ConversationViewModel.Load`, replace the call:

```csharp
        // Cards are laid out by their estimated height (issue 82), from the text each one shows.
        AutoLayoutService.Apply(conversation.Nodes,
            id => CardMetrics.EstimateHeight(nodeMap.TryGetValue(id, out var n) ? n.DefaultText : null),
            (id, x, y) =>
            {
                if (nodeMap.TryGetValue(id, out var vm))
                    vm.Location = new LayoutPoint(x, y);
            });
```

In `MainWindowViewModel` (import path, around line 1274), build a text lookup from `imported.Texts` (`IReadOnlyList<NodeTranslation>`, with `NodeId` and `DefaultText`):

```csharp
        var importedText = imported.Texts.ToDictionary(t => t.NodeId, t => t.DefaultText);
        AutoLayoutService.Apply(layoutNodes,
            id => CardMetrics.EstimateHeight(importedText.GetValueOrDefault(id)),
            (id, x, y) => layout[id] = new LayoutPoint(x, y));
```

If `imported.Texts` can contain duplicate node ids, use `GroupBy(t => t.NodeId).ToDictionary(g => g.Key, g => g.First().DefaultText)`, and check the importers in `DialogEditor.Core/Import/`.

- [ ] **Step 4: Run the tests and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConversationViewModel|FullyQualifiedName~Import"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/ConversationViewModel.cs DialogEditor.ViewModels/ViewModels/MainWindowViewModel.cs DialogEditor.Tests/ViewModels/ConversationViewModelLayoutTests.cs
git commit -m "feat: canvas layout uses each card's estimated height (#82)"
```

---

### Task 4: `Canvas.*` tokens that font scale leaves alone

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`. Add a block next to the `ColumnWidth.*` tokens (around line 33).
- Create: `DialogEditor.Tests/Theming/CanvasTokenTests.cs`
- Check: `DialogEditor.Avalonia.Shared/Theming/FontScaleApplier.cs` needs no change. It only scales keys listed in `FontSizeTokens.BaseValues` and `ColumnWidthTokens.BaseValues`.

**Interfaces:**
- Consumes: `CardMetrics` (Task 1). The test project references `DialogEditor.Core`.
- Produces these `x:Double` resources, used by Tasks 5 and 6:
  - `Canvas.CardWidth`;
  - font sizes `Canvas.FontSize.Header`, `Canvas.FontSize.Body`, `Canvas.FontSize.Footer`, `Canvas.FontSize.Badge`;
  - line heights `Canvas.LineHeight.Header`, `Canvas.LineHeight.Body`, `Canvas.LineHeight.Footer`.

- [ ] **Step 1: Write the failing tests**

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Core.Layout;

namespace DialogEditor.Tests.Theming;

/// Issue 82: the canvas card's sizes are Canvas.* tokens pinned to CardMetrics. Font scale must
/// NOT change them: on the canvas it magnifies the zoom instead, so scaling the tokens too would
/// scale the canvas twice.
public class CanvasTokenTests
{
    private static readonly Dictionary<string, double> Expected = new()
    {
        ["Canvas.CardWidth"]         = CardMetrics.Width,
        ["Canvas.FontSize.Header"]   = CardMetrics.HeaderFontSize,
        ["Canvas.FontSize.Body"]     = CardMetrics.BodyFontSize,
        ["Canvas.FontSize.Footer"]   = CardMetrics.FooterFontSize,
        ["Canvas.FontSize.Badge"]    = 10,
        ["Canvas.LineHeight.Header"] = CardMetrics.HeaderLineHeight,
        ["Canvas.LineHeight.Body"]   = CardMetrics.BodyLineHeight,
        ["Canvas.LineHeight.Footer"] = CardMetrics.FooterLineHeight,
    };

    private static double Resource(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var r), key);
        return (double)r!;
    }

    [AvaloniaFact]
    public void TokensAxaml_MatchesCardMetrics()
    {
        foreach (var (key, value) in Expected) Assert.Equal(value, Resource(key));
    }

    [AvaloniaFact]
    public void FontScale_LeavesCanvasTokensUnscaled()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            foreach (var (key, value) in Expected) Assert.Equal(value, Resource(key));
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void ThemeSwitch_LeavesCanvasTokensUnscaled()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            new ThemeApplier().Apply("Light");
            foreach (var (key, value) in Expected) Assert.Equal(value, Resource(key));
        }
        finally
        {
            new ThemeApplier().Apply("Dark");
            new FontScaleApplier().Apply(1.0);
        }
    }
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasTokenTests"`
Expected: FAIL on `Canvas.CardWidth` (resource not found).

- [ ] **Step 3: Implement.** Add to `Tokens.axaml`:

```xml
    <!-- Issue 82: conversation-canvas card sizes, in canvas units. Pinned to
         DialogEditor.Core/Layout/CardMetrics.cs by CanvasTokenTests. Deliberately NOT in
         FontSizeTokens / ColumnWidthTokens, so FontScaleApplier never scales them: on the canvas,
         font scale magnifies the zoom instead (ConversationView), so a card shows the same text
         at every scale and saved layouts stay valid. -->
    <x:Double x:Key="Canvas.CardWidth">240</x:Double>
    <x:Double x:Key="Canvas.FontSize.Header">12</x:Double>
    <x:Double x:Key="Canvas.FontSize.Body">12</x:Double>
    <x:Double x:Key="Canvas.FontSize.Footer">10</x:Double>
    <x:Double x:Key="Canvas.FontSize.Badge">10</x:Double>
    <x:Double x:Key="Canvas.LineHeight.Header">16</x:Double>
    <x:Double x:Key="Canvas.LineHeight.Body">16</x:Double>
    <x:Double x:Key="Canvas.LineHeight.Footer">13</x:Double>
```

- [ ] **Step 4: Run them and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasTokenTests|FullyQualifiedName~FontSizeToken|FullyQualifiedName~ColumnWidthToken"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Resources/Tokens.axaml DialogEditor.Tests/Theming/CanvasTokenTests.cs
git commit -m "feat: Canvas.* card tokens that font scale leaves alone (#82)"
```

---

### Task 5: The card template uses the tokens and grows to four lines

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml:260-400`. That covers the node `Grid Width="200"`, the header `TextBlock`, the body `TextBlock`s with `MaxHeight="70"`, the footer `TextBlock`, and the badge glyphs using `FontSize.Label`.
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml` and `Palette.*.axaml` (add `Brush.Node.Border`).
- Create: `DialogEditor.Tests/Views/CanvasCardRenderTests.cs`

**Interfaces:**
- Consumes: the Task 4 tokens and `CardMetrics`.
- Produces:
  - the card root `Grid` carries `Classes="card"`;
  - the body text `TextBlock` carries `Classes="cardBody"`;
  - Task 6 and Task 9 tests find them by these classes.

- [ ] **Step 1: Write the failing tests**

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Layout;
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Views;

/// Issue 82: the rendered card matches CardMetrics, the numbers auto-layout uses. If the template
/// and the estimate drift apart, cards overlap (estimate too short) or waste space (too tall).
public class CanvasCardRenderTests
{
    public CanvasCardRenderTests() => Loc.Configure(new AvaloniaStringProvider());

    public static IEnumerable<object[]> Lines() => new[]
    {
        new object[] { "Tell me about the storm." },
        new object[] { "You're the one asking about the lighthouse keeper? Sit down. Nobody has seen him since the storm put the lamp out." },
        new object[] { string.Concat(Enumerable.Repeat("A long line of dialogue that keeps going. ", 12)) },
        new object[] { "SetGlobalValue(lighthouse_rumour_seen_by_the_harbour_master_on_the_second_night,1)" },
        new object[] { "First paragraph.\nSecond paragraph after a line break." },
    };

    private static (Window, Control card, TextBlock body) Render(string text)
    {
        var node = new NodeViewModel(
            new ConversationNode(1, false, SpeakerCategory.Npc, "", "", [], [], [], "Conversation", "None"),
            new StringEntry(1, text, ""));
        node.Input.Owner = node; node.Output.Owner = node;
        var vm = new ConversationViewModel(new StubDispatcher());
        vm.Nodes.Add(node);
        var window = new Window { Width = 1200, Height = 800, Content = new ConversationView { DataContext = vm } };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        var card = window.GetVisualDescendants().OfType<Grid>().Single(g => g.Classes.Contains("card"));
        var body = card.GetVisualDescendants().OfType<TextBlock>().Single(t => t.Classes.Contains("cardBody"));
        return (window, card, body);
    }

    [AvaloniaTheory]
    [MemberData(nameof(Lines))]
    public void CardRender_HeightMatchesEstimate(string text)
    {
        var (w, card, _) = Render(text);
        try
        {
            var estimate = CardMetrics.EstimateHeight(text);
            Assert.True(card.Bounds.Height <= estimate + 4,
                $"rendered {card.Bounds.Height:0.#} > estimate {estimate:0.#}: layout would overlap");
            Assert.True(card.Bounds.Height >= estimate - CardMetrics.BodyLineHeight,
                $"rendered {card.Bounds.Height:0.#} is more than a line under estimate {estimate:0.#}");
        }
        finally { w.Close(); }
    }

    [AvaloniaTheory]
    [MemberData(nameof(Lines))]
    public void CardRender_WidthIsCardMetricsWidth(string text)
    {
        var (w, card, _) = Render(text);
        try { Assert.Equal(CardMetrics.Width, card.Bounds.Width, 1); }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_NoSpaceLine_StaysInsideCard()
    {
        var (w, card, body) = Render(new string('x', 300));
        try
        {
            Assert.Equal(CardMetrics.Width, card.Bounds.Width, 1);
            Assert.True(body.Bounds.Right <= card.Bounds.Width + 0.5);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_LongText_TrimsAfterFourLines()
    {
        var (w, _, body) = Render(string.Concat(Enumerable.Repeat("A long line of dialogue. ", 40)));
        try
        {
            Assert.Equal(CardMetrics.MaxBodyLines, body.MaxLines);
            Assert.Equal(TextTrimming.CharacterEllipsis, body.TextTrimming);
            Assert.True(body.TextLayout.TextLines.Count <= CardMetrics.MaxBodyLines);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_EmptyText_MatchesOneLineEstimate()
    {
        var (w, card, _) = Render("");
        try { Assert.True(card.Bounds.Height <= CardMetrics.EstimateHeight("") + 4); }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void CardRender_FourLinesAtMost113()
    {
        var (w, card, _) = Render(string.Concat(Enumerable.Repeat("A long line of dialogue. ", 40)));
        try { Assert.True(card.Bounds.Height < 130, $"{card.Bounds.Height} >= the old 130 px pitch"); }
        finally { w.Close(); }
    }
}
```

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasCardRenderTests"`
Expected: FAIL. `Single(g => g.Classes.Contains("card"))` finds no element.

- [ ] **Step 3: Implement**, in `ConversationView.axaml`'s node `DataTemplate`:

  1. **Card root.** Change the card `Grid` to
     `<Grid Classes="card" Width="{DynamicResource Canvas.CardWidth}" RowDefinitions="Auto,Auto,Auto,Auto" ...>`.
     Put a 1 px outline on it with a `Border` spanning `Grid.RowSpan="3"`, `BorderThickness="1"`, `BorderBrush="{DynamicResource Brush.Node.Border}"` and `IsHitTestVisible="False"`, placed before the overlay borders. Then give the header, body and footer borders `Margin="1,0"` (header also `1,1,1,0`, footer `1,0,1,1`), so the 1 px outline is included in `CardMetrics.BorderThickness`.
  2. **Header.** Set the `TextBlock` to `FontSize="{DynamicResource Canvas.FontSize.Header}"` and `LineHeight="{DynamicResource Canvas.LineHeight.Header}"`, keep `TextTrimming="CharacterEllipsis"`, and set `Padding="0,3,6,3"`. The shape marker `Path` becomes `Width="9" Height="9"` (unchanged).
  3. **Body.** Replace both body `TextBlock`s' `MaxHeight="70"` and `FontSize="{DynamicResource FontSize.Small}"` with:

     ```xml
     <!-- Issue 82: the card grows to fit its line, up to CardMetrics.MaxBodyLines, then ends in "…".
          Full text, not TextPreview: TextPreview's 80-character cut stays for one-line summaries
          elsewhere (Node Details' link list, Batch VO Import, VO Validation, the alias picker). -->
     <TextBlock Classes="cardBody" Text="{Binding DefaultText}"
                TextWrapping="Wrap" MaxLines="4" TextTrimming="CharacterEllipsis"
                Padding="6,4" FontSize="{DynamicResource Canvas.FontSize.Body}"
                LineHeight="{DynamicResource Canvas.LineHeight.Body}"
                Foreground="{DynamicResource Brush.Text.OnLight}"/>
     ```

     The placeholder `TextBlock` gets the same `MaxLines`, `TextTrimming`, `FontSize` and `LineHeight`, and keeps its `IsVisible` and `FontStyle`. Since the main body binds to `DefaultText`, hide the main one when the placeholder shows: `IsVisible="{Binding !HasTextPlaceholder}"`.
  4. **Footer.** Set `FontSize="{DynamicResource Canvas.FontSize.Footer}"`, `LineHeight="{DynamicResource Canvas.LineHeight.Footer}"` and `TextTrimming="CharacterEllipsis"`.
  5. **Badge glyphs.** Both 18 × 18 badges (diff and connect-mode) move from `FontSize.Label` to `{DynamicResource Canvas.FontSize.Badge}`.
  6. **`Brush.Node.Border`.** Add a `Palette.Node.Border` primitive to each of `Palette.Dark/Light/HighContrast/Colourblind.axaml`: Dark `#4D000000` (black at 30%), Light `#33000000` (black at 20%), High Contrast `#FFFFFFFF`, and Colourblind the same as Dark. Then add `<SolidColorBrush x:Key="Brush.Node.Border" Color="{StaticResource Palette.Node.Border}"/>` in `Tokens.axaml` next to the `Brush.Node.*` brushes (around line 85).

- [ ] **Step 4: Run them and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasCardRenderTests|FullyQualifiedName~ConversationView|FullyQualifiedName~PaletteContrast|FullyQualifiedName~NoClippedText"`
Expected: PASS.

If `CardRender_HeightMatchesEstimate` fails with **rendered > estimate**, the estimate under-counts lines. Raise `CardMetrics.AverageCharWidth` in 0.2 steps until it passes, then re-run Task 1's and Task 2's tests. Never loosen the `+ 4` tolerance. If the card's measured height is off by a constant (borders, padding), fix the template margins to match `CardMetrics`, not the other way round.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Avalonia.Shared/Resources/ DialogEditor.Tests/Views/CanvasCardRenderTests.cs DialogEditor.Core/Layout/CardMetrics.cs
git commit -m "feat: canvas cards use 12 px text and grow to four lines (#82)"
```

---

### Task 6: Ports become small dots on the card's edge

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml:244-258`. These are `Node.InputConnectorTemplate` and `Node.OutputConnectorTemplate`, plus a `nodify:Node` control theme or styles in `UserControl.Styles` (line ~13).
- Test: `DialogEditor.Tests/Views/CanvasCardRenderTests.cs` (add)

**Interfaces:**
- Consumes: `Classes="card"` (Task 5).
- Produces: the node's total width is at most `CardMetrics.Width + 16`. The ports are `nodify:NodeInput`/`NodeOutput`, each with a hit area of at least 16 × 16.

- [ ] **Step 1: Write the failing tests** (add to `CanvasCardRenderTests`)

```csharp
    [AvaloniaFact]
    public void Ports_DoNotAddSideColumns()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var node = card.GetVisualAncestors().OfType<Nodify.Node>().First();
            Assert.True(node.Bounds.Width <= CardMetrics.Width + 16,
                $"node is {node.Bounds.Width:0} wide for a {CardMetrics.Width} card: connector columns are back");
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void Ports_HaveAtLeast16pxHitArea()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var node = card.GetVisualAncestors().OfType<Nodify.Node>().First();
            var ports = node.GetVisualDescendants().OfType<Nodify.Connector>().ToList();
            Assert.Equal(2, ports.Count);
            Assert.All(ports, p => Assert.True(p.Bounds.Width >= 16 && p.Bounds.Height >= 16,
                $"port is {p.Bounds.Width:0}×{p.Bounds.Height:0}"));
        }
        finally { w.Close(); }
    }
```

- [ ] **Step 2: Run them and confirm `Ports_DoNotAddSideColumns` fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasCardRenderTests.Ports"`
Expected: FAIL. The node is about 300 px wide because of the default connector columns.

- [ ] **Step 3: Implement.** In `UserControl.Styles` add:

```xml
        <!-- Issue 82: no connector side columns. Nodify's default Node draws Input/Output in
             padded columns with their own background, which took about a third of the node's
             width. The card's own colours (ContentBrush/HeaderBrush) are unused: the DataTemplate
             draws the card. -->
        <Style Selector="nodify|Node">
            <Setter Property="Padding" Value="0"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="ContentBrush" Value="Transparent"/>
            <Setter Property="HeaderBrush" Value="Transparent"/>
            <Setter Property="FooterBrush" Value="Transparent"/>
        </Style>
        <Style Selector="nodify|NodeInput, nodify|NodeOutput">
            <Setter Property="Padding" Value="0"/>
            <Setter Property="Margin" Value="-8,6,-8,0"/>
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="VerticalAlignment" Value="Top"/>
            <Setter Property="ConnectorTemplate">
                <ControlTemplate>
                    <!-- 16×16 hit area around an 8 px dot, so ports are no harder to grab than before. -->
                    <Border Width="16" Height="16" Background="Transparent">
                        <Ellipse Width="8" Height="8"
                                 Fill="{DynamicResource Brush.Connection.Default}"
                                 Stroke="{DynamicResource Brush.Canvas.Background}" StrokeThickness="1.5"/>
                    </Border>
                </ControlTemplate>
            </Setter>
        </Style>
```

  - **If the test still finds wide side columns:** set `Node.InputGroupStyle`/`OutputGroupStyle` (both `Style`s, documented in `Nodify.xml` under `P:Nodify.Node.InputGroupStyle`) with `Width=8`.
  - **If a setter fails to build:** Nodify may expose `ConnectorTemplate` as a `DataTemplate` rather than a `ControlTemplate`. Check `Nodify.xml` for `P:Nodify.NodeInput.ConnectorTemplate` and use the type it names.
  - **Connections:** after the change, confirm they still attach. The `Anchor` binding in the connector templates must still update. Run the existing keyboard and connect-mode tests.

- [ ] **Step 4: Run them and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasCardRenderTests|FullyQualifiedName~ConversationView|FullyQualifiedName~Connect"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Tests/Views/CanvasCardRenderTests.cs
git commit -m "feat: canvas ports are small dots on the card edge (#82)"
```

---

### Task 7: Font scale magnifies the canvas

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs`, in the constructor and a new region next to `FitToScreen_Click` (line ~226).
- Test: `DialogEditor.Tests/Views/CanvasFontScaleTests.cs` (create)

**Interfaces:**
- Consumes: `FontScaleService.Current` (`DialogEditor.Avalonia.Shared.Theming`), an `ObservableObject` with a `double Scale` property. `Editor` is the `NodifyEditor`, with `ViewportZoom`, `MinViewportZoom`, `MaxViewportZoom`, `ViewportLocation` and `ViewportSize`.
- Produces: `internal double FontScaleFactor` on `ConversationView`, the scale currently applied.

- [ ] **Step 1: Write the failing tests**

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using Nodify;

namespace DialogEditor.Tests.Views;

/// Issue 82: on the canvas, font scale magnifies the zoom, so a card shows the same text at every scale.
public class CanvasFontScaleTests
{
    private static (Window, NodifyEditor) Open()
    {
        var view = new ConversationView { DataContext = new ConversationViewModel(new StubDispatcher()) };
        var window = new Window { Width = 1200, Height = 800, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        return (window, view.FindControl<NodifyEditor>("Editor")!);
    }

    [AvaloniaFact]
    public void Opens_AtZoomEqualToFontScale()
    {
        try
        {
            new FontScaleApplier().Apply(2.0);
            var (w, editor) = Open();
            try { Assert.Equal(2.0, editor.ViewportZoom, 3); }
            finally { w.Close(); }
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void ZoomLimits_AreMultipliedByFontScale()
    {
        var (w1, e1) = Open();
        var (min, max) = (e1.MinViewportZoom, e1.MaxViewportZoom);
        w1.Close();
        try
        {
            new FontScaleApplier().Apply(2.0);
            var (w2, e2) = Open();
            try
            {
                Assert.Equal(min * 2, e2.MinViewportZoom, 3);
                Assert.Equal(max * 2, e2.MaxViewportZoom, 3);
            }
            finally { w2.Close(); }
        }
        finally { new FontScaleApplier().Apply(1.0); }
    }

    [AvaloniaFact]
    public void LiveFontScaleChange_MultipliesTheCurrentZoom()
    {
        var (w, editor) = Open();
        try
        {
            editor.ViewportZoom = 0.8;
            new FontScaleApplier().Apply(2.0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(1.6, editor.ViewportZoom, 3);
        }
        finally { new FontScaleApplier().Apply(1.0); w.Close(); }
    }

    [AvaloniaFact]
    public void FontScaleRoundTrip_RestoresZoom()
    {
        var (w, editor) = Open();
        try
        {
            editor.ViewportZoom = 0.8;
            new FontScaleApplier().Apply(2.0);
            new FontScaleApplier().Apply(1.0);
            Dispatcher.UIThread.RunJobs();
            Assert.Equal(0.8, editor.ViewportZoom, 3);
        }
        finally { new FontScaleApplier().Apply(1.0); w.Close(); }
    }
}
```

Check first that `FontScaleApplier.Apply` sets `FontScaleService.Current.Scale`. Its doc comment says it does, at lines 14–15. If it doesn't, the tests should set `FontScaleService.Current.Scale` directly as well.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasFontScaleTests"`
Expected: `Opens_AtZoomEqualToFontScale` fails (zoom is 1.0), and so do the other three.

- [ ] **Step 3: Implement**, in `ConversationView.axaml.cs`:

```csharp
    // ── Font scale magnifies the canvas (issue 82) ───────────────────────────────
    // The canvas's text, cards and gaps are fixed canvas units (Canvas.* tokens / CardMetrics)
    // that FontScaleApplier never touches. Font scale multiplies the zoom instead, so a card
    // shows exactly the same text at 2.0× as at 1.0×, and saved layouts stay valid at every
    // scale. The zoom range is scaled too, so the user's zoom range doesn't change.
    private double _baseMinZoom, _baseMaxZoom;
    internal double FontScaleFactor { get; private set; } = 1.0;

    private void InitFontScaleZoom()
    {
        _baseMinZoom = Editor.MinViewportZoom;
        _baseMaxZoom = Editor.MaxViewportZoom;
        ApplyFontScale(FontScaleService.Current.Scale, keepUserZoom: false);
        FontScaleService.Current.PropertyChanged += OnFontScaleChanged;
        DetachedFromVisualTree += (_, _) => FontScaleService.Current.PropertyChanged -= OnFontScaleChanged;
    }

    private void OnFontScaleChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(FontScaleService.Scale))
            ApplyFontScale(FontScaleService.Current.Scale, keepUserZoom: true);
    }

    private void ApplyFontScale(double scale, bool keepUserZoom)
    {
        if (scale <= 0) return;
        var userZoom = keepUserZoom ? Editor.ViewportZoom / FontScaleFactor : 1.0;
        // Keep the viewport centre in place while the zoom changes.
        var centre = Editor.ViewportLocation + new global::Avalonia.Vector(
            Editor.ViewportSize.Width / 2, Editor.ViewportSize.Height / 2);
        FontScaleFactor = scale;
        // Widen the range before zooming, so the new zoom is never clamped by the old limits.
        Editor.MinViewportZoom = Math.Min(_baseMinZoom * scale, Editor.MinViewportZoom);
        Editor.MaxViewportZoom = Math.Max(_baseMaxZoom * scale, Editor.MaxViewportZoom);
        Editor.ViewportZoom    = userZoom * scale;
        Editor.MinViewportZoom = _baseMinZoom * scale;
        Editor.MaxViewportZoom = _baseMaxZoom * scale;
        Editor.ViewportLocation = centre - new global::Avalonia.Vector(
            Editor.ViewportSize.Width / 2, Editor.ViewportSize.Height / 2);
    }
```

Call `InitFontScaleZoom();` in the constructor, after `InitializeComponent()`. Add `using DialogEditor.Avalonia.Shared.Theming;` if it's missing. `ViewportSize` is in canvas units, so it is computed after the zoom has changed. If Nodify recomputes it only on layout, call `Editor.UpdateLayout()` before reading it the second time.

- [ ] **Step 4: Run them and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasFontScaleTests|FullyQualifiedName~ConversationView|FullyQualifiedName~Annotation"`
Expected: PASS. The annotation tests confirm `SyncScreen` still follows the zoom.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml.cs DialogEditor.Tests/Views/CanvasFontScaleTests.cs
git commit -m "feat: font scale magnifies the conversation canvas (#82)"
```

---

### Task 8: A canvas background per theme

**Files:**
- Modify:
  - `DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml`
  - `DialogEditor.Avalonia.Shared/Resources/Palette.Light.axaml`
  - `DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml`
  - `DialogEditor.Avalonia.Shared/Resources/Palette.Colourblind.axaml`
  - `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml:266`
- Test: `DialogEditor.Tests/Theming/PaletteContrastTests.cs` (add)

**Interfaces:**
- Produces: a `Palette.Canvas.Base` primitive in every palette. `Brush.Canvas.Background` now points at it instead of `Palette.Neutral.520`.

- [ ] **Step 1: Write the failing tests** (add to `PaletteContrastTests`, which uses `PaletteHarness.Load("Palette.Dark")`, `PaletteHarness.Color(dict, key)` and `Wcag.ContrastRatio`):

```csharp
    // Issue 82: the canvas backdrop follows the theme. Resting connections must stay at least
    // 3:1 against it (non-text UI), and the backdrop must differ from the panels around it.
    [AvaloniaTheory]
    [InlineData("Palette.Dark", "#FF2B2B2B")]
    [InlineData("Palette.Light", "#FFE4E4E0")]
    [InlineData("Palette.HighContrast", "#FF3A3A3A")]
    [InlineData("Palette.Colourblind", "#FF2B2B2B")]
    public void CanvasBackdrop_HasTheSpecValue(string palette, string expected)
    {
        var dict = PaletteHarness.Load(palette);
        Assert.Equal(Avalonia.Media.Color.Parse(expected), PaletteHarness.Color(dict, "Palette.Canvas.Base"));
    }

    [AvaloniaTheory]
    [InlineData("Palette.Dark")]
    [InlineData("Palette.Light")]
    [InlineData("Palette.HighContrast")]
    [InlineData("Palette.Colourblind")]
    public void RestingConnection_IsVisibleOnTheCanvas(string palette)
    {
        var dict = PaletteHarness.Load(palette);
        var ratio = Wcag.ContrastRatio(PaletteHarness.Color(dict, "Palette.Connection.Base"),
                                       PaletteHarness.Color(dict, "Palette.Canvas.Base"));
        Assert.True(ratio >= 3.0, $"{palette}: resting connection on canvas {ratio:F2}:1 < 3:1");
    }
```

The existing HC pair `("Palette.Line.Default", "Palette.Neutral.520")` in `HcBorderPairs` must become `("Palette.Line.Default", "Palette.Canvas.Base")`, since the GridSplitter now borders the new backdrop.

- [ ] **Step 2: Run them and confirm they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PaletteContrastTests"`
Expected: FAIL. `Palette.Canvas.Base` isn't found.

- [ ] **Step 3: Implement.** Add to each palette, next to `Palette.Connection.Base`:
  - Dark: `<Color x:Key="Palette.Canvas.Base">#FF2B2B2B</Color>  <!-- canvas backdrop (issue 82): a step lighter than panels (#252525), so it reads as its own surface -->`
  - Light: `#FFE4E4E0`, with the comment "a step darker than panels (#E9E9E7)".
  - High Contrast: `#FF3A3A3A`, with the comment "unchanged from Neutral.520".
  - Colourblind: `#FF2B2B2B`, with the comment "matches its base (dark) theme".

  In `Tokens.axaml:266` change `Brush.Canvas.Background` to `Color="{StaticResource Palette.Canvas.Base}"`. Leave `Palette.Neutral.520` in place if anything else uses it. Check with a grep; at the time of writing only the canvas brush did, so remove it if nothing else references it.

  **Never links** (`Brush.Connection.Never` = `Neutral.335`, `#555555` in Dark) are dim on `#2B2B2B` by design. Check by eye in Task 11's screenshots; there is no test, since they are meant to be faint.

- [ ] **Step 4: Run them and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Palette|FullyQualifiedName~Theme"`
Expected: PASS. Light's resting connection (`#4F4F4F` on `#E4E4E0`) is about 6.4:1. If any ratio does fail, adjust the canvas colour, not the connection colour, and update the spec table and the test value together.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Resources/ DialogEditor.Tests/Theming/PaletteContrastTests.cs
git commit -m "feat: the canvas backdrop follows the theme (#82)"
```

---

### Task 9: Connections stay behind cards

**Files:**
- Investigate: `DialogEditor.Avalonia/Views/ConversationView.axaml` (`NodifyEditor` attributes, `SearchMatchState` opacity on the card `Grid`).
- Test: `DialogEditor.Tests/Views/CanvasCardRenderTests.cs` (add)

**Interfaces:**
- Consumes: `Classes="card"` (Task 5).

The review saw connection lines crossing card text. `NodifyEditor.DisplayConnectionsOnTop` defaults to false, and nothing in the repo sets it. The likely cause is the card `Grid`'s `Opacity="{Binding SearchMatchState, Converter={StaticResource SearchStateOpacity}}"`: a dimmed, non-matching card is translucent, so lines show through. Confirm the cause before fixing it. Use superpowers:systematic-debugging if the first hypothesis is wrong.

- [ ] **Step 1: Write the failing test**

```csharp
    [AvaloniaFact]
    public void Connections_AreNotDrawnOverCards()
    {
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var editor = card.GetVisualAncestors().OfType<Nodify.NodifyEditor>().First();
            Assert.False(editor.DisplayConnectionsOnTop);
            // Every part of the card that carries a background must be opaque, or lines show through.
            foreach (var b in card.GetVisualDescendants().OfType<Border>().Where(b => b.Background is ISolidColorBrush))
                Assert.Equal(255, ((ISolidColorBrush)b.Background!).Color.A);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void DimmedCard_StaysOpaque()
    {
        // Search dims non-matching cards. Dimming must not make them see-through, or every line
        // behind them shows through their text.
        var (w, card, _) = Render("Tell me about the storm.");
        try
        {
            var vm = (ConversationViewModel)card.GetVisualAncestors().OfType<ConversationView>().First().DataContext!;
            vm.SearchQuery = "no node contains this";
            Dispatcher.UIThread.RunJobs();
            var opacity = card.GetSelfAndVisualAncestors().TakeWhile(v => v is not Nodify.ItemContainer)
                              .Aggregate(1.0, (o, v) => o * v.Opacity);
            Assert.Equal(1.0, opacity, 3);
        }
        finally { w.Close(); }
    }
```

If `SearchQuery` isn't what sets `SearchMatchState`, read `ConversationViewModel` for the property that does (search for `SearchMatchState`) and use it.

- [ ] **Step 2: Run them and confirm `DimmedCard_StaysOpaque` fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasCardRenderTests.Connections|FullyQualifiedName~CanvasCardRenderTests.DimmedCard"`
Expected: `DimmedCard_StaysOpaque` FAILs, with opacity below 1. If it passes instead, the hypothesis is wrong: investigate before changing anything.

- [ ] **Step 3: Implement.** Keep dimmed cards opaque:
  - Remove the `Opacity` binding from the card `Grid`.
  - Dim with an overlay: a `Border` spanning the card's rows, with `Background="{DynamicResource Brush.Canvas.Background}"`, `IsHitTestVisible="False"`, `ZIndex="8"`, and `Opacity` bound to a converter returning 0 for a match/no search and 0.6 for a non-match.
  - Add that converter next to `SearchStateOpacity`, or adapt it. Keep the existing converter's tests green and add one for the new mapping.
  - Comment the reason in the XAML: issue 82, lines showed through translucent cards.

- [ ] **Step 4: Run them and confirm they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasCardRenderTests|FullyQualifiedName~Search"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/ DialogEditor.Tests/
git commit -m "fix: dimmed canvas cards stay opaque, so links don't show through (#82)"
```

---

### Task 10: Fit-to-screen leaves the minimap free

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml.cs:226` (`FitToScreen_Click`)
- Test: `DialogEditor.Tests/Views/CanvasFontScaleTests.cs` (add)

**Interfaces:**
- Consumes: `Editor.FitToScreen(Rect? area)` and `Editor.ItemsExtent` (Nodify). The minimap is the `nodify:Minimap` with `Width="160" Height="110" Margin="8"`, in the canvas `Grid`'s bottom-right.
- Produces: `internal void FitToScreenClearOfMinimap()`.

- [ ] **Step 1: Write the failing test**

```csharp
    [AvaloniaFact]
    public void FitToScreen_KeepsCardsOutFromUnderTheMinimap()
    {
        var vm = new ConversationViewModel(new StubDispatcher());
        for (var i = 0; i < 12; i++)
        {
            var n = CanvasNavigationServiceTests.MakeNode(i, i * 340, (i % 4) * 140);
            vm.Nodes.Add(n);
        }
        var view = new ConversationView { DataContext = vm };
        var window = new Window { Width = 1000, Height = 700, Content = view };
        window.Show();
        Dispatcher.UIThread.RunJobs();
        try
        {
            view.FitToScreenClearOfMinimap();
            Dispatcher.UIThread.RunJobs();
            var minimap = view.GetVisualDescendants().OfType<Nodify.Minimap>().Single();
            var mmRect = new Rect(minimap.TranslatePoint(default, view)!.Value, minimap.Bounds.Size);
            foreach (var card in view.GetVisualDescendants().OfType<Grid>().Where(g => g.Classes.Contains("card")))
            {
                var r = new Rect(card.TranslatePoint(default, view)!.Value, card.Bounds.Size * view.FindControl<NodifyEditor>("Editor")!.ViewportZoom);
                Assert.False(r.Intersects(mmRect), $"a card at {r} is under the minimap {mmRect}");
            }
        }
        finally { window.Close(); }
    }
```

Add `using Avalonia;`, `using Avalonia.VisualTree;` and `using DialogEditor.Tests.ViewModels;` (for `CanvasNavigationServiceTests.MakeNode`).

- [ ] **Step 2: Run it and confirm it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FitToScreen_KeepsCards"`
Expected: build error. `FitToScreenClearOfMinimap` doesn't exist.

- [ ] **Step 3: Implement**

```csharp
    // Issue 82: plain FitToScreen fits content to the whole viewport, so the bottom-right minimap
    // covered cards. Widening the fitted area on the right by the minimap's share of the viewport
    // width leaves a free strip there for it.
    internal void FitToScreenClearOfMinimap()
    {
        var extent = Editor.ItemsExtent;
        if (extent.Width <= 0 || Editor.Bounds.Width <= 0) { Editor.FitToScreen(); return; }
        var reserved = Minimap.Bounds.Width + Minimap.Margin.Left + Minimap.Margin.Right;
        var usable = Math.Max(1, Editor.Bounds.Width - reserved);
        var widened = extent.Width * Editor.Bounds.Width / usable;
        Editor.FitToScreen(new global::Avalonia.Rect(extent.X, extent.Y, widened, extent.Height));
    }

    private void FitToScreen_Click(object? sender, RoutedEventArgs e) => FitToScreenClearOfMinimap();
```

Give the minimap `x:Name="Minimap"` in `ConversationView.axaml` (around line 431), if it has no name. If `FitToScreenExtentMargin` adds margin all round, the reserved strip still holds, because the area is only widened on the right.

- [ ] **Step 4: Run it and confirm it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~CanvasFontScaleTests|FullyQualifiedName~ConversationView"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConversationView.axaml DialogEditor.Avalonia/Views/ConversationView.axaml.cs DialogEditor.Tests/Views/CanvasFontScaleTests.cs
git commit -m "feat: fit-to-screen keeps cards out from under the minimap (#82)"
```

---

### Task 11: Dot grid, full verification, screenshots and PR

**Files:**
- Modify (optional dot grid): `ConversationView.axaml` (`NodifyEditor.Background`), `Tokens.axaml`, and the palettes (`Palette.Canvas.Grid`).
- No new production tests for the grid. It is decoration, and the spec allows dropping it.

- [ ] **Step 1: Dot grid (optional; skip if it costs frame time).**
  1. Add `Palette.Canvas.Grid`: Dark `#FF3A3A3A`, Light `#FFCFCFCA`, High Contrast `#FF4A4A4A`, Colourblind the same as Dark.
  2. Add a `Brush.Canvas.Grid` brush.
  3. Set the editor's background to a tiled `DrawingBrush`: a 20 × 20 tile with a 1 px dot, `TileMode="Tile"`, `DestinationRect="0,0,20,20"`, and `Transform="{Binding DpiScaledViewportTransform, ElementName=Editor}"` so it pans and zooms with the canvas.
  4. Keep `Brush.Canvas.Background` as the base colour by layering: a `Border` with `Background="{DynamicResource Brush.Canvas.Background}"` behind the editor, and the editor's `Background` set to the grid brush.
  5. Open a large conversation with the `running-the-app` skill and pan around. If panning visibly stutters compared with `main`, revert the grid and say so in the PR.

- [ ] **Step 2: Full headless suite**

Run: `dotnet test --filter "Category!=Gui"`
Expected: all pass. Report the exact summary line. Pay particular attention to `NoClippedTextTests` and `UiaAddressability*`.

- [ ] **Step 3: GUI tests.** These launch the real app and drive it, so tell the user first and ask them not to use the mouse or keyboard during the run.

Run: `dotnet test --filter "Category=Gui"`
Expected: all pass.

- [ ] **Step 4: Before/after screenshots** with the `running-the-app` skill (`tools/ui-automation/DriveApp.ps1`, and the theme × font-scale capture from #86):
  - **Matrix:** Dark and Light × font scale 1.0 and 2.0 × as opened and fitted.
  - **Conversations:** a short linear one, a long-line one, and a branching companion hub.
  - **Before:** from `main`.
  - **After:** from this branch.

  Check by eye:
  - no card overlaps another;
  - lines end in "…" only after four lines;
  - ports sit on the card edges;
  - the minimap is clear after fit;
  - `Never` links are visible but faint.

  Attach the screenshots to the PR, as #82's review did, rather than committing them to the repository.

- [ ] **Step 5: Open the PR.** `Closes #82`. The body lists what changed, links the spec and the mockup, carries the screenshot matrix, and names #221 and #222 as follow-ups. After `gh pr create`, enable Auto-fix (standing instruction) and post the decisions summary on #82 with a link to the spec, as #82's done-when asks.
