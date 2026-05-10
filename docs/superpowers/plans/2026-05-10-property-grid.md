# Property Grid Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the bespoke per-property TextBlock display in the node detail panel with a uniform, data-driven property grid that always shows all properties in four named groups plus a links section.

**Architecture:** `NodeDetailViewModel` is reduced from 26 observable properties to 6 — two strings for the text section, a bool for female-text styling, and two collections (`IReadOnlyList<PropertyGroup>` and `IReadOnlyList<LinkRow>`) for the grid. `NodeDetailView.axaml` is rebuilt around two `ItemsControl`s and named `DataTemplate` resources; no property-specific XAML exists at all. Colour-coding is preserved via a `PropertyValueStyle` enum on each row, resolved to a `Brush` in the XAML `DataTemplate`.

**Tech Stack:** .NET 8, Avalonia, CommunityToolkit.Mvvm, xUnit

---

### Task 1: Presentation model records

**Files:**
- Create: `DialogEditor.ViewModels/Models/PropertyValueStyle.cs`
- Create: `DialogEditor.ViewModels/Models/PropertyRow.cs`
- Create: `DialogEditor.ViewModels/Models/PropertyGroup.cs`
- Create: `DialogEditor.ViewModels/Models/LinkRow.cs`

- [ ] **Step 1: Create `PropertyValueStyle.cs`**

```csharp
namespace DialogEditor.ViewModels.Models;

public enum PropertyValueStyle
{
    Default,
    Condition,  // orange — conditions text
    Script,     // green  — scripts text
    Code,       // monospace — file paths / GUIDs
}
```

- [ ] **Step 2: Create `PropertyRow.cs`**

```csharp
using DialogEditor.ViewModels.Models;

namespace DialogEditor.ViewModels.Models;

public record PropertyRow(
    string Label,
    string Value,
    PropertyValueStyle Style = PropertyValueStyle.Default);
```

- [ ] **Step 3: Create `PropertyGroup.cs`**

```csharp
namespace DialogEditor.ViewModels.Models;

public record PropertyGroup(string Name, IReadOnlyList<PropertyRow> Rows);
```

- [ ] **Step 4: Create `LinkRow.cs`**

```csharp
namespace DialogEditor.ViewModels.Models;

public record LinkRow(string Arrow, string Detail);
```

- [ ] **Step 5: Build to confirm no errors**

Run: `dotnet build "DialogEditor.ViewModels/DialogEditor.ViewModels.csproj"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```bash
git add DialogEditor.ViewModels/Models/
git commit -m "feat: add PropertyRow, PropertyGroup, LinkRow presentation model records"
```

---

### Task 2: Test infrastructure

**Files:**
- Modify: `DialogEditor.Tests/DialogEditor.Tests.csproj`
- Create: `DialogEditor.Tests/Helpers/StubStringProvider.cs`

- [ ] **Step 1: Add ViewModels project reference to test project**

In `DialogEditor.Tests/DialogEditor.Tests.csproj`, add inside the existing `<ItemGroup>` that holds `<ProjectReference>`:

```xml
<ProjectReference Include="..\DialogEditor.ViewModels\DialogEditor.ViewModels.csproj" />
```

The full `<ItemGroup>` block becomes:

```xml
<ItemGroup>
  <ProjectReference Include="..\DialogEditor.Core\DialogEditor.Core.csproj" />
  <ProjectReference Include="..\DialogEditor.ViewModels\DialogEditor.ViewModels.csproj" />
</ItemGroup>
```

- [ ] **Step 2: Create `StubStringProvider.cs`**

```csharp
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Helpers;

