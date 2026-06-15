# Focusable Legend Swatches (Gaps Item 12) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the 7 legend swatches in `LegendWindow`'s "Connections" and "Node Types" sections keyboard-focusable tab stops that carry the same explanatory text sighted mouse users get from `ToolTip.Tip`, mirrored into `AutomationProperties.Name`/`AutomationProperties.HelpText` for screen readers.

**Architecture:** Wrap each of the 7 existing `StackPanel` rows in a borderless, transparent `Button` (class `legendRow`) that is visually identical to today's row but is a focus target. Seven new `_Help` resource strings (full sentences) drive `AutomationProperties.Name`, `ToolTip.Tip`, and `AutomationProperties.HelpText` on each button, all three set to the same resource. A new solution-wide structural test pins this contract.

**Tech Stack:** Avalonia 11 (.axaml), C#/.NET 8, xUnit (`DialogEditor.Tests`).

**Spec:** `docs/superpowers/specs/2026-06-14-legend-swatch-accessibility-design.md`

---

## File Structure

- **Create:** `DialogEditor.Tests/Accessibility/LegendSwatchAccessibilityTests.cs` — structural scan asserting `LegendWindow.axaml` has exactly 7 `Button.legendRow` elements, each with matching `Name`/`ToolTip.Tip`/`HelpText` and a `Border` + `TextBlock` child.
- **Modify:** `DialogEditor.Avalonia/Resources/Strings.axaml` — add 7 new `Legend_*_Help` string resources.
- **Modify:** `DialogEditor.Avalonia/Views/LegendWindow.axaml` — add a `Button.legendRow` style (plus `:pointerover`/`:pressed` overrides) to `<Window.Styles>`, and wrap the 7 swatch rows in `Button.legendRow`.
- **Modify:** `Gaps.md` — mark item 12 implemented.

---

### Task 1: Write the failing structural test (RED)

**Files:**
- Create: `DialogEditor.Tests/Accessibility/LegendSwatchAccessibilityTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
using System.Xml;
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Gaps.md item 12: the 7 legend swatches in LegendWindow's "Connections" and "Node
/// Types" sections are wrapped in focusable Button.legendRow elements so keyboard and
/// screen-reader users can reach the same explanations sighted mouse users get from
/// hover. Pins the structural contract: exactly 7 such buttons, each with
/// AutomationProperties.Name / ToolTip.Tip / AutomationProperties.HelpText all set to
/// the same {StaticResource ...} reference, and each still wrapping a Border +
/// TextBlock so a future edit can't silently drop the visible swatch/label while
/// keeping the wrapper.
///
/// Mirrors AutomationHelpTextTests' structural-contract approach, but scoped to a
/// single known file rather than a solution-wide scan.
/// </summary>
public class LegendSwatchAccessibilityTests
{
    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    [Fact]
    public void LegendRows_AreFocusableButtonsWithMirroredAccessibilityText()
    {
        var path = Path.Combine(SolutionRoot(), "DialogEditor.Avalonia", "Views", "LegendWindow.axaml");
        var doc = XDocument.Load(path, LoadOptions.SetLineInfo);

        var rows = doc.Descendants()
            .Where(e => e.Name.LocalName == "Button"
                        && (e.Attribute("Classes")?.Value ?? "").Split(' ').Contains("legendRow"))
            .ToList();

        Assert.Equal(7, rows.Count);

        foreach (var row in rows)
        {
            var line = ((IXmlLineInfo)row).HasLineInfo() ? ((IXmlLineInfo)row).LineNumber : 0;

            var name = row.Attribute("AutomationProperties.Name")?.Value;
            var tip = row.Attribute("ToolTip.Tip")?.Value;
            var help = row.Attribute("AutomationProperties.HelpText")?.Value;

            Assert.True(name is not null && name.StartsWith("{StaticResource ", StringComparison.Ordinal),
                $"LegendWindow.axaml:{line}: legendRow Button must have AutomationProperties.Name set to a {{StaticResource ...}} reference");
            Assert.Equal(name, tip);
            Assert.Equal(name, help);

            Assert.True(row.Descendants().Any(e => e.Name.LocalName == "Border"),
                $"LegendWindow.axaml:{line}: legendRow Button must still contain a Border (the colour/shape swatch)");
            Assert.True(row.Descendants().Any(e => e.Name.LocalName == "TextBlock"),
                $"LegendWindow.axaml:{line}: legendRow Button must still contain a TextBlock (the label)");
        }
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test --filter "FullyQualifiedName~LegendSwatchAccessibilityTests"`

