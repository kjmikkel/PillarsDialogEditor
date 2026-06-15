# Focus-hint bar for small dialogs (Gaps item 16) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add the shared `FocusHintBar` control to the 3 small dialogs whose `AutomationProperties.HelpText` carries information beyond their visible text (`AboutWindow`, `ConflictResolutionDialog`, `HistoryWindow`), and document why the other 4 small dialogs are deliberately left unchanged.

**Architecture:** Reuses the `FocusHintBar` shared `UserControl` and `AttachTo(Window)` wiring established for Gaps item 13 — no new control code. Each window adds `xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"`, places `<shared:FocusHintBar x:Name="HintBar"/>` as the last element in its layout, and calls `HintBar.AttachTo(this)` in every constructor that calls `InitializeComponent()`.

**Tech Stack:** Avalonia 11 (Headless + XUnit for tests), C#, XAML. Design spec: `docs/superpowers/specs/2026-06-13-focus-hint-bar-small-dialogs-design.md`.

---

### Task 1: Extend `FocusHintBarPresenceTests` with the item-16 windows (RED)

**Files:**
- Modify: `DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs`

The current file (64 lines) has one `WindowsInScope` array (the 10 item-13 windows), one `[Theory]`/`[MemberData(nameof(WindowFiles))]` pair, and the assertion logic inline in `WindowHasFocusHintBar`. This task adds a second array/theory pair for the 3 item-16 windows and extracts the shared assertion logic so both theories reuse it.

- [ ] **Step 1: Replace the whole file with the extended version**

Replace the entire contents of `DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs` with:

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

    /// <summary>
    /// Gaps.md a11y item 16: of the 7 small 1-3-control dialogs left out of item 13,
    /// only these 3 have at least one AutomationProperties.HelpText value that adds
    /// information beyond text already visible in the dialog (see design spec
    /// docs/superpowers/specs/2026-06-13-focus-hint-bar-small-dialogs-design.md). The
    /// other 4 (BranchNameDialog, CommitConsentDialog, ChangelogWindow,
    /// ForceDeleteDialog) deliberately do NOT get a FocusHintBar — their HelpText
    /// duplicates visible text, so a bar would only echo the screen.
    /// </summary>
    private static readonly string[] WindowsInScopeItem16 =
    {
        "AboutWindow.axaml",
        "ConflictResolutionDialog.axaml",
        "HistoryWindow.axaml",
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

    private static void AssertHasFocusHintBar(string fileName)
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

    public static IEnumerable<object[]> WindowFiles() => WindowsInScope.Select(f => new object[] { f });

    public static IEnumerable<object[]> WindowFilesItem16() => WindowsInScopeItem16.Select(f => new object[] { f });

    [Theory]
    [MemberData(nameof(WindowFiles))]
    public void WindowHasFocusHintBar(string fileName) => AssertHasFocusHintBar(fileName);

    [Theory]
    [MemberData(nameof(WindowFilesItem16))]
    public void Item16WindowHasFocusHintBar(string fileName) => AssertHasFocusHintBar(fileName);
}
```

- [ ] **Step 2: Run the new theory to confirm it fails for all 3 new windows**

Run: `dotnet test --filter "FullyQualifiedName~FocusHintBarPresenceTests"`

Expected: `Item16WindowHasFocusHintBar` FAILs for `AboutWindow.axaml`, `ConflictResolutionDialog.axaml`, and `HistoryWindow.axaml` (3 failures), each with message `"<file> is missing <shared:FocusHintBar x:Name=\"HintBar\"/>"`. The original `WindowHasFocusHintBar` theory still PASSes for all 10 item-13 windows (10 passes). Total: 10 passed, 3 failed, 13 total.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Tests/Accessibility/FocusHintBarPresenceTests.cs
git commit -m "test(a11y): add item-16 FocusHintBar presence theory (RED)"
```

---

### Task 2: ConflictResolutionDialog — wiring test (RED) + FocusHintBar (GREEN)

**Files:**
- Modify: `DialogEditor.Tests/Views/ConflictResolutionDialogTests.cs`
- Modify: `DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml`
- Modify: `DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml.cs`

This is the window chosen for the item-13-style end-to-end wiring test: its Cancel/Force buttons each have a distinct, multi-sentence `HelpText`, so focusing one then the other gives a clear "the bar's text changes" demonstration.

