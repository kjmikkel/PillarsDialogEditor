# Yarn Spinner Import — Inline Conditional Choices Design

## Context

The Yarn Spinner importer already warns about line-level `<<if>>`, `<<set>>`,
`<<command>>` constructs (committed 2026-05-28). A remaining gap: constructs written
inline on choice or dialogue lines — `-> Choice text <<if $x>>`, `Npc: Text. <<fade_in>>`
— are not stripped from the imported node text and are not counted in the warning tally.
The `<<...>>` suffix ends up visible in the editor as part of the node's `DefaultText`,
and the writer receives no warning that it was dropped.

## Scope

- **In scope:** detecting and stripping inline `<<...>>` constructs from both choice lines
  (`->`) and dialogue/NPC lines during import; counting them in the warning tally
  alongside the already-tallied standalone command lines.
- **Out of scope:** actually importing conditions; changes to any other importer.

## Architecture

Two responsibilities, two helpers, no method-signature changes:

- **`StripInlineConstructs(string text)`** — called by `GeneratePendingNodes` to remove
  all `<<...>>` spans from choice text and dialogue text before storing them as
  `DefaultText`. Returns the cleaned string.
- **`ScanEmbeddedConstructs(string text, Dictionary<string,int> counts)`** — called by
  `TallySkippedConstructs` when it encounters a `->` or dialogue body line. Scans the
  string for all `<<keyword>>` patterns and adds them to the shared `counts` dictionary.

Both helpers reuse the existing `ExtractKeyword` method so keyword extraction stays in
one place. `GeneratePendingNodes` only strips (never counts); `TallySkippedConstructs`
only counts (never modifies text). The two passes remain independent and their
responsibilities remain distinct.

## Implementation

All changes are in `DialogEditor.Core/Import/YarnSpinnerImporter.cs`.

### New helper — `StripInlineConstructs`

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
```

### New helper — `ScanEmbeddedConstructs`

```csharp
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

### Changes to `GeneratePendingNodes`

One `StripInlineConstructs` call added to each line type, after all other extraction:

```csharp
// choice branch — after [[jump]] extraction
choiceText = StripInlineConstructs(choiceText);
contentLines.Add((true, choiceText, jumpTarget));

// dialogue branch — after speaker-prefix stripping
dialogText = StripInlineConstructs(dialogText);
contentLines.Add((false, dialogText, null));
```

### Changes to `TallySkippedConstructs`

The inner loop gains two new branches after the existing standalone-`<<` check:

```csharp
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
```

Standalone `<<` lines and inline constructs are unified into the same `counts`
dictionary, so a file mixing both gets correct aggregate counts.

## Tests

Five new tests in `DialogEditor.Tests/Import/YarnSpinnerImporterTests.cs`, under the
existing `// ── Skipped-construct warnings ──` section, using the existing `WriteTempYarn`
helper and `Importer` field:

1. **`Import_ChoiceWithInlineConstruct_StripsFromText`** — `-> Yes I can <<if $x>>`
   yields `DefaultText == "Yes I can"` (no `<<...>>` in the node text).
2. **`Import_ChoiceWithInlineConstruct_TalliesWarning`** — same file yields
   `Warnings` containing `("if", 1)`.
3. **`Import_DialogueWithInlineConstruct_StripsFromText`** — `Npc: Come in. <<fade_in>>`
   yields `DefaultText == "Come in."`.
4. **`Import_DialogueWithInlineConstruct_TalliesWarning`** — same file yields
   `Warnings` containing `("fade_in", 1)`.
5. **`Import_MixedStandaloneAndInlineConstructs_TalliedTogether`** — file with one
   standalone `<<if>>`, one inline `<<if>>` on a dialogue line, one inline `<<if>>` on a
   choice line yields `Warnings` containing `("if", 3)`.

All existing tests must continue to pass.

## Error Handling

No new exception paths. `StripInlineConstructs` and `ScanEmbeddedConstructs` handle
unclosed `<<` gracefully — if `>>` is not found, they advance to end-of-string and stop.
No `FormatException` is thrown for malformed inline constructs; they are silently skipped
(consistent with how the rest of the importer handles unexpected syntax).

## Verification

1. `dotnet test DialogEditor.Tests` — all tests pass (~807 expected).
2. Manual: import a `.yarn` file containing `-> Choice <<if $x>>` — warning dialog
   lists `<<if>>`, status bar shows the suffix, and the imported node's text in the
   canvas contains no `<<...>>`.
3. Manual: import a `.yarn` file mixing standalone `<<if>>` and inline `<<if>>` — the
   warning shows the combined count.