Expected: FAIL — `Assert.Equal(7, rows.Count)` fails because `rows.Count == 0` (no `Button.legendRow` elements exist yet).

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Accessibility/LegendSwatchAccessibilityTests.cs
git commit -m "test(a11y): add item-12 legend swatch accessibility theory (RED)"
```

---

### Task 2: Add the `_Help` resource strings

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml:183` (after the `Legend_ScriptAction` entry)

- [ ] **Step 1: Insert the 7 new resource strings**

Insert immediately after the line `<sys:String x:Key="Legend_ScriptAction">Script / automated action</sys:String>` (currently line 183), before the blank line that precedes the "Legend panel — symbol descriptions" comment:

```xml
    <sys:String x:Key="Legend_ScriptAction">Script / automated action</sys:String>

    <!-- ─── Legend panel — swatch accessibility help text (Gaps item 12) ──── -->
    <!-- Full-sentence explanations, shared across AutomationProperties.Name,
         ToolTip.Tip, and AutomationProperties.HelpText for each focusable swatch row. -->
    <sys:String x:Key="Legend_ShowOnce_Help">This colour marks connections that are shown to the player only once and hidden after they're selected.</sys:String>
    <sys:String x:Key="Legend_Always_Help">This colour marks connections that remain available to the player even after selection.</sys:String>
    <sys:String x:Key="Legend_Never_Help">This colour marks connections that are never shown to the player.</sys:String>
    <sys:String x:Key="Legend_NpcLine_Help">This colour marks NPC dialogue lines on the canvas.</sys:String>
    <sys:String x:Key="Legend_PlayerChoice_Help">This colour marks player choice options on the canvas, also marked with a ✦ symbol.</sys:String>
    <sys:String x:Key="Legend_Narrator_Help">This colour marks narrator text nodes on the canvas.</sys:String>
    <sys:String x:Key="Legend_ScriptAction_Help">This colour marks script and automated-action nodes on the canvas.</sys:String>
```

(i.e. replace the original single line with the block above — the original `Legend_ScriptAction` line stays as the first line of the block, unchanged.)

- [ ] **Step 2: Commit is deferred to the end of Task 3** (this change alone doesn't make the Task 1 test pass, and CLAUDE.md's red/green flow expects the next commit to be the green one).

---

### Task 3: Add the `legendRow` style and wrap the 7 rows (GREEN)

**Files:**
- Modify: `DialogEditor.Avalonia/Views/LegendWindow.axaml`

- [ ] **Step 1: Add `<Window.Styles>` with the `legendRow` style**

Insert immediately after the `<Window ...>` opening tag's closing `>` (currently line 9, ending `Background="{DynamicResource Brush.Surface.Info}">`), before the blank line and `<ScrollViewer>`:

```xml
        Background="{DynamicResource Brush.Surface.Info}">

    <Window.Styles>
        <!-- legendRow: makes a legend swatch row a focusable tab stop while keeping
             it visually identical to a plain row — no border, no padding, no size
             change, and no hover/press highlight (it has no click action; it exists
             purely so AutomationProperties.HelpText reaches keyboard/screen-reader
             users). MinHeight/MinWidth="0" override Fluent's default Button
             MinHeight="32", which would otherwise nearly double each ~17px row. -->
        <Style Selector="Button.legendRow">
            <Setter Property="Background" Value="Transparent"/>
            <Setter Property="BorderThickness" Value="0"/>
            <Setter Property="Padding" Value="0"/>
            <Setter Property="MinHeight" Value="0"/>
            <Setter Property="MinWidth" Value="0"/>
            <Setter Property="HorizontalAlignment" Value="Stretch"/>
            <Setter Property="HorizontalContentAlignment" Value="Stretch"/>
        </Style>
        <Style Selector="Button.legendRow:pointerover /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="Background" Value="Transparent"/>
        </Style>
        <Style Selector="Button.legendRow:pressed /template/ ContentPresenter#PART_ContentPresenter">
            <Setter Property="Background" Value="Transparent"/>
        </Style>
    </Window.Styles>

    <ScrollViewer>
```

