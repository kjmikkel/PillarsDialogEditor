# Focus-hint bar for non-MainWindow dialogs — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the 10 "workhorse" secondary windows a passive bottom hint bar that mirrors the focused control's `AutomationProperties.HelpText`, mirroring `MainWindow`'s item-5-Part-B status-bar hint for sighted keyboard users.

**Architecture:** A new shared `UserControl` (`FocusHintBar`, in `DialogEditor.Avalonia.Shared`) wraps a `Border`/`TextBlock` styled like `MainWindow`'s status bar. Its `AttachTo(Window)` method adds a bubbling `GotFocus` handler that copies `AutomationProperties.GetHelpText(...)` of the focused element into its own `Text` property. Each of the 10 windows gets one `<shared:FocusHintBar x:Name="HintBar"/>` added to its layout plus one `HintBar.AttachTo(this);` call in its constructor(s).

**Tech Stack:** Avalonia 11 (Headless + XUnit for tests), C#, XAML.

**Reference spec:** `docs/superpowers/specs/2026-06-13-focus-hint-bar-design.md`

---

## Task 1: `FocusHintBar` shared control (TDD)

**Files:**
- Create: `DialogEditor.Tests/Controls/FocusHintBarTests.cs`
- Create: `DialogEditor.Avalonia.Shared/FocusHintBar.axaml`
- Create: `DialogEditor.Avalonia.Shared/FocusHintBar.axaml.cs`

- [ ] **Step 1: Write the failing test**

Create `DialogEditor.Tests/Controls/FocusHintBarTests.cs`:

```csharp
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using DialogEditor.Avalonia.Shared;
using Xunit;

namespace DialogEditor.Tests.Controls;

/// <summary>
/// Gaps.md a11y item 13: FocusHintBar mirrors the focused control's
/// AutomationProperties.HelpText into its own Text, the same way
/// MainWindow.OnAnyGotFocus feeds MainWindowViewModel.FocusHintText (item 5 Part B),
/// but as a self-contained control any secondary window can drop in.
/// </summary>
public class FocusHintBarTests
{
    [AvaloniaFact]
    public void GotFocus_OnElementWithHelpText_SetsText()
    {
        var window = new Window();
        var bar = new FocusHintBar();
        var button = new Button();
        AutomationProperties.SetHelpText(button, "Does the thing");

        var root = new StackPanel();
        root.Children.Add(button);
        root.Children.Add(bar);
        window.Content = root;

        bar.AttachTo(window);
        window.Show();

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal("Does the thing", bar.Text);
    }

    [AvaloniaFact]
    public void GotFocus_OnElementWithoutHelpText_ClearsText()
    {
        var window = new Window();
        var bar = new FocusHintBar();
        var button = new Button();

        var root = new StackPanel();
        root.Children.Add(button);
        root.Children.Add(bar);
        window.Content = root;

        bar.AttachTo(window);
        window.Show();
        bar.Text = "stale hint";

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal(string.Empty, bar.Text);
    }
}
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FocusHintBarTests"`
Expected: build error — `DialogEditor.Avalonia.Shared.FocusHintBar` does not exist yet.

- [ ] **Step 3: Create the control's XAML**

Create `DialogEditor.Avalonia.Shared/FocusHintBar.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="DialogEditor.Avalonia.Shared.FocusHintBar"
             x:CompileBindings="False">

    <!-- Gaps.md a11y item 13: passive display of the focused control's HelpText for
         sighted keyboard users, mirroring MainWindow's status bar styling exactly.
         Always visible (even when empty) to avoid layout shift on the first Tab press;
         no ToolTip/live-region — see design spec §"Explicitly not included". -->
    <Border Background="{DynamicResource Brush.Surface.Card}" Padding="8,4">
        <TextBlock x:Name="HintText"
                   Text=""
                   FontSize="11"
                   Foreground="{DynamicResource Brush.Text.Muted}"/>
    </Border>
</UserControl>
```

- [ ] **Step 4: Create the control's code-behind**

Create `DialogEditor.Avalonia.Shared/FocusHintBar.axaml.cs`:

```csharp
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;

namespace DialogEditor.Avalonia.Shared;

public partial class FocusHintBar : UserControl
{
    public FocusHintBar() => InitializeComponent();

    public string Text
    {
        get => HintText.Text ?? string.Empty;
        set => HintText.Text = value;
    }

    /// <summary>
    /// Mirrors MainWindow.OnAnyGotFocus (item 5 Part B): on any focus change within
    /// <paramref name="window"/>, copy the focused element's HelpText into Text.
    /// </summary>
    public void AttachTo(Window window)
        => window.AddHandler(InputElement.GotFocusEvent, OnGotFocus, RoutingStrategies.Bubble);

    private void OnGotFocus(object? sender, GotFocusEventArgs e)
        => Text = e.Source is StyledElement el
            ? AutomationProperties.GetHelpText(el) ?? string.Empty
            : string.Empty;
}
```

