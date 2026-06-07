# Centralised UI Colour Tokens — Layer 0 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace every hardcoded colour in the Avalonia app with a two-tier named-token registry (`Palette.*` primitives → `Brush.*` semantics), so colour has a single source of truth and the drift bug is impossible by construction.

**Architecture:** Two merged `ResourceDictionary` files — `Palette.axaml` (`<Color>`/effect primitives; the *only* place a hex literal may live) and `Tokens.axaml` (`<SolidColorBrush>` semantics referencing primitives via `{StaticResource}`). XAML consumers bind `{DynamicResource Brush.*}`; the 9 brush converters resolve keys through a `TokenBrushes.Resolve` helper instead of constructing brushes. A no-stray-hex test is the migration's definition-of-done.

**Tech Stack:** C# / .NET 8, Avalonia 11, `Avalonia.Headless.XUnit` for tests, xUnit (serial — `DisableTestParallelization`).

**Spec:** `docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md` — its **Appendix A** is the per-line migration source of truth; **§5/§7** are the primitive/token registries reproduced below.

---

## File Structure

**Create:**
- `DialogEditor.Avalonia/Resources/Palette.axaml` — primitive `<Color>` registry + effect primitives (BoxShadow, alpha scrims). Only file with hex.
- `DialogEditor.Avalonia/Resources/Tokens.axaml` — semantic `<SolidColorBrush>` registry. The public contract.
- `DialogEditor.Avalonia/Theming/TokenBrushes.cs` — `Resolve(string key)` helper for converters.
- `DialogEditor.Tests/Theming/PaletteRegistryTests.cs` — golden values + no-dangling.
- `DialogEditor.Tests/Theming/TokenRegistryTests.cs` — every `Brush.*` resolves; golden brush values.
- `DialogEditor.Tests/Theming/ConverterTokenTests.cs` — converters return the registry brush.
- `DialogEditor.Tests/Theming/NoStrayHexTests.cs` — the contract enforcer (added last).

**Modify:**
- `DialogEditor.Avalonia/App.axaml` — merge the two dictionaries; rewrite the two toolbar control themes to `DynamicResource`.
- The 9 brush converters / controls (Appendix B of the spec).
- 29 `.axaml` view files (Appendix A of the spec).

**Conventions to copy from the codebase:**
- Resource includes follow the existing `App.axaml` pattern: `<ResourceInclude Source="avares://DialogEditor.Avalonia/Resources/Strings.axaml"/>`.
- Tests use `[AvaloniaFact]`/`[AvaloniaTheory]`; inside them `Application.Current` is the real `App` with resources loaded.
- Resource resolution in tests: `Application.Current!.TryGetResource("<key>", Application.Current!.ActualThemeVariant, out var v)`.

---

## Task 1: Palette.axaml — primitive colour registry

**Files:**
- Create: `DialogEditor.Avalonia/Resources/Palette.axaml`
- Test: `DialogEditor.Tests/Theming/PaletteRegistryTests.cs`

- [ ] **Step 1: Write the failing test**

`DialogEditor.Tests/Theming/PaletteRegistryTests.cs`:

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace DialogEditor.Tests.Theming;

public class PaletteRegistryTests
{
    private static Color Resolve(string key)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, Application.Current!.ActualThemeVariant, out var v),
            $"Palette key '{key}' is not defined");
        return ((Color)v!);
    }

    [AvaloniaTheory]
    [InlineData("Palette.Neutral.80",  0xFF, 0x14, 0x14, 0x14)]
    [InlineData("Palette.Neutral.145", 0xFF, 0x25, 0x25, 0x25)]
    [InlineData("Palette.Neutral.200", 0xFF, 0x33, 0x33, 0x33)]
    [InlineData("Palette.Neutral.535", 0xFF, 0x88, 0x88, 0x88)]
    [InlineData("Palette.Neutral.910", 0xFF, 0xe8, 0xe8, 0xe8)]
    [InlineData("Palette.Crimson.700", 0xFF, 0x7b, 0x24, 0x1c)]
    [InlineData("Palette.Azure.600",   0xFF, 0x1a, 0x52, 0x76)]
    [InlineData("Palette.Green.550",   0xFF, 0x3a, 0x7a, 0x3a)]
    [InlineData("Palette.Amber.600",   0xFF, 0xc0, 0x8a, 0x2a)]
    [InlineData("Palette.Maroon.800",  0xFF, 0x7a, 0x2a, 0x2a)]
    [InlineData("Palette.Sky.250",     0xFF, 0x9c, 0xdc, 0xfe)]
    [InlineData("Palette.Sky.300",     0xFF, 0x9c, 0xc4, 0xff)]
    [InlineData("Palette.Cream.100",   0xFF, 0xff, 0xf8, 0xdc)]
    [InlineData("Palette.Amber.570",   0xFF, 0xe8, 0xd0, 0x80)] // bark footer, distinct from Amber.560
    [InlineData("Palette.Alpha.Scrim", 0xBB, 0x00, 0x00, 0x00)]
    public void PrimitiveResolvesToExpectedColor(string key, byte a, byte r, byte g, byte b)
        => Assert.Equal(Color.FromArgb(a, r, g, b), Resolve(key));

    [AvaloniaFact]
    public void AbsorbedAmber530_DoesNotExist()
        => Assert.False(
            Application.Current!.TryGetResource("Palette.Amber.530",
                Application.Current!.ActualThemeVariant, out _),
            "Amber.530 (#e0a030) was absorbed into Amber.540 per spec §6/§8 and must not be defined");
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PaletteRegistryTests"`
Expected: FAIL — `Palette key 'Palette.Neutral.80' is not defined` (file does not exist / not merged yet).

- [ ] **Step 3: Create `Palette.axaml`**