- [ ] **Step 1: Write the failing wiring test**

The current `DialogEditor.Tests/Views/ConflictResolutionDialogTests.cs` is:

```csharp
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Views;

public class ConflictResolutionDialogTests
{
    private static PatchConflictException MakeEx() =>
        new(42, "DefaultText", "old value", "new value");

    [AvaloniaFact]
    public void ForceButton_SetsForceApplyTrue()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        dialog.FindControl<Button>("ForceButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(dialog.ForceApply);
    }

    [AvaloniaFact]
    public void CancelButton_LeavesForceApplyFalse()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(dialog.ForceApply);
    }

    [AvaloniaFact]
    public void Constructor_PopulatesDetailFields()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        Assert.Equal("42",        dialog.FindControl<TextBlock>("NodeIdBlock")!.Text);
        Assert.Equal("DefaultText", dialog.FindControl<TextBlock>("FieldNameBlock")!.Text);
        Assert.Equal("old value", dialog.FindControl<TextBlock>("ExpectedBlock")!.Text);
        Assert.Equal("new value", dialog.FindControl<TextBlock>("ActualBlock")!.Text);
    }
}
```

Replace it with (adds two `using`s and one new test at the end):

```csharp
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Views;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Views;

public class ConflictResolutionDialogTests
{
    private static PatchConflictException MakeEx() =>
        new(42, "DefaultText", "old value", "new value");

    [AvaloniaFact]
    public void ForceButton_SetsForceApplyTrue()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        dialog.FindControl<Button>("ForceButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(dialog.ForceApply);
    }

    [AvaloniaFact]
    public void CancelButton_LeavesForceApplyFalse()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        dialog.FindControl<Button>("CancelButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.False(dialog.ForceApply);
    }

    [AvaloniaFact]
    public void Constructor_PopulatesDetailFields()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();
        Assert.Equal("42",        dialog.FindControl<TextBlock>("NodeIdBlock")!.Text);
        Assert.Equal("DefaultText", dialog.FindControl<TextBlock>("FieldNameBlock")!.Text);
        Assert.Equal("old value", dialog.FindControl<TextBlock>("ExpectedBlock")!.Text);
        Assert.Equal("new value", dialog.FindControl<TextBlock>("ActualBlock")!.Text);
    }

    [AvaloniaFact]
    public void Tab_ToControlWithHelpText_UpdatesHintBar()
    {
        var dialog = new ConflictResolutionDialog(MakeEx());
        dialog.Show();

        var button = dialog.FindControl<Button>("ForceButton")!;
        var expectedHint = AutomationProperties.GetHelpText(button);
        Assert.False(string.IsNullOrEmpty(expectedHint));

        button.RaiseEvent(new GotFocusEventArgs
        {
            RoutedEvent = InputElement.GotFocusEvent,
            NavigationMethod = NavigationMethod.Tab,
        });

        Assert.Equal(expectedHint, dialog.FindControl<FocusHintBar>("HintBar")!.Text);
    }
}
```

- [ ] **Step 2: Run it to confirm it fails**

Run: `dotnet test --filter "FullyQualifiedName~ConflictResolutionDialogTests.Tab_ToControlWithHelpText_UpdatesHintBar"`

Expected: FAIL — `dialog.FindControl<FocusHintBar>("HintBar")` returns `null`, so `!.Text` throws a `NullReferenceException`.

- [ ] **Step 3: Add the FocusHintBar to ConflictResolutionDialog.axaml**

The current `DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml` opening tag is:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.ConflictResolutionDialog"
        Title="{StaticResource ConflictDialog_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="520" SizeToContent="Height"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface.Panel}">
```

Change it to add the `shared` namespace:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.ConflictResolutionDialog"
        Title="{StaticResource ConflictDialog_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="520" SizeToContent="Height"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface.Panel}">
```

The current end of the file is:

```xml
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button x:Name="CancelButton"
                    Content="{StaticResource ConflictDialog_Cancel}"
                    ToolTip.Tip="{StaticResource ConflictDialog_CancelTooltip}"
                    AutomationProperties.HelpText="{StaticResource ConflictDialog_CancelTooltip}"
                    Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
            <Button x:Name="ForceButton"
                    Content="{StaticResource ConflictDialog_Force}"
                    ToolTip.Tip="{StaticResource ConflictDialog_ForceTooltip}"
                    AutomationProperties.HelpText="{StaticResource ConflictDialog_ForceTooltip}"
                    Background="{DynamicResource Brush.Button.Caution.Background}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
        </StackPanel>

    </StackPanel>

</Window>
```

Change it to add the FocusHintBar as the last child of the outer `StackPanel`:

```xml
        <StackPanel Orientation="Horizontal" HorizontalAlignment="Right" Spacing="8">
            <Button x:Name="CancelButton"
                    Content="{StaticResource ConflictDialog_Cancel}"
                    ToolTip.Tip="{StaticResource ConflictDialog_CancelTooltip}"
                    AutomationProperties.HelpText="{StaticResource ConflictDialog_CancelTooltip}"
                    Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
            <Button x:Name="ForceButton"
                    Content="{StaticResource ConflictDialog_Force}"
                    ToolTip.Tip="{StaticResource ConflictDialog_ForceTooltip}"
                    AutomationProperties.HelpText="{StaticResource ConflictDialog_ForceTooltip}"
                    Background="{DynamicResource Brush.Button.Caution.Background}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
        </StackPanel>

        <!-- Focus hint bar (Gaps item 16) -->
        <shared:FocusHintBar x:Name="HintBar"/>

    </StackPanel>

</Window>
```

- [ ] **Step 4: Wire `AttachTo` in both constructors**

The current `DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml.cs` is:

```csharp
using Avalonia.Controls;
using DialogEditor.Patch;

namespace DialogEditor.Avalonia.Views;

public partial class ConflictResolutionDialog : Window
{
    public bool ForceApply { get; private set; }

    // Parameterless ctor so the XAML resource is reachable via the runtime loader (avoids AVLN3001).
    public ConflictResolutionDialog() => InitializeComponent();

    public ConflictResolutionDialog(PatchConflictException ex)
    {
        InitializeComponent();
        NodeIdBlock.Text    = ex.NodeId.ToString();
        FieldNameBlock.Text = ex.FieldName;
        ExpectedBlock.Text  = ex.ExpectedFrom;
        ActualBlock.Text    = ex.ActualValue;

        ForceButton.Click  += (_, _) => { ForceApply = true;  Close(); };
        CancelButton.Click += (_, _) => { ForceApply = false; Close(); };
    }
}
```

Replace it with:

```csharp
using Avalonia.Controls;
using DialogEditor.Patch;

namespace DialogEditor.Avalonia.Views;

public partial class ConflictResolutionDialog : Window
{
    public bool ForceApply { get; private set; }

    // Parameterless ctor so the XAML resource is reachable via the runtime loader (avoids AVLN3001).
    public ConflictResolutionDialog()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public ConflictResolutionDialog(PatchConflictException ex)
    {
        InitializeComponent();
        HintBar.AttachTo(this);
        NodeIdBlock.Text    = ex.NodeId.ToString();
        FieldNameBlock.Text = ex.FieldName;
        ExpectedBlock.Text  = ex.ExpectedFrom;
        ActualBlock.Text    = ex.ActualValue;

        ForceButton.Click  += (_, _) => { ForceApply = true;  Close(); };
        CancelButton.Click += (_, _) => { ForceApply = false; Close(); };
    }
}
```

- [ ] **Step 5: Run the wiring test and confirm it passes**

Run: `dotnet test --filter "FullyQualifiedName~ConflictResolutionDialogTests"`

Expected: PASS — `ForceButton_SetsForceApplyTrue`, `CancelButton_LeavesForceApplyFalse`, `Constructor_PopulatesDetailFields`, `Tab_ToControlWithHelpText_UpdatesHintBar` (4/4).

- [ ] **Step 6: Run the item-16 presence theory for this window and confirm it passes**

Run: `dotnet test --filter "FullyQualifiedName~FocusHintBarPresenceTests"`

Expected: `Item16WindowHasFocusHintBar("ConflictResolutionDialog.axaml")` now PASSes; `AboutWindow.axaml` and `HistoryWindow.axaml` still FAIL (11 passed, 2 failed, 13 total).