/// <summary>
/// Returns the key name as the string value so tests can assert on predictable labels
/// without loading XAML resources.
/// </summary>
public sealed class StubStringProvider : IStringProvider
{
    public string Get(string key) => key;
}
```

- [ ] **Step 3: Build to confirm no errors**

Run: `dotnet build "DialogEditor.Tests/DialogEditor.Tests.csproj"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.Tests/DialogEditor.Tests.csproj DialogEditor.Tests/Helpers/StubStringProvider.cs
git commit -m "test: add ViewModels project reference and StubStringProvider"
```

---

### Task 3: Failing NodeDetailViewModel tests

**Files:**
- Create: `DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs`

These tests reference `PropertyGroups` and `Links` which don't exist on `NodeDetailViewModel` yet. The build will fail with compile errors — that is the expected red state for TDD.

- [ ] **Step 1: Create `NodeDetailViewModelTests.cs`**

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelTests
{
    private readonly NodeDetailViewModel _vm = new();

    public NodeDetailViewModelTests()
    {
        Loc.Configure(new StubStringProvider());
    }

    private static NodeViewModel MakeNode(
        int id = 1,
        bool isPlayerChoice = false,
        string speakerGuid = "",
        string listenerGuid = "",
        string displayType = "Conversation",
        string persistence = "None",
        string actorDirection = "",
        string conditionExpression = "",
        string comments = "",
        string externalVO = "",
        bool hasVO = false,
        bool hideSpeaker = false,
        IReadOnlyList<string>? scripts = null,
        IReadOnlyList<string>? conditionStrings = null,
        IReadOnlyList<NodeLink>? links = null,
        string defaultText = "Hello",
        string femaleText = "")
    {
        var node = new ConversationNode(
            NodeId: id,
            IsPlayerChoice: isPlayerChoice,
            SpeakerCategory: SpeakerCategory.Npc,
            SpeakerGuid: speakerGuid,
            ListenerGuid: listenerGuid,
            Links: links ?? [],
            ConditionStrings: conditionStrings ?? [],
            Scripts: scripts ?? [],
            DisplayType: displayType,
            Persistence: persistence,
            ActorDirection: actorDirection,
            Comments: comments,
            ExternalVO: externalVO,
            HasVO: hasVO,
            HideSpeaker: hideSpeaker,
            ConditionExpression: conditionExpression);

        var entry = new StringEntry(id, defaultText, femaleText);
        return new NodeViewModel(node, entry);
    }

    [Fact]
    public void Load_SetsHasContentTrue()
    {
        _vm.Load(MakeNode());
        Assert.True(_vm.HasContent);
    }

    [Fact]
    public void Load_WithAllProperties_PopulatesAllFourGroups()
    {
        _vm.Load(MakeNode());
        Assert.Equal(4, _vm.PropertyGroups.Count);
    }

    [Fact]
    public void Load_IdentityGroup_ContainsFiveRows()
    {
        _vm.Load(MakeNode());
        Assert.Equal(5, _vm.PropertyGroups[0].Rows.Count);
    }

    [Fact]
    public void Load_DisplayGroup_AlwaysContainsActorDirectionRow_EvenWhenEmpty()
    {
        _vm.Load(MakeNode(actorDirection: ""));
        var displayGroup = _vm.PropertyGroups[1];
        Assert.Equal(3, displayGroup.Rows.Count);
        Assert.Contains(displayGroup.Rows, r => r.Label == "PropertyRow_ActorDirection");
    }

    [Fact]
    public void Load_LogicGroup_AlwaysContainsCommentsRow_EvenWhenEmpty()
    {
        _vm.Load(MakeNode(comments: ""));
        var logicGroup = _vm.PropertyGroups[2];
        Assert.Equal(3, logicGroup.Rows.Count);
        Assert.Contains(logicGroup.Rows, r => r.Label == "PropertyRow_Comments");
    }

    [Fact]
    public void Load_VoiceGroup_AlwaysContainsAllThreeRows()
    {
        _vm.Load(MakeNode(externalVO: "", hasVO: false, hideSpeaker: false));
        var voiceGroup = _vm.PropertyGroups[3];
        Assert.Equal(3, voiceGroup.Rows.Count);
    }

    [Fact]
    public void Load_WithMultipleLinks_CreatesOneRowPerLink()
    {
        var links = new[]
        {
            new NodeLink(1, 10, false, 1f, "ShowOnce"),
            new NodeLink(1, 20, false, 2f, "Always"),
        };
        _vm.Load(MakeNode(links: links));
        Assert.Equal(2, _vm.Links.Count);
    }

    [Fact]
    public void Load_WithNoLinks_ProducesEmptyLinksList()
    {
        _vm.Load(MakeNode(links: []));
        Assert.Empty(_vm.Links);
    }

    [Fact]
    public void Load_FemaleTextDisplay_WhenEmpty_ShowsSameAsDefaultString()
    {
        _vm.Load(MakeNode(femaleText: ""));
        Assert.Equal("NodeDetail_SameAsDefault", _vm.FemaleTextDisplay);
        Assert.False(_vm.HasFemaleText);
    }

    [Fact]
    public void Load_FemaleTextDisplay_WhenPresent_ShowsActualText()
    {
        _vm.Load(MakeNode(femaleText: "Her voice"));
        Assert.Equal("Her voice", _vm.FemaleTextDisplay);
        Assert.True(_vm.HasFemaleText);
    }

    [Fact]
    public void Clear_SetsHasContentFalse()
    {
        _vm.Load(MakeNode());
        _vm.Clear();
        Assert.False(_vm.HasContent);
    }

    [Fact]
    public void Clear_DoesNotThrowWhenCalledBeforeLoad()
    {
        var exception = Record.Exception(() => _vm.Clear());
        Assert.Null(exception);
    }
}
```