- [ ] **Step 5: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FocusHintBarTests"`
Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.Tests/Controls/FocusHintBarTests.cs DialogEditor.Avalonia.Shared/FocusHintBar.axaml DialogEditor.Avalonia.Shared/FocusHintBar.axaml.cs
git commit -m "feat(a11y): add FocusHintBar shared control (Gaps item 13)"
```

---

## Task 2: `FocusHintBarPresenceTests` (RED before rollout)

**Files:**
- Create: `DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs`

- [ ] **Step 1: Write the failing presence-scan test**

Create `DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs`:

```csharp
using System.Xml.Linq;

namespace DialogEditor.Tests.Accessibility;

/// <summary>
/// Gaps.md a11y item 13: each of the 10 "workhorse" secondary windows must carry a
/// &lt;shared:FocusHintBar x:Name="HintBar"/&gt; (see design spec
/// docs/superpowers/specs/2026-06-13-focus-hint-bar-design.md Part 2). Solution-wide
/// scan anchored on DialogEditor.slnx, mirroring AutomationHelpTextTests/
/// FakeWatermarkTests' pattern — a future accidental removal fails this too.
/// </summary>
public class FocusHintBarPresenceTests
{
    private static readonly string[] WindowsInScope =
    {
        "SettingsWindow.axaml",
        "ScriptEditorWindow.axaml",
        "ConditionEditorWindow.axaml",
        "FindReplaceWindow.axaml",
        "DiffWindow.axaml",
        "BatchReplaceWindow.axaml",
        "ExportConversationsWindow.axaml",
        "FlowAnalyticsWindow.axaml",
        "BranchesWindow.axaml",
        "GitConflictResolutionWindow.axaml",
    };

    private static readonly XNamespace XamlNs = "http://schemas.microsoft.com/winfx/2006/xaml";

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static bool IsExcluded(string path) =>
        path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}") ||
        path.Contains($"{Path.DirectorySeparatorChar}.worktrees{Path.DirectorySeparatorChar}");

    public static IEnumerable<object[]> WindowFiles() => WindowsInScope.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(WindowFiles))]
    public void WindowHasFocusHintBar(string fileName)
    {
        var root = SolutionRoot();
        var matches = Directory.EnumerateFiles(root, fileName, SearchOption.AllDirectories)
            .Where(f => !IsExcluded(f))
            .ToList();

        Assert.Single(matches);
        var doc = XDocument.Load(matches[0]);

        var hasHintBar = doc.Descendants()
            .Where(e => e.Name.LocalName == "FocusHintBar")
            .Any(e => e.Attribute(XamlNs + "Name")?.Value == "HintBar");

        Assert.True(hasHintBar, $"{fileName} is missing <shared:FocusHintBar x:Name=\"HintBar\"/>");
    }
}
```

- [ ] **Step 2: Run the test to verify it fails for all 10 windows**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FocusHintBarPresenceTests"`
Expected: FAIL — 10 failures, one per window (none have `FocusHintBar` yet).

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs
git commit -m "test(a11y): add FocusHintBarPresenceTests (RED) for Gaps item 13 rollout"
```

---

## Task 3: Roll out to `SettingsWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/SettingsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/SettingsWindow.axaml.cs`

`SettingsWindow.axaml` already declares `xmlns:shared`. Its root `Grid` has
`RowDefinitions="*,Auto" Margin="16"` with the content `StackPanel` in row 0 and the
Close button in row 1.

- [ ] **Step 1: Add the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/SettingsWindow.axaml`, change:

```xml
    <Grid RowDefinitions="*,Auto" Margin="16">
```

to:

```xml
    <Grid RowDefinitions="*,Auto,Auto" Margin="16">
```

Then, immediately after the closing `</Button>` of the Close button (the last element
before `</Grid>`), add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>
```

So the end of the file reads:

```xml
        <!-- Close button -->
        <Button Grid.Row="1"
                Content="{StaticResource Settings_Close}"
                HorizontalAlignment="Right"
                Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.Secondary}" BorderThickness="0"
                Padding="16,5" Margin="0,12,0,0"
                Click="Close_Click"/>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>

    </Grid>
</Window>
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/SettingsWindow.axaml.cs`, change:

```csharp
    public SettingsWindow()
    {
        InitializeComponent();
        // The theme picker is self-contained: it owns its ViewModel + applier rather than
        // sharing the window's SettingsViewModel, so the same control drops into PatchManager.
        ThemePicker.DataContext = new ThemePickerViewModel(new ThemeApplier());
    }