- [ ] **Step 7: Commit**

```bash
git add DialogEditor.Tests/Views/ConflictResolutionDialogTests.cs DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml DialogEditor.Avalonia/Views/ConflictResolutionDialog.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to ConflictResolutionDialog (Gaps item 16)"
```

---

### Task 3: AboutWindow — `SizeToContent` fix + FocusHintBar

**Files:**
- Modify: `DialogEditor.Avalonia/Views/AboutWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/AboutWindow.axaml.cs`

- [ ] **Step 1: Update AboutWindow.axaml**

The current `DialogEditor.Avalonia/Views/AboutWindow.axaml` is:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.AboutWindow"
        Title="{StaticResource About_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="420" Height="320" CanResize="False"
        ShowInTaskbar="False" Background="{DynamicResource Brush.Surface.Info}">
    <StackPanel Margin="18,16,18,16" Spacing="8">
        <TextBlock Text="{Binding AppName}" Foreground="{DynamicResource Brush.Text.Primary}" FontSize="18" FontWeight="Bold"/>
        <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="{StaticResource About_VersionLabel}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12"/>
            <TextBlock Text="{Binding Version}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
        </StackPanel>
        <TextBlock Text="{Binding Description}" Foreground="{DynamicResource Brush.Text.Tertiary}" FontSize="12" TextWrapping="Wrap"/>
        <TextBlock Text="{Binding License}" Foreground="{DynamicResource Brush.Text.Caption}" FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"/>
        <TextBlock Text="{Binding Credits}" Foreground="{DynamicResource Brush.Text.Caption}" FontSize="11" TextWrapping="Wrap"/>

        <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8,0,0">
            <Button Content="{StaticResource About_OpenRepository}"
                    ToolTip.Tip="{StaticResource About_OpenRepositoryTip}"
                    AutomationProperties.HelpText="{StaticResource About_OpenRepositoryTip}"
                    Command="{Binding OpenRepositoryCommand}"/>
            <Button Content="{StaticResource About_OpenDocs}"
                    ToolTip.Tip="{StaticResource About_OpenDocsTip}"
                    AutomationProperties.HelpText="{StaticResource About_OpenDocsTip}"
                    Command="{Binding OpenDocsCommand}"/>
        </StackPanel>

        <TextBlock Text="{Binding Status}" Foreground="{DynamicResource Brush.Text.Status.Pending}" FontSize="11" TextWrapping="Wrap"/>

        <Button HorizontalAlignment="Right" Margin="0,8,0,0"
                Content="{StaticResource About_Close}"
                ToolTip.Tip="{StaticResource About_Close}"
                AutomationProperties.HelpText="{StaticResource About_Close}"
                Click="Close_Click"/>
    </StackPanel>
</Window>
```

Replace it with (changes: added `xmlns:shared`, `Width="420" Height="320" CanResize="False"` → `Width="420" SizeToContent="Height" CanResize="False"`, and a new `FocusHintBar` as the last child):

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.AboutWindow"
        Title="{StaticResource About_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="420" SizeToContent="Height" CanResize="False"
        ShowInTaskbar="False" Background="{DynamicResource Brush.Surface.Info}">
    <StackPanel Margin="18,16,18,16" Spacing="8">
        <TextBlock Text="{Binding AppName}" Foreground="{DynamicResource Brush.Text.Primary}" FontSize="18" FontWeight="Bold"/>
        <StackPanel Orientation="Horizontal" Spacing="6">
            <TextBlock Text="{StaticResource About_VersionLabel}" Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12"/>
            <TextBlock Text="{Binding Version}" Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
        </StackPanel>
        <TextBlock Text="{Binding Description}" Foreground="{DynamicResource Brush.Text.Tertiary}" FontSize="12" TextWrapping="Wrap"/>
        <TextBlock Text="{Binding License}" Foreground="{DynamicResource Brush.Text.Caption}" FontSize="11" TextWrapping="Wrap" Margin="0,4,0,0"/>
        <TextBlock Text="{Binding Credits}" Foreground="{DynamicResource Brush.Text.Caption}" FontSize="11" TextWrapping="Wrap"/>

        <StackPanel Orientation="Horizontal" Spacing="8" Margin="0,8,0,0">
            <Button Content="{StaticResource About_OpenRepository}"
                    ToolTip.Tip="{StaticResource About_OpenRepositoryTip}"
                    AutomationProperties.HelpText="{StaticResource About_OpenRepositoryTip}"
                    Command="{Binding OpenRepositoryCommand}"/>
            <Button Content="{StaticResource About_OpenDocs}"
                    ToolTip.Tip="{StaticResource About_OpenDocsTip}"
                    AutomationProperties.HelpText="{StaticResource About_OpenDocsTip}"
                    Command="{Binding OpenDocsCommand}"/>
        </StackPanel>

        <TextBlock Text="{Binding Status}" Foreground="{DynamicResource Brush.Text.Status.Pending}" FontSize="11" TextWrapping="Wrap"/>

        <Button HorizontalAlignment="Right" Margin="0,8,0,0"
                Content="{StaticResource About_Close}"
                ToolTip.Tip="{StaticResource About_Close}"
                AutomationProperties.HelpText="{StaticResource About_Close}"
                Click="Close_Click"/>

        <!-- Focus hint bar (Gaps item 16) -->
        <shared:FocusHintBar x:Name="HintBar"/>
    </StackPanel>
</Window>
```