- [ ] **Step 2: Run build and confirm compile failure**

Run: `dotnet build "DialogEditor.Tests/DialogEditor.Tests.csproj"`
Expected: Build FAILS with errors like:
```
'NodeDetailViewModel' does not contain a definition for 'PropertyGroups'
'NodeDetailViewModel' does not contain a definition for 'Links'
```
This confirms the tests are driving the implementation.

- [ ] **Step 3: Commit the failing tests**

```bash
git add DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs
git commit -m "test(red): NodeDetailViewModel property grid tests — failing"
```

---

### Task 4: Rewrite NodeDetailViewModel

**Files:**
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`

- [ ] **Step 1: Replace the entire file contents**

```csharp
using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.ViewModels.Models;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels;

public partial class NodeDetailViewModel : ObservableObject
{
    [ObservableProperty] private bool _hasContent;
    [ObservableProperty] private string _defaultText = string.Empty;
    [ObservableProperty] private string _femaleTextDisplay = string.Empty;
    [ObservableProperty] private bool _hasFemaleText;
    [ObservableProperty] private IReadOnlyList<PropertyGroup> _propertyGroups = [];
    [ObservableProperty] private IReadOnlyList<LinkRow> _links = [];

    public void Load(NodeViewModel? node)
    {
        if (node is null) { HasContent = false; return; }

        DefaultText = node.DefaultText;
        HasFemaleText = !string.IsNullOrEmpty(node.FemaleText);
        FemaleTextDisplay = HasFemaleText ? node.FemaleText : Loc.Get("NodeDetail_SameAsDefault");

        var none = Loc.Get("NodeDetail_None");

        PropertyGroups =
        [
            new PropertyGroup(Loc.Get("Label_GroupIdentity"),
            [
                new PropertyRow(Loc.Get("PropertyRow_NodeId"),     node.NodeId.ToString()),
                new PropertyRow(Loc.Get("PropertyRow_Type"),       node.IsPlayerChoice ? Loc.Get("NodeDetail_PlayerChoice") : Loc.Get("NodeDetail_NpcLine")),
                new PropertyRow(Loc.Get("PropertyRow_Speaker"),    node.SpeakerName),
                new PropertyRow(Loc.Get("PropertyRow_SpeakerGuid"),node.SpeakerGuid, PropertyValueStyle.Code),
                new PropertyRow(Loc.Get("PropertyRow_Listener"),   node.ListenerName),
            ]),
            new PropertyGroup(Loc.Get("Label_GroupDisplay"),
            [
                new PropertyRow(Loc.Get("PropertyRow_DisplayType"),    node.DisplayType),
                new PropertyRow(Loc.Get("PropertyRow_Persistence"),    node.Persistence),
                new PropertyRow(Loc.Get("PropertyRow_ActorDirection"), string.IsNullOrEmpty(node.ActorDirection) ? none : node.ActorDirection),
            ]),
            new PropertyGroup(Loc.Get("Label_GroupLogic"),
            [
                new PropertyRow(Loc.Get("PropertyRow_Conditions"), string.IsNullOrEmpty(node.ConditionExpression) ? none : node.ConditionExpression, PropertyValueStyle.Condition),
                new PropertyRow(Loc.Get("PropertyRow_Scripts"),    node.Scripts.Count == 0 ? none : string.Join(Environment.NewLine, node.Scripts), PropertyValueStyle.Script),
                new PropertyRow(Loc.Get("PropertyRow_Comments"),   string.IsNullOrEmpty(node.Comments) ? none : node.Comments),
            ]),
            new PropertyGroup(Loc.Get("Label_GroupVoice"),
            [
                new PropertyRow(Loc.Get("PropertyRow_ExternalVO"),   string.IsNullOrEmpty(node.ExternalVO) ? none : node.ExternalVO, PropertyValueStyle.Code),
                new PropertyRow(Loc.Get("PropertyRow_HasVO"),        node.HasVO.ToString()),
                new PropertyRow(Loc.Get("PropertyRow_HideSpeaker"),  node.HideSpeaker.ToString()),
            ]),
        ];

        Links = node.Links.Select(BuildLinkRow).ToList();
        HasContent = true;
    }