```

to:

```csharp
    public SettingsWindow()
    {
        InitializeComponent();
        // The theme picker is self-contained: it owns its ViewModel + applier rather than
        // sharing the window's SettingsViewModel, so the same control drops into PatchManager.
        ThemePicker.DataContext = new ThemePickerViewModel(new ThemeApplier());
        HintBar.AttachTo(this);
    }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/SettingsWindow.axaml DialogEditor.Avalonia/Views/SettingsWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to SettingsWindow (Gaps item 13)"
```

---

## Task 4: Roll out to `ScriptEditorWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml.cs`

`ScriptEditorWindow.axaml`'s root `Grid` has `RowDefinitions="*,Auto"`, with the
`ScrollViewer` in row 0 and the footer `Border` in row 1.

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml`, change the root element's
namespace declarations from:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.ScriptEditorWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.ScriptEditorWindow"
```

Then change:

```xml
    <Grid RowDefinitions="*,Auto">
```

to:

```xml
    <Grid RowDefinitions="*,Auto,Auto">
```

Then, immediately after the footer `Border`'s closing `</Border>` (the last element
before `</Grid>`), add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>
```

So the end of the file reads:

```xml
        <!-- Footer -->
        <Border Grid.Row="1" Background="{DynamicResource Brush.Surface.Panel}" Padding="14,10">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
                <Button Classes="footer-btn" Content="Cancel"
                        Command="{Binding CancelCommand}"
                        ToolTip.Tip="{StaticResource ToolTip_ScriptEditorCancel}"
                        AutomationProperties.HelpText="{StaticResource ToolTip_ScriptEditorCancel}"/>
                <Button Classes="footer-btn primary" Content="OK"
                        Command="{Binding ConfirmCommand}"
                        ToolTip.Tip="{StaticResource ToolTip_ScriptEditorOK}"
                        AutomationProperties.HelpText="{StaticResource ToolTip_ScriptEditorOK}"/>
            </StackPanel>
        </Border>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>

    </Grid>
</Window>
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml.cs`, change:

```csharp
    public ScriptEditorWindow() => InitializeComponent();
```

to:

```csharp
    public ScriptEditorWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }
```

(The second constructor, `ScriptEditorWindow(ScriptEditorViewModel vm) : this()`,
chains to the parameterless one and needs no change.)

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml DialogEditor.Avalonia/Views/ScriptEditorWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to ScriptEditorWindow (Gaps item 13)"
```

---

## Task 5: Roll out to `ConditionEditorWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml.cs`

`ConditionEditorWindow.axaml`'s root `Grid` has `RowDefinitions="*,Auto"`, with the
`ScrollViewer` in row 0 and the footer `Border` in row 1.

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml`, change:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:types="clr-namespace:DialogEditor.ViewModels.Services;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.ConditionEditorWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:types="clr-namespace:DialogEditor.ViewModels.Services;assembly=DialogEditor.ViewModels"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.ConditionEditorWindow"
```

Then change:

```xml
    <Grid RowDefinitions="*,Auto">
```

to:

```xml
    <Grid RowDefinitions="*,Auto,Auto">
```

Then, immediately after the footer `Border`'s closing `</Border>` (the last element
before `</Grid>`), add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>
```

So the end of the file reads:

```xml
        <!-- Footer -->
        <Border Grid.Row="1" Background="{DynamicResource Brush.Surface.Panel}" Padding="14,10">
            <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
                <Button Classes="footer-btn"
                        Content="Cancel"
                        Command="{Binding CancelCommand}"
                        ToolTip.Tip="{StaticResource ToolTip_ConditionEditorCancel}"
                        AutomationProperties.HelpText="{StaticResource ToolTip_ConditionEditorCancel}"/>
                <Button Classes="footer-btn primary"
                        Content="OK"
                        Command="{Binding ConfirmCommand}"
                        ToolTip.Tip="{StaticResource ToolTip_ConditionEditorOK}"
                        AutomationProperties.HelpText="{StaticResource ToolTip_ConditionEditorOK}"/>
            </StackPanel>
        </Border>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>

    </Grid>
</Window>
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml.cs`, change:

```csharp
    public ConditionEditorWindow() => InitializeComponent();
```

to:

```csharp
    public ConditionEditorWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }
```

