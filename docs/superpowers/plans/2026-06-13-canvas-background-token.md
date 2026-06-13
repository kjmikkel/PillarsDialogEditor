# Canvas Backdrop Colour (`Brush.Canvas.Background`) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Rename `Brush.Accent.Badge` → `Brush.Canvas.Background` and its
underlying primitive `Palette.Mauve.500` → `Palette.Neutral.520`, retinting
the dialog canvas backdrop from muted mauve to a neutral grey (`#838585` for
Dark/Light/Colourblind, `#3A3A3A` for High-Contrast) across all four themes.

**Architecture:** This is a pure rename + value change inside the existing
Layer 0/Layer 1 colour token system (`Palette.*.axaml` → `Tokens.axaml`).
Update the two structural contract tests first (`TokenRegistryTests`,
`PaletteContrastTests`) so they fail against the new names (RED), then rename
across the five resource files plus the single consumer in
`ConversationView.axaml` (GREEN), then regenerate the
`PaletteGoldenTests` approval snapshot.

**Tech Stack:** C#/.NET 8, Avalonia 11.3.14, xUnit, `Avalonia.Headless.XUnit`.

**Spec:** `docs/superpowers/specs/2026-06-13-canvas-background-token-design.md`

---

### Task 1: Update contract tests to reference the new names (RED)

**Files:**
- Modify: `DialogEditor.Tests/Theming/TokenRegistryTests.cs:66`
- Modify: `DialogEditor.Tests/Theming/PaletteContrastTests.cs:70-73`

- [ ] **Step 1: Rename the token in `TokenRegistryTests.cs`'s `AllTokens` list**

In `DialogEditor.Tests/Theming/TokenRegistryTests.cs`, line 66 currently reads:

```csharp
        "Brush.Accent.Badge",
```

Change it to:

```csharp
        "Brush.Canvas.Background",
```

- [ ] **Step 2: Rename the primitive in `PaletteContrastTests.cs`'s `HcBorderPairs`**

In `DialogEditor.Tests/Theming/PaletteContrastTests.cs`, lines 70-73 currently
read:

```csharp
        // The pane GridSplitters (Brush.Border.Default) border the canvas backdrop on their
        // canvas side (Brush.Accent.Badge → Palette.Mauve.500), not just the dark panels — so
        // the divider must stay visible against the canvas too, or it vanishes into it.
        ("Palette.Line.Default", "Palette.Mauve.500"),   // GridSplitter on the canvas backdrop
```

Change them to:

```csharp
        // The pane GridSplitters (Brush.Border.Default) border the canvas backdrop on their
        // canvas side (Brush.Canvas.Background → Palette.Neutral.520), not just the dark panels —
        // so the divider must stay visible against the canvas too, or it vanishes into it.
        ("Palette.Line.Default", "Palette.Neutral.520"), // GridSplitter on the canvas backdrop
```

- [ ] **Step 3: Run the affected tests and verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenRegistryTests|FullyQualifiedName~PaletteContrastTests"`

Expected: **FAIL** — `TokenRegistryTests` fails because `Brush.Canvas.Background`
doesn't resolve to a resource in any theme yet; `PaletteContrastTests` fails
because `Palette.Neutral.520` doesn't exist in any palette yet (the contrast
pair can't be resolved).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Tests/Theming/TokenRegistryTests.cs DialogEditor.Tests/Theming/PaletteContrastTests.cs
git commit -m "test(theming): reference Brush.Canvas.Background / Palette.Neutral.520 (red)"
```

---

### Task 2: Rename + retint the resource files and consumer (GREEN)

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml:154`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml:97`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.Light.axaml:96`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.Colourblind.axaml:97`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml:90`
- Modify: `DialogEditor.Avalonia/Views/ConversationView.axaml:100`

- [ ] **Step 1: Rename the semantic token in `Tokens.axaml`**

In `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml`, line 154 currently
reads:

```xml
    <SolidColorBrush x:Key="Brush.Accent.Badge"      Color="{StaticResource Palette.Mauve.500}"/>
```

Change it to:

```xml
    <SolidColorBrush x:Key="Brush.Canvas.Background" Color="{StaticResource Palette.Neutral.520}"/>
```

- [ ] **Step 2: Rename + retint the primitive in `Palette.Dark.axaml`**

In `DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml`, line 97
currently reads:

```xml
    <Color x:Key="Palette.Mauve.500">#FF7A6A8E</Color>
```

Change it to:

```xml
    <Color x:Key="Palette.Neutral.520">#FF838585</Color>
```

- [ ] **Step 3: Rename + retint the primitive in `Palette.Light.axaml`**

In `DialogEditor.Avalonia.Shared/Resources/Palette.Light.axaml`, line 96
currently reads:

```xml
    <Color x:Key="Palette.Mauve.500">#FF7A6A8E</Color>
```

Change it to:

```xml
    <Color x:Key="Palette.Neutral.520">#FF838585</Color>
```

- [ ] **Step 4: Rename + retint the primitive in `Palette.Colourblind.axaml`**

In `DialogEditor.Avalonia.Shared/Resources/Palette.Colourblind.axaml`, line 97
currently reads:

```xml
    <Color x:Key="Palette.Mauve.500">#FF7A6A8E</Color>
```

Change it to:

```xml
    <Color x:Key="Palette.Neutral.520">#FF838585</Color>
```

- [ ] **Step 5: Rename + retint the primitive in `Palette.HighContrast.axaml`**

In `DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml`, line
90 currently reads:

```xml
    <Color x:Key="Palette.Mauve.500">#FF332A47</Color>    <!-- canvas backdrop (Brush.Accent.Badge): mid-dark so the bright Line.Default GridSplitters stay visible (~5.7:1) and white node cards pop, while still stepping off the near-black chrome -->
```

Change it to:

```xml
    <Color x:Key="Palette.Neutral.520">#FF3A3A3A</Color>  <!-- canvas backdrop (Brush.Canvas.Background): neutral grey matching Dark/Light/Colourblind's tone, dark enough that Line.Default GridSplitters (~4.8:1) and white node cards stay visible against the near-black chrome -->
```

- [ ] **Step 6: Update the single consumer in `ConversationView.axaml`**

In `DialogEditor.Avalonia/Views/ConversationView.axaml`, line 100 currently
reads:

```xml
                                 Background="{DynamicResource Brush.Accent.Badge}"
```

Change it to:

```xml
                                 Background="{DynamicResource Brush.Canvas.Background}"
```

- [ ] **Step 7: Run the affected tests and verify they pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenRegistryTests|FullyQualifiedName~PaletteContrastTests"`

Expected: **PASS** for both `TokenRegistryTests` and `PaletteContrastTests`.

Note: `PaletteGoldenTests` will now **fail** — this is expected and handled in
Task 3. Do not run the full suite yet.

- [ ] **Step 8: Commit**

```bash
git add DialogEditor.Avalonia.Shared/Resources/Tokens.axaml DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml DialogEditor.Avalonia.Shared/Resources/Palette.Light.axaml DialogEditor.Avalonia.Shared/Resources/Palette.Colourblind.axaml DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml DialogEditor.Avalonia/Views/ConversationView.axaml
git commit -m "feat(theming): rename canvas backdrop to Brush.Canvas.Background, retint to neutral grey"
```

---

### Task 3: Regenerate the `PaletteGoldenTests` approval snapshot

**Files:**
- Modify: `DialogEditor.Tests/Theming/palette-golden.approved.txt`

- [ ] **Step 1: Run `PaletteGoldenTests` and confirm it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PaletteGoldenTests"`

Expected: **FAIL** with `Palette golden mismatch. If intentional, copy
<...>palette-golden.received.txt over <...>palette-golden.approved.txt.` The
test writes `DialogEditor.Tests/Theming/palette-golden.received.txt`.

- [ ] **Step 2: Inspect the diff between received and approved**

Run: `git diff --no-index -- "DialogEditor.Tests/Theming/palette-golden.approved.txt" "DialogEditor.Tests/Theming/palette-golden.received.txt"`

Expected diff: each of the four `Palette.<Set>\tPalette.Mauve.500\t#FF<old>`
lines is replaced by a `Palette.<Set>\tPalette.Neutral.520\t#FF<new>` line
(possibly reordered within its set, since the golden file is sorted
ordinally by key and `Neutral.520` sorts differently than `Mauve.500`):

- `Palette.Dark`: `#FF7A6A8E` → `#FF838585`
- `Palette.Light`: `#FF7A6A8E` → `#FF838585`
- `Palette.Colourblind`: `#FF7A6A8E` → `#FF838585`
- `Palette.HighContrast`: `#FF332A47` → `#FF3A3A3A`

No other lines should differ. If anything else differs, stop and investigate
before proceeding.

- [ ] **Step 3: Copy the received file over the approved file**

Run: `Copy-Item "DialogEditor.Tests/Theming/palette-golden.received.txt" "DialogEditor.Tests/Theming/palette-golden.approved.txt" -Force`

- [ ] **Step 4: Run `PaletteGoldenTests` again and confirm it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PaletteGoldenTests"`

Expected: **PASS**. The test also deletes the now-stale
`palette-golden.received.txt` on success — confirm it no longer exists:

Run: `Test-Path "DialogEditor.Tests/Theming/palette-golden.received.txt"`

Expected: `False`

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Tests/Theming/palette-golden.approved.txt
git commit -m "test(theming): regenerate palette golden snapshot for Palette.Neutral.520"
```

---

### Task 4: Full suite run and `Gaps.md`/spec bookkeeping

**Files:**
- None to modify unless the full run surfaces a regression.

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test`

Expected: **all tests pass** (1331 baseline + no new/removed tests from this
plan — Tasks 1-3 only renamed existing entries, so the total count should be
unchanged at 1331).

- [ ] **Step 2: Manual sanity check (optional)**

Launch the app (`/run` or `dotnet run --project DialogEditor.Avalonia`), open
a conversation in each of the four themes (Settings → Theme), and confirm the
canvas backdrop behind the node cards is now neutral grey (not mauve/purple),
and that the pane GridSplitters remain visible against it in every theme.

- [ ] **Step 3: If everything passes, no further commit is needed**

Tasks 1-3 already committed all the necessary changes. This task is a
verification gate only.