`DialogEditor.Avalonia/Resources/Palette.axaml` — full content (header comment explains the *why* per the standing requirement):

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--  PALETTE — primitive colour ramp (Layer 0, private tier).
          The ONLY file in the app permitted to contain a hex literal; enforced by
          DialogEditor.Tests/Theming/NoStrayHexTests. Nothing outside the registry
          references Palette.*; views/converters bind the semantic Brush.* keys in
          Tokens.axaml instead. Neutral tone numbers are an ABSTRACT 0..1000 lightness
          rank, not the hex byte, so a future palette (Layer 1) can re-value a slot
          without the name becoming a lie. See
          docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md §3.2, §5. -->

    <!-- Neutrals (grayscale ramp) -->
    <Color x:Key="Palette.Neutral.80">#FF141414</Color>
    <Color x:Key="Palette.Neutral.100">#FF1A1A1A</Color>
    <Color x:Key="Palette.Neutral.115">#FF1E1E1E</Color>
    <Color x:Key="Palette.Neutral.125">#FF202020</Color>
    <Color x:Key="Palette.Neutral.145">#FF252525</Color>
    <Color x:Key="Palette.Neutral.175">#FF2D2D2D</Color>
    <Color x:Key="Palette.Neutral.200">#FF333333</Color>
    <Color x:Key="Palette.Neutral.225">#FF3A3A3A</Color>
    <Color x:Key="Palette.Neutral.265">#FF444444</Color>
    <Color x:Key="Palette.Neutral.290">#FF4A4A4A</Color>
    <Color x:Key="Palette.Neutral.335">#FF555555</Color>
    <Color x:Key="Palette.Neutral.400">#FF666666</Color>
    <Color x:Key="Palette.Neutral.535">#FF888888</Color>
    <Color x:Key="Palette.Neutral.600">#FF999999</Color>
    <Color x:Key="Palette.Neutral.665">#FFAAAAAA</Color>
    <Color x:Key="Palette.Neutral.735">#FFBBBBBB</Color>
    <Color x:Key="Palette.Neutral.785">#FFC8C8C8</Color>
    <Color x:Key="Palette.Neutral.800">#FFCCCCCC</Color>
    <Color x:Key="Palette.Neutral.865">#FFDDDDDD</Color>
    <Color x:Key="Palette.Neutral.875">#FFE0E0E0</Color>
    <Color x:Key="Palette.Neutral.910">#FFE8E8E8</Color>
    <Color x:Key="Palette.Black">#FF000000</Color>
    <Color x:Key="Palette.White">#FFFFFFFF</Color>

    <!-- Accents -->
    <Color x:Key="Palette.Crimson.700">#FF7B241C</Color>
    <Color x:Key="Palette.Maroon.800">#FF7A2A2A</Color>
    <Color x:Key="Palette.Maroon.900">#FF3A1A1A</Color>
    <Color x:Key="Palette.Red.500">#FFC0392B</Color>
    <Color x:Key="Palette.Red.450">#FFE74C3C</Color>
    <Color x:Key="Palette.Red.550">#FFE05555</Color>
    <Color x:Key="Palette.Red.300">#FFFF9C9C</Color>
    <Color x:Key="Palette.Azure.600">#FF1A5276</Color>
    <Color x:Key="Palette.Azure.150">#FFD5E8F5</Color>
    <Color x:Key="Palette.Azure.250">#FFB0CDE8</Color>
    <Color x:Key="Palette.Slate.700">#FF2C3E50</Color>
    <Color x:Key="Palette.Navy.100">#FF1A1A2A</Color>
    <Color x:Key="Palette.Navy.150">#FF1A2738</Color>
    <Color x:Key="Palette.Sky.300">#FF9CC4FF</Color>
    <Color x:Key="Palette.Sky.250">#FF9CDCFE</Color>
    <Color x:Key="Palette.Sky.350">#FF99AADD</Color>
    <Color x:Key="Palette.Sky.400">#FF4A9EFF</Color>
    <Color x:Key="Palette.Sky.450">#FF5DADE2</Color>
    <Color x:Key="Palette.Teal.600">#FF0E6655</Color>
    <Color x:Key="Palette.Teal.150">#FFD5F0E8</Color>
    <Color x:Key="Palette.Teal.250">#FFB0E0D5</Color>
    <Color x:Key="Palette.Green.600">#FF2D6A2D</Color>
    <Color x:Key="Palette.Green.550">#FF3A7A3A</Color>
    <Color x:Key="Palette.Green.500">#FF5DB55D</Color>
    <Color x:Key="Palette.Green.400">#FF7DCEA0</Color>
    <Color x:Key="Palette.Amber.500">#FFB8760A</Color>
    <Color x:Key="Palette.Amber.600">#FFC08A2A</Color>
    <Color x:Key="Palette.Amber.520">#FFF0A830</Color>
    <Color x:Key="Palette.Amber.540">#FFF0AD4E</Color>
    <Color x:Key="Palette.Amber.560">#FFE8C050</Color>
    <Color x:Key="Palette.Amber.570">#FFE8D080</Color>
    <Color x:Key="Palette.Amber.550">#FFE8A020</Color>
    <Color x:Key="Palette.Amber.900">#FF7A5C00</Color>
    <Color x:Key="Palette.Amber.950">#FF2A2000</Color>
    <Color x:Key="Palette.Olive.500">#FFBDBD80</Color>
    <Color x:Key="Palette.Burnt.600">#FF8E4912</Color>
    <Color x:Key="Palette.Parchment.100">#FFF5F0D0</Color>
    <Color x:Key="Palette.Parchment.200">#FFE8E0B0</Color>
    <Color x:Key="Palette.Cream.100">#FFFFF8DC</Color>
    <Color x:Key="Palette.Mauve.500">#FF7A6A8E</Color>
    <Color x:Key="Palette.Magenta.500">#FFEE00AA</Color>

    <!-- Alpha / effect primitives (overlays + shadow live here so hex stays confined) -->
    <Color x:Key="Palette.Alpha.Scrim">#BB000000</Color>
    <Color x:Key="Palette.Alpha.Shadow">#A0000000</Color>
    <Color x:Key="Palette.Alpha.HairlineOnDark">#33FFFFFF</Color>
    <BoxShadows x:Key="Palette.Effect.CardShadow">0 0 18 0 #A0000000</BoxShadows>