(The second constructor, `ConditionEditorWindow(ConditionEditorViewModel vm) : this()`,
chains to the parameterless one — including the recursive sub-window construction in
`EditBranchGroup_Click` — and needs no change.)

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml DialogEditor.Avalonia/Views/ConditionEditorWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to ConditionEditorWindow (Gaps item 13)"
```

---

## Task 6: Roll out to `FindReplaceWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/FindReplaceWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/FindReplaceWindow.axaml.cs`

`FindReplaceWindow.axaml`'s root `Grid` has `RowDefinitions="Auto,Auto,Auto,Auto"
Margin="14,10,14,10"`, with rows 0–3 already used (find row, replace row, options row,
buttons `WrapPanel`).

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/FindReplaceWindow.axaml`, change:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.FindReplaceWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.FindReplaceWindow"
```

Then change:

```xml
    <Grid RowDefinitions="Auto,Auto,Auto,Auto" Margin="14,10,14,10">
```

to:

```xml
    <Grid RowDefinitions="Auto,Auto,Auto,Auto,Auto" Margin="14,10,14,10">
```

Then, immediately after the buttons `WrapPanel`'s closing `</WrapPanel>` (the last
element before `</Grid>`), add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="4" x:Name="HintBar"/>
```

So the end of the file reads:

```xml
        <!-- Buttons -->
        <WrapPanel Grid.Row="3" Orientation="Horizontal" ItemWidth="88">
            <Button Classes="action-btn" Margin="0,0,4,4"
                    Content="{StaticResource FindReplace_FindAll}"
                    Command="{Binding FindCommand}"
                    ToolTip.Tip="{StaticResource ToolTip_FindAll}"
                    AutomationProperties.HelpText="{StaticResource ToolTip_FindAll}"/>
            <Button Classes="action-btn" Margin="0,0,4,4"
                    Content="{StaticResource FindReplace_FindPrev}"
                    Command="{Binding FindPrevCommand}"
                    ToolTip.Tip="{StaticResource ToolTip_FindPrev}"
                    AutomationProperties.HelpText="{StaticResource ToolTip_FindPrev}"/>
            <Button Classes="action-btn" Margin="0,0,4,4"
                    Content="{StaticResource FindReplace_FindNext}"
                    Command="{Binding FindNextCommand}"
                    ToolTip.Tip="{StaticResource ToolTip_FindNext}"
                    AutomationProperties.HelpText="{StaticResource ToolTip_FindNext}"/>
            <Button Classes="action-btn" Margin="0,0,4,4"
                    Content="{StaticResource FindReplace_ReplaceOne}"
                    Command="{Binding ReplaceCommand}"
                    ToolTip.Tip="{StaticResource ToolTip_Replace}"
                    AutomationProperties.HelpText="{StaticResource ToolTip_Replace}"/>
            <Button Classes="primary-btn" Margin="0,0,4,4"
                    Content="{StaticResource FindReplace_ReplaceAll}"
                    Command="{Binding ReplaceAllCommand}"
                    ToolTip.Tip="{StaticResource ToolTip_ReplaceAll}"
                    AutomationProperties.HelpText="{StaticResource ToolTip_ReplaceAll}"/>
        </WrapPanel>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="4" x:Name="HintBar"/>

    </Grid>
</Window>
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/FindReplaceWindow.axaml.cs`, change:

```csharp
    public FindReplaceWindow() => InitializeComponent();
```

to:

```csharp
    public FindReplaceWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }
```

(The second constructor, `FindReplaceWindow(FindReplaceViewModel vm) : this()`, chains
to the parameterless one and needs no change.)

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/FindReplaceWindow.axaml DialogEditor.Avalonia/Views/FindReplaceWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to FindReplaceWindow (Gaps item 13)"
```

---

## Task 7: Roll out to `DiffWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/DiffWindow.axaml.cs`

`DiffWindow.axaml`'s root is a `DockPanel`. Its last child is the fill `<Grid>` (no
`DockPanel.Dock` attribute) containing the changed-conversations list and canvas.

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/DiffWindow.axaml`, change:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:diff="clr-namespace:DialogEditor.Patch.Diff;assembly=DialogEditor.Patch"
        xmlns:views="clr-namespace:DialogEditor.Avalonia.Views"
        xmlns:controls="clr-namespace:DialogEditor.Avalonia.Controls"
        x:Class="DialogEditor.Avalonia.Views.DiffWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:diff="clr-namespace:DialogEditor.Patch.Diff;assembly=DialogEditor.Patch"
        xmlns:views="clr-namespace:DialogEditor.Avalonia.Views"
        xmlns:controls="clr-namespace:DialogEditor.Avalonia.Controls"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.DiffWindow"
```

Then, immediately before the `<!-- ── Main area: list + detail ──... -->` comment and
its `<Grid>` (the fill element), add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar DockPanel.Dock="Bottom" x:Name="HintBar"/>

```