- [ ] **Step 2: Wrap the "Show Once" row (Connections section)**

Replace:

```xml
            <StackPanel Orientation="Horizontal" Margin="0,0,0,5">
                <Border Width="34" Height="2.5" Background="{DynamicResource Brush.Connection.Default}" VerticalAlignment="Center" Margin="0,0,10,0"/>
                <TextBlock FontSize="12" VerticalAlignment="Center">
                    <Run FontWeight="Bold" Text="{StaticResource Legend_ShowOnce}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
                    <Run Text="{StaticResource Legend_ShowOnce_Desc}" Foreground="{DynamicResource Brush.Text.Muted}"/>
                </TextBlock>
            </StackPanel>
```

with:

```xml
            <Button Classes="legendRow" Margin="0,0,0,5"
                    AutomationProperties.Name="{StaticResource Legend_ShowOnce_Help}"
                    ToolTip.Tip="{StaticResource Legend_ShowOnce_Help}"
                    AutomationProperties.HelpText="{StaticResource Legend_ShowOnce_Help}">
                <StackPanel Orientation="Horizontal">
                    <Border Width="34" Height="2.5" Background="{DynamicResource Brush.Connection.Default}" VerticalAlignment="Center" Margin="0,0,10,0"/>
                    <TextBlock FontSize="12" VerticalAlignment="Center">
                        <Run FontWeight="Bold" Text="{StaticResource Legend_ShowOnce}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
                        <Run Text="{StaticResource Legend_ShowOnce_Desc}" Foreground="{DynamicResource Brush.Text.Muted}"/>
                    </TextBlock>
                </StackPanel>
            </Button>
```

- [ ] **Step 3: Wrap the "Always" row (Connections section)**

Replace:

```xml
            <StackPanel Orientation="Horizontal" Margin="0,0,0,5">
                <Border Width="34" Height="2.5" Background="{DynamicResource Brush.Connection.Always}" VerticalAlignment="Center" Margin="0,0,10,0"/>
                <TextBlock FontSize="12" VerticalAlignment="Center">
                    <Run FontWeight="Bold" Text="{StaticResource Legend_Always}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
                    <Run Text="{StaticResource Legend_Always_Desc}" Foreground="{DynamicResource Brush.Text.Muted}"/>
                </TextBlock>
            </StackPanel>
```

with:

```xml
            <Button Classes="legendRow" Margin="0,0,0,5"
                    AutomationProperties.Name="{StaticResource Legend_Always_Help}"
                    ToolTip.Tip="{StaticResource Legend_Always_Help}"
                    AutomationProperties.HelpText="{StaticResource Legend_Always_Help}">
                <StackPanel Orientation="Horizontal">
                    <Border Width="34" Height="2.5" Background="{DynamicResource Brush.Connection.Always}" VerticalAlignment="Center" Margin="0,0,10,0"/>
                    <TextBlock FontSize="12" VerticalAlignment="Center">
                        <Run FontWeight="Bold" Text="{StaticResource Legend_Always}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
                        <Run Text="{StaticResource Legend_Always_Desc}" Foreground="{DynamicResource Brush.Text.Muted}"/>
                    </TextBlock>
                </StackPanel>
            </Button>
```

- [ ] **Step 4: Wrap the "Never" row (Connections section)**

Replace:

```xml
            <StackPanel Orientation="Horizontal" Margin="0,0,0,14">
                <Border Width="34" Height="2.5" Background="{DynamicResource Brush.Connection.Never}" VerticalAlignment="Center" Margin="0,0,10,0"/>
                <TextBlock FontSize="12" VerticalAlignment="Center">
                    <Run FontWeight="Bold" Text="{StaticResource Legend_Never}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
                    <Run Text="{StaticResource Legend_Never_Desc}" Foreground="{DynamicResource Brush.Text.Muted}"/>
                </TextBlock>
            </StackPanel>
```

with:

```xml
            <Button Classes="legendRow" Margin="0,0,0,14"
                    AutomationProperties.Name="{StaticResource Legend_Never_Help}"
                    ToolTip.Tip="{StaticResource Legend_Never_Help}"
                    AutomationProperties.HelpText="{StaticResource Legend_Never_Help}">
                <StackPanel Orientation="Horizontal">
                    <Border Width="34" Height="2.5" Background="{DynamicResource Brush.Connection.Never}" VerticalAlignment="Center" Margin="0,0,10,0"/>
                    <TextBlock FontSize="12" VerticalAlignment="Center">
                        <Run FontWeight="Bold" Text="{StaticResource Legend_Never}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
                        <Run Text="{StaticResource Legend_Never_Desc}" Foreground="{DynamicResource Brush.Text.Muted}"/>
                    </TextBlock>
                </StackPanel>
            </Button>
```

