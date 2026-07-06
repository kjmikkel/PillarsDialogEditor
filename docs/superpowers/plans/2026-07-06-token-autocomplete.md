# Token & Markup Autocomplete Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add IDE-style autocomplete to the node editor's Default/Female dialog-text fields so typing `[` or `<` offers the game's known substitution tokens / rich-text markup and inserts a ready-to-fill template.

**Architecture:** A pure `TokenCompletionService` (in `DialogEditor.ViewModels`) owns all logic — detecting the completion context under the caret, ranking candidates from the engine-verified `TagCatalogue`, and computing the exact edit (text + caret/selection) on accept. A thin Avalonia attached behaviour renders a `Popup` and applies the service's computed edit; it makes no decisions of its own. Insertion templates are authored as data (a new optional `insert` field in `tags.json`).

**Tech Stack:** C# / .NET, Avalonia UI, xUnit (+ `Avalonia.Headless.XUnit` for View tests), CommunityToolkit.Mvvm. Spec: `docs/superpowers/specs/2026-07-06-token-autocomplete-design.md`.

## Global Constraints

- **TDD red/green** — a failing test precedes every implementation change (CLAUDE.md).
- **No hardcoded user-visible strings** — any static UI copy uses a resource key; token `Name`/`Description` shown in the popup are `tags.json` data, not UI strings (CLAUDE.md Localisation).
- **Tooltips on new interactive controls** (CLAUDE.md UI/UX). The popup is a transient completion aid on already-tooltipped `TextBox`es; no new labelled/toolbar controls are introduced.
- **UI Automation** — do not suppress automation peers; the popup list must stay keyboard-operable and name-bearing (CLAUDE.md UI Automation).
- **Error handling** — every caught exception logged via `AppLog.Error(...)`/`AppLog.Warn(...)`; no bare `catch { }`; swallow only `OperationCanceledException` (CLAUDE.md).
- **`tags.json` exists in two copies that must stay identical:** the embedded `DialogEditor.ViewModels/Resources/tags.json` (used at runtime) and the mirror `data/tags.json`. Every data edit touches **both**.
- **Tests run serially** (AppSettings/Loc global-state race) — do not introduce new mutable statics; tests use `TagCatalogue.Instance` (real embedded data), never `TagCatalogue.Configure`.
- **Only `Token` (on `[`) and `Markup` (on `<`) are ever offered; `Convention` never is.**

---

### Task 1: `insert` templates in the tag data

Add the optional `insert` field to `TagEntry` and author it for every parameterised or paired entry in both `tags.json` copies, pinned by a data-integrity test.

**Files:**
- Modify: `DialogEditor.ViewModels/Services/TagCatalogue.cs` (the `TagEntry` record, ~line 12-20)
- Modify: `DialogEditor.ViewModels/Resources/tags.json`
- Modify: `data/tags.json`
- Test: `DialogEditor.Tests/Services/TagInsertTemplateTests.cs` (create)

**Interfaces:**
- Produces: `TagEntry.Insert` (`string?`, default `null`); the marker convention `${}` (empty caret) / `${text}` (pre-selected `text`), exactly one marker per `insert`.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Services/TagInsertTemplateTests.cs
using System.Text.RegularExpressions;
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

/// The insert templates drive autocomplete's "caret at first placeholder"
/// behaviour. A parameterised token ([Specified n]) or paired/attribute markup
/// (<i>…</i>, <color="…">…</color>) MUST carry an `insert`; plain tokens
/// ([Player Name]) may omit it (they insert Name verbatim).
/// Spec: docs/superpowers/specs/2026-07-06-token-autocomplete-design.md
public class TagInsertTemplateTests
{
    private static readonly Regex Marker = new(@"\$\{[^}]*\}", RegexOptions.Compiled);

    private static bool NeedsInsert(TagEntry e) =>
        (e.Kind == "Token" || e.Kind == "Markup") &&
        (e.Name.Contains('…') || e.Name.EndsWith(" n]"));

    [Fact]
    public void ParameterisedAndPairedEntries_HaveInsert()
    {
        foreach (var e in TagCatalogue.Instance.All)
            if (NeedsInsert(e))
                Assert.False(string.IsNullOrEmpty(e.Insert),
                    $"'{e.Name}' needs an insert template.");
    }