    public void Clear() => HasContent = false;

    private static LinkRow BuildLinkRow(NodeLink link)
    {
        var extras = new List<string>();
        if (link.RandomWeight != 1f)
            extras.Add($"{Loc.Get("Link_WeightPrefix")}{link.RandomWeight:0.##}");
        if (!string.IsNullOrEmpty(link.QuestionNodeTextDisplay) && link.QuestionNodeTextDisplay != "ShowOnce")
            extras.Add(link.QuestionNodeTextDisplay);
        var arrow = $"{Loc.Get("Link_Arrow")} {link.ToNodeId}";
        var detail = extras.Count == 0 ? Loc.Get("NodeDetail_None") : $"[{string.Join(", ", extras)}]";
        return new LinkRow(arrow, detail);
    }
}
```

- [ ] **Step 2: Run tests and confirm all 12 pass**

Run: `dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj" --filter "FullyQualifiedName~NodeDetailViewModelTests"`
Expected: 12 passed, 0 failed.

- [ ] **Step 3: Run the full test suite to confirm nothing regressed**

Run: `dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj"`
Expected: All tests pass.

- [ ] **Step 4: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs
git commit -m "feat: rewrite NodeDetailViewModel with data-driven property groups"
```

---

### Task 5: Add localisation keys

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

- [ ] **Step 1: Add group header keys**

After the closing `-->` of the `<!-- ─── Detail panel — section labels ──` comment block (around line 131 in `Strings.axaml`, after `Label_HideSpeaker`), add a new comment block:

```xml
    <!-- ─── Detail panel — property group headers ────────────────────── -->
    <!-- Header labels for the four property groups in the node detail panel -->
    <sys:String x:Key="Label_GroupIdentity">IDENTITY</sys:String>
    <sys:String x:Key="Label_GroupDisplay">DISPLAY</sys:String>
    <sys:String x:Key="Label_GroupLogic">LOGIC</sys:String>
    <sys:String x:Key="Label_GroupVoice">VOICE</sys:String>
    <sys:String x:Key="Label_GroupLinks">LINKS</sys:String>

    <!-- ─── Detail panel — property row labels ───────────────────────── -->
    <!-- Used by NodeDetailViewModel to label each row in the property grid -->
    <sys:String x:Key="PropertyRow_NodeId">Node ID</sys:String>
    <sys:String x:Key="PropertyRow_Type">Type</sys:String>
    <sys:String x:Key="PropertyRow_Speaker">Speaker</sys:String>
    <sys:String x:Key="PropertyRow_SpeakerGuid">Speaker GUID</sys:String>
    <sys:String x:Key="PropertyRow_Listener">Listener</sys:String>
    <sys:String x:Key="PropertyRow_DisplayType">Display Type</sys:String>
    <sys:String x:Key="PropertyRow_Persistence">Persistence</sys:String>
    <sys:String x:Key="PropertyRow_ActorDirection">Actor Direction</sys:String>
    <sys:String x:Key="PropertyRow_Conditions">Conditions</sys:String>
    <sys:String x:Key="PropertyRow_Scripts">Scripts</sys:String>
    <sys:String x:Key="PropertyRow_Comments">Comments</sys:String>
    <sys:String x:Key="PropertyRow_ExternalVO">External VO</sys:String>
    <sys:String x:Key="PropertyRow_HasVO">Has VO</sys:String>
    <sys:String x:Key="PropertyRow_HideSpeaker">Hide Speaker</sys:String>