</ResourceDictionary>
```

- [ ] **Step 4: Merge the dictionary in `App.axaml` so tests can resolve it**

In `DialogEditor.Avalonia/App.axaml`, add to `ResourceDictionary.MergedDictionaries` (before the existing string includes is fine):

```xml
<ResourceInclude Source="avares://DialogEditor.Avalonia/Resources/Palette.axaml"/>
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~PaletteRegistryTests"`
Expected: PASS (all theory rows + the absorbed-Amber.530 assertion).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Palette.axaml DialogEditor.Avalonia/App.axaml DialogEditor.Tests/Theming/PaletteRegistryTests.cs
git commit -m "feat(theming): add Palette.axaml primitive colour registry"
```

---

## Task 2: Tokens.axaml — semantic brush registry

**Files:**
- Create: `DialogEditor.Avalonia/Resources/Tokens.axaml`
- Test: `DialogEditor.Tests/Theming/TokenRegistryTests.cs`

- [ ] **Step 1: Write the failing test** (golden values for representative tokens + a no-dangling sweep)

`DialogEditor.Tests/Theming/TokenRegistryTests.cs`:

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace DialogEditor.Tests.Theming;

public class TokenRegistryTests
{
    private static ISolidColorBrush Brush(string key)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, Application.Current!.ActualThemeVariant, out var v),
            $"Brush key '{key}' is not defined");
        return Assert.IsAssignableFrom<ISolidColorBrush>(v);
    }

    [AvaloniaTheory]
    [InlineData("Brush.Node.Npc.Header",        0xFF, 0x7b, 0x24, 0x1c)]
    [InlineData("Brush.Node.Player.Header",      0xFF, 0x1a, 0x52, 0x76)]
    [InlineData("Brush.Node.Script.Body",        0xFF, 0xe0, 0xe0, 0xe0)]
    [InlineData("Brush.Node.Bark.Footer",        0xFF, 0xe8, 0xd0, 0x80)]
    [InlineData("Brush.Diff.Added.Fill",         0xFF, 0x3a, 0x7a, 0x3a)]
    [InlineData("Brush.Diff.Changed.Fill",       0xFF, 0xc0, 0x8a, 0x2a)]
    [InlineData("Brush.Severity.Error",          0xFF, 0xc0, 0x39, 0x2b)]
    [InlineData("Brush.Toolbar.Button.Background",0xFF, 0x33, 0x33, 0x33)]
    [InlineData("Brush.Surface.Window",          0xFF, 0x1e, 0x1e, 0x1e)]
    [InlineData("Brush.Border.Default",          0xFF, 0x33, 0x33, 0x33)]
    [InlineData("Brush.Text.Primary",            0xFF, 0xe8, 0xe8, 0xe8)]
    [InlineData("Brush.Text.Muted",              0xFF, 0x88, 0x88, 0x88)]
    [InlineData("Brush.Text.Female.Active",      0xFF, 0xe8, 0xe8, 0xe8)]
    [InlineData("Brush.Text.Status.Changed",     0xFF, 0xf0, 0xad, 0x4e)] // #e0a030 absorbed
    [InlineData("Brush.Syntax.Code",             0xFF, 0x9c, 0xdc, 0xfe)]
    [InlineData("Brush.Conflict.Theirs.Foreground",0xFF, 0x9c, 0xc4, 0xff)]
    public void TokenResolvesToExpectedColor(string key, byte a, byte r, byte g, byte b)
        => Assert.Equal(Color.FromArgb(a, r, g, b), Brush(key).Color);

    // No-dangling sweep: every Brush.* the app declares must resolve. This list is the
    // public contract from spec §7; keep it in sync when tokens are added.
    public static readonly string[] AllTokens =
    {
        "Brush.Node.Npc.Header","Brush.Node.Npc.Body","Brush.Node.Npc.Footer",
        "Brush.Node.Player.Header","Brush.Node.Player.Body","Brush.Node.Player.Footer",
        "Brush.Node.Narrator.Header","Brush.Node.Narrator.Body","Brush.Node.Narrator.Footer",
        "Brush.Node.Script.Header","Brush.Node.Script.Body","Brush.Node.Script.Footer",
        "Brush.Node.Bark.Header","Brush.Node.Bark.Body","Brush.Node.Bark.Footer",
        "Brush.Diff.Added.Fill","Brush.Diff.Changed.Fill","Brush.Diff.Removed.Fill",
        "Brush.Severity.Info","Brush.Severity.Warning","Brush.Severity.Error",
        "Brush.Toolbar.Button.Background","Brush.Toolbar.Button.Foreground","Brush.Toolbar.Button.Hover",
        "Brush.Toolbar.Button.Pressed","Brush.Toolbar.Button.Checked","Brush.Toolbar.Button.CheckedHover",
        "Brush.Surface.Window","Brush.Surface.Panel","Brush.Surface.Card","Brush.Surface.Input",
        "Brush.Surface.Inset","Brush.Surface.Header","Brush.Surface.Info","Brush.Surface.Overlay.Scrim",
        "Brush.Border.Default","Brush.Border.Subtle","Brush.Border.Strong","Brush.Border.Muted",
        "Brush.Border.OnDark","Brush.Border.Focus",
        "Brush.Text.Primary","Brush.Text.Emphasis","Brush.Text.Secondary","Brush.Text.Tertiary",
        "Brush.Text.Muted.Light","Brush.Text.Caption","Brush.Text.Muted","Brush.Text.Disabled",
        "Brush.Text.OnAccent","Brush.Text.Female.Active","Brush.Text.Female.Dim",
        "Brush.Text.Link","Brush.Text.Link.Subtle","Brush.Text.Info","Brush.Text.Highlight",
        "Brush.Accent.Badge",
        "Brush.Text.Status.New","Brush.Text.Status.Added","Brush.Text.Status.Changed",
        "Brush.Text.Status.Removed","Brush.Text.Status.Success","Brush.Text.Status.Error",
        "Brush.Text.Status.Pending","Brush.Text.Meta.Commit",
        "Brush.Syntax.Condition","Brush.Syntax.Script","Brush.Syntax.Code","Brush.Syntax.Default",
        "Brush.Conflict.Mine.Background","Brush.Conflict.Mine.Foreground",
        "Brush.Conflict.Theirs.Background","Brush.Conflict.Theirs.Foreground",
        "Brush.Button.Confirm.Background","Brush.Button.Caution.Background",
        "Brush.Bark.Detail.Background","Brush.Bark.Detail.Border","Brush.Bark.Detail.Text",
        "Brush.Node.Bark.Outline","Brush.Node.Quotidian.Note",
    };

    public static IEnumerable<object[]> AllTokenCases() => AllTokens.Select(t => new object[] { t });

    [AvaloniaTheory]
    [MemberData(nameof(AllTokenCases))]
    public void EveryDeclaredTokenResolves(string key) => Assert.NotNull(Brush(key));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenRegistryTests"`
Expected: FAIL — `Brush key 'Brush.Node.Npc.Header' is not defined`.

- [ ] **Step 3: Create `Tokens.axaml`**

`DialogEditor.Avalonia/Resources/Tokens.axaml` — semantics referencing primitives. Header comment points back to the spec; effect/alpha brushes reference the `Palette.Alpha.*` colours so no hex appears here.

