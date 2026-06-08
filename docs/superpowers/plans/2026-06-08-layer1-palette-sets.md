# Layer 1 Palette Sets Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add three alternate colour palettes (Light, High-Contrast, Colourblind-tuned) alongside the existing Dark set, as inert groundwork whose accessibility is enforced by tests, without changing the running app.

**Architecture:** Three new `Palette.<set>.axaml` primitive dictionaries beside a renamed `Palette.Dark.axaml` in `DialogEditor.Avalonia.Shared/Resources`. All four declare the identical `Palette.*` key set with different `<Color>` values. `Tokens.axaml` (the frozen semantic layer) is edited in exactly one place — the §3.3 dark-on-light "Ink" split. The new palettes are merged nowhere; runtime switching is Layer 2. Accessibility is enforced at the primitive/value level: structural parity, golden snapshots, and WCAG contrast ratios on the three new palettes (Dark grandfathered).

**Tech Stack:** Avalonia 11 XAML `ResourceDictionary`, C# / .NET 8, xUnit + `Avalonia.Headless.XUnit` (`[AvaloniaFact]`/`[AvaloniaTheory]`).

**Spec:** `docs/superpowers/specs/2026-06-08-layer1-palette-sets-design.md`

**Conventions:**
- Tests run **serially** (parallelization is disabled in this project) — fine to touch `Application.Current`.
- Run a single test class with: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.<ClassName>"`.
- Run the full suite with: `dotnet test DialogEditor.Tests`.
- Commit messages end with the `Co-Authored-By: Claude Opus 4.8 <noreply@anthropic.com>` trailer (see existing history).
- Work happens directly on `main`, consistent with this repo's solo workflow.

---

## Task 1: Rename Palette.axaml → Palette.Dark.axaml + widen the enforcer

**Files:**
- Rename: `DialogEditor.Avalonia.Shared/Resources/Palette.axaml` → `DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml`
- Modify: `DialogEditor.Avalonia/App.axaml:17`
- Modify: `DialogEditor.PatchManager/App.axaml:17`
- Modify: `DialogEditor.Tests/Theming/NoStrayHexTests.cs`

- [ ] **Step 1: Rename the file (preserves git history)**

```bash
git mv "DialogEditor.Avalonia.Shared/Resources/Palette.axaml" "DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml"
```

- [ ] **Step 2: Point the editor app at the renamed file**

In `DialogEditor.Avalonia/App.axaml`, change line 17 from:

```xml
<ResourceInclude Source="avares://DialogEditor.Avalonia.Shared/Resources/Palette.axaml"/>
```

to:

```xml
<ResourceInclude Source="avares://DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml"/>
```

- [ ] **Step 3: Point the PatchManager app at the renamed file**

In `DialogEditor.PatchManager/App.axaml`, make the identical change to line 17 (same old/new strings as Step 2).

- [ ] **Step 4: Widen the hex-allow rule in the enforcer**

In `DialogEditor.Tests/Theming/NoStrayHexTests.cs`:

(a) Update the class doc-comment phrase "hex primitives live ONLY in Palette.axaml" to "hex primitives live ONLY in the palette family (`Palette*.axaml`)".

(b) Add this field next to the existing `Hex` / `CSharpColour` regexes:

```csharp
    // The sanctioned hex tier is the whole palette family: Palette.Dark.axaml plus the Layer 1
    // alternates (Palette.Light/HighContrast/Colourblind.axaml). Any other filename with hex fails.
    private static readonly Regex PaletteFile =
        new(@"^Palette(\.[A-Za-z]+)?\.axaml$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
```

(c) In `NoHexLiteralsOutsidePalette`, replace:

```csharp
            if (file.EndsWith("Palette.axaml", StringComparison.OrdinalIgnoreCase)) continue;
```

with:

```csharp
            if (PaletteFile.IsMatch(Path.GetFileName(file))) continue;
```

- [ ] **Step 5: Verify the whole suite is still green (pure rename, zero value change)**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — every test, including `NoStrayHexTests`, `PaletteRegistryTests`, `TokenRegistryTests`. No colour value changed, so nothing should regress.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(theming): rename Palette.axaml to Palette.Dark.axaml; widen hex enforcer to palette family"
```

---

## Task 2: Dark-on-light text split (the one Tokens edit, §3.3)

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.Dark.axaml`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Tokens.axaml:82-83`
- Modify: `DialogEditor.Tests/Theming/PaletteRegistryTests.cs`
- Modify: `DialogEditor.Tests/Theming/TokenRegistryTests.cs`

- [ ] **Step 1: Write the failing golden rows for the new primitives + re-pointed tokens**

In `PaletteRegistryTests.cs`, add two `[InlineData]` rows to `PrimitiveResolvesToExpectedColor`:

```csharp
    [InlineData("Palette.Ink.Strong", 0xFF, 0x33, 0x33, 0x33)]
    [InlineData("Palette.Ink.Muted",  0xFF, 0x66, 0x66, 0x66)]