So that region reads:

```xml
        <!-- ── Dangling-link panel ... -->
        <Expander DockPanel.Dock="Bottom"
                  x:Name="DanglingPanel"
                  ...
        </Expander>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar DockPanel.Dock="Bottom" x:Name="HintBar"/>

        <!-- ── Main area: list + detail ───────────────────────────────── -->
        <Grid>
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/DiffWindow.axaml.cs`, change:

```csharp
    public DiffWindow() => InitializeComponent();

    public DiffWindow(DiffViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
```

to:

```csharp
    public DiffWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public DiffWindow(DiffViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        HintBar.AttachTo(this);
    }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/DiffWindow.axaml DialogEditor.Avalonia/Views/DiffWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to DiffWindow (Gaps item 13)"
```

---

## Task 8: Roll out to `BatchReplaceWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml.cs`

`BatchReplaceWindow.axaml`'s root `Grid` has `RowDefinitions="Auto,*,Auto"
Margin="14,12,14,12"`, with the search form in row 0, results `ScrollViewer` in row 1,
and the status `TextBlock` in row 2.

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml`, change:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.BatchReplaceWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.BatchReplaceWindow"
```

Then change:

```xml
    <Grid RowDefinitions="Auto,*,Auto" Margin="14,12,14,12">
```

to:

```xml
    <Grid RowDefinitions="Auto,*,Auto,Auto" Margin="14,12,14,12">
```

Then, immediately after the status `TextBlock` (the last element before `</Grid>`),
add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="3" x:Name="HintBar"/>
```

So the end of the file reads:

```xml
        <!-- ── Status bar ────────────────────────────────────────────────── -->
        <TextBlock Grid.Row="2"
                   Text="{Binding StatusText}"
                   Foreground="{DynamicResource Brush.Text.Muted}" FontSize="11"
                   VerticalAlignment="Center"/>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="3" x:Name="HintBar"/>
    </Grid>

</Window>
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml.cs`, change:

```csharp
    public BatchReplaceWindow() => InitializeComponent();
```

to:

```csharp
    public BatchReplaceWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }
```

(The second constructor, `BatchReplaceWindow(BatchReplaceViewModel vm) : this()`,
chains to the parameterless one and needs no change.)

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml DialogEditor.Avalonia/Views/BatchReplaceWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to BatchReplaceWindow (Gaps item 13)"
```

---

## Task 9: Roll out to `ExportConversationsWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml.cs`

`ExportConversationsWindow.axaml`'s root is a `DockPanel`. Its last child is the
"Format selector" `StackPanel` (no `DockPanel.Dock` attribute — the fill element).

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml`, change:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.ExportConversationsWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.ExportConversationsWindow"
```

Then, immediately before the `<!-- ── Format selector ──... -->` comment and its
`<StackPanel>` (the fill element), add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar DockPanel.Dock="Bottom" x:Name="HintBar"/>

```

So that region reads:

```xml
        <!-- ── Footer ────────────────────────────────────────────────── -->
        <Grid DockPanel.Dock="Bottom" Margin="0,8,0,0">
            ...
        </Grid>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar DockPanel.Dock="Bottom" x:Name="HintBar"/>

        <!-- ── Format selector ───────────────────────────────────────── -->
        <StackPanel Margin="0,0,0,12">
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml.cs`, change:

```csharp
    public ExportConversationsWindow() => InitializeComponent();

    public ExportConversationsWindow(ExportConversationsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
    }
```

to:

```csharp
    public ExportConversationsWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public ExportConversationsWindow(ExportConversationsViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        HintBar.AttachTo(this);
    }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml DialogEditor.Avalonia/Views/ExportConversationsWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to ExportConversationsWindow (Gaps item 13)"
```

---

## Task 10: Roll out to `FlowAnalyticsWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml.cs`

`FlowAnalyticsWindow.axaml`'s root `Grid` has `RowDefinitions="Auto,*,Auto"
Margin="14,12,14,12"`, with the stats/placeholder panel in row 0, issues
`ScrollViewer` in row 1, and the bottom bar `Grid` in row 2.

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml`, change:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.FlowAnalyticsWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.FlowAnalyticsWindow"
```

Then change:

```xml
    <Grid RowDefinitions="Auto,*,Auto" Margin="14,12,14,12">
```

to:

```xml
    <Grid RowDefinitions="Auto,*,Auto,Auto" Margin="14,12,14,12">
```

Then, immediately after the bottom bar `Grid`'s closing `</Grid>` (the last element
before the root `</Grid>`), add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="3" x:Name="HintBar"/>
```

So the end of the file reads:

```xml
        <!-- ── Bottom bar ─────────────────────────────────────────────────── -->
        <Grid Grid.Row="2" ColumnDefinitions="*,Auto">
            <TextBlock Grid.Column="0"
                       Text="{Binding LastAnalysed}"
                       Foreground="{DynamicResource Brush.Text.Disabled}" FontSize="10"
                       VerticalAlignment="Center"/>
            <Button Grid.Column="1" Classes="action-btn"
                    Content="{StaticResource FlowAnalytics_Refresh}"
                    Command="{Binding RefreshCommand}"
                    ToolTip.Tip="{StaticResource ToolTip_FlowAnalytics_Refresh}"
                    AutomationProperties.HelpText="{StaticResource ToolTip_FlowAnalytics_Refresh}"/>
        </Grid>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="3" x:Name="HintBar"/>

    </Grid>

</Window>
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml.cs`, change:

```csharp
    public FlowAnalyticsWindow() => InitializeComponent();
```

to:

```csharp
    public FlowAnalyticsWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }
```

(The second constructor, `FlowAnalyticsWindow(FlowAnalyticsViewModel vm) : this()`,
chains to the parameterless one and needs no change.)

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml DialogEditor.Avalonia/Views/FlowAnalyticsWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to FlowAnalyticsWindow (Gaps item 13)"
```

---

## Task 11: Roll out to `BranchesWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/BranchesWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/BranchesWindow.axaml.cs`

`BranchesWindow.axaml`'s root is a `DockPanel`. Its last child is the `ListBox`
(no `DockPanel.Dock` attribute — the fill element).

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/BranchesWindow.axaml`, change:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        x:Class="DialogEditor.Avalonia.Views.BranchesWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:vm="clr-namespace:DialogEditor.ViewModels;assembly=DialogEditor.ViewModels"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.BranchesWindow"
```

Then, immediately before the `<ListBox x:Name="BranchList" ...>` element (the fill
element), add:

```xml
    <shared:FocusHintBar DockPanel.Dock="Bottom" x:Name="HintBar"/>

```

So that region reads:

```xml
    <StackPanel DockPanel.Dock="Bottom" Orientation="Horizontal" Spacing="6" Margin="0,8,0,0">
      <Button x:Name="SwitchButton" ... />
      ...
      <Button x:Name="CloseButton" Content="{StaticResource Branches_Close}"/>
    </StackPanel>

    <shared:FocusHintBar DockPanel.Dock="Bottom" x:Name="HintBar"/>

    <ListBox x:Name="BranchList" ItemsSource="{Binding Branches}"
             SelectedItem="{Binding Selected}">
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/BranchesWindow.axaml.cs`, change:

```csharp
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000/AVLN3001).
    public BranchesWindow() => InitializeComponent();

    public BranchesWindow(BranchesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
```

to:

```csharp
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000/AVLN3001).
    public BranchesWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public BranchesWindow(BranchesViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
        HintBar.AttachTo(this);
    }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/BranchesWindow.axaml DialogEditor.Avalonia/Views/BranchesWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to BranchesWindow (Gaps item 13)"
```

---

## Task 12: Roll out to `GitConflictResolutionWindow`

**Files:**
- Modify: `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs`

`GitConflictResolutionWindow.axaml`'s root `Grid` has `Margin="16"
RowDefinitions="Auto,*,Auto" ColumnDefinitions="260,*"`, with the intro `TextBlock`
spanning both columns in row 0, the conflict list + detail in row 1, and the footer
`StackPanel` (column 1 only) in row 2.

- [ ] **Step 1: Add `xmlns:shared` and the hint bar to the XAML**

In `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml`, change:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.GitConflictResolutionWindow"
```

to:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.GitConflictResolutionWindow"
```

Then change:

```xml
    <Grid Margin="16" RowDefinitions="Auto,*,Auto" ColumnDefinitions="260,*">
```

to:

```xml
    <Grid Margin="16" RowDefinitions="Auto,*,Auto,Auto" ColumnDefinitions="260,*">
```

Then, immediately after the footer `StackPanel`'s closing `</StackPanel>` (the last
element before `</Grid>`), add:

```xml
        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="3" Grid.ColumnSpan="2" x:Name="HintBar"/>
```

So the end of the file reads:

```xml
        <!-- Footer -->
        <StackPanel Grid.Row="2" Grid.Column="1" Orientation="Horizontal"
                    HorizontalAlignment="Right" Spacing="8" Margin="0,14,0,0">
            <Button x:Name="CancelButton"
                    ... />
            <Button x:Name="ApplyButton"
                    ... />
        </StackPanel>

        <!-- Focus hint bar (Gaps item 13) -->
        <shared:FocusHintBar Grid.Row="3" Grid.ColumnSpan="2" x:Name="HintBar"/>

    </Grid>