- [ ] **Step 2: Wire `AttachTo` in the parameterless constructor**

The current `DialogEditor.Avalonia/Views/AboutWindow.axaml.cs` is:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow() => InitializeComponent();

    public AboutWindow(AboutViewModel viewModel) : this()
        => DataContext = viewModel;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
```

Replace it with:

```csharp
using Avalonia.Controls;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public AboutWindow(AboutViewModel viewModel) : this()
        => DataContext = viewModel;

    private void Close_Click(object? sender, RoutedEventArgs e) => Close();
}
```

(`AboutWindow(AboutViewModel)` chains to the parameterless constructor via `: this()`, so it does not call `AttachTo` again.)

- [ ] **Step 3: Run AboutWindowTests and the item-16 presence theory**

Run: `dotnet test --filter "FullyQualifiedName~AboutWindowTests"`

Expected: PASS — `Constructs_AndShows` (1/1).

Run: `dotnet test --filter "FullyQualifiedName~FocusHintBarPresenceTests"`

Expected: `Item16WindowHasFocusHintBar("AboutWindow.axaml")` now PASSes; `HistoryWindow.axaml` still FAILs (12 passed, 1 failed, 13 total).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/AboutWindow.axaml DialogEditor.Avalonia/Views/AboutWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to AboutWindow (Gaps item 16)"
```

---

### Task 4: HistoryWindow — FocusHintBar

**Files:**
- Modify: `DialogEditor.Avalonia/Views/HistoryWindow.axaml`
- Modify: `DialogEditor.Avalonia/Views/HistoryWindow.axaml.cs`

- [ ] **Step 1: Update HistoryWindow.axaml**

The current `DialogEditor.Avalonia/Views/HistoryWindow.axaml` is:

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="DialogEditor.Avalonia.Views.HistoryWindow"
        Title="{StaticResource HistoryWindow_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="720" Height="480"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface.Panel}">

    <Grid Margin="16" RowDefinitions="*,Auto">

        <TextBlock Grid.Row="0"
                   Text="{Binding StatusText}"
                   Foreground="{DynamicResource Brush.Text.Meta.Commit}" FontSize="13" TextWrapping="Wrap"
                   IsVisible="{Binding !HasCommits}"
                   VerticalAlignment="Top"/>

        <ListBox Grid.Row="0" x:Name="CommitList"
                 ItemsSource="{Binding Commits}"
                 SelectedItem="{Binding Selected, Mode=TwoWay}"
                 Background="{DynamicResource Brush.Surface.Card}" BorderThickness="0"
                 IsVisible="{Binding HasCommits}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid ColumnDefinitions="110,140,90,*" Margin="0,3">
                        <TextBlock Grid.Column="0"
                                   Text="{Binding Date, Converter={StaticResource CommitDate}}"
                                   ToolTip.Tip="{Binding Date}"
                                   Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                        <TextBlock Grid.Column="1" Text="{Binding Author}"
                                   Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Grid.Column="2" Text="{Binding ShortSha}"
                                   Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" FontFamily="Consolas,monospace"/>
                        <TextBlock Grid.Column="3" Text="{Binding Subject}"
                                   Foreground="{DynamicResource Brush.Text.Primary}" FontSize="12" TextWrapping="Wrap"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <StackPanel Grid.Row="1" Orientation="Horizontal"
                    HorizontalAlignment="Right" Spacing="8" Margin="0,14,0,0">
            <Button x:Name="CompareButton"
                    Content="{StaticResource HistoryWindow_CompareButton}"
                    Command="{Binding CompareCommand}"
                    ToolTip.Tip="{StaticResource HistoryWindow_CompareTooltip}"
                    AutomationProperties.HelpText="{StaticResource HistoryWindow_CompareTooltip}"
                    Background="{DynamicResource Brush.Button.Confirm.Background}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
            <Button x:Name="CloseButton"
                    Content="{StaticResource HistoryWindow_Close}"
                    Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
        </StackPanel>

    </Grid>