```

In `TokenRegistryTests.cs`, add two `[InlineData]` rows to `TokenResolvesToExpectedColor` (locks that the re-point keeps the Dark colour identical):

```csharp
    [InlineData("Brush.Text.OnLight",       0xFF, 0x33, 0x33, 0x33)] // now -> Palette.Ink.Strong
    [InlineData("Brush.Text.OnLight.Muted", 0xFF, 0x66, 0x66, 0x66)] // now -> Palette.Ink.Muted
```

- [ ] **Step 2: Run the new rows to verify they fail**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteRegistryTests"`
Expected: FAIL — `Palette.Ink.Strong` / `Palette.Ink.Muted` are "not defined".

- [ ] **Step 3: Add the two Ink primitives to the Dark palette**

In `Palette.Dark.axaml`, immediately after the neutral ramp block (after `Palette.White`), add:

```xml
    <!--  INK — dedicated dark-on-light text primitives (spec §3.3). Kept on their OWN keys (not
          Neutral.200/400) so the always-light node-card body text can stay dark while Neutral.200/400
          re-value for surface/border/disabled roles in the Light & High-Contrast palettes. Dark values
          equal the former Neutral.200/400 so this layer changes nothing on screen. Ink is a named
          role-primitive (defined by use, dark-on-light), not a numeric tone in the neutral ramp. -->
    <Color x:Key="Palette.Ink.Strong">#FF333333</Color>
    <Color x:Key="Palette.Ink.Muted">#FF666666</Color>
```

- [ ] **Step 4: Re-point the two OnLight tokens**

In `Tokens.axaml`, change lines 82-83 from:

```xml
    <SolidColorBrush x:Key="Brush.Text.OnLight"       Color="{StaticResource Palette.Neutral.200}"/>
    <SolidColorBrush x:Key="Brush.Text.OnLight.Muted" Color="{StaticResource Palette.Neutral.400}"/>
```

to:

```xml
    <SolidColorBrush x:Key="Brush.Text.OnLight"       Color="{StaticResource Palette.Ink.Strong}"/>
    <SolidColorBrush x:Key="Brush.Text.OnLight.Muted" Color="{StaticResource Palette.Ink.Muted}"/>
```

(Leave the existing explanatory comment above these lines; append " Re-pointed to Palette.Ink.* per Layer 1 spec §3.3." to it.)

- [ ] **Step 5: Run the suite to verify green**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — the new golden rows pass, and every other test (including `EveryDeclaredTokenResolves`, which already lists `Brush.Text.OnLight*`) stays green because the resolved colours are unchanged.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "refactor(theming): split dark-on-light text onto Palette.Ink.* primitives (Dark unchanged)"
```

---

## Task 3: WCAG + palette-loading test helpers

**Files:**
- Create: `DialogEditor.Tests/Theming/Wcag.cs`
- Create: `DialogEditor.Tests/Theming/PaletteHarness.cs`
- Create: `DialogEditor.Tests/Theming/PaletteHarnessTests.cs`

- [ ] **Step 1: Write the helper self-tests (failing)**

Create `DialogEditor.Tests/Theming/PaletteHarnessTests.cs`:

```csharp
using Avalonia.Media;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

public class PaletteHarnessTests
{
    [AvaloniaFact]
    public void WhiteOnBlackIsMaxContrast()
        => Assert.Equal(21.0, Wcag.ContrastRatio(Colors.White, Colors.Black), 1);

    [AvaloniaFact]
    public void SameColourIsMinContrast()
        => Assert.Equal(1.0, Wcag.ContrastRatio(Colors.White, Colors.White), 3);

