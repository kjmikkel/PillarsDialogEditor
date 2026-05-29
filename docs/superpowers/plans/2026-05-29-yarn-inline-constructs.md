# Yarn Spinner Inline Construct Stripping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Strip `<<...>>` constructs that appear inline on Yarn choice and dialogue lines from node text, and include them in the import warning tally.

**Architecture:** Two helpers added to `YarnSpinnerImporter` — `StripInlineConstructs` removes all `<<...>>` spans from a string, `ScanEmbeddedConstructs` counts them into a shared dictionary. Task 1 calls the strip helper in `GeneratePendingNodes` (fixes `DefaultText`). Task 2 extends `TallySkippedConstructs` to call the scan helper on `->` and dialogue lines (fixes warnings). Both helpers reuse the existing `ExtractKeyword` method.

**Tech Stack:** C# 12 / .NET 8, xUnit.

---

## Background — Conventions This Plan Follows

- **TDD is mandatory** (see `CLAUDE.md`): write the failing test first, run it to confirm it fails, then implement.
- Test command from repo root: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerImporterTests"`
- Full suite: `dotnet test DialogEditor.Tests`
- The existing test class (`DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`) already has a `WriteTempYarn(string content)` helper and a `private static readonly YarnSpinnerImporter Importer = new();` field — reuse both, never recreate them.

## File Structure

| File | Change |
|------|--------|
| `DialogEditor.Core/Import/YarnSpinnerImporter.cs` | Add 2 helpers; 2 call sites in `GeneratePendingNodes`; rewrite `TallySkippedConstructs` loop |
| `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs` | Add 5 tests |
| `Gaps.md` | Remove the inline-conditional-choices gap entry |

---

### Task 1: Strip inline constructs from node text

Add the two new helpers and call `StripInlineConstructs` in `GeneratePendingNodes` for both choice lines and dialogue lines. After this task, `DefaultText` on imported nodes is clean of `<<...>>` markup.

**Files:**
- Modify: `DialogEditor.Core/Import/YarnSpinnerImporter.cs`
- Test: `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`

- [ ] **Step 1: Write the two failing tests**

Open `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`. Locate the `// ── Skipped-construct warnings ──` section (near the bottom). Add these two tests after the existing three:

```csharp
[Fact]
public void Import_ChoiceWithInlineConstruct_StripsFromText()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        Npc: Choose.
        -> Yes I can <<if $x>>
        ===
        """);

    var result = Importer.Import(path);

    var choice = result.Nodes.Single(n => n.IsPlayerChoice);
    Assert.Equal("Yes I can", choice.DefaultText);
}

[Fact]
public void Import_DialogueWithInlineConstruct_StripsFromText()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        Npc: Come in. <<fade_in>>
        ===
        """);

    var result = Importer.Import(path);

    Assert.Equal("Come in.", result.Nodes[0].DefaultText);
}
```

- [ ] **Step 2: Run the tests — verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerImporterTests"`
Expected: the 2 new tests **FAIL** (DefaultText still contains `<<...>>`); all other tests pass.

- [ ] **Step 3: Add the two helpers to `YarnSpinnerImporter.cs`**

Open `DialogEditor.Core/Import/YarnSpinnerImporter.cs`. Locate the comment at line 305:
```
// "<<if $gold > 10>>" -> "if";  "<<endif>>" -> "endif";  "<<>>" -> "".
private static string ExtractKeyword(string line)
```

Insert both helpers AFTER the closing `}` of `ExtractKeyword` (before the `// ── Link resolution ──` comment):

```csharp
    // Remove all <<...>> spans from text, returning the trimmed result.
    // "Yes I can <<if $x>>" → "Yes I can"
    private static string StripInlineConstructs(string text)
    {
        var sb = new System.Text.StringBuilder();
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf("<<", i, StringComparison.Ordinal);
            if (open < 0) { sb.Append(text, i, text.Length - i); break; }
            sb.Append(text, i, open - i);
            int close = text.IndexOf(">>", open + 2, StringComparison.Ordinal);
            i = close < 0 ? text.Length : close + 2;
        }
        return sb.ToString().Trim();
    }

    // Scan text for all <<keyword>> patterns and add their counts to `counts`.
    private static void ScanEmbeddedConstructs(string text, Dictionary<string, int> counts)
    {
        int i = 0;
        while (i < text.Length)
        {
            int open = text.IndexOf("<<", i, StringComparison.Ordinal);
            if (open < 0) break;
            var keyword = ExtractKeyword(text[open..]);
            if (keyword.Length > 0)
                counts[keyword] = counts.GetValueOrDefault(keyword) + 1;
            int close = text.IndexOf(">>", open + 2, StringComparison.Ordinal);
            i = close < 0 ? text.Length : close + 2;
        }
    }
```

- [ ] **Step 4: Call `StripInlineConstructs` in `GeneratePendingNodes`**

In the same file, locate `GeneratePendingNodes` (around line 135). Find the `->` branch that ends at line 159:

```csharp
                contentLines.Add((true, choiceText, jumpTarget));
```

Change it to:

```csharp
                choiceText = StripInlineConstructs(choiceText);
                contentLines.Add((true, choiceText, jumpTarget));
```

Then find the dialogue branch that ends around line 171:

```csharp
                contentLines.Add((false, dialogText, null));
```

Change it to:

```csharp
                dialogText = StripInlineConstructs(dialogText);
                contentLines.Add((false, dialogText, null));
```

- [ ] **Step 5: Run the tests — verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerImporterTests"`
Expected: ALL pass (existing + 2 new).

- [ ] **Step 6: Commit**

```
git add DialogEditor.Core/Import/YarnSpinnerImporter.cs DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs
git commit -m "feat: strip inline <<...>> constructs from Yarn node text"
```
End the commit message with a trailing line:
`Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`

---

### Task 2: Tally inline constructs in warnings + close the gap

Extend `TallySkippedConstructs` to call `ScanEmbeddedConstructs` on `->` and dialogue lines, so inline constructs appear in the warning list alongside standalone `<<` lines. Then remove the now-resolved entry from `Gaps.md`.

**Files:**
- Modify: `DialogEditor.Core/Import/YarnSpinnerImporter.cs`
- Test: `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`
- Modify: `Gaps.md`

- [ ] **Step 1: Write the three failing tests**

Add these three tests to `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`, after the two added in Task 1:

```csharp
[Fact]
public void Import_ChoiceWithInlineConstruct_TalliesWarning()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        Npc: Choose.
        -> Yes I can <<if $x>>
        ===
        """);

    var result = Importer.Import(path);

    Assert.Contains(result.Warnings, w => w.Construct == "if" && w.Count == 1);
}

[Fact]
public void Import_DialogueWithInlineConstruct_TalliesWarning()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        Npc: Come in. <<fade_in>>
        ===
        """);

    var result = Importer.Import(path);

    Assert.Contains(result.Warnings, w => w.Construct == "fade_in" && w.Count == 1);
}

[Fact]
public void Import_MixedStandaloneAndInlineConstructs_TalliedTogether()
{
    var path = WriteTempYarn("""
        title: Start
        ---
        <<if $gold>>
        Merchant: Buy this. <<if $offer>>
        -> Deal. <<if $agree>>
        ===
        """);

    var result = Importer.Import(path);

    Assert.Contains(result.Warnings, w => w.Construct == "if" && w.Count == 3);
}
```

- [ ] **Step 2: Run the tests — verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerImporterTests"`
Expected: the 3 new tests **FAIL** (inline constructs not yet tallied); all others pass.

- [ ] **Step 3: Rewrite the `TallySkippedConstructs` loop**

In `DialogEditor.Core/Import/YarnSpinnerImporter.cs`, locate `TallySkippedConstructs` (around line 283). Replace the entire inner `foreach (var raw in block.BodyLines)` loop body with:

```csharp
            foreach (var raw in block.BodyLines)
            {
                if (raw.StartsWith("<<", StringComparison.Ordinal))
                {
                    // Standalone command line — existing behaviour
                    var keyword = ExtractKeyword(raw);
                    if (keyword.Length > 0)
                        counts[keyword] = counts.GetValueOrDefault(keyword) + 1;
                }
                else if (raw.StartsWith("->", StringComparison.Ordinal))
                {
                    // Choice line — scan for inline <<...>> after the "->"
                    ScanEmbeddedConstructs(raw[2..], counts);
                }
                else if (!raw.StartsWith("//", StringComparison.Ordinal)
                         && !string.IsNullOrWhiteSpace(raw))
                {
                    // Dialogue line — scan the whole line for inline <<...>>
                    ScanEmbeddedConstructs(raw, counts);
                }
            }
```

The complete updated method looks like this:

```csharp
    private static List<ImportWarning> TallySkippedConstructs(IReadOnlyList<RawBlock> blocks)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);

        foreach (var block in blocks)
        {
            foreach (var raw in block.BodyLines)
            {
                if (raw.StartsWith("<<", StringComparison.Ordinal))
                {
                    // Standalone command line — existing behaviour
                    var keyword = ExtractKeyword(raw);
                    if (keyword.Length > 0)
                        counts[keyword] = counts.GetValueOrDefault(keyword) + 1;
                }
                else if (raw.StartsWith("->", StringComparison.Ordinal))
                {
                    // Choice line — scan for inline <<...>> after the "->"
                    ScanEmbeddedConstructs(raw[2..], counts);
                }
                else if (!raw.StartsWith("//", StringComparison.Ordinal)
                         && !string.IsNullOrWhiteSpace(raw))
                {
                    // Dialogue line — scan the whole line for inline <<...>>
                    ScanEmbeddedConstructs(raw, counts);
                }
            }
        }

        return counts.Select(kv => new ImportWarning(kv.Key, kv.Value)).ToList();
    }
```

- [ ] **Step 4: Run the tests — verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~YarnSpinnerImporterTests"`
Expected: ALL pass (existing + 5 new from Tasks 1 and 2).

- [ ] **Step 5: Run the full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: ALL pass (~807 total).

- [ ] **Step 6: Remove the gap entry from `Gaps.md`**

Open `Gaps.md` (in the repo root). Find and delete the entire `### Yarn Spinner Import — Inline Conditional Choices` section (the heading line and its following paragraph). Leave all surrounding sections intact.

- [ ] **Step 7: Commit**

```
git add DialogEditor.Core/Import/YarnSpinnerImporter.cs DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs Gaps.md
git commit -m "feat: tally inline <<...>> constructs in Yarn import warnings; close gap"
```
End the commit message with a trailing line:
`Co-Authored-By: Claude Sonnet 4.6 <noreply@anthropic.com>`