```

- [ ] **Step 2: Build to confirm no XAML errors**

Run: `dotnet build "DialogEditor.Avalonia/DialogEditor.Avalonia.csproj"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "feat: add property grid localisation keys to Strings.axaml"
```

---

### Task 6: Rewrite NodeDetailView.axaml

**Files:**
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml`

The view needs a value converter to map `PropertyValueStyle` to a `Brush`. Check whether `DialogEditor.Avalonia` already has a converters file — if not, create one.

- [ ] **Step 1: Check for existing converters file**

Run: `dir "DialogEditor.Avalonia\Converters" /b 2>nul || echo "No converters folder"`

If converters already exist, add the new converter to the existing file following its pattern. If not, create `DialogEditor.Avalonia/Converters/PropertyValueStyleToBrushConverter.cs`:

```csharp
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.ViewModels.Models;

namespace DialogEditor.Avalonia.Converters;

public sealed class PropertyValueStyleToBrushConverter : IValueConverter
{
    public static readonly PropertyValueStyleToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PropertyValueStyle style ? style switch
        {
            PropertyValueStyle.Condition => Brush.Parse("#e8a020"),
            PropertyValueStyle.Script    => Brush.Parse("#7dcea0"),
            PropertyValueStyle.Code      => Brush.Parse("#9cdcfe"),
            _                            => Brush.Parse("#e8e8e8"),
        } : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
```

- [ ] **Step 2: Replace `NodeDetailView.axaml` entirely**

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:conv="clr-namespace:DialogEditor.Avalonia.Converters"
             xmlns:models="clr-namespace:DialogEditor.ViewModels.Models;assembly=DialogEditor.ViewModels"
             x:Class="DialogEditor.Avalonia.Views.NodeDetailView" x:CompileBindings="False">

    <UserControl.Resources>

        <conv:PropertyValueStyleToBrushConverter x:Key="StyleToBrush"/>

        <!-- Section header style: small bold caps with a bottom separator line -->
        <Style x:Key="GroupHeaderStyle" TargetType="TextBlock">
            <Setter Property="Foreground" Value="#888"/>
            <Setter Property="FontSize" Value="8"/>
            <Setter Property="FontWeight" Value="Bold"/>
            <Setter Property="Margin" Value="0,10,0,3"/>
        </Style>

        <!-- One label/value row inside a property group -->
        <DataTemplate x:Key="PropertyRowTemplate" DataType="models:PropertyRow">
            <Grid ColumnDefinitions="110,*" Margin="0,1,0,1">
                <TextBlock Grid.Column="0"
                           Text="{Binding Label}"
                           Foreground="#666" FontSize="10"
                           VerticalAlignment="Top"/>
                <TextBlock Grid.Column="1"
                           Text="{Binding Value}"
                           Foreground="{Binding Style, Converter={StaticResource StyleToBrush}}"
                           FontSize="10" TextWrapping="Wrap" LineHeight="15"/>
            </Grid>
        </DataTemplate>

        <!-- One outgoing link row -->
        <DataTemplate x:Key="LinkRowTemplate" DataType="models:LinkRow">
            <Grid ColumnDefinitions="60,*" Margin="0,1,0,1">
                <TextBlock Grid.Column="0"
                           Text="{Binding Arrow}"
                           Foreground="#5dade2" FontSize="10"/>
                <TextBlock Grid.Column="1"
                           Text="{Binding Detail}"
                           Foreground="#666" FontSize="10"/>
            </Grid>
        </DataTemplate>

    </UserControl.Resources>

    <Grid RowDefinitions="*">

        <TextBlock Grid.Row="0"
                   Text="{StaticResource Label_SelectNode}"
                   Foreground="#555" FontSize="11"
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   IsVisible="{Binding HasContent, Converter={StaticResource InverseBoolToVis}}"/>

        <ScrollViewer Grid.Row="0" Background="#2d2d2d"
                      IsVisible="{Binding HasContent}">
            <StackPanel Margin="8">

                <!-- ── Text section ─────────────────────────────── -->
                <TextBlock Text="{StaticResource Label_DefaultMaleText}"
                           Style="{StaticResource GroupHeaderStyle}"
                           Margin="0,0,0,3"/>
                <Border Background="#1a1a1a" Padding="6" CornerRadius="3" Margin="0,0,0,10">
                    <TextBlock Text="{Binding DefaultText}"
                               Foreground="#e8e8e8" FontSize="10"
                               TextWrapping="Wrap" LineHeight="16"/>
                </Border>

                <TextBlock Text="{StaticResource Label_FemaleText}"
                           Style="{StaticResource GroupHeaderStyle}"
                           Margin="0,0,0,3"/>
                <Border Background="#1a1a1a" Padding="6" CornerRadius="3" Margin="0,0,0,4">
                    <TextBlock Text="{Binding FemaleTextDisplay}"
                               Foreground="{Binding HasFemaleText, Converter={StaticResource BoolToFemaleTextBrush}}"
                               FontSize="10" TextWrapping="Wrap" LineHeight="16"/>
                </Border>

                <!-- ── Property groups ──────────────────────────── -->
                <ItemsControl ItemsSource="{Binding PropertyGroups}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate>
                            <StackPanel>
                                <TextBlock Text="{Binding Name}"
                                           Style="{StaticResource GroupHeaderStyle}"/>
                                <ItemsControl ItemsSource="{Binding Rows}"
                                             ItemTemplate="{StaticResource PropertyRowTemplate}"/>
                            </StackPanel>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- ── Links section ────────────────────────────── -->
                <TextBlock Text="{StaticResource Label_GroupLinks}"
                           Style="{StaticResource GroupHeaderStyle}"/>
                <ItemsControl ItemsSource="{Binding Links}"
                             ItemTemplate="{StaticResource LinkRowTemplate}"/>

            </StackPanel>
        </ScrollViewer>

    </Grid>