```xml
<ResourceDictionary xmlns="https://github.com/avaloniaui"
                    xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <!--  TOKENS — semantic brush layer (Layer 0, public contract).
          Every view, converter, and dependent gap binds THESE keys (never Palette.*).
          Each brush references exactly one Palette.* colour; two tokens may share a
          primitive on purpose (e.g. Text.Female.Active and Text.Primary) to decouple
          meaning from value. Foreground vs background of the same concept are distinct
          tokens by design. See spec §3.1, §4, §7. -->

    <!-- Node cards (dissolves SpeakerCategoryToBrushConverter / NodeColorConverter dup) -->
    <SolidColorBrush x:Key="Brush.Node.Npc.Header"      Color="{StaticResource Palette.Crimson.700}"/>
    <SolidColorBrush x:Key="Brush.Node.Npc.Body"        Color="{StaticResource Palette.Parchment.100}"/>
    <SolidColorBrush x:Key="Brush.Node.Npc.Footer"      Color="{StaticResource Palette.Parchment.200}"/>
    <SolidColorBrush x:Key="Brush.Node.Player.Header"    Color="{StaticResource Palette.Azure.600}"/>
    <SolidColorBrush x:Key="Brush.Node.Player.Body"      Color="{StaticResource Palette.Azure.150}"/>
    <SolidColorBrush x:Key="Brush.Node.Player.Footer"    Color="{StaticResource Palette.Azure.250}"/>
    <SolidColorBrush x:Key="Brush.Node.Narrator.Header"  Color="{StaticResource Palette.Teal.600}"/>
    <SolidColorBrush x:Key="Brush.Node.Narrator.Body"    Color="{StaticResource Palette.Teal.150}"/>
    <SolidColorBrush x:Key="Brush.Node.Narrator.Footer"  Color="{StaticResource Palette.Teal.250}"/>
    <SolidColorBrush x:Key="Brush.Node.Script.Header"    Color="{StaticResource Palette.Slate.700}"/>
    <SolidColorBrush x:Key="Brush.Node.Script.Body"      Color="{StaticResource Palette.Neutral.875}"/>
    <SolidColorBrush x:Key="Brush.Node.Script.Footer"    Color="{StaticResource Palette.Neutral.785}"/>
    <SolidColorBrush x:Key="Brush.Node.Bark.Header"      Color="{StaticResource Palette.Amber.900}"/>
    <SolidColorBrush x:Key="Brush.Node.Bark.Body"        Color="{StaticResource Palette.Cream.100}"/>
    <SolidColorBrush x:Key="Brush.Node.Bark.Footer"      Color="{StaticResource Palette.Amber.570}"/>

    <!-- Diff canvas fills -->
    <SolidColorBrush x:Key="Brush.Diff.Added.Fill"    Color="{StaticResource Palette.Green.550}"/>
    <SolidColorBrush x:Key="Brush.Diff.Changed.Fill"  Color="{StaticResource Palette.Amber.600}"/>
    <SolidColorBrush x:Key="Brush.Diff.Removed.Fill"  Color="{StaticResource Palette.Maroon.800}"/>

    <!-- Severity -->
    <SolidColorBrush x:Key="Brush.Severity.Info"     Color="{StaticResource Palette.Sky.450}"/>
    <SolidColorBrush x:Key="Brush.Severity.Warning"  Color="{StaticResource Palette.Amber.500}"/>
    <SolidColorBrush x:Key="Brush.Severity.Error"    Color="{StaticResource Palette.Red.500}"/>

    <!-- Toolbar buttons -->
    <SolidColorBrush x:Key="Brush.Toolbar.Button.Background"   Color="{StaticResource Palette.Neutral.200}"/>
    <SolidColorBrush x:Key="Brush.Toolbar.Button.Foreground"   Color="{StaticResource Palette.Neutral.665}"/>
    <SolidColorBrush x:Key="Brush.Toolbar.Button.Hover"        Color="{StaticResource Palette.Neutral.265}"/>
    <SolidColorBrush x:Key="Brush.Toolbar.Button.Pressed"      Color="{StaticResource Palette.Neutral.290}"/>
    <SolidColorBrush x:Key="Brush.Toolbar.Button.Checked"      Color="{StaticResource Palette.Neutral.225}"/>
    <SolidColorBrush x:Key="Brush.Toolbar.Button.CheckedHover" Color="{StaticResource Palette.Neutral.265}"/>

    <!-- Surfaces -->
    <SolidColorBrush x:Key="Brush.Surface.Window"  Color="{StaticResource Palette.Neutral.115}"/>
    <SolidColorBrush x:Key="Brush.Surface.Panel"   Color="{StaticResource Palette.Neutral.145}"/>
    <SolidColorBrush x:Key="Brush.Surface.Card"    Color="{StaticResource Palette.Neutral.100}"/>
    <SolidColorBrush x:Key="Brush.Surface.Input"   Color="{StaticResource Palette.Neutral.80}"/>
    <SolidColorBrush x:Key="Brush.Surface.Inset"   Color="{StaticResource Palette.Neutral.125}"/>
    <SolidColorBrush x:Key="Brush.Surface.Header"  Color="{StaticResource Palette.Neutral.200}"/>
    <SolidColorBrush x:Key="Brush.Surface.Info"    Color="{StaticResource Palette.Navy.100}"/>
    <SolidColorBrush x:Key="Brush.Surface.Overlay.Scrim" Color="{StaticResource Palette.Alpha.Scrim}"/>

    <!-- Borders -->
    <SolidColorBrush x:Key="Brush.Border.Default" Color="{StaticResource Palette.Neutral.200}"/>
    <SolidColorBrush x:Key="Brush.Border.Subtle"  Color="{StaticResource Palette.Neutral.175}"/>
    <SolidColorBrush x:Key="Brush.Border.Strong"  Color="{StaticResource Palette.Neutral.265}"/>
    <SolidColorBrush x:Key="Brush.Border.Muted"   Color="{StaticResource Palette.Neutral.335}"/>
    <SolidColorBrush x:Key="Brush.Border.OnDark"  Color="{StaticResource Palette.Alpha.HairlineOnDark}"/>
    <SolidColorBrush x:Key="Brush.Border.Focus"   Color="{StaticResource Palette.Azure.600}"/>

    <!-- Text -->
    <!-- Text emphasis ramp (light→dim), each a distinct used grey level -->
    <SolidColorBrush x:Key="Brush.Text.Primary"      Color="{StaticResource Palette.Neutral.910}"/>
    <SolidColorBrush x:Key="Brush.Text.Emphasis"     Color="{StaticResource Palette.Neutral.865}"/>
    <SolidColorBrush x:Key="Brush.Text.Secondary"    Color="{StaticResource Palette.Neutral.800}"/>
    <SolidColorBrush x:Key="Brush.Text.Tertiary"     Color="{StaticResource Palette.Neutral.735}"/>
    <SolidColorBrush x:Key="Brush.Text.Muted.Light"  Color="{StaticResource Palette.Neutral.665}"/>
    <SolidColorBrush x:Key="Brush.Text.Caption"      Color="{StaticResource Palette.Neutral.600}"/>
    <SolidColorBrush x:Key="Brush.Text.Muted"        Color="{StaticResource Palette.Neutral.535}"/>
    <SolidColorBrush x:Key="Brush.Text.Disabled"     Color="{StaticResource Palette.Neutral.400}"/>
    <SolidColorBrush x:Key="Brush.Text.OnAccent"     Color="{StaticResource Palette.White}"/>
    <SolidColorBrush x:Key="Brush.Text.Female.Active" Color="{StaticResource Palette.Neutral.910}"/>
    <SolidColorBrush x:Key="Brush.Text.Female.Dim"    Color="{StaticResource Palette.Neutral.335}"/>

    <!-- Status foreground accents -->
    <SolidColorBrush x:Key="Brush.Text.Status.New"     Color="{StaticResource Palette.Green.400}"/>
    <SolidColorBrush x:Key="Brush.Text.Status.Added"   Color="{StaticResource Palette.Green.400}"/>
    <SolidColorBrush x:Key="Brush.Text.Status.Changed" Color="{StaticResource Palette.Amber.540}"/>
    <SolidColorBrush x:Key="Brush.Text.Status.Removed"  Color="{StaticResource Palette.Red.450}"/>
    <SolidColorBrush x:Key="Brush.Text.Status.Success"  Color="{StaticResource Palette.Green.500}"/>
    <SolidColorBrush x:Key="Brush.Text.Status.Error"    Color="{StaticResource Palette.Red.550}"/>
    <SolidColorBrush x:Key="Brush.Text.Status.Pending"  Color="{StaticResource Palette.Magenta.500}"/>
    <SolidColorBrush x:Key="Brush.Text.Meta.Commit"     Color="{StaticResource Palette.Amber.600}"/>

    <!-- Syntax / parameter styling -->
    <SolidColorBrush x:Key="Brush.Syntax.Condition" Color="{StaticResource Palette.Amber.550}"/>
    <SolidColorBrush x:Key="Brush.Syntax.Script"    Color="{StaticResource Palette.Green.400}"/>
    <SolidColorBrush x:Key="Brush.Syntax.Code"      Color="{StaticResource Palette.Sky.250}"/>
    <SolidColorBrush x:Key="Brush.Syntax.Default"   Color="{StaticResource Palette.Neutral.910}"/>

    <!-- Merge-conflict mine/theirs -->
    <SolidColorBrush x:Key="Brush.Conflict.Mine.Background"   Color="{StaticResource Palette.Maroon.900}"/>
    <SolidColorBrush x:Key="Brush.Conflict.Mine.Foreground"   Color="{StaticResource Palette.Red.300}"/>
    <SolidColorBrush x:Key="Brush.Conflict.Theirs.Background"  Color="{StaticResource Palette.Navy.150}"/>
    <SolidColorBrush x:Key="Brush.Conflict.Theirs.Foreground"  Color="{StaticResource Palette.Sky.300}"/>

    <!-- Action buttons -->
    <SolidColorBrush x:Key="Brush.Button.Confirm.Background" Color="{StaticResource Palette.Green.600}"/>
    <SolidColorBrush x:Key="Brush.Button.Caution.Background" Color="{StaticResource Palette.Burnt.600}"/>

    <!-- Bark detail block + canvas bark outline -->
    <SolidColorBrush x:Key="Brush.Bark.Detail.Background" Color="{StaticResource Palette.Amber.950}"/>
    <SolidColorBrush x:Key="Brush.Bark.Detail.Border"    Color="{StaticResource Palette.Amber.900}"/>
    <SolidColorBrush x:Key="Brush.Bark.Detail.Text"      Color="{StaticResource Palette.Amber.560}"/>
    <SolidColorBrush x:Key="Brush.Node.Bark.Outline"     Color="{StaticResource Palette.Amber.520}"/>
    <SolidColorBrush x:Key="Brush.Node.Quotidian.Note"   Color="{StaticResource Palette.Olive.500}"/>

    <!-- Links, info text, highlight, badge (accent foregrounds) -->
    <SolidColorBrush x:Key="Brush.Text.Link"         Color="{StaticResource Palette.Sky.400}"/>
    <SolidColorBrush x:Key="Brush.Text.Link.Subtle"  Color="{StaticResource Palette.Sky.350}"/>
    <SolidColorBrush x:Key="Brush.Text.Info"         Color="{StaticResource Palette.Sky.450}"/>
    <SolidColorBrush x:Key="Brush.Text.Highlight"    Color="{StaticResource Palette.Amber.520}"/>
    <SolidColorBrush x:Key="Brush.Accent.Badge"      Color="{StaticResource Palette.Mauve.500}"/>
</ResourceDictionary>
```