</Window>
```

- [ ] **Step 2: Wire `AttachTo` in the code-behind**

In `DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs`, change:

```csharp
    // Parameterless constructor required so the XAML compiler embeds this type
    // (avoids AVLN3000 wiping precompiled resources on a clean build).
    public GitConflictResolutionWindow() => InitializeComponent();

    public GitConflictResolutionWindow(GitConflictResolutionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.RequestClose += Close;
        CancelButton.Click += (_, _) => Close();
        vm.PropertyChanged += OnVmPropertyChanged;
        UpdateDiff(vm.Selected);
    }
```

to:

```csharp
    // Parameterless constructor required so the XAML compiler embeds this type
    // (avoids AVLN3000 wiping precompiled resources on a clean build).
    public GitConflictResolutionWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public GitConflictResolutionWindow(GitConflictResolutionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.RequestClose += Close;
        CancelButton.Click += (_, _) => Close();
        vm.PropertyChanged += OnVmPropertyChanged;
        UpdateDiff(vm.Selected);
        HintBar.AttachTo(this);
    }
```

- [ ] **Step 3: Build to verify**

Run: `dotnet build DialogEditor.Avalonia`
Expected: builds with no errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml DialogEditor.Avalonia/Views/GitConflictResolutionWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to GitConflictResolutionWindow (Gaps item 13)"
```

---

## Task 13: Run the presence-test suite (GREEN) and commit

**Files:**
- (none — verification only)

- [ ] **Step 1: Run the presence test**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~FocusHintBarPresenceTests"`
Expected: PASS — all 10 cases now green.

This task has no code changes; it's a checkpoint confirming Tasks 3–12 are complete
before moving on to the representative end-to-end tests.

---

## Task 14: End-to-end test — `DiffWindow`

**Files:**
- Modify: `DialogEditor.Tests/Views/DiffWindowTests.cs`

`AutoPullCheck` (the `CheckBox` named `AutoPullCheck`) carries
`AutomationProperties.HelpText="{StaticResource ToolTip_Diff_AutoPull}"` and is already
used by `AutoPullCheckbox_DefaultsChecked_AndBindsToViewModel`.

- [ ] **Step 1: Write the failing test**

In `DialogEditor.Tests/Views/DiffWindowTests.cs`, add these two `using` statements at
the top of the file (alongside the existing ones):

```csharp
using Avalonia.Automation;
using Avalonia.Input;
```

Then add the following test method inside the `DiffWindowTests` class, after
`AutoPullCheckbox_DefaultsChecked_AndBindsToViewModel`:

```csharp
    [AvaloniaFact]
    public void Tab_ToControlWithHelpText_UpdatesHintBar()
    {
        var disk = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1), Node(2)], [], []));
        var refp = DialogProject.Empty("p").WithPatch(
            new ConversationPatch("greeting", ConversationPatch.CurrentSchemaVersion, [Node(1)], [], []));

        var path = WriteTempProject(disk);
        var dir  = Path.GetDirectoryName(Path.GetFullPath(path))!;
        var git  = MakeFakeGit(dir, refContent: DialogProjectSerializer.Serialize(refp));
        var vm   = new DiffViewModel(git, new StubDispatcher(), path);

        var window = new DiffWindow(vm);
        window.Show();

        var checkbox = window.FindControl<CheckBox>("AutoPullCheck")!;
        var expectedHint = AutomationProperties.GetHelpText(checkbox);
        Assert.False(string.IsNullOrEmpty(expectedHint));

        checkbox.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal(expectedHint, window.HintBar.Text);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffWindowTests.Tab_ToControlWithHelpText_UpdatesHintBar"`
Expected: FAIL — if Tasks 7 and 13 are already done, this should actually PASS; if it
fails, check that `DiffWindow.axaml` has `x:Name="HintBar"` and that
`HintBar.AttachTo(this)` is called in both constructors (Task 7).

- [ ] **Step 3: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~DiffWindowTests.Tab_ToControlWithHelpText_UpdatesHintBar"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Tests/Views/DiffWindowTests.cs
git commit -m "test(a11y): verify FocusHintBar wiring end-to-end on DiffWindow (Gaps item 13)"
```

---

## Task 15: End-to-end test — `BranchesWindow`

**Files:**
- Modify: `DialogEditor.Tests/Views/BranchesWindowTests.cs`

`SwitchButton` carries `AutomationProperties.HelpText="{StaticResource
Branches_SwitchTip}"` and is already used by `SwitchButton_DisabledUntilSelected`.