</Window>
```

Replace it with (changes: added `xmlns:shared`, `RowDefinitions="*,Auto"` → `RowDefinitions="*,Auto,Auto"`, and a new `FocusHintBar` row):

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        xmlns:shared="clr-namespace:DialogEditor.Avalonia.Shared;assembly=DialogEditor.Avalonia.Shared"
        x:Class="DialogEditor.Avalonia.Views.HistoryWindow"
        Title="{StaticResource HistoryWindow_Title}"
        Icon="avares://DialogEditor.Avalonia/Assets/app.ico"
        Width="720" Height="480"
        WindowStartupLocation="CenterOwner"
        Background="{DynamicResource Brush.Surface.Panel}">

    <Grid Margin="16" RowDefinitions="*,Auto,Auto">

        <TextBlock Grid.Row="0"
                   Text="{Binding StatusText}"
                   Foreground="{DynamicResource Brush.Text.Meta.Commit}" FontSize="13" TextWrapping="Wrap"
                   IsVisible="{Binding !HasCommits}"
                   VerticalAlignment="Top"/>

        <ListBox Grid.Row="0" x:Name="CommitList"
                 ItemsSource="{Binding Commits}"
                 SelectedItem="{Binding Selected, Mode=TwoWay}"
                 Background="{DynamicResource Brush.Surface.Card}" BorderThickness="0"
                 IsVisible="{Binding HasCommits}">
            <ListBox.ItemTemplate>
                <DataTemplate>
                    <Grid ColumnDefinitions="110,140,90,*" Margin="0,3">
                        <TextBlock Grid.Column="0"
                                   Text="{Binding Date, Converter={StaticResource CommitDate}}"
                                   ToolTip.Tip="{Binding Date}"
                                   Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12"/>
                        <TextBlock Grid.Column="1" Text="{Binding Author}"
                                   Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="12" TextTrimming="CharacterEllipsis"/>
                        <TextBlock Grid.Column="2" Text="{Binding ShortSha}"
                                   Foreground="{DynamicResource Brush.Text.Muted}" FontSize="12" FontFamily="Consolas,monospace"/>
                        <TextBlock Grid.Column="3" Text="{Binding Subject}"
                                   Foreground="{DynamicResource Brush.Text.Primary}" FontSize="12" TextWrapping="Wrap"/>
                    </Grid>
                </DataTemplate>
            </ListBox.ItemTemplate>
        </ListBox>

        <StackPanel Grid.Row="1" Orientation="Horizontal"
                    HorizontalAlignment="Right" Spacing="8" Margin="0,14,0,0">
            <Button x:Name="CompareButton"
                    Content="{StaticResource HistoryWindow_CompareButton}"
                    Command="{Binding CompareCommand}"
                    ToolTip.Tip="{StaticResource HistoryWindow_CompareTooltip}"
                    AutomationProperties.HelpText="{StaticResource HistoryWindow_CompareTooltip}"
                    Background="{DynamicResource Brush.Button.Confirm.Background}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
            <Button x:Name="CloseButton"
                    Content="{StaticResource HistoryWindow_Close}"
                    Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.OnAccent}" BorderThickness="0"
                    Padding="16,6" FontSize="12"/>
        </StackPanel>

        <!-- Focus hint bar (Gaps item 16) -->
        <shared:FocusHintBar Grid.Row="2" x:Name="HintBar"/>

    </Grid>

</Window>
```