- [ ] **Step 4: Merge it in `App.axaml`** (immediately after the Palette include):

```xml
<ResourceInclude Source="avares://DialogEditor.Avalonia/Resources/Tokens.axaml"/>
```

- [ ] **Step 5: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~TokenRegistryTests"`
Expected: PASS (golden rows + every-token-resolves sweep).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Tokens.axaml DialogEditor.Avalonia/App.axaml DialogEditor.Tests/Theming/TokenRegistryTests.cs
git commit -m "feat(theming): add Tokens.axaml semantic brush registry"
```

---

## Task 3: TokenBrushes.Resolve helper

**Files:**
- Create: `DialogEditor.Avalonia/Theming/TokenBrushes.cs`
- Test: `DialogEditor.Tests/Theming/ConverterTokenTests.cs` (helper portion)

- [ ] **Step 1: Write the failing test**

`DialogEditor.Tests/Theming/ConverterTokenTests.cs`:

```csharp
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;

namespace DialogEditor.Tests.Theming;

public class ConverterTokenTests
{
    [AvaloniaFact]
    public void Resolve_KnownKey_ReturnsRegistryBrushInstance()
    {
        Application.Current!.TryGetResource("Brush.Node.Npc.Header",
            Application.Current!.ActualThemeVariant, out var expected);
        Assert.Same(expected, TokenBrushes.Resolve("Brush.Node.Npc.Header"));
    }