- [ ] **Step 5: Wrap the "NPC line" row (Node Types section)**

Replace:

```xml
            <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                <Border Width="14" Height="14" Background="{DynamicResource Brush.Node.Npc.Body}" CornerRadius="2" Margin="0,0,10,0" VerticalAlignment="Center"/>
                <TextBlock Text="{StaticResource Legend_NpcLine}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12" VerticalAlignment="Center"/>
            </StackPanel>
```

with:

```xml
            <Button Classes="legendRow" Margin="0,0,0,4"
                    AutomationProperties.Name="{StaticResource Legend_NpcLine_Help}"
                    ToolTip.Tip="{StaticResource Legend_NpcLine_Help}"
                    AutomationProperties.HelpText="{StaticResource Legend_NpcLine_Help}">
                <StackPanel Orientation="Horizontal">
                    <Border Width="14" Height="14" Background="{DynamicResource Brush.Node.Npc.Body}" CornerRadius="2" Margin="0,0,10,0" VerticalAlignment="Center"/>
                    <TextBlock Text="{StaticResource Legend_NpcLine}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>
```

- [ ] **Step 6: Wrap the "Player choice" row (Node Types section)**

Replace:

```xml
            <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                <Border Width="14" Height="14" Background="{DynamicResource Brush.Node.Player.Body}" CornerRadius="2" Margin="0,0,10,0" VerticalAlignment="Center"/>
                <TextBlock FontSize="12" VerticalAlignment="Center">
                    <Run Text="{StaticResource Legend_PlayerChoice}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
                    <Run Text="{StaticResource Legend_PlayerChoiceMark}" Foreground="{DynamicResource Brush.Text.Muted.Light}"/>
                </TextBlock>
            </StackPanel>
```

with:

```xml
            <Button Classes="legendRow" Margin="0,0,0,4"
                    AutomationProperties.Name="{StaticResource Legend_PlayerChoice_Help}"
                    ToolTip.Tip="{StaticResource Legend_PlayerChoice_Help}"
                    AutomationProperties.HelpText="{StaticResource Legend_PlayerChoice_Help}">
                <StackPanel Orientation="Horizontal">
                    <Border Width="14" Height="14" Background="{DynamicResource Brush.Node.Player.Body}" CornerRadius="2" Margin="0,0,10,0" VerticalAlignment="Center"/>
                    <TextBlock FontSize="12" VerticalAlignment="Center">
                        <Run Text="{StaticResource Legend_PlayerChoice}" Foreground="{DynamicResource Brush.Text.Secondary}"/>
                        <Run Text="{StaticResource Legend_PlayerChoiceMark}" Foreground="{DynamicResource Brush.Text.Muted.Light}"/>
                    </TextBlock>
                </StackPanel>
            </Button>
```

- [ ] **Step 7: Wrap the "Narrator" row (Node Types section)**

Replace:

```xml
            <StackPanel Orientation="Horizontal" Margin="0,0,0,4">
                <Border Width="14" Height="14" Background="{DynamicResource Brush.Node.Narrator.Body}" CornerRadius="2" Margin="0,0,10,0" VerticalAlignment="Center"/>
                <TextBlock Text="{StaticResource Legend_Narrator}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12" VerticalAlignment="Center"/>
            </StackPanel>
```

with:

```xml
            <Button Classes="legendRow" Margin="0,0,0,4"
                    AutomationProperties.Name="{StaticResource Legend_Narrator_Help}"
                    ToolTip.Tip="{StaticResource Legend_Narrator_Help}"
                    AutomationProperties.HelpText="{StaticResource Legend_Narrator_Help}">
                <StackPanel Orientation="Horizontal">
                    <Border Width="14" Height="14" Background="{DynamicResource Brush.Node.Narrator.Body}" CornerRadius="2" Margin="0,0,10,0" VerticalAlignment="Center"/>
                    <TextBlock Text="{StaticResource Legend_Narrator}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>
```