    [Fact]
    public void EveryInsert_HasExactlyOneMarker()
    {
        foreach (var e in TagCatalogue.Instance.All)
            if (!string.IsNullOrEmpty(e.Insert))
                Assert.Equal(1, Marker.Matches(e.Insert!).Count);
    }

    [Fact]
    public void InsertPrefix_AgreesWithName()
    {
        // The part of `insert` before its marker must be a prefix of `Name`,
        // guarding typos between the display and insert forms.
        foreach (var e in TagCatalogue.Instance.All)
        {
            if (string.IsNullOrEmpty(e.Insert)) continue;
            var open = e.Insert!.IndexOf("${", System.StringComparison.Ordinal);
            var prefix = e.Insert[..open];
            Assert.StartsWith(prefix, e.Name, System.StringComparison.Ordinal);
        }
    }

    [Fact]
    public void KnownSample_Specified_HasExpectedInsert()
    {
        var specified = TagCatalogue.Instance.All.Single(e => e.Name == "[Specified n]");
        Assert.Equal("[Specified ${0}]", specified.Insert);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TagInsertTemplateTests"`
Expected: FAIL — `TagEntry` has no `Insert` member (compile error), or once added, `Insert` is null for `[Specified n]`.

- [ ] **Step 3: Add the `Insert` field to `TagEntry`**

In `DialogEditor.ViewModels/Services/TagCatalogue.cs`, extend the record (append after `Notes` so existing positional/JSON use is unaffected):

```csharp
public record TagEntry(
    string Name,
    string Kind,
    IReadOnlyList<string> Games,
    string Category,
    string Description,
    string? Example = null,
    int Count = 0,
    string? Notes = null,
    string? Insert = null);
```

- [ ] **Step 4: Author `insert` in both `tags.json` copies**

Add an `"insert"` value to each of these entries in **both** `DialogEditor.ViewModels/Resources/tags.json` **and** `data/tags.json` (leave all other entries untouched — plain tokens insert their `Name`):

| Entry `name`                            | Add `"insert": …`                              |
|-----------------------------------------|------------------------------------------------|
| `[Specified n]`                         | `"[Specified ${0}]"`                          |
| `[SkillCheck n]`                        | `"[SkillCheck ${0}]"`                         |
| `[Slot n]`                              | `"[Slot ${0}]"`                               |
| `<i>…</i>`                              | `"<i>${}</i>"`                                |
| `<ispeech>…</ispeech>`                  | `"<ispeech>${}</ispeech>"`                    |
| `<xg>…</xg>`                            | `"<xg>${}</xg>"`                              |
| `<color="…">…</color>`                 | `"<color=\"${}\"></color>"`                   |
| `<link="glossary://…">…</link>`         | `"<link=\"glossary://${}\"></link>"`          |
| `<link="stringtooltip://…">…</link>`    | `"<link=\"stringtooltip://${}\"></link>"`     |
| `<link="neutralvalue://…">…</link>`     | `"<link=\"neutralvalue://${}\"></link>"`      |
| `<sprite="Inline" name="…" tint=1>`     | `"<sprite=\"Inline\" name=\"${}\" tint=1>"`   |

Example (the `[Specified n]` entry becomes):

```json
  { "name": "[Specified n]", "kind": "Token", "games": ["poe1", "poe2"], "category": "CharacterReference",
    "description": "The character bound to the conversation's Specified Speaker slot n (script-selected, e.g. a specific companion).",
    "example": "[Specified 0] takes a firm grip on the rope and descends.", "count": 2091,
    "insert": "[Specified ${0}]",
    "notes": "Shipped text uses n = 0–5 (Deadfire) and 0–1 (PoE1); any bound slot works." },
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TagInsertTemplateTests"`
Expected: PASS (4 tests). Also run `--filter "FullyQualifiedName~TagCatalogueTests"` to confirm no regression.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/Services/TagCatalogue.cs DialogEditor.ViewModels/Resources/tags.json data/tags.json DialogEditor.Tests/Services/TagInsertTemplateTests.cs
git commit -m "feat(tags): insert templates on parameterised/paired tag entries"
```

---

### Task 2: Completion-context detection

`TokenCompletionService.TryGetContext` finds whether the caret sits inside an open `[`/`<` token being typed, and returns the trigger + fragment.

**Files:**
- Create: `DialogEditor.ViewModels/Services/TokenCompletionService.cs`
- Test: `DialogEditor.Tests/Services/TokenCompletionServiceTests.cs` (create)

**Interfaces:**
- Produces: `public sealed record CompletionContext(char Delimiter, int FragmentStart, string Fragment);`
- Produces: `public CompletionContext? TryGetContext(string text, int caretIndex)` — `null` when there is no open context (caret after a `]`/`>`, across a newline, or no opener before it).

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Services/TokenCompletionServiceTests.cs
using DialogEditor.ViewModels.Services;
using Xunit;

namespace DialogEditor.Tests.Services;

public class TokenCompletionServiceTests
{
    private readonly TokenCompletionService _svc = new();

    [Fact]
    public void TryGetContext_OpenBracket_ReturnsTokenContext()
    {
        var ctx = _svc.TryGetContext("hello [Pla", 10);
        Assert.NotNull(ctx);
        Assert.Equal('[', ctx!.Delimiter);
        Assert.Equal(6, ctx.FragmentStart);
        Assert.Equal("[Pla", ctx.Fragment);
    }

    [Fact]
    public void TryGetContext_OpenAngle_ReturnsMarkupContext()
    {
        var ctx = _svc.TryGetContext("say <is", 7);
        Assert.NotNull(ctx);
        Assert.Equal('<', ctx!.Delimiter);
        Assert.Equal("<is", ctx.Fragment);
    }

    [Fact]
    public void TryGetContext_AfterClosingBracket_ReturnsNull()
        => Assert.Null(_svc.TryGetContext("[Player Name]", 13));

    [Fact]
    public void TryGetContext_AfterClosedTag_ReturnsNull()
        => Assert.Null(_svc.TryGetContext("<i>text", 7));

    [Fact]
    public void TryGetContext_NoOpener_ReturnsNull()
        => Assert.Null(_svc.TryGetContext("plain text", 5));

    [Fact]
    public void TryGetContext_StopsAtNewline()
        => Assert.Null(_svc.TryGetContext("[unclosed\nnext", 14));

    [Fact]
    public void TryGetContext_SecondOpenBracket_UsesNearest()
    {
        var ctx = _svc.TryGetContext("[Player Name] [Sl", 17);
        Assert.NotNull(ctx);
        Assert.Equal(14, ctx!.FragmentStart);
        Assert.Equal("[Sl", ctx.Fragment);
    }

    [Fact]
    public void TryGetContext_SpaceInsideToken_DoesNotDismiss()
    {
        var ctx = _svc.TryGetContext("[Player Na", 10);
        Assert.NotNull(ctx);
        Assert.Equal("[Player Na", ctx!.Fragment);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenCompletionServiceTests"`
Expected: FAIL — `TokenCompletionService` does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.ViewModels/Services/TokenCompletionService.cs
namespace DialogEditor.ViewModels.Services;

/// An open completion context under the caret: which delimiter opened it, where
/// that delimiter is, and the text typed so far (delimiter included).
public sealed record CompletionContext(char Delimiter, int FragmentStart, string Fragment);

/// Pure logic for token/markup autocomplete in dialog text. Owns context
/// detection, candidate ranking, and the exact edit applied on accept — no UI.
/// Spec: docs/superpowers/specs/2026-07-06-token-autocomplete-design.md
public sealed class TokenCompletionService
{
    private readonly TagCatalogue _catalogue;

    public TokenCompletionService(TagCatalogue? catalogue = null)
        => _catalogue = catalogue ?? TagCatalogue.Instance;

    public CompletionContext? TryGetContext(string text, int caretIndex)
    {
        if (string.IsNullOrEmpty(text) || caretIndex <= 0 || caretIndex > text.Length)
            return null;

        for (var i = caretIndex - 1; i >= 0; i--)
        {
            var c = text[i];
            if (c is ']' or '>' or '\n' or '\r')
                return null; // context closed, or a line boundary — no completion
            if (c is '[' or '<')
                return new CompletionContext(c, i, text.Substring(i, caretIndex - i));
        }
        return null;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenCompletionServiceTests"`
Expected: PASS (8 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/TokenCompletionService.cs DialogEditor.Tests/Services/TokenCompletionServiceTests.cs
git commit -m "feat(autocomplete): completion-context detection"
```

---

### Task 3: Candidate ranking & filtering

`GetCandidates` turns a context + game id into the ranked list of offerable entries.

**Files:**
- Modify: `DialogEditor.ViewModels/Services/TokenCompletionService.cs`
- Test: `DialogEditor.Tests/Services/TokenCompletionServiceTests.cs`

**Interfaces:**
- Produces: `public IReadOnlyList<TagEntry> GetCandidates(CompletionContext context, string gameId)`
- Produces (internal, reused by Task 4): `internal static (string Literal, int SelStart, int SelLen) InsertionOf(TagEntry entry)` — expands the `insert` marker (or falls back to `Name` with the caret at the end).

- [ ] **Step 1: Write the failing test**

```csharp
// append to TokenCompletionServiceTests
[Fact]
public void GetCandidates_BracketOffersTokens_NotMarkupOrConvention()
{
    var ctx = _svc.TryGetContext("[", 1)!;
    var names = _svc.GetCandidates(ctx, "poe2").Select(e => e.Name).ToList();
    Assert.Contains("[Player Name]", names);
    Assert.DoesNotContain(names, n => n.StartsWith('<'));
    Assert.DoesNotContain("Stage directions", names); // Convention never offered
}

[Fact]
public void GetCandidates_AngleOffersMarkup()
{
    var ctx = _svc.TryGetContext("<", 1)!;
    var names = _svc.GetCandidates(ctx, "poe2").Select(e => e.Name).ToList();
    Assert.Contains("<i>…</i>", names);
    Assert.All(names, n => Assert.StartsWith("<", n));
}

[Fact]
public void GetCandidates_Poe1_ExcludesShipDuelTokens()
{
    var ctx = _svc.TryGetContext("[Ship", 5)!;
    var names = _svc.GetCandidates(ctx, "poe1").Select(e => e.Name).ToList();
    Assert.DoesNotContain(names, n => n.StartsWith("[ShipDuel_"));
}

[Fact]
public void GetCandidates_UnknownGame_OffersUnion()
{
    var ctx = _svc.TryGetContext("[Ship", 5)!;
    var names = _svc.GetCandidates(ctx, "").Select(e => e.Name).ToList();
    Assert.Contains(names, n => n.StartsWith("[ShipDuel_")); // poe2-only entry still offered
}

[Fact]
public void GetCandidates_PrefixMatch_IsCaseInsensitive()
{
    var ctx = _svc.TryGetContext("[pla", 4)!;
    var names = _svc.GetCandidates(ctx, "poe2").Select(e => e.Name).ToList();
    Assert.Contains("[Player Name]", names);
}

[Fact]
public void GetCandidates_RankedByShippedCountDescending()
{
    var ctx = _svc.TryGetContext("[S", 2)!;
    var cands = _svc.GetCandidates(ctx, "poe2");
    // [Specified n] (count 2091) outranks [SkillCheck n] (count 238) and [Slot n] (717)
    for (var i = 1; i < cands.Count; i++)
        Assert.True(cands[i - 1].Count >= cands[i].Count);
}

[Fact]
public void GetCandidates_NoMatch_ReturnsEmpty()
{
    var ctx = _svc.TryGetContext("[Zzz", 4)!;
    Assert.Empty(_svc.GetCandidates(ctx, "poe2"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenCompletionServiceTests"`
Expected: FAIL — `GetCandidates` not defined.

- [ ] **Step 3: Write minimal implementation**

Add to `TokenCompletionService` (and the `using System.StringComparison` sites are fully qualified below):

```csharp
public IReadOnlyList<TagEntry> GetCandidates(CompletionContext context, string gameId)
{
    var kind = context.Delimiter == '[' ? "Token" : "Markup";
    var offerUnion = string.IsNullOrEmpty(gameId);

    return _catalogue.All
        .Where(e => e.Kind == kind)
        .Where(e => offerUnion || e.Games.Any(g =>
            string.Equals(g, gameId, System.StringComparison.OrdinalIgnoreCase)))
        .Where(e => InsertionOf(e).Literal.StartsWith(
            context.Fragment, System.StringComparison.OrdinalIgnoreCase))
        .OrderByDescending(e => e.Count)
        .ThenBy(e => InsertionOf(e).Literal, System.StringComparer.Ordinal)
        .ToList();
}

/// Expands an entry's `insert` marker into literal text plus the selection to
/// place on accept. No `insert` → the Name inserted verbatim, caret at the end.
internal static (string Literal, int SelStart, int SelLen) InsertionOf(TagEntry entry)
{
    if (string.IsNullOrEmpty(entry.Insert))
        return (entry.Name, entry.Name.Length, 0);

    var s = entry.Insert!;
    var open = s.IndexOf("${", System.StringComparison.Ordinal);
    var close = s.IndexOf('}', open);
    var before = s[..open];
    var token = s[(open + 2)..close];
    var after = s[(close + 1)..];
    return (before + token + after, before.Length, token.Length);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenCompletionServiceTests"`
Expected: PASS (15 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/TokenCompletionService.cs DialogEditor.Tests/Services/TokenCompletionServiceTests.cs
git commit -m "feat(autocomplete): game-aware candidate ranking"
```

---

### Task 4: Apply-completion edit

`ApplyCompletion` computes the exact text replacement and resulting caret/selection when an entry is accepted.

**Files:**
- Modify: `DialogEditor.ViewModels/Services/TokenCompletionService.cs`
- Test: `DialogEditor.Tests/Services/TokenCompletionServiceTests.cs`

**Interfaces:**
- Produces: `public sealed record CompletionEdit(int ReplaceStart, int ReplaceLength, string InsertedText, int SelectionStart, int SelectionLength);`
- Produces: `public CompletionEdit ApplyCompletion(CompletionContext context, TagEntry entry)`

- [ ] **Step 1: Write the failing test**

```csharp
// append to TokenCompletionServiceTests
private TagEntry Entry(string name) => TagCatalogue.Instance.All.Single(e => e.Name == name);

[Fact]
public void ApplyCompletion_PlainToken_CaretAtEnd()
{
    var ctx = _svc.TryGetContext("[Pla", 4)!;
    var edit = _svc.ApplyCompletion(ctx, Entry("[Player Name]"));
    Assert.Equal(0, edit.ReplaceStart);
    Assert.Equal(4, edit.ReplaceLength);
    Assert.Equal("[Player Name]", edit.InsertedText);
    Assert.Equal("[Player Name]".Length, edit.SelectionStart);
    Assert.Equal(0, edit.SelectionLength);
}

[Fact]
public void ApplyCompletion_ParameterisedToken_SelectsNumber()
{
    var ctx = _svc.TryGetContext("[Spe", 4)!;
    var edit = _svc.ApplyCompletion(ctx, Entry("[Specified n]"));
    Assert.Equal("[Specified 0]", edit.InsertedText);
    Assert.Equal("[Specified ".Length, edit.SelectionStart); // caret on the '0'
    Assert.Equal(1, edit.SelectionLength);
}

[Fact]
public void ApplyCompletion_PairedMarkup_CaretBetweenTags()
{
    var ctx = _svc.TryGetContext("<i", 2)!;
    var edit = _svc.ApplyCompletion(ctx, Entry("<i>…</i>"));
    Assert.Equal("<i></i>", edit.InsertedText);
    Assert.Equal("<i>".Length, edit.SelectionStart);
    Assert.Equal(0, edit.SelectionLength);
}

[Fact]
public void ApplyCompletion_AttributeMarkup_CaretInsideQuotes()
{
    var ctx = _svc.TryGetContext("<col", 4)!;
    var edit = _svc.ApplyCompletion(ctx, Entry("<color=\"…\">…</color>"));
    Assert.Equal("<color=\"\"></color>", edit.InsertedText);
    Assert.Equal("<color=\"".Length, edit.SelectionStart);
    Assert.Equal(0, edit.SelectionLength);
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenCompletionServiceTests"`
Expected: FAIL — `ApplyCompletion` / `CompletionEdit` not defined.

- [ ] **Step 3: Write minimal implementation**

Add the record beside `CompletionContext`:

```csharp
/// The edit to apply when a completion is accepted: which span of the text to
/// replace, the text to insert, and where to leave the caret/selection.
public sealed record CompletionEdit(
    int ReplaceStart, int ReplaceLength, string InsertedText,
    int SelectionStart, int SelectionLength);
```

Add the method to `TokenCompletionService`:

```csharp
public CompletionEdit ApplyCompletion(CompletionContext context, TagEntry entry)
{
    var (literal, selStart, selLen) = InsertionOf(entry);
    return new CompletionEdit(
        ReplaceStart:    context.FragmentStart,
        ReplaceLength:   context.Fragment.Length,
        InsertedText:    literal,
        SelectionStart:  context.FragmentStart + selStart,
        SelectionLength: selLen);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenCompletionServiceTests"`
Expected: PASS (19 tests).

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.ViewModels/Services/TokenCompletionService.cs DialogEditor.Tests/Services/TokenCompletionServiceTests.cs
git commit -m "feat(autocomplete): apply-completion edit math"
```

---

### Task 5: Popup attached behaviour

An attached behaviour that renders the completion `Popup` on a `TextBox` and applies the service's edit. The service does the thinking; this is mechanical.

**Files:**
- Create: `DialogEditor.Avalonia/Behaviors/TokenCompletion.cs`
- Test: `DialogEditor.Tests/Views/TokenCompletionBehaviorTests.cs` (create)

**Interfaces:**
- Consumes: `TokenCompletionService`, `CompletionContext`, `CompletionEdit` (Tasks 2-4).
- Produces: attached properties `TokenCompletion.IsEnabled` (`bool`) and `TokenCompletion.GameId` (`string`) on `TextBox`; a `TokenCompletion.PopupName = "TokenCompletionPopup"` constant used by the test.

- [ ] **Step 1: Write the failing test**

```csharp
// DialogEditor.Tests/Views/TokenCompletionBehaviorTests.cs
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Behaviors;
using Xunit;

namespace DialogEditor.Tests.Views;

public class TokenCompletionBehaviorTests
{
    private static (Window w, TextBox tb, Popup popup) Setup(string gameId = "poe2")
    {
        var tb = new TextBox();
        TokenCompletion.SetGameId(tb, gameId);
        TokenCompletion.SetIsEnabled(tb, true);
        var window = new Window { Content = tb };
        window.Show();
        tb.Focus();
        var popup = tb.GetVisualDescendantPopup(); // helper below, or find by name
        return (window, tb, popup);
    }

    [AvaloniaFact]
    public void TypingOpenBracket_ShowsPopupWithCandidates()
    {
        var (_, tb, _) = Setup();
        tb.Text = "[Pla";
        tb.CaretIndex = 4;
        var popup = FindPopup(tb);
        Assert.True(popup.IsOpen);
        Assert.NotEmpty(((ListBox)popup.Child!).Items);
    }

    [AvaloniaFact]
    public void Enter_InsertsSelectedCompletion_AndClosesPopup()
    {
        var (_, tb, _) = Setup();
        tb.Text = "[Player Nam";
        tb.CaretIndex = 11;
        var popup = FindPopup(tb);
        ((ListBox)popup.Child!).SelectedIndex = 0;
        tb.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter });
        Assert.Contains("[Player Name]", tb.Text);
        Assert.False(popup.IsOpen);
    }

    [AvaloniaFact]
    public void Escape_HidesPopup()
    {
        var (_, tb, _) = Setup();
        tb.Text = "[Pla";
        tb.CaretIndex = 4;
        var popup = FindPopup(tb);
        Assert.True(popup.IsOpen);
        tb.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Escape });
        Assert.False(popup.IsOpen);
    }

    private static Popup FindPopup(TextBox tb) =>
        (Popup)((Panel)((Grid)tb.GetLogicalParent()!).Children[^1]).Children[0]; // see note
}
```

> **Note for the implementer:** the exact popup-hosting/lookup wiring (how the `Popup` is parented to the `TextBox` and found in the test) is the one place to adapt to Avalonia's API under green — keep the *assertions* (popup opens on `[`, Enter inserts the ranked-and-selected entry, Esc closes) and adjust the `FindPopup` plumbing to match your implementation. Expose the `Popup` via a stable `x:Name`/field so the test can locate it deterministically.

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenCompletionBehaviorTests"`
Expected: FAIL — `TokenCompletion` behaviour does not exist.

- [ ] **Step 3: Write minimal implementation**

```csharp
// DialogEditor.Avalonia/Behaviors/TokenCompletion.cs
using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Behaviors;

/// IDE-style token/markup autocomplete on a TextBox. Every decision (context,
/// candidates, insertion) comes from TokenCompletionService; this behaviour only
/// shows a Popup and applies the service's computed edit.
/// Spec: docs/superpowers/specs/2026-07-06-token-autocomplete-design.md
public static class TokenCompletion
{
    public const string PopupName = "TokenCompletionPopup";
    private static readonly TokenCompletionService Service = new();

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(TokenCompletion));
    public static readonly AttachedProperty<string> GameIdProperty =
        AvaloniaProperty.RegisterAttached<TextBox, string>("GameId", typeof(TokenCompletion), string.Empty);
    private static readonly AttachedProperty<Session?> SessionProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Session?>("Session", typeof(TokenCompletion));

    public static void SetIsEnabled(TextBox t, bool v) => t.SetValue(IsEnabledProperty, v);
    public static bool GetIsEnabled(TextBox t) => t.GetValue(IsEnabledProperty);
    public static void SetGameId(TextBox t, string v) => t.SetValue(GameIdProperty, v);
    public static string GetGameId(TextBox t) => t.GetValue(GameIdProperty);

    static TokenCompletion()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>((t, e) =>
        {
            if (e.NewValue is true && t.GetValue(SessionProperty) is null)
                t.SetValue(SessionProperty, new Session(t));
        });
    }

    /// Per-TextBox popup state and event wiring.
    private sealed class Session
    {
        private readonly TextBox _box;
        private readonly Popup _popup;
        private readonly ListBox _list;
        private CompletionContext? _context;

        public Session(TextBox box)
        {
            _box = box;
            _list = new ListBox { MaxHeight = 200, MinWidth = 220 };
            _popup = new Popup
            {
                Name = PopupName,
                Child = _list,
                PlacementTarget = box,
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                IsLightDismissEnabled = true,
            };
            ((ISetLogicalParent)_popup).SetParent(box);

            box.PropertyChanged += (_, e) =>
            {
                if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty)
                    Refresh();
            };
            box.AddHandler(InputElement.KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _list.DoubleTapped += (_, _) => Accept();
        }

        private void Refresh()
        {
            try
            {
                _context = Service.TryGetContext(_box.Text ?? "", _box.CaretIndex);
                if (_context is null) { _popup.IsOpen = false; return; }
                var candidates = Service.GetCandidates(_context, GetGameId(_box));
                if (candidates.Count == 0) { _popup.IsOpen = false; return; }
                _list.ItemsSource = candidates;
                _list.SelectedIndex = 0;
                _popup.IsOpen = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Token autocomplete refresh failed", ex);
                _popup.IsOpen = false;
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (!_popup.IsOpen) return;
            switch (e.Key)
            {
                case Key.Escape: _popup.IsOpen = false; e.Handled = true; break;
                case Key.Enter or Key.Tab: Accept(); e.Handled = true; break;
                case Key.Down: Move(+1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
            }
        }

        private void Move(int delta)
        {
            var n = _list.ItemCount;
            if (n == 0) return;
            _list.SelectedIndex = (_list.SelectedIndex + delta + n) % n;
        }

        private void Accept()
        {
            if (_context is null || _list.SelectedItem is not TagEntry entry) return;
            var edit = Service.ApplyCompletion(_context, entry);
            var text = _box.Text ?? "";
            _box.Text = text.Remove(edit.ReplaceStart, edit.ReplaceLength)
                            .Insert(edit.ReplaceStart, edit.InsertedText);
            _box.SelectionStart = edit.SelectionStart;
            _box.SelectionEnd = edit.SelectionStart + edit.SelectionLength;
            _box.CaretIndex = edit.SelectionStart + edit.SelectionLength;
            _popup.IsOpen = false;
        }
    }
}
```

> `AppLog` lives in `DialogEditor.ViewModels.Services` (already `using`-ed).

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenCompletionBehaviorTests"`
Expected: PASS (3 tests). Adjust the `FindPopup` plumbing (not the assertions) if the harness can't locate the popup.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Behaviors/TokenCompletion.cs DialogEditor.Tests/Views/TokenCompletionBehaviorTests.cs
git commit -m "feat(autocomplete): token-completion popup behaviour"
```

---

### Task 6: Wire into the node editor & close the gap

Attach the behaviour to the Default/Female fields and record the gap as done.

**Files:**
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml` (the two dialog-text `TextBox`es, ~lines 77-92)
- Modify: `Gaps.md` (the "Token autocomplete and validation in node text editing" section)

**Interfaces:**
- Consumes: `TokenCompletion.IsEnabled`/`GameId` (Task 5); `NodeDetailViewModel.ActiveGameId`.

- [ ] **Step 1: Add the behaviour namespace and attach it in XAML**

In `NodeDetailView.axaml`, add the xmlns (top of the file, beside the other `xmlns:` lines):

```xml
xmlns:beh="clr-namespace:DialogEditor.Avalonia.Behaviors;assembly=DialogEditor.Avalonia"
```

On `DefaultTextBox` and the Female `TextBox`, add the two attached properties (Default shown; apply the same two lines to the Female box):

```xml
<TextBox x:Name="DefaultTextBox" Classes="detail-field"
         Text="{Binding DefaultText, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="52"
         beh:TokenCompletion.IsEnabled="True"
         beh:TokenCompletion.GameId="{Binding ActiveGameId}"
         ToolTip.Tip="{DynamicResource ToolTip_DefaultText}"
         AutomationProperties.HelpText="{DynamicResource ToolTip_DefaultText}"
         AutomationProperties.Name="{DynamicResource Label_DefaultMaleText}"/>
```

- [ ] **Step 2: Build and run the app to verify by hand**

Use the `running-the-app` skill. In an open PoE2 project, select a node, click into the **Default** text field, type `[`. Confirm: a popup lists tokens; typing `Pla` narrows to `[Player Name]`; Enter inserts it with the caret after `]`; typing `[Specified ` then accepting selects the `0`; typing `<i` then Enter inserts `<i></i>` with the caret between the tags; Esc dismisses. Open a PoE1 project and confirm `[ShipDuel_…]` is **not** offered.

Expected: all of the above behave as described; no exceptions in the log.

- [ ] **Step 3: Run the full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (green suite, including the new `TagInsertTemplateTests`, `TokenCompletionServiceTests`, `TokenCompletionBehaviorTests`).

- [ ] **Step 4: Mark the gap's autocomplete half done**

In `Gaps.md`, under "Token autocomplete and validation in node text editing", record the **Autocomplete** bullet as implemented (IDE-style popup on `[`/`<` in the Default/Female fields, game-aware, data-driven `insert` templates, `TokenCompletionService` + `TokenCompletion` behaviour; spec `docs/superpowers/specs/2026-07-06-token-autocomplete-design.md`). Leave the **Validation** bullet open as the remaining half.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/NodeDetailView.axaml Gaps.md
git commit -m "feat(autocomplete): enable token autocomplete on node text fields"
```

---

## Self-Review

**Spec coverage:**
- IDE-style popup on `[`/`<` → Tasks 5-6. ✓
- Smart insert, caret at first placeholder → Task 4 (`ApplyCompletion`) + Task 1 (`insert` data). ✓
- Data-driven `insert` field → Task 1. ✓
- Pure `TokenCompletionService` (context, candidates, apply) → Tasks 2-4. ✓
- `Convention` never offered; `Token`/`Markup` split by delimiter → Task 3. ✓
- Game filter incl. union-on-unknown-game → Task 3. ✓
- Ranking by shipped `Count` → Task 3. ✓
- Scope = Default + Female fields only → Task 6. ✓
- Popup shows Name + Description → Task 5 (`ListBox` items over `TagEntry`; the `ItemTemplate` showing Name primary / Description secondary is part of Task 5's `ListBox` setup — implementer adds the two-line `DataTemplate`). ✓
- Cross-cutting rules (localisation, tooltips, UIA, `AppLog`) → Global Constraints + Task 5 `catch`. ✓
- Testing plan (service unit, data integrity, headless View) → Tasks 1-5. ✓
- Validation explicitly deferred → Task 6 Step 4. ✓

**Placeholder scan:** none — every code step shows complete code; the one adaptation point (popup lookup in the headless test) is called out explicitly with the assertions held fixed.

**Type consistency:** `CompletionContext(Delimiter, FragmentStart, Fragment)`, `CompletionEdit(ReplaceStart, ReplaceLength, InsertedText, SelectionStart, SelectionLength)`, `InsertionOf → (Literal, SelStart, SelLen)`, `TagEntry.Insert`, `TokenCompletion.{IsEnabled,GameId,PopupName}` are used identically across Tasks 2-6.

**Note added during review:** Task 5's `ListBox` needs a two-line `ItemTemplate` (Name primary, Description secondary) to satisfy the spec's popup-layout decision — the implementer adds this `DataTemplate` when building the `ListBox`; the behaviour test only asserts item presence, not layout.