    [AvaloniaFact]
    public void Resolve_UnknownKey_Throws()
        => Assert.Throws<KeyNotFoundException>(() => TokenBrushes.Resolve("Brush.Does.Not.Exist"));
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConverterTokenTests"`
Expected: FAIL — `TokenBrushes` does not exist (compile error).

- [ ] **Step 3: Create the helper**

`DialogEditor.Avalonia/Theming/TokenBrushes.cs`:

```csharp
using System.Collections.Generic;
using Avalonia;
using Avalonia.Media;

namespace DialogEditor.Avalonia.Theming;

/// <summary>
/// Resolves a semantic Brush.* token from the application resource registry
/// (Tokens.axaml). Converters call this instead of constructing brushes, so colour
/// has exactly one source of truth and the duplicated-RGB drift bug is impossible.
/// Fails fast on an unknown key — the keys are compile-time constants and
/// TokenRegistryTests guarantees every declared token resolves. See
/// docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md §10.
/// </summary>
public static class TokenBrushes
{
    public static IBrush Resolve(string key)
    {
        var app = Application.Current
            ?? throw new KeyNotFoundException($"No Application to resolve brush token '{key}'.");
        if (app.TryGetResource(key, app.ActualThemeVariant, out var value) && value is IBrush brush)
            return brush;
        throw new KeyNotFoundException($"Brush token '{key}' is not defined in the registry.");
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConverterTokenTests"`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Theming/TokenBrushes.cs DialogEditor.Tests/Theming/ConverterTokenTests.cs
git commit -m "feat(theming): add TokenBrushes.Resolve registry helper"
```

---

## Task 4: Migrate the toolbar control themes in App.axaml

**Files:**
- Modify: `DialogEditor.Avalonia/App.axaml:24-84` (the two `ControlTheme` blocks)

- [ ] **Step 1: Replace the hardcoded greys with DynamicResource**

In `ToolbarPlainButton` and `ToolbarPlainToggleButton`, replace every literal:
- `Value="#333"` → `Value="{DynamicResource Brush.Toolbar.Button.Background}"`
- `Value="#aaa"` → `Value="{DynamicResource Brush.Toolbar.Button.Foreground}"`
- `Value="#444"` (pointerover) → `Value="{DynamicResource Brush.Toolbar.Button.Hover}"`
- `Value="#4a4a4a"` (pressed) → `Value="{DynamicResource Brush.Toolbar.Button.Pressed}"`
- `Value="#3a3a3a"` (checked) → `Value="{DynamicResource Brush.Toolbar.Button.Checked}"`
- the `:checked:pointerover` `#444` → `Value="{DynamicResource Brush.Toolbar.Button.CheckedHover}"`

- [ ] **Step 2: Build to verify XAML is valid**

Run: `dotnet build DialogEditor.Avalonia`
Expected: Build succeeded.

- [ ] **Step 3: Run the full headless suite (control themes are exercised by view tests)**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — no regressions (existing view tests still construct windows using these themes).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/App.axaml
git commit -m "refactor(theming): toolbar themes use Brush.Toolbar.* tokens"
```

---

## Task 5: Convert the node brush converters (delete the duplication)

**Files:**
- Modify: `DialogEditor.Avalonia/Converters/SpeakerCategoryToBrushConverter.cs`
- Modify: `DialogEditor.Avalonia/Converters/NodeColorConverter.cs`
- Safety net: existing `DialogEditor.Tests/Converters/BrushConverterTests.cs` (asserts exact colours — must stay green).

> The registry already holds the exact same colours these converters returned, so the
> existing `BrushConverterTests` are the regression guard: they must keep passing
> unchanged. No new colour values are introduced here.

- [ ] **Step 1: Rewrite `SpeakerCategoryToBrushConverter`**

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Converts SpeakerCategory → node brush by resolving Brush.Node.* tokens.
/// ConverterParameter: "body" / "footer" / omitted = header. The palette lives in
/// Tokens.axaml — this converter holds no colours (was a hand-copied RGB table that
/// drifted against NodeColorConverter; both now share the same keys).
/// </summary>
public sealed class SpeakerCategoryToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat = value is SpeakerCategory c ? c : SpeakerCategory.Npc;
        return TokenBrushes.Resolve(Key(cat, parameter as string));
    }

    internal static string Key(SpeakerCategory cat, string? zone)
    {
        var subject = cat switch
        {
            SpeakerCategory.Player   => "Player",
            SpeakerCategory.Narrator => "Narrator",
            SpeakerCategory.Script   => "Script",
            _                        => "Npc",
        };
        var part = zone switch { "body" => "Body", "footer" => "Footer", _ => "Header" };
        return $"Brush.Node.{subject}.{part}";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotImplementedException();
}
```

- [ ] **Step 2: Rewrite `NodeColorConverter` (reuses the same keys; adds Bark)**

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;
using DialogEditor.Core.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// IMultiValueConverter — values[0] = SpeakerCategory, values[1] = DisplayType string.
/// ConverterParameter: "body" / "footer" / omit = header. Returns Brush.Node.Bark.*
/// when DisplayType is "Bark"; otherwise reuses SpeakerCategoryToBrushConverter.Key so
/// the conversation palette is defined exactly once (the old duplicate RGB table is gone).
/// </summary>
public sealed class NodeColorConverter : IMultiValueConverter
{
    public object Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var cat         = values.Count > 0 && values[0] is SpeakerCategory c ? c : SpeakerCategory.Npc;
        var displayType = values.Count > 1 ? values[1] as string ?? string.Empty : string.Empty;
        var zone        = parameter as string;

        if (displayType == "Bark")
        {
            var part = zone switch { "body" => "Body", "footer" => "Footer", _ => "Header" };
            return TokenBrushes.Resolve($"Brush.Node.Bark.{part}");
        }
        return TokenBrushes.Resolve(SpeakerCategoryToBrushConverter.Key(cat, zone));
    }
}
```

- [ ] **Step 3: Run the converter tests (regression guard)**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~BrushConverterTests"`
Expected: PASS — every `SpeakerCategoryToBrush_*` and `NodeColorConverter_*` row still returns the same colour.

- [ ] **Step 4: Add a registry-identity test** to `ConverterTokenTests`:

```csharp
[AvaloniaTheory]
[InlineData(DialogEditor.Core.Models.SpeakerCategory.Npc, "body", "Brush.Node.Npc.Body")]
[InlineData(DialogEditor.Core.Models.SpeakerCategory.Script, null, "Brush.Node.Script.Header")]
public void SpeakerCategoryConverter_ReturnsRegistryBrush(
    DialogEditor.Core.Models.SpeakerCategory cat, string? zone, string key)
{
    var conv = new DialogEditor.Avalonia.Converters.SpeakerCategoryToBrushConverter();
    var result = conv.Convert(cat, typeof(Avalonia.Media.IBrush), zone, System.Globalization.CultureInfo.InvariantCulture);
    Assert.Same(TokenBrushes.Resolve(key), result);
}
```

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~ConverterTokenTests"` → PASS.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Converters/SpeakerCategoryToBrushConverter.cs DialogEditor.Avalonia/Converters/NodeColorConverter.cs DialogEditor.Tests/Theming/ConverterTokenTests.cs
git commit -m "refactor(theming): node converters resolve Brush.Node.* tokens; delete duplicated RGB"
```

---

## Task 6: Convert the remaining value converters

**Files:**
- Modify: `DiffStatusToBrushConverter.cs`, `FlowIssueKindToSeverityBrushConverter.cs`, `BoolToNewConversationBrushConverter.cs`, `BoolToFemaleTextBrushConverter.cs`, `PropertyValueStyleToBrushConverter.cs`
- Safety net: `BrushConverterTests` (must stay green).

> Same principle: resolve a token; colours are unchanged. Below is each converter's new
> body. The `DiffStatusToBrushConverter` keeps returning `Brushes.Transparent` for the
> Unchanged/null cases (no token for "transparent").

- [ ] **Step 1: `DiffStatusToBrushConverter` — map to `Brush.Diff.*.Fill`**

```csharp
public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    => value is DiffStatus status
        ? status switch
        {
            DiffStatus.Added   => TokenBrushes.Resolve("Brush.Diff.Added.Fill"),
            DiffStatus.Changed => TokenBrushes.Resolve("Brush.Diff.Changed.Fill"),
            DiffStatus.Removed => TokenBrushes.Resolve("Brush.Diff.Removed.Fill"),
            _                  => Brushes.Transparent,
        }
        : Brushes.Transparent;
```

- [ ] **Step 2: `FlowIssueKindToSeverityBrushConverter` — map to `Brush.Severity.*`**

```csharp
public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    => value is FlowIssueKind kind && kind == FlowIssueKind.Unreachable
        ? TokenBrushes.Resolve("Brush.Severity.Error")
        : TokenBrushes.Resolve("Brush.Severity.Warning");