</UserControl>
```

**Note on Female Text styling:** `BoolToFemaleTextBrush` already exists in the project (used by the old XAML). It returns a dimmed colour when `HasFemaleText` is false, which is sufficient to signal "same as default". No FontStyle binding is needed.

- [ ] **Step 3: Build to confirm no XAML/compile errors**

Run: `dotnet build "DialogEditor.Avalonia/DialogEditor.Avalonia.csproj"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the app and visually verify the panel**

Start the app, open a PoE1 or PoE2 game folder, load a conversation, and click several nodes. Verify:
- All four groups (IDENTITY, DISPLAY, LOGIC, VOICE) are always visible
- Empty Actor Direction and Comments show `(none)` instead of disappearing
- Conditions text is orange, scripts text is green
- Speaker GUID and External VO are in code colour
- Links section shows one row per outgoing link
- Female text section always present; shows dimmed italic when same as default

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Views/NodeDetailView.axaml
git add DialogEditor.Avalonia/Converters/
git commit -m "feat: rebuild NodeDetailView as data-driven property grid"
```

---

### Task 7: Remove obsolete localisation keys

**Files:**
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

The following keys from the old bespoke XAML are now unused. Remove them and their comments:

```
Label_Node
Label_Speaker
Label_Listener
Label_Comments
Label_Conditions
Label_DisplayPersist
Label_ActorDirection
Label_Scripts
Label_LinksTo
Label_Voice
Label_File
Label_HasVO          ← keep if used elsewhere; check first
Label_HideSpeaker    ← keep if used elsewhere; check first
```

`Label_DefaultMaleText` and `Label_FemaleText` are still used in the new XAML — keep those.
`Label_NodeDetails` is used for the panel header — keep it.

- [ ] **Step 1: Grep for each key before removing**

Run: `grep -r "Label_Node\"" DialogEditor.Avalonia/ --include="*.axaml" --include="*.cs"`

Repeat for each key in the list above. Remove only keys that appear exclusively in `Strings.axaml` (i.e., no other file references them).

- [ ] **Step 2: Remove confirmed-unused keys from `Strings.axaml`**

Delete the `<sys:String>` entries and their associated comments for each confirmed-unused key.

- [ ] **Step 3: Build to confirm no missing-resource errors**

Run: `dotnet build "DialogEditor.Avalonia/DialogEditor.Avalonia.csproj"`
Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run the full test suite one final time**

Run: `dotnet test "DialogEditor.Tests/DialogEditor.Tests.csproj"`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add DialogEditor.Avalonia/Resources/Strings.axaml
git commit -m "chore: remove obsolete detail-panel label keys from Strings.axaml"
```