- [ ] **Step 1: Write the failing test**

In `DialogEditor.Tests/Views/BranchesWindowTests.cs`, add these two `using` statements
at the top of the file (alongside the existing ones):

```csharp
using Avalonia.Automation;
using Avalonia.Input;
```

Then add the following test method inside the `BranchesWindowTests` class, after
`SwitchButton_DisabledUntilSelected`:

```csharp
    [AvaloniaFact]
    public void Tab_ToControlWithHelpText_UpdatesHintBar()
    {
        var vm = TwoBranchesVm();
        var win = new BranchesWindow(vm);
        win.Show();

        var button = win.FindControl<Button>("SwitchButton")!;
        var expectedHint = AutomationProperties.GetHelpText(button);
        Assert.False(string.IsNullOrEmpty(expectedHint));

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal(expectedHint, win.HintBar.Text);
    }
```

- [ ] **Step 2: Run the test to verify it fails**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~BranchesWindowTests.Tab_ToControlWithHelpText_UpdatesHintBar"`
Expected: FAIL — if Tasks 11 and 13 are already done, this should actually PASS; if it
fails, check that `BranchesWindow.axaml` has `x:Name="HintBar"` and that
`HintBar.AttachTo(this)` is called in the `BranchesWindow(BranchesViewModel vm)`
constructor (Task 11).

- [ ] **Step 3: Run the test to verify it passes**

Run: `dotnet test DialogEditor.Tests --filter "FullyQualifiedName~BranchesWindowTests.Tab_ToControlWithHelpText_UpdatesHintBar"`
Expected: PASS.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Tests/Views/BranchesWindowTests.cs
git commit -m "test(a11y): verify FocusHintBar wiring end-to-end on BranchesWindow (Gaps item 13)"
```

---

## Task 16: Full test suite + mark Gaps item 13 done

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Run the full test suite**

Run: `dotnet test DialogEditor.Tests`
Expected: PASS — all tests green, including the new `FocusHintBarTests`,
`FocusHintBarPresenceTests`, and the two end-to-end additions.

- [ ] **Step 2: Mark Gaps item 13 as implemented**

In `Gaps.md`, change:

```markdown
13. **Focused-control hint is MainWindow-only.** Item 5's status-bar hint (Part B)
    depends on `MainWindow`'s status bar, which other windows/dialogs
    (`SettingsWindow`, `ScriptEditorWindow`, `ConditionEditorWindow`, `FindReplaceWindow`,
    `DiffWindow`, etc.) don't have — their `AutomationProperties.HelpText` (mirrored
    solution-wide by item 5's Part A) is reachable by screen readers but has no
    sighted-keyboard-user equivalent there. Worth a lightweight hint surface (e.g. a
    bottom hint bar) for dialogs once item 5's pattern proves out.
```

to:

```markdown
13. **✅ IMPLEMENTED (2026-06-13).** Focused-control hint is no longer MainWindow-only.
    A new shared `FocusHintBar` control (`DialogEditor.Avalonia.Shared`) mirrors the
    focused control's `AutomationProperties.HelpText` into a passive status-bar-styled
    bar, the same way item 5 Part B's `MainWindow.OnAnyGotFocus` feeds the status bar.
    Rolled out to the 10 "workhorse" windows: `SettingsWindow`, `ScriptEditorWindow`,
    `ConditionEditorWindow`, `FindReplaceWindow`, `DiffWindow`, `BatchReplaceWindow`,
    `ExportConversationsWindow`, `FlowAnalyticsWindow`, `BranchesWindow`,
    `GitConflictResolutionWindow`. The remaining 7 small 1–3-control dialogs are
    tracked separately as item 16. See
    docs/superpowers/specs/2026-06-13-focus-hint-bar-design.md.
```

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark a11y item 13 (focus-hint bar) implemented"
```

---

## Self-Review Notes

- **Spec coverage:** Part 1 (control) → Task 1. Part 2 (rollout) → Tasks 3–12, one per
  named window, each using the placement rule (Grid `Auto` row vs. `DockPanel.Dock="Bottom"`
  before the fill element) from the spec. Testing section → Task 1 (component tests),
  Task 2 + 13 (presence scan, red→green), Tasks 14–15 (DiffWindow/BranchesWindow
  end-to-end). Out-of-scope items (the 7 small dialogs, visibility toggle, live regions)
  are deliberately not implemented here — already tracked as Gaps item 16.
- **Placeholder scan:** no TBD/TODO; every step shows the exact code/XAML to write.
- **Type consistency:** `FocusHintBar.Text` (string property) and `AttachTo(Window)`
  (Task 1) are used identically in Tasks 3–15 and in the component tests.