- [ ] **Step 2: Wire `AttachTo` in both constructors**

The current `DialogEditor.Avalonia/Views/HistoryWindow.axaml.cs` is:

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class HistoryWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public HistoryWindow() => InitializeComponent();

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
```

Replace it with:

```csharp
using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class HistoryWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public HistoryWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public HistoryWindow(HistoryViewModel vm)
    {
        InitializeComponent();
        HintBar.AttachTo(this);
        DataContext = vm;
        CloseButton.Click += (_, _) => Close();
    }
}
```

- [ ] **Step 3: Run HistoryWindowTests and the item-16 presence theory**

Run: `dotnet test --filter "FullyQualifiedName~HistoryWindowTests"`

Expected: PASS — `List_PopulatesFromCommits`, `CompareButton_DisabledUntilSelected` (2/2).

Run: `dotnet test --filter "FullyQualifiedName~FocusHintBarPresenceTests"`

Expected: all 13 PASS (10 item-13 + 3 item-16).

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Avalonia/Views/HistoryWindow.axaml DialogEditor.Avalonia/Views/HistoryWindow.axaml.cs
git commit -m "feat(a11y): add focus-hint bar to HistoryWindow (Gaps item 16)"
```

---

### Task 5: Update Gaps.md and run the full suite

**Files:**
- Modify: `Gaps.md`

- [ ] **Step 1: Replace item 16's entry**

The current item 16 entry (in the "Accessibility — Assistive Technology & Keyboard" numbered list) is:

```markdown
16. **Focus-hint bar for small dialogs.** Split off from item 13, whose
    `FocusHintBar` rollout is scoped to the 10 "workhorse" windows with many
    controls (`SettingsWindow`, `ScriptEditorWindow`, `ConditionEditorWindow`,
    `FindReplaceWindow`, `DiffWindow`, `BatchReplaceWindow`,
    `ExportConversationsWindow`, `FlowAnalyticsWindow`, `BranchesWindow`,
    `GitConflictResolutionWindow`). Seven small 1–3-control dialogs were left out:
    `AboutWindow`, `BranchNameDialog`, `ChangelogWindow`, `CommitConsentDialog`,
    `ConflictResolutionDialog`, `ForceDeleteDialog`, `HistoryWindow`. Their few
    `HelpText` entries are mostly icon-only close/help buttons, where a hint bar
    may be more clutter than value relative to these windows' small size — worth a
    separate judgment call once item 13 has shipped and its UX can be assessed in
    practice.
```

Replace it with:

```markdown
16. **✅ IMPLEMENTED (2026-06-13).** Split off from item 13. Of the 7 small
    1–3-control dialogs left out of the item-13 rollout, each control's
    `AutomationProperties.HelpText` was compared against text already visible in
    its dialog. Three dialogs had at least one control whose `HelpText` is a real
    explanation beyond its visible label, and got a `FocusHintBar`:
    `AboutWindow` (Open Repository / Open Docs buttons; also switched from a fixed
    `Height` to `SizeToContent="Height"`, since its content is entirely
    ViewModel-bound and a fixed height was already a latent clipping risk),
    `ConflictResolutionDialog` (Cancel / Force Apply buttons), and `HistoryWindow`
    (Compare button). The other 4 — `BranchNameDialog`, `CommitConsentDialog`,
    `ChangelogWindow`, `ForceDeleteDialog` — deliberately do **not** get a
    `FocusHintBar`: every `HelpText` value in these dialogs duplicates text already
    on screen (an adjacent label, or the control's own caption), so a bar would
    only echo the screen. See
    docs/superpowers/specs/2026-06-13-focus-hint-bar-small-dialogs-design.md.
```

- [ ] **Step 2: Run the full test suite**

Run: `dotnet test`

Expected: all tests pass, total count = 1358 + 4 new (3 `Item16WindowHasFocusHintBar` theory cases + 1 `Tab_ToControlWithHelpText_UpdatesHintBar`) = 1362, 0 failures.

- [ ] **Step 3: Commit**

```bash
git add Gaps.md
git commit -m "docs(gaps): mark a11y item 16 (focus-hint bar for small dialogs) implemented"
```