```

- [ ] **Step 3: `BoolToNewConversationBrushConverter`**

```csharp
public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    => value is true
        ? TokenBrushes.Resolve("Brush.Text.Status.New")
        : TokenBrushes.Resolve("Brush.Text.Secondary");
```

- [ ] **Step 4: `BoolToFemaleTextBrushConverter`**

```csharp
public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    => value is true
        ? TokenBrushes.Resolve("Brush.Text.Female.Active")
        : TokenBrushes.Resolve("Brush.Text.Female.Dim");
```

- [ ] **Step 5: `PropertyValueStyleToBrushConverter`** (keep returning `null` for non-style values)

```csharp
public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
    value is PropertyValueStyle style ? style switch
    {
        PropertyValueStyle.Condition => TokenBrushes.Resolve("Brush.Syntax.Condition"),
        PropertyValueStyle.Script    => TokenBrushes.Resolve("Brush.Syntax.Script"),
        PropertyValueStyle.Code      => TokenBrushes.Resolve("Brush.Syntax.Code"),
        _                            => TokenBrushes.Resolve("Brush.Syntax.Default"),
    } : null;
```

> Add `using DialogEditor.Avalonia.Theming;` to each file; remove the now-unused
> `private static readonly ISolidColorBrush ...` fields and the `Color`/`SolidColorBrush`
> usings where they become unused.

- [ ] **Step 6: Run the converter tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~BrushConverterTests"`
Expected: PASS — all colours unchanged.

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Avalonia/Converters/
git commit -m "refactor(theming): value converters resolve Brush.* tokens"
```

---

## Task 7: Convert the code-behind colour users

**Files:**
- Modify: `DialogEditor.Avalonia/Controls/InlineDiffTextBlock.cs`
- Modify: `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs`
- Safety net: `DialogEditor.Tests/Controls/InlineDiffTextBlockTests.cs`

- [ ] **Step 1: Inspect the current constants**

Run: `dotnet build DialogEditor.Avalonia` first to confirm a clean baseline, then open each
file and locate every `new SolidColorBrush(...)` / `Color.FromRgb` / `Color.Parse`.

- [ ] **Step 2: Replace each with `TokenBrushes.Resolve("Brush.<...>")`** using the spec
Appendix B mapping (`InlineDiffTextBlock` → `Brush.Diff.*` / `Brush.Text.Status.*`;
`GitConflictResolutionWindow` → `Brush.Conflict.{Mine,Theirs}.{Background,Foreground}`).
For each replaced colour, pick the token whose value in §7 equals the current literal; if a
literal has no exact token, STOP and add the token to `Tokens.axaml` + its primitive +
update `TokenRegistryTests.AllTokens` (do not invent a colour).

- [ ] **Step 3: Run the affected tests**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~InlineDiffTextBlockTests|FullyQualifiedName~GitConflictResolutionWindowTests"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Controls/InlineDiffTextBlock.cs DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs
git commit -m "refactor(theming): code-behind colour users resolve Brush.* tokens"
```

---

## Tasks 8–11: Migrate the 29 `.axaml` view files

**Source of truth:** spec **Appendix A** — every `file:line → value → Palette primitive` row.
Translate each row's *primitive* to its *semantic token* using §7 and the **attribute rule**:

| Attribute on the line | Token family |
|---|---|
| `Background=` | `Brush.Surface.*` / `Brush.Node.*` / `Brush.Button.*` / `Brush.Conflict.*.Background` |
| `BorderBrush=` / `Stroke=` | `Brush.Border.*` (or accent border, e.g. `Brush.Bark.Detail.Border`) |
| `Foreground=` / `Fill=` (text) | `Brush.Text.*` / `Brush.Text.Status.*` / `Brush.Syntax.*` |
| `<SolidColorBrush Color=...>` swatch | the swatch's semantic token (e.g. legend swatches → `Brush.Node.*`) |

**Replacement pattern:** `Foreground="#888"` → `Foreground="{DynamicResource Brush.Text.Muted}"`.

**Dual-role greys** (`#333`, `#444`, `#555`) resolve by the attribute rule above:
`Background="#333"`→`Brush.Surface.Header`; `BorderBrush="#333"`→`Brush.Border.Default`;
`BorderBrush="#555"`→`Brush.Border.Muted`; `Foreground="#555"`→`Brush.Text.Female.Dim` (in the
female-text context) or `Brush.Text.Disabled` (general dim text — judge by the surrounding
control; both share `Palette.Neutral.335`/`400`, so pick the one that names the intent).