    [AvaloniaFact]
    public void DarkPaletteLoadsAndResolvesAKnownPrimitive()
    {
        var dark = PaletteHarness.Load("Palette.Dark");
        Assert.Equal(Color.FromArgb(0xFF, 0x14, 0x14, 0x14),
                     PaletteHarness.Color(dark, "Palette.Neutral.80"));
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteHarnessTests"`
Expected: FAIL — `Wcag` and `PaletteHarness` do not exist (compile error).

- [ ] **Step 3: Implement the WCAG helper**

Create `DialogEditor.Tests/Theming/Wcag.cs`:

```csharp
using System;
using Avalonia.Media;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// WCAG 2.x relative-luminance contrast ratio. Used to enforce the Layer 1 accessibility
/// targets at the colour-value level (spec §5). Alpha is ignored — the curated contrast
/// pairs are all opaque tokens.
/// </summary>
internal static class Wcag
{
    public static double ContrastRatio(Color a, Color b)
    {
        var la = Luminance(a);
        var lb = Luminance(b);
        var hi = Math.Max(la, lb);
        var lo = Math.Min(la, lb);
        return (hi + 0.05) / (lo + 0.05);
    }

    private static double Luminance(Color c)
        => 0.2126 * Channel(c.R) + 0.7152 * Channel(c.G) + 0.0722 * Channel(c.B);

    private static double Channel(byte v)
    {
        var s = v / 255.0;
        return s <= 0.03928 ? s / 12.92 : Math.Pow((s + 0.055) / 1.055, 2.4);
    }
}
```

- [ ] **Step 4: Implement the palette-loading harness**

Create `DialogEditor.Tests/Theming/PaletteHarness.cs`:

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Media;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Loads a single Palette.&lt;set&gt;.axaml as an isolated ResourceDictionary and reads its
/// Color primitives. Palette files contain only &lt;Color&gt;/&lt;BoxShadows&gt; (no StaticResource),
/// so they load standalone with no need for Tokens.axaml. Layer 1 tests run entirely at the
/// primitive level (spec §5). Data-driven over AllSets so future palettes are one list entry.
/// </summary>
internal static class PaletteHarness
{
    private const string ResDir = "avares://DialogEditor.Avalonia.Shared/Resources/";

    // The default first; the three Layer 1 alternates follow. A new palette = one entry here.
    public static readonly string[] AllSets =
        { "Palette.Dark", "Palette.Light", "Palette.HighContrast", "Palette.Colourblind" };

    // The sets the accessibility contract is enforced on (Dark is the grandfathered baseline, §5).
    public static readonly string[] EnforcedSets =
        { "Palette.Light", "Palette.HighContrast", "Palette.Colourblind" };

    public static ResourceDictionary Load(string set)
        => (ResourceDictionary)AvaloniaXamlLoader.Load(new Uri(ResDir + set + ".axaml"));

    public static Color Color(ResourceDictionary dict, string key)
    {
        Assert.True(dict.TryGetResource(key, null, out var value),
            $"Palette key '{key}' is not defined");
        return Assert.IsType<Color>(value);
    }
}
```

- [ ] **Step 5: Run to verify the self-tests pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteHarnessTests"`
Expected: PASS (3 tests).

> If `AvaloniaXamlLoader.Load` throws because the runtime isn't initialised, confirm the test
> uses `[AvaloniaFact]` (not `[Fact]`) — that attribute boots the headless app. If `TryGetResource`
> has a different overload in this Avalonia version, use `dict.TryGetValue(key, out var value)` from
> `IDictionary<object, object?>` instead; the rest is unchanged.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "test(theming): add WCAG contrast + palette-loading test helpers"
```

---

## Task 4: Structural parity test + create the three skeleton palettes

**Files:**
- Create: `DialogEditor.Tests/Theming/PaletteSetParityTests.cs`
- Create: `DialogEditor.Avalonia.Shared/Resources/Palette.Light.axaml`
- Create: `DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml`
- Create: `DialogEditor.Avalonia.Shared/Resources/Palette.Colourblind.axaml`

- [ ] **Step 1: Write the failing parity test**

Create `DialogEditor.Tests/Theming/PaletteSetParityTests.cs`:

```csharp
using System.Linq;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

public class PaletteSetParityTests
{
    [AvaloniaTheory]
    [InlineData("Palette.Light")]
    [InlineData("Palette.HighContrast")]
    [InlineData("Palette.Colourblind")]
    public void AlternatePaletteHasExactlySameKeysAsDark(string set)
    {
        var dark = PaletteHarness.Load("Palette.Dark").Keys.Cast<string>().ToHashSet();
        var alt = PaletteHarness.Load(set).Keys.Cast<string>().ToHashSet();

        Assert.True(dark.SetEquals(alt),
            $"{set} key set differs from Dark.\n" +
            $"Missing from {set}: {string.Join(", ", dark.Except(alt).OrderBy(k => k))}\n" +
            $"Extra in {set}: {string.Join(", ", alt.Except(dark).OrderBy(k => k))}");
    }
}
```

- [ ] **Step 2: Run to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteSetParityTests"`
Expected: FAIL — `AvaloniaXamlLoader.Load` throws for the three missing files.

- [ ] **Step 3: Create the three skeletons as verbatim copies of Dark**

Copy the renamed Dark file to each new palette file (identical keys and values for now; the authoring tasks revise the values). Run from the repo root (PowerShell `Copy-Item`, no directory change):

```powershell
$res = "DialogEditor.Avalonia.Shared/Resources"
Copy-Item "$res/Palette.Dark.axaml" "$res/Palette.Light.axaml"
Copy-Item "$res/Palette.Dark.axaml" "$res/Palette.HighContrast.axaml"
Copy-Item "$res/Palette.Dark.axaml" "$res/Palette.Colourblind.axaml"
```

Then, at the top of each new file, replace the Dark header comment's first line with a one-line marker so the file's identity is clear (full rationale headers are added in Task 8), e.g. for `Palette.Light.axaml`:

```xml
<!--  PALETTE — LIGHT set (Layer 1). Same keys as Palette.Dark.axaml, light-theme values.
      Skeleton = Dark values until Task (Light authoring) fills them. See spec §4.1. -->
```

(Do the analogous one-liner for HighContrast → §4.2 and Colourblind → §4.3.)

- [ ] **Step 4: Run to verify parity passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteSetParityTests"`
Expected: PASS (3 cases) — the copies have an identical key set.

- [ ] **Step 5: Confirm the enforcer still accepts the new files**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.NoStrayHexTests"`
Expected: PASS — the `Palette(\.[A-Za-z]+)?\.axaml` rule from Task 1 sanctions the three new files' hex.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "test(theming): structural parity test + Light/HighContrast/Colourblind skeleton palettes"
```

---

## Task 5: Golden snapshot (approval) test across all palettes

**Files:**
- Create: `DialogEditor.Tests/Theming/PaletteGoldenTests.cs`
- Create: `DialogEditor.Tests/Theming/palette-golden.approved.txt`

This locks every primitive value in every palette. It is updated deliberately whenever a palette's values change (Tasks 6-8), so accidental drift fails the build.

- [ ] **Step 1: Write the golden test (failing — no approved file yet)**

Create `DialogEditor.Tests/Theming/PaletteGoldenTests.cs`:

```csharp
using System;
using System.IO;
using System.Linq;
using System.Text;
using Avalonia.Media;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Approval-style golden lock: serialises every palette's every Color primitive to a sorted
/// text block and compares it to a committed file. When a palette's values change intentionally,
/// copy the generated .received.txt over .approved.txt in the same commit. Locks all Layer 1
/// values, including the Okabe-Ito colourblind remaps, so they cannot silently drift (spec §5).
/// </summary>
public class PaletteGoldenTests
{
    private static string Render()
    {
        var sb = new StringBuilder();
        foreach (var set in PaletteHarness.AllSets)
        {
            var dict = PaletteHarness.Load(set);
            foreach (var key in dict.Keys.Cast<string>().OrderBy(k => k, StringComparer.Ordinal))
            {
                if (dict.TryGetResource(key, null, out var v) && v is Color c)
                    sb.Append(set).Append('\t').Append(key).Append('\t')
                      .AppendFormat("#{0:X2}{1:X2}{2:X2}{3:X2}", c.A, c.R, c.G, c.B).Append('\n');
            }
        }
        return sb.ToString();
    }

    private static string Dir()
    {
        var d = new DirectoryInfo(AppContext.BaseDirectory);
        while (d is not null && !File.Exists(Path.Combine(d.FullName, "DialogEditor.slnx")))
            d = d.Parent;
        Assert.NotNull(d);
        return Path.Combine(d!.FullName, "DialogEditor.Tests", "Theming");
    }

    [AvaloniaFact]
    public void AllPaletteValuesMatchApproved()
    {
        var actual = Render();
        var approvedPath = Path.Combine(Dir(), "palette-golden.approved.txt");
        var receivedPath = Path.Combine(Dir(), "palette-golden.received.txt");

        var approved = File.Exists(approvedPath)
            ? File.ReadAllText(approvedPath).Replace("\r\n", "\n")
            : "";

        if (actual != approved)
        {
            File.WriteAllText(receivedPath, actual);
            Assert.Fail($"Palette golden mismatch. If intentional, copy {receivedPath} over {approvedPath}.");
        }
        else if (File.Exists(receivedPath))
        {
            File.Delete(receivedPath);
        }
    }
}
```

- [ ] **Step 2: Run to verify it fails and emits the received file**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"`
Expected: FAIL — no approved file yet; `palette-golden.received.txt` is written.

- [ ] **Step 3: Approve the current (skeleton) snapshot**

```bash
cp "DialogEditor.Tests/Theming/palette-golden.received.txt" "DialogEditor.Tests/Theming/palette-golden.approved.txt"
```

- [ ] **Step 4: Run to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"`
Expected: PASS — actual equals approved (all four palettes currently carry Dark values).

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "test(theming): golden snapshot lock for all palette values"
```

---

## Task 6: Author the Light palette + its contrast gate

**Files:**
- Create: `DialogEditor.Tests/Theming/PaletteContrastTests.cs`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.Light.axaml`
- Modify: `DialogEditor.Tests/Theming/palette-golden.approved.txt`

- [ ] **Step 1: Write the contrast test, enabling it for Light only**

Create `DialogEditor.Tests/Theming/PaletteContrastTests.cs`. The pairs are expressed as primitive
tuples (the token each backs is in the comment), per spec §5.1. `true` = normal-text bar, `false` =
large/UI bar.

```csharp
using Avalonia.Media;
using Avalonia.Headless.XUnit;

namespace DialogEditor.Tests.Theming;

public class PaletteContrastTests
{
    private readonly record struct Pair(string Label, string Fg, string Bg, bool NormalText);

    // Curated load-bearing role pairs (spec §5.1). Primitive keys; backing token in the label.
    private static readonly Pair[] Pairs =
    {
        new("Text.Primary / Surface.Window",  "Palette.Neutral.910", "Palette.Neutral.115", true),
        new("Text.Primary / Surface.Panel",   "Palette.Neutral.910", "Palette.Neutral.145", true),
        new("Text.Primary / Surface.Card",    "Palette.Neutral.910", "Palette.Neutral.100", true),
        new("Text.Primary / Surface.Input",   "Palette.Neutral.910", "Palette.Neutral.80",  true),
        new("Text.Secondary / Surface.Panel", "Palette.Neutral.800", "Palette.Neutral.145", true),
        new("Text.Secondary / Surface.Card",  "Palette.Neutral.800", "Palette.Neutral.100", true),
        new("Text.OnLight / Node.Npc.Body",      "Palette.Ink.Strong", "Palette.Parchment.100", true),
        new("Text.OnLight / Node.Player.Body",   "Palette.Ink.Strong", "Palette.Azure.150",     true),
        new("Text.OnLight / Node.Narrator.Body", "Palette.Ink.Strong", "Palette.Teal.150",      true),
        new("Text.OnAccent / Node.Npc.Header",      "Palette.White", "Palette.Crimson.700", true),
        new("Text.OnAccent / Node.Player.Header",   "Palette.White", "Palette.Azure.600",   true),
        new("Text.OnAccent / Node.Narrator.Header", "Palette.White", "Palette.Teal.600",    true),
        new("Text.OnAccent / Node.Script.Header",   "Palette.White", "Palette.Slate.700",   true),
        new("Text.OnAccent / Button.Confirm",     "Palette.White", "Palette.Green.600",  true),
        new("Text.OnAccent / Button.Caution",     "Palette.White", "Palette.Burnt.600",  true),
        new("Text.Status.Added / Surface.Card",   "Palette.Green.400", "Palette.Neutral.100", true),
        new("Text.Status.Changed / Surface.Card", "Palette.Amber.540", "Palette.Neutral.100", true),
        new("Text.Status.Removed / Surface.Card", "Palette.Red.450",   "Palette.Neutral.100", true),
        new("Text.Caption / Surface.Card",  "Palette.Neutral.600", "Palette.Neutral.100", false),
        new("Text.Muted / Surface.Card",    "Palette.Neutral.535", "Palette.Neutral.100", false),
        new("Severity.Warning / Surface.Panel", "Palette.Amber.500", "Palette.Neutral.145", false),
        new("Severity.Error / Surface.Panel",   "Palette.Red.500",   "Palette.Neutral.145", false),
        new("Severity.Info / Surface.Panel",    "Palette.Sky.450",   "Palette.Neutral.145", false),
    };

    // Dark is exempt (grandfathered baseline, §5). Thresholds: {normalText, largeUI}.
    [AvaloniaTheory]
    [InlineData("Palette.Light", 4.5, 3.0)]
    public void PaletteMeetsContrastTargets(string set, double normalMin, double largeMin)
    {
        var dict = PaletteHarness.Load(set);
        var failures = new System.Collections.Generic.List<string>();
        foreach (var p in Pairs)
        {
            var ratio = Wcag.ContrastRatio(
                PaletteHarness.Color(dict, p.Fg), PaletteHarness.Color(dict, p.Bg));
            var min = p.NormalText ? normalMin : largeMin;
            if (ratio < min)
                failures.Add($"{p.Label}: {ratio:F2}:1 < {min:F1}:1");
        }
        Assert.True(failures.Count == 0,
            $"{set} contrast failures:\n" + string.Join("\n", failures));
    }
}
```

- [ ] **Step 2: Run to verify Light fails (skeleton still carries Dark values)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteContrastTests"`
Expected: FAIL for `Palette.Light` — the skeleton still carries Dark values, and a few of those sit under AA (e.g. `Severity.Error / Surface.Panel` ≈ 2.8:1 < 3.0). The failure list names each failing pair. (Most pairs pass even on Dark values; authoring Step 3 must keep them passing while making the theme actually light.)

- [ ] **Step 3: Author the Light palette values (§4.1)**

Edit `Palette.Light.axaml`. Apply the §4.1 strategy: **invert the neutral ramp by slot** and adjust
accents for legibility on light surfaces. Keep `Ink.*` dark (cards stay light). Worked anchors
(set these, then tune the rest until Step 4 is green):

```xml
<!-- neutrals: invert by slot identity -->
<Color x:Key="Palette.Neutral.115">#FFF4F4F2</Color> <!-- window: was #1e1e1e -->
<Color x:Key="Palette.Neutral.145">#FFECECEA</Color> <!-- panel -->
<Color x:Key="Palette.Neutral.100">#FFFFFFFF</Color> <!-- card -->
<Color x:Key="Palette.Neutral.80">#FFFAFAFA</Color>  <!-- input -->
<Color x:Key="Palette.Neutral.910">#FF1A1A1A</Color> <!-- primary text: was #e8e8e8 -->
<Color x:Key="Palette.Neutral.800">#FF333333</Color> <!-- secondary text -->
<Color x:Key="Palette.Neutral.600">#FF595959</Color> <!-- caption -->
<Color x:Key="Palette.Neutral.535">#FF6B6B6B</Color> <!-- muted -->
<!-- Ink stays dark (cards remain light cards) -->
<Color x:Key="Palette.Ink.Strong">#FF2A2A2A</Color>
<Color x:Key="Palette.Ink.Muted">#FF595959</Color>
<!-- accents: keep hue, deepen for light bg -->
<Color x:Key="Palette.Green.400">#FF1F7A3D</Color>  <!-- status added text -->
<Color x:Key="Palette.Amber.540">#FFB3700A</Color>  <!-- status changed text -->
<Color x:Key="Palette.Red.450">#FFB3261E</Color>    <!-- status removed text -->
```

The full ramp must be authored — every primitive gets its light value. The contrast test is the
objective gate; the visual companion (`http://localhost:58447`, or relaunch per the spec's brainstorm
notes) can be used to eyeball a node card. Node header fills (Crimson.700/Azure.600/Teal.600/Slate.700)
stay saturated so white header text still passes — only darken them if a header pair fails Step 4.

- [ ] **Step 4: Run the contrast test until Light passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteContrastTests"`
Expected: PASS — iterate values until zero failures. Then confirm parity still holds:
Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteSetParityTests"`
Expected: PASS (you changed only values, not keys).

- [ ] **Step 5: Update the golden snapshot**

Run the golden test to emit the new received file, then approve it:

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
cp "DialogEditor.Tests/Theming/palette-golden.received.txt" "DialogEditor.Tests/Theming/palette-golden.approved.txt"
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
```
Expected: second run PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(theming): author Light palette (WCAG AA enforced)"
```

---

## Task 7: Author the High-Contrast palette + AAA gate

**Files:**
- Modify: `DialogEditor.Tests/Theming/PaletteContrastTests.cs`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.HighContrast.axaml`
- Modify: `DialogEditor.Tests/Theming/palette-golden.approved.txt`

- [ ] **Step 1: Add the HighContrast case to the contrast test (AAA)**

In `PaletteContrastTests.cs`, add a second `[InlineData]` above the existing Light row:

```csharp
    [InlineData("Palette.HighContrast", 7.0, 4.5)]
```

- [ ] **Step 2: Run to verify HighContrast fails (skeleton = Dark values, not AAA)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteContrastTests"`
Expected: FAIL for `Palette.HighContrast` — Dark values do not meet 7:1 on most pairs.

- [ ] **Step 3: Author the High-Contrast values (§4.2)**

Edit `Palette.HighContrast.axaml`. Apply §4.2: surfaces collapse to near-black, text to white, accents
fully saturated/bright, each enforced pair ≥ 7:1 (≥ 4.5:1 for the large/UI pairs). Node cards stay
light cards with near-black ink (so `Text.OnLight` on the body clears AAA). Worked anchors:

```xml
<Color x:Key="Palette.Neutral.115">#FF000000</Color> <!-- window -->
<Color x:Key="Palette.Neutral.145">#FF000000</Color> <!-- panel -->
<Color x:Key="Palette.Neutral.100">#FF000000</Color> <!-- card surface -->
<Color x:Key="Palette.Neutral.80">#FF000000</Color>  <!-- input -->
<Color x:Key="Palette.Neutral.910">#FFFFFFFF</Color> <!-- primary text -->
<Color x:Key="Palette.Ink.Strong">#FF000000</Color>  <!-- body text on light card -->
<Color x:Key="Palette.Parchment.100">#FFFFFFFF</Color> <!-- npc card body -> white -->
<Color x:Key="Palette.Azure.150">#FFFFFFFF</Color>     <!-- player card body -->
<Color x:Key="Palette.Teal.150">#FFFFFFFF</Color>      <!-- narrator card body -->
<Color x:Key="Palette.Green.400">#FF5BFF8F</Color>  <!-- status added text -->
<Color x:Key="Palette.Amber.540">#FFFFD23B</Color>  <!-- status changed text -->
<Color x:Key="Palette.Red.450">#FFFF6B6B</Color>    <!-- status removed text -->
```

Header fills must stay dark enough for white text at 7:1 (the Dark crimson/azure/teal/slate already
clear ~7:1 or more — verify in Step 4 and deepen any that miss). Author every primitive; the test is
the gate.

- [ ] **Step 4: Run the contrast test until HighContrast (and Light) pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteContrastTests"`
Expected: PASS (both `Palette.Light` and `Palette.HighContrast`). Then:
Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteSetParityTests"`
Expected: PASS.

- [ ] **Step 5: Update the golden snapshot**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
cp "DialogEditor.Tests/Theming/palette-golden.received.txt" "DialogEditor.Tests/Theming/palette-golden.approved.txt"
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
```
Expected: second run PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(theming): author High-Contrast palette (WCAG AAA enforced)"
```

---

## Task 8: Author the Colourblind palette + AA gate

**Files:**
- Modify: `DialogEditor.Tests/Theming/PaletteContrastTests.cs`
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.Colourblind.axaml`
- Modify: `DialogEditor.Tests/Theming/palette-golden.approved.txt`

- [ ] **Step 1: Add the Colourblind case to the contrast test (AA)**

In `PaletteContrastTests.cs`, add:

```csharp
    [InlineData("Palette.Colourblind", 4.5, 3.0)]
```

- [ ] **Step 2: Run to verify it fails where Dark is sub-AA**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteContrastTests"`
Expected: FAIL for `Palette.Colourblind` on the status/severity pairs Dark sits under AA on (e.g. `Severity.Error / Surface.Panel` ≈ 2.8:1).

- [ ] **Step 3: Author the Colourblind values (§4.3 — Okabe–Ito remap)**

Edit `Palette.Colourblind.axaml`. Start from Dark (it already is a copy). Remap **only** the primitives
that back the red↔green-collision roles onto Okabe–Ito, choosing a lighter tint for text-role
primitives (so they clear AA on dark surfaces) and a deeper tone for fills. Leave everything else
(neutrals, surfaces, node headers, `Conflict.*`, `Button.Confirm` green, syntax/bark ambers) as Dark.

Remap these primitives:

```xml
<!-- added/success (green family used for status/diff) -> Okabe-Ito blues -->
<Color x:Key="Palette.Green.400">#FF56B4E9</Color>  <!-- status added/new text (sky blue, AA on dark) -->
<Color x:Key="Palette.Green.500">#FF56B4E9</Color>  <!-- inline success text -->
<Color x:Key="Palette.Green.550">#FF0072B2</Color>  <!-- diff added canvas fill (deep blue) -->
<!-- changed/warning (amber family used for status/severity) -> Okabe-Ito orange -->
<Color x:Key="Palette.Amber.500">#FFE69F00</Color>  <!-- severity warning -->
<Color x:Key="Palette.Amber.540">#FFE69F00</Color>  <!-- status changed text -->
<Color x:Key="Palette.Amber.600">#FFE69F00</Color>  <!-- diff changed fill + commit-meta gold -->
<!-- removed/error (red/maroon family used for status/severity/diff) -> Okabe-Ito vermillion -->
<Color x:Key="Palette.Red.450">#FFFF7A45</Color>    <!-- status removed text (lightened for AA) -->
<Color x:Key="Palette.Red.500">#FFD55E00</Color>    <!-- severity error -->
<Color x:Key="Palette.Red.550">#FFFF7A45</Color>    <!-- inline error text -->
<Color x:Key="Palette.Maroon.800">#FFD55E00</Color> <!-- diff removed canvas fill -->
```

Do **not** remap: `Palette.Green.600` (Confirm button), `Palette.Crimson.700` (NPC header /
destructive), `Palette.Red.300` / `Palette.Maroon.900` (Conflict "theirs", already CVD-safe),
`Palette.Amber.520/550/560/570/900/950` (bark / syntax / highlight). Note in a comment that remapping
a shared primitive shifts every role using it (e.g. `Amber.600` also tints the commit-meta gold) —
this is accepted; those roles are not meaning-by-hue.

- [ ] **Step 4: Run the contrast test until all three new palettes pass**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteContrastTests"`
Expected: PASS (Light, HighContrast, Colourblind). Adjust any remapped tint that misses AA.

- [ ] **Step 5: Update the golden snapshot**

```bash
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
cp "DialogEditor.Tests/Theming/palette-golden.received.txt" "DialogEditor.Tests/Theming/palette-golden.approved.txt"
dotnet test DialogEditor.Tests --filter "FullyQualifiedName~Theming.PaletteGoldenTests"
```
Expected: second run PASS.

- [ ] **Step 6: Commit**

```bash
git add -A
git commit -m "feat(theming): author Colourblind palette (Okabe-Ito remap, WCAG AA enforced)"
```

---

## Task 9: Rationale headers + doc updates

**Files:**
- Modify: `DialogEditor.Avalonia.Shared/Resources/Palette.Light.axaml`, `Palette.HighContrast.axaml`, `Palette.Colourblind.axaml` (headers)
- Modify: `docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md` (§3.2)
- Modify: `Gaps.md`

- [ ] **Step 1: Add full rationale headers to each new palette file**

Replace each new palette's one-line marker with a full header comment stating: the set's purpose, its
§4.x accessibility aim, that keys mirror `Palette.Dark.axaml` (enforced by `PaletteSetParityTests`),
that values are locked by `PaletteGoldenTests` and gated by `PaletteContrastTests`, and a pointer to
`docs/superpowers/specs/2026-06-08-layer1-palette-sets-design.md`. (Per the project's "reasoning lives
in the files" convention.) For Colourblind, also note the Okabe–Ito remap and that node headers /
`Conflict.*` are intentionally left as Dark.

- [ ] **Step 2: Revise Layer 0 spec §3.2**

In `docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md` §3.2, add a note that under
Layer 1 the `Neutral.<n>` numbers denote **slot identity**, not absolute lightness — the lightness
ordering inverts for the Light palette — cross-referencing the Layer 1 spec §3.1. Do not delete the
original reasoning; append the revision.

- [ ] **Step 3: Update Gaps.md**

In `Gaps.md`, under *Centralised UI Colour Tokens*:
- Update the **Layer 1** bullet: mark it designed + planned, pointing at
  `docs/superpowers/specs/2026-06-08-layer1-palette-sets-design.md` and this plan.
- Fix the Layer 0 wording "`Palette.axaml` (the only file permitted a hex literal)" to "the palette
  family (`Palette*.axaml`)".
- Reflect the `Palette.axaml` → `Palette.Dark.axaml` rename and the `Palette.Ink.*` split where Layer 0
  text names the file.

- [ ] **Step 4: Run the full suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — all theming tests (parity, golden, contrast, registry, no-stray-hex) green.

- [ ] **Step 5: Commit**

```bash
git add -A
git commit -m "docs(theming): Layer 1 palette rationale headers + Gaps/taxonomy updates"
```

---

## Done criteria

- `Palette.Dark/Light/HighContrast/Colourblind.axaml` exist with an identical key set (parity green).
- `Tokens.axaml` changed in exactly one place (the `Ink` re-point); Dark renders byte-identical.
- `PaletteContrastTests` enforces AA on Light/Colourblind and AAA on High-Contrast; Dark exempt.
- `PaletteGoldenTests` locks every value; `NoStrayHexTests` sanctions the whole palette family and still forbids hex everywhere else.
- The three new palettes are merged nowhere — the running app is unchanged. Runtime selection is Layer 2.