- [ ] **Step 8: Wrap the "Script / automated action" row (Node Types section)**

Replace:

```xml
            <StackPanel Orientation="Horizontal" Margin="0,0,0,14">
                <Border Width="14" Height="14" Background="{DynamicResource Brush.Node.Script.Body}" CornerRadius="2" Margin="0,0,10,0" VerticalAlignment="Center"/>
                <TextBlock Text="{StaticResource Legend_ScriptAction}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12" VerticalAlignment="Center"/>
            </StackPanel>
```

with:

```xml
            <Button Classes="legendRow" Margin="0,0,0,14"
                    AutomationProperties.Name="{StaticResource Legend_ScriptAction_Help}"
                    ToolTip.Tip="{StaticResource Legend_ScriptAction_Help}"
                    AutomationProperties.HelpText="{StaticResource Legend_ScriptAction_Help}">
                <StackPanel Orientation="Horizontal">
                    <Border Width="14" Height="14" Background="{DynamicResource Brush.Node.Script.Body}" CornerRadius="2" Margin="0,0,10,0" VerticalAlignment="Center"/>
                    <TextBlock Text="{StaticResource Legend_ScriptAction}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12" VerticalAlignment="Center"/>
                </StackPanel>
            </Button>
```

- [ ] **Step 9: Run the new test to verify it passes**

Run: `dotnet test --filter "FullyQualifiedName~LegendSwatchAccessibilityTests"`

Expected: PASS

- [ ] **Step 10: Run the existing accessibility suite to confirm no regressions**

Run: `dotnet test --filter "FullyQualifiedName~DialogEditor.Tests.Accessibility"`

Expected: PASS — in particular:
- `AutomationHelpTextTests.FocusableControlsWithTooltipsMirrorHelpText` should pass for the 7 new buttons (their `AutomationProperties.HelpText` already equals `ToolTip.Tip` by construction).
- `HitTargetSizeTests` should still pass — the new buttons have no explicit `Width`/`Height` set, so they're not in scope for that check.

- [ ] **Step 11: Run the LegendWindow view test to confirm the window still constructs/shows correctly**

Run: `dotnet test --filter "FullyQualifiedName~LegendWindowTests"`

Expected: PASS

- [ ] **Step 12: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml DialogEditor.Avalonia/Views/LegendWindow.axaml
git commit -m "feat(a11y): make legend swatches focusable with mirrored Name/Tip/HelpText (Gaps item 12)"
```

---

### Task 4: Update Gaps.md

**Files:**
- Modify: `Gaps.md:364-370` (item 12)

- [ ] **Step 1: Mark item 12 implemented**

Replace:

```markdown
12. **Info icons carry no tooltip/HelpText.** Item 5's sweep is scoped to *focusable*
    controls — static info icons (legend swatches, inline "i"/help glyphs rendered as
    `TextBlock`/`Border`) are skipped because they can't receive keyboard focus, so
    `AutomationProperties.HelpText` would never be announced on them. Making these
    operable (e.g. focusable `Button`-styled icons with both `ToolTip.Tip` and
    `AutomationProperties.HelpText`) would let keyboard/screen-reader users reach the
    same explanations sighted mouse users get.
```

with:

```markdown
12. **✅ IMPLEMENTED (2026-06-14).** The 7 legend swatches in `LegendWindow`'s
    "Connections" (Show Once, Always, Never) and "Node Types" (NPC line, Player
    choice, Narrator, Script/automated action) sections are now wrapped in
    borderless, transparent `Button.legendRow` elements — keyboard-focusable tab
    stops carrying `AutomationProperties.Name`, `ToolTip.Tip`, and
    `AutomationProperties.HelpText`, all three set to the same new full-sentence
    `Legend_*_Help` resource. `AutomationHelpTextTests` now auto-enforces the
    Tip/HelpText mirroring on these rows. `DiffWindow`/`DiffHelpWindow`/
    `FlowAnalyticsWindow` swatches were surveyed but deferred — `DiffWindow`'s
    legend sits next to an already-accessible Help button with the full
    explanation, and `FlowAnalyticsWindow`'s per-row icons are a different "many
    tab stops in a list" problem. See
    docs/superpowers/specs/2026-06-14-legend-swatch-accessibility-design.md.
```

- [ ] **Step 2: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark item 12 (focusable legend swatches) implemented"
```