**Special cases (call out, don't guess):**
- `MainWindow.axaml:186` `BoxShadow … #A0000000` is a **string**, not a brush. Replace the whole
  property value with the effect resource: `BoxShadow="{DynamicResource Palette.Effect.CardShadow}"`.
  (This is the one sanctioned `Palette.*` reference from a view, because BoxShadow has no brush
  token; the NoStrayHex test allowlists `Palette.Effect.*`.)
- `#1a1a2a` window backgrounds (About/Changelog/DiffHelp/Legend, ConversationView:239) →
  `Brush.Surface.Info`.
- Legend swatches (`#F5F0D0`,`#D5E8F5`,`#D5F0E8`,`#E0E0E0`) → the matching `Brush.Node.*.Body`.

Group the work so each task is one commit-sized batch; after each, build + run the suite.

### Task 8: Dialog windows (small files)
**Files:** `BranchNameDialog`, `CommitConsentDialog`, `ConversationNameDialog`, `ForceDeleteDialog`, `ImportWarningsDialog`, `LanguageCodeDialog`, `UnsavedChangesDialog`, `ConflictResolutionDialog`, `ForceDeleteDialog`, `AboutWindow`, `ChangelogWindow`.

- [ ] **Step 1:** For each file, apply its Appendix A rows with the attribute rule. Example —
`ForceDeleteDialog.axaml`: `Background="#252525"`→`{DynamicResource Brush.Surface.Panel}`,
`Foreground="#e8e8e8"`→`{DynamicResource Brush.Text.Primary}`, `BorderBrush="#333"`→
`{DynamicResource Brush.Border.Default}`, the `#7b241c` accent →`{DynamicResource Brush.Node.Npc.Header}`
(or a dedicated accent token if you prefer; reuse is fine since the value matches).
- [ ] **Step 2:** `dotnet build DialogEditor.Avalonia` → succeeds.
- [ ] **Step 3:** `dotnet test DialogEditor.Tests` → PASS.
- [ ] **Step 4:** `git add` the batch + `git commit -m "refactor(theming): tokenise dialog windows"`.

### Task 9: Editor / browser views
**Files:** `ConditionEditorWindow`, `ScriptEditorWindow`, `FindReplaceWindow`, `BatchReplaceWindow`, `GameBrowserView`, `SettingsWindow`, `ExportConversationsWindow`, `FlowAnalyticsWindow`, `TestModeOverlay`, `PatchManagerWindow`.

- [ ] Apply Appendix A rows (attribute rule); `#BB000000` scrim → `Brush.Surface.Overlay.Scrim`.
- [ ] Build → succeeds; `dotnet test DialogEditor.Tests` → PASS.
- [ ] Commit: `refactor(theming): tokenise editor and browser views`.

### Task 10: Diff / git / history views
**Files:** `DiffWindow`, `DiffHelpWindow`, `HistoryWindow`, `BlameWindow`, `GitConflictResolutionWindow.axaml`.

- [ ] Apply rows; diff legend swatches/text → `Brush.Text.Status.*`; `#e0a030`→`Brush.Text.Status.Changed`
  (the absorbed value); conflict panels → `Brush.Conflict.*`.
- [ ] Build → succeeds; `dotnet test DialogEditor.Tests` → PASS.
- [ ] Commit: `refactor(theming): tokenise diff, git and history views`.

### Task 11: Canvas / main / node views (the big ones)
**Files:** `MainWindow`, `ConversationView`, `NodeDetailView`, `LegendWindow`.

- [ ] Apply rows; `MainWindow:186` BoxShadow → `Palette.Effect.CardShadow` (special case above);
  `LegendWindow` `#1a1a2a`→`Brush.Surface.Info`, swatches → `Brush.Node.*.Body`, the `#888`/`#ccc`
  alternation → `Brush.Text.Muted`/`Brush.Text.Secondary`; NodeDetail bark block (`#2A2000`/`#7A5C00`/
  `#E8C050`) → `Brush.Bark.Detail.{Background,Border,Text}`, `#bdbd80`→`Brush.Node.Quotidian.Note`.
- [ ] Build → succeeds; `dotnet test DialogEditor.Tests` → PASS.
- [ ] Commit: `refactor(theming): tokenise canvas and main views`.

---

## Task 12: NoStrayHex enforcement test (definition-of-done)

**Files:**
- Create: `DialogEditor.Tests/Theming/NoStrayHexTests.cs`

- [ ] **Step 1: Write the test**

```csharp
using System.Text.RegularExpressions;
using Xunit;

namespace DialogEditor.Tests.Theming;

public class NoStrayHexTests
{
    // Repo root: walk up from the test bin dir until we find the Avalonia project folder.
    private static string AvaloniaRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !Directory.Exists(Path.Combine(dir.FullName, "DialogEditor.Avalonia")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return Path.Combine(dir!.FullName, "DialogEditor.Avalonia");
    }

    private static readonly Regex Hex = new(@"#[0-9A-Fa-f]{3,8}\b", RegexOptions.Compiled);
    private static readonly Regex CSharpColour = new(
        @"new\s+SolidColorBrush|Color\.FromRgb|Color\.FromArgb|Color\.Parse", RegexOptions.Compiled);

    [Fact]
    public void NoHexLiteralsOutsidePalette()
    {
        var root = AvaloniaRoot();
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            if (file.EndsWith("Palette.axaml", StringComparison.OrdinalIgnoreCase)) continue;
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (Hex.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "Hex colour literals are only allowed in Palette.axaml. Offenders:\n" + string.Join("\n", offenders));
    }

    [Fact]
    public void NoBrushConstructionInConverters()
    {
        var dir = Path.Combine(AvaloniaRoot(), "Converters");
        var offenders = new List<string>();
        foreach (var file in Directory.EnumerateFiles(dir, "*.cs", SearchOption.AllDirectories))
        {
            var lines = File.ReadAllLines(file);
            for (var i = 0; i < lines.Length; i++)
                if (CSharpColour.IsMatch(lines[i]))
                    offenders.Add($"{Path.GetFileName(file)}:{i + 1}: {lines[i].Trim()}");
        }
        Assert.True(offenders.Count == 0,
            "Converters must resolve tokens, not construct colours. Offenders:\n" + string.Join("\n", offenders));
    }
}
```

- [ ] **Step 2: Run it — it surfaces every remaining stray literal**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~NoStrayHexTests"`
Expected: FAIL initially **iff** any `.axaml`/converter literal was missed — the failure message
lists each `file:line`. Fix each by applying the §7 token (Tasks 8–11 should have cleared them).
Re-run until PASS. **Green here = the migration is complete.**

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Theming/NoStrayHexTests.cs
git commit -m "test(theming): enforce hex literals only in Palette.axaml"
```

---

## Task 13: Rationale comments + gap close-out

**Files:**
- Modify: `Palette.axaml`, `Tokens.axaml`, `TokenBrushes.cs`, `NoStrayHexTests.cs` (header comments — mostly added already in their tasks; verify each points back to the spec).
- Modify: `Gaps.md` (mark Layer 0 done).

- [ ] **Step 1:** Confirm each created artifact carries a header comment that (a) states the
two-tier rule, (b) names the public vs private tier, and (c) links
`docs/superpowers/specs/2026-06-07-colour-token-taxonomy-design.md`. Add where missing.

- [ ] **Step 2:** In `Gaps.md`, update the **Layer 0** bullet to note it is **implemented**
(keep Layers 1/2/2.5 as deferred), referencing the spec and the `NoStrayHex` enforcement.

- [ ] **Step 3: Final full run**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS (whole suite, serial).

- [ ] **Step 4: Commit**

```bash
git add Palette.axaml Tokens.axaml DialogEditor.Avalonia/Theming/TokenBrushes.cs DialogEditor.Tests/Theming/NoStrayHexTests.cs Gaps.md
git commit -m "docs(theming): rationale comments + mark Layer 0 token registry done"
```

---

## Self-Review Notes (for the executor)

- **Coverage:** Tasks 1–2 build §5/§7 registries; Task 3 the §10 helper; Task 4 the §7.4 toolbar
  themes; Tasks 5–7 the 9 §Appendix B converters/controls (deleting the §1 duplication); Tasks
  8–11 the §Appendix A `.axaml` migration; Task 12 the §11 enforcement test; Task 13 the
  §reasoning-in-files requirement + gap close-out. The four §11 tests map to Tasks 1, 2, 5/6, 12.
- **Drift policy (§6/§8):** only the four imperceptible grey merges + `#e0a030`→`Amber.540` are
  intentional changes; `TokenRegistryTests` asserts `Status.Changed == #f0ad4e` and
  `PaletteRegistryTests` asserts `Amber.530` is absent.
- **No invented colours:** if any code-behind/axaml literal lacks an exact token (Task 7/11),
  STOP and add the primitive+token+`AllTokens` entry rather than approximating.
- **Serial tests:** the new `[AvaloniaFact]` tests touch `Application.Current.Resources` (global);
  the suite already runs serially (`AssemblyInfo.cs` `DisableTestParallelization`). Do not enable
  parallelism.
