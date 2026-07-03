# Node Detail Pane Rework Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Restructure the node detail pane hot-first (text + links on top) with collapsible summary-bearing groups and readable link cards, per `docs/superpowers/specs/2026-07-02-detail-pane-rework-design.md`.

**Architecture:** Three independently-landable tasks: (1) flat reorder + shared fixes — header line replacing `PropertyGroups`, HideSpeaker regrouped, GUID toggle; (2) wrap secondary groups in `Expander`s driven by new testable summary properties and session-static expansion state; (3) replace the link item template with cards. All new logic lives in `NodeDetailViewModel` / `ConnectionViewModel` and is TDD'd; XAML consumes it.

**Tech Stack:** Avalonia 11 (`Expander`, `ToggleButton`), CommunityToolkit.Mvvm, `Loc.Get` localisation, xUnit (suite runs serially — do not parallelise; `StubStringProvider` echoes keys, so VM tests assert key names, not English).

## Global Constraints

- No user-visible text hard-coded in XAML or C# — everything via `Strings.axaml` keys (`DynamicResource` / `Loc.Get`).
- Strict red/green TDD for all new ViewModel members; observe the failing test before implementing.
- Every interactive control keeps/gains `ToolTip.Tip`; icon-only controls also get `AutomationProperties.Name`. The pane's existing `AutomationProperties` annotations must survive the restructure.
- Every caught exception logs via `AppLog` (no new catches expected).
- `CHANGELOG.md` is frozen — do not touch it.
- Embed design reasoning as comments in the .axaml/.cs files, not only in the spec.

---

### Task 1: Flat reorder + shared fixes (Option C baseline)

**Files:**
- Test: `DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs` (**create**)
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml` (full-body rewrite)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

**Interfaces:**
- Consumes: `SpeakerNameService.FindByGuid(string?) → SpeakerEntry?` (`.Name`), `SpeakerNameService.HasNames`, existing proxies (`SpeakerCategoryString`, `NodeTypeString`).
- Produces (Tasks 2–3 and XAML rely on these exact names):
  - `string NodeHeaderSummary` — `#<id> · <category> · <type>[ · <speaker name>]`, empty when no node.
  - `bool ShowSpeakerGuidBox` / `bool ShowListenerGuidBox` (settable, default false).
  - `bool IsSpeakerGuidBoxVisible` / `bool IsListenerGuidBoxVisible` — true when `!HasSpeakerData` OR the corresponding toggle is on.
  - **Removed:** `PropertyGroups`, `RefreshReadOnlyGroups` (node ID lives in the header now).

- [x] **Step 1: Write the failing tests**

Create `DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs`:

```csharp
using DialogEditor.Core.Models;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.ViewModels;

public class NodeDetailViewModelPaneTests
{
    private readonly NodeDetailViewModel _vm = new();

    public NodeDetailViewModelPaneTests()
        => Loc.Configure(new StubStringProvider());

    // StubStringProvider echoes keys, so the localised separator appears as its key.
    private const string Sep = "NodeDetail_HeaderSeparator";

    private void LoadNode(int id = 1, bool playerChoice = false)
    {
        var node = new ConversationNode(
            NodeId: id, IsPlayerChoice: playerChoice,
            SpeakerCategory: playerChoice ? SpeakerCategory.Player : SpeakerCategory.Npc,
            SpeakerGuid: "", ListenerGuid: "",
            Links: [], Conditions: [], Scripts: [],
            DisplayType: "Conversation", Persistence: "None",
            ActorDirection: "", Comments: "",
            ExternalVO: "", HasVO: false, HideSpeaker: false);
        _vm.Load(new NodeViewModel(node, new StringEntry(id, "Test line", "")));
    }

    // ── NodeHeaderSummary ────────────────────────────────────────────────

    [Fact]
    public void NodeHeaderSummary_EmptyWhenNoNode()
        => Assert.Equal(string.Empty, _vm.NodeHeaderSummary);

    [Fact]
    public void NodeHeaderSummary_NpcNode_ComposesIdCategoryAndType()
    {
        LoadNode(id: 42);
        // No speaker data loaded in tests → no speaker-name segment.
        Assert.Equal($"#42{Sep}Speaker_Npc{Sep}Option_NpcLine", _vm.NodeHeaderSummary);
    }

    [Fact]
    public void NodeHeaderSummary_PlayerNode_ShowsPlayerCategoryAndChoiceType()
    {
        LoadNode(id: 7, playerChoice: true);
        Assert.Equal($"#7{Sep}Speaker_Player{Sep}Option_PlayerChoice", _vm.NodeHeaderSummary);
    }

    // ── GUID box visibility ──────────────────────────────────────────────

    [Fact]
    public void GuidBoxes_VisibleByDefault_WhenNoSpeakerData()
    {
        // SpeakerNameService has no names in the test environment → the raw GUID
        // boxes are the only way to edit, so they must be visible without the toggle.
        LoadNode();
        Assert.False(_vm.ShowSpeakerGuidBox);
        Assert.True(_vm.IsSpeakerGuidBoxVisible);
        Assert.True(_vm.IsListenerGuidBoxVisible);
    }

    [Fact]
    public void GuidToggle_RaisesVisibilityChange()
    {
        LoadNode();
        var raised = new List<string?>();
        _vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        _vm.ShowSpeakerGuidBox = true;
        Assert.Contains(nameof(NodeDetailViewModel.IsSpeakerGuidBoxVisible), raised);
        _vm.ShowListenerGuidBox = true;
        Assert.Contains(nameof(NodeDetailViewModel.IsListenerGuidBoxVisible), raised);
    }
}
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NodeDetailViewModelPaneTests"`
Expected: **build failure** — `error CS1061: 'NodeDetailViewModel' does not contain a definition for 'NodeHeaderSummary'`.

- [x] **Step 3: Implement the ViewModel changes**

In `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`:

**(a)** Add after the `HasFemaleText` property (currently line ~408):

```csharp
    // ── Pane header + GUID toggles (2026-07-02 pane rework) ─────────────

    /// Bold identity line at the top of the pane: "#42 · NPC · Talk[ · Edér]".
    /// Replaces the old read-only PropertyGroups block (which held only the ID).
    public string NodeHeaderSummary
    {
        get
        {
            if (_node is null) return string.Empty;
            var sep = Loc.Get("NodeDetail_HeaderSeparator");
            var s = $"#{_node.NodeId}{sep}{SpeakerCategoryString}{sep}{NodeTypeString}";
            var speaker = SpeakerNameService.FindByGuid(_node.SpeakerGuid)?.Name;
            return speaker is null ? s : s + sep + speaker;
        }
    }

    // Raw GUID boxes double every picker field, so they hide behind a {} toggle
    // when friendly speaker data exists. Without speaker data (PoE1) they are the
    // only editing surface and stay visible unconditionally.
    private bool _showSpeakerGuidBox;
    public bool ShowSpeakerGuidBox
    {
        get => _showSpeakerGuidBox;
        set
        {
            if (SetProperty(ref _showSpeakerGuidBox, value))
                OnPropertyChanged(nameof(IsSpeakerGuidBoxVisible));
        }
    }

    private bool _showListenerGuidBox;
    public bool ShowListenerGuidBox
    {
        get => _showListenerGuidBox;
        set
        {
            if (SetProperty(ref _showListenerGuidBox, value))
                OnPropertyChanged(nameof(IsListenerGuidBoxVisible));
        }
    }

    public bool IsSpeakerGuidBoxVisible  => !HasSpeakerData || ShowSpeakerGuidBox;
    public bool IsListenerGuidBoxVisible => !HasSpeakerData || ShowListenerGuidBox;
```

**(b)** Delete the `PropertyGroups` observable (line ~410: `[ObservableProperty] private IReadOnlyList<PropertyGroup> _propertyGroups = [];`) and the whole `RefreshReadOnlyGroups` method (lines ~562–573).

**(c)** In `Load(...)` delete the call `RefreshReadOnlyGroups(node);`. In `OnNodePropertyChanged(...)` delete both `RefreshReadOnlyGroups(_node);` calls (keep the surrounding summary notifications).

**(d)** At the end of `NotifyAllProxies()` add:

```csharp
        OnPropertyChanged(nameof(NodeHeaderSummary));
```

- [x] **Step 4: Add the Task 1 strings**

In `DialogEditor.Avalonia/Resources/Strings.axaml`, add a new block after the `<!-- ── Batch VO import dialog ── -->` block's last key:

```xml
    <!-- ── Node detail pane rework (2026-07-02) ─────────────────────────── -->
    <sys:String x:Key="NodeDetail_HeaderSeparator"> · </sys:String>
    <sys:String x:Key="NodeDetail_GuidToggleGlyph">{}</sys:String>
    <sys:String x:Key="ToolTip_GuidToggle">Show or hide the raw GUID box for manual editing. The picker above sets it automatically.</sys:String>
    <sys:String x:Key="AutomationName_GuidToggle_Speaker">Toggle raw speaker GUID field</sys:String>
    <sys:String x:Key="AutomationName_GuidToggle_Listener">Toggle raw listener GUID field</sys:String>
```

- [x] **Step 5: Rewrite `NodeDetailView.axaml`**

Replace the entire content of the `<ScrollViewer>`'s `StackPanel` (and delete the now-unused `ReadOnlyRowTemplate` from `UserControl.Resources`). The `UserControl.Styles` block is unchanged. Full new body between `<Grid>` and `</Grid>`:

```xml
        <TextBlock Text="{DynamicResource Label_SelectNode}"
                   Foreground="{DynamicResource Brush.Text.Disabled}" FontSize="{DynamicResource FontSize.Label}"
                   HorizontalAlignment="Center" VerticalAlignment="Center"
                   IsVisible="{Binding HasContent, Converter={StaticResource InverseBoolToVis}}"/>

        <ScrollViewer Background="{DynamicResource Brush.Surface.Subtle}" IsVisible="{Binding HasContent}">
            <!-- Hot-first order (2026-07-02 pane rework spec): identity header, then the
                 two constantly-used areas (dialogue text, links), then secondary groups.
                 Task 2 wraps the secondary groups in collapsed Expanders. -->
            <StackPanel Margin="8">

                <!-- ── Node header: replaces the old read-only node-ID group ── -->
                <TextBlock Text="{Binding NodeHeaderSummary}"
                           FontWeight="Bold" FontSize="{DynamicResource FontSize.Small}"
                           Foreground="{DynamicResource Brush.Text.Primary}"
                           TextTrimming="CharacterEllipsis"
                           ToolTip.Tip="{Binding NodeHeaderSummary}"/>
                <TextBlock IsVisible="{Binding HasAttribution}"
                           Text="{Binding LastEditedSummary}"
                           ToolTip.Tip="{Binding LastEditedTooltip}"
                           Foreground="{DynamicResource Brush.Text.Muted}"
                           FontSize="{DynamicResource FontSize.Caption}"
                           TextWrapping="Wrap" Margin="0,1,0,2"/>

                <!-- ── Default / Male text ─────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{DynamicResource Label_DefaultMaleText}"/>
                <TextBox x:Name="DefaultTextBox" Classes="detail-field"
                         Text="{Binding DefaultText, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="52"
                         ToolTip.Tip="{DynamicResource ToolTip_DefaultText}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_DefaultText}"
                         AutomationProperties.Name="{DynamicResource Label_DefaultMaleText}"/>

                <!-- ── Female text ─────────────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{DynamicResource Label_FemaleText}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding FemaleText, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="36"
                         Watermark="{DynamicResource Placeholder_FemaleText}"
                         ToolTip.Tip="{DynamicResource ToolTip_FemaleText}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_FemaleText}"
                         AutomationProperties.Name="{DynamicResource Label_FemaleText}"/>

                <!-- ── Links (hot: moved above all secondary groups) ───── -->
                <TextBlock Classes="group-header" Text="{DynamicResource Label_GroupLinks}"/>
                <ItemsControl ItemsSource="{Binding Links}">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate DataType="vm:ConnectionViewModel">
                            <Grid RowDefinitions="Auto,Auto" Margin="0,2,0,2">
                                <!-- Row 0: target + delete -->
                                <Grid Grid.Row="0" ColumnDefinitions="Auto,*,Auto,Auto">
                                    <TextBlock Grid.Column="0"
                                               Text="{DynamicResource Link_Arrow}"
                                               Foreground="{DynamicResource Brush.Text.Info}" FontSize="{DynamicResource FontSize.Small}"
                                               VerticalAlignment="Center" Margin="0,0,4,0"/>
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding Target.Owner.NodeId}"
                                               Foreground="{DynamicResource Brush.Text.Info}" FontSize="{DynamicResource FontSize.Small}"
                                               VerticalAlignment="Center"/>
                                    <!-- Condition editor button — dim when no conditions, accent when present -->
                                    <Button Grid.Column="2"
                                            Content="{Binding ConditionCount,
                                                StringFormat='⚙{0}'}"
                                            Click="LinkConditions_Click"
                                            Tag="{Binding}"
                                            Background="Transparent" BorderThickness="0"
                                            FontSize="{DynamicResource FontSize.Small}" Padding="3,1" Margin="0,0,2,0"
                                            Foreground="{Binding HasConditions,
                                                Converter={StaticResource BoolToFemaleTextBrush}}"
                                            ToolTip.Tip="{DynamicResource ToolTip_LinkConditions_None}"
                                            AutomationProperties.HelpText="{DynamicResource ToolTip_LinkConditions_None}"/>
                                    <Button Grid.Column="3"
                                            Content="{DynamicResource Button_DeleteLink}"
                                            Command="{Binding DataContext.DeleteLinkCommand,
                                                      RelativeSource={RelativeSource AncestorType=UserControl}}"
                                            CommandParameter="{Binding}"
                                            Background="Transparent" BorderThickness="0"
                                            Foreground="{DynamicResource Brush.Text.Muted}" FontSize="{DynamicResource FontSize.Caption}" Padding="4,1"
                                            ToolTip.Tip="{DynamicResource ToolTip_DeleteLink}"
                                            AutomationProperties.HelpText="{DynamicResource ToolTip_DeleteLink}"/>
                                </Grid>
                                <!-- Row 1: editable properties -->
                                <Grid Grid.Row="1" ColumnDefinitions="*,Auto,Auto" Margin="12,2,0,0">
                                    <ComboBox Grid.Column="0"
                                              SelectedItem="{Binding QuestionNodeTextDisplay, Mode=TwoWay}"
                                              ItemsSource="{x:Static vm:ConnectionViewModel.QTDOptions}"
                                              Background="{DynamicResource Brush.Surface.Card}" Foreground="{DynamicResource Brush.Text.Secondary}"
                                              BorderBrush="{DynamicResource Brush.Border.Strong}" BorderThickness="1"
                                              FontSize="{DynamicResource FontSize.Small}" Padding="3,1" Height="22"
                                              ToolTip.Tip="{DynamicResource ToolTip_LinkQTD}"
                                              AutomationProperties.HelpText="{DynamicResource ToolTip_LinkQTD}"
                                              AutomationProperties.Name="{DynamicResource AutomationName_LinkQTD}">
                                        <ComboBox.ItemTemplate>
                                            <DataTemplate>
                                                <TextBlock Text="{Binding Converter={StaticResource QTDDisplay},
                                                                  ConverterParameter={DynamicResource Option_QTD_Default}}"/>
                                            </DataTemplate>
                                        </ComboBox.ItemTemplate>
                                    </ComboBox>
                                    <TextBlock Grid.Column="1"
                                               Text="{DynamicResource Label_LinkWeight}"
                                               Foreground="{DynamicResource Brush.Text.Disabled}" FontSize="{DynamicResource FontSize.Small}"
                                               VerticalAlignment="Center" Margin="6,0,4,0"/>
                                    <NumericUpDown Grid.Column="2"
                                                   Value="{Binding RandomWeight, Mode=TwoWay, Converter={StaticResource FloatDecimal}}"
                                                   Minimum="0" Maximum="100"
                                                   Increment="0.1" FormatString="0.##"
                                                   Background="{DynamicResource Brush.Surface.Card}" Foreground="{DynamicResource Brush.Text.Secondary}"
                                                   BorderBrush="{DynamicResource Brush.Border.Strong}" BorderThickness="1"
                                                   FontSize="{DynamicResource FontSize.Small}" Padding="2,0" Width="72"
                                                   ShowButtonSpinner="False"
                                                   ClipValueToMinMax="True"
                                                   ToolTip.Tip="{DynamicResource ToolTip_LinkWeight}"
                                                   AutomationProperties.HelpText="{DynamicResource ToolTip_LinkWeight}"
                                                   AutomationProperties.Name="{DynamicResource Label_LinkWeight}"/>
                                </Grid>
                            </Grid>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>

                <!-- Add link row -->
                <Grid ColumnDefinitions="*,Auto" Margin="0,4,0,0">
                    <TextBox Grid.Column="0"
                             Text="{Binding AddLinkTargetId, Mode=TwoWay}"
                             Watermark="{DynamicResource Placeholder_AddLinkTargetId}"
                             Background="{DynamicResource Brush.Surface.Card}" Foreground="{DynamicResource Brush.Text.Primary}"
                             BorderBrush="{DynamicResource Brush.Border.Strong}" BorderThickness="1"
                             FontSize="{DynamicResource FontSize.Small}" Padding="4,3"
                             ToolTip.Tip="{DynamicResource ToolTip_AddLinkTargetId}"
                             AutomationProperties.HelpText="{DynamicResource ToolTip_AddLinkTargetId}"
                             AutomationProperties.Name="{DynamicResource Placeholder_AddLinkTargetId}"/>
                    <Button Grid.Column="1"
                            Content="{DynamicResource Button_AddLink}"
                            Command="{Binding AddLinkCommand}"
                            Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.Muted.Light}" BorderThickness="0"
                            Padding="6,2" Margin="4,0,0,0"
                            ToolTip.Tip="{DynamicResource ToolTip_AddLink}"
                            AutomationProperties.HelpText="{DynamicResource ToolTip_AddLink}"/>
                </Grid>

                <!-- ── Speaker & Identity ──────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{DynamicResource Label_GroupIdentity}"/>

                <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_Type}"/>
                <ComboBox Classes="detail-combo"
                          ItemsSource="{x:Static vm:NodeDetailViewModel.NodeTypeOptions}"
                          SelectedItem="{Binding NodeTypeString, Mode=TwoWay}"
                          ToolTip.Tip="{DynamicResource ToolTip_NodeType}"
                          AutomationProperties.HelpText="{DynamicResource ToolTip_NodeType}"
                          AutomationProperties.Name="{DynamicResource PropertyRow_Type}"/>

                <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_SpeakerCategory}"/>
                <ComboBox Classes="detail-combo"
                          ItemsSource="{x:Static vm:NodeDetailViewModel.SpeakerCategoryOptions}"
                          SelectedItem="{Binding SpeakerCategoryString, Mode=TwoWay}"
                          ToolTip.Tip="{DynamicResource ToolTip_SpeakerCategory}"
                          AutomationProperties.HelpText="{DynamicResource ToolTip_SpeakerCategory}"
                          AutomationProperties.Name="{DynamicResource PropertyRow_SpeakerCategory}"/>

                <!-- Speaker: friendly picker; raw GUID box behind a {} toggle when picker data exists -->
                <Grid ColumnDefinitions="*,Auto">
                    <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_SpeakerGuid}"/>
                    <ToggleButton Grid.Column="1"
                                  Content="{DynamicResource NodeDetail_GuidToggleGlyph}"
                                  IsChecked="{Binding ShowSpeakerGuidBox, Mode=TwoWay}"
                                  IsVisible="{Binding HasSpeakerData}"
                                  FontSize="{DynamicResource FontSize.Caption}" Padding="4,0"
                                  ToolTip.Tip="{DynamicResource ToolTip_GuidToggle}"
                                  AutomationProperties.Name="{DynamicResource AutomationName_GuidToggle_Speaker}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_GuidToggle}"/>
                </Grid>
                <AutoCompleteBox Classes="detail-field"
                                 ItemsSource="{Binding AvailableSpeakers}"
                                 SelectedItem="{Binding SelectedSpeakerEntry, Mode=TwoWay}"
                                 Watermark="{DynamicResource Placeholder_SpeakerSearch}"
                                 FilterMode="Contains"
                                 IsVisible="{Binding HasSpeakerData}"
                                 ToolTip.Tip="{DynamicResource ToolTip_SpeakerPicker}"
                                 AutomationProperties.HelpText="{DynamicResource ToolTip_SpeakerPicker}"
                                 AutomationProperties.Name="{DynamicResource PropertyRow_SpeakerGuid}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding SpeakerGuid, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         FontFamily="Consolas,Courier New,monospace"
                         IsVisible="{Binding IsSpeakerGuidBoxVisible}"
                         ToolTip.Tip="{DynamicResource ToolTip_SpeakerGuid}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_SpeakerGuid}"
                         AutomationProperties.Name="{DynamicResource PropertyRow_SpeakerGuid}"/>

                <!-- Listener: same pattern -->
                <Grid ColumnDefinitions="*,Auto">
                    <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_ListenerGuid}"/>
                    <ToggleButton Grid.Column="1"
                                  Content="{DynamicResource NodeDetail_GuidToggleGlyph}"
                                  IsChecked="{Binding ShowListenerGuidBox, Mode=TwoWay}"
                                  IsVisible="{Binding HasSpeakerData}"
                                  FontSize="{DynamicResource FontSize.Caption}" Padding="4,0"
                                  ToolTip.Tip="{DynamicResource ToolTip_GuidToggle}"
                                  AutomationProperties.Name="{DynamicResource AutomationName_GuidToggle_Listener}"
                                  AutomationProperties.HelpText="{DynamicResource ToolTip_GuidToggle}"/>
                </Grid>
                <AutoCompleteBox Classes="detail-field"
                                 ItemsSource="{Binding AvailableSpeakers}"
                                 SelectedItem="{Binding SelectedListenerEntry, Mode=TwoWay}"
                                 Watermark="{DynamicResource Placeholder_SpeakerSearch}"
                                 FilterMode="Contains"
                                 IsVisible="{Binding HasSpeakerData}"
                                 ToolTip.Tip="{DynamicResource ToolTip_SpeakerPicker}"
                                 AutomationProperties.HelpText="{DynamicResource ToolTip_SpeakerPicker}"
                                 AutomationProperties.Name="{DynamicResource PropertyRow_ListenerGuid}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding ListenerGuid, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         FontFamily="Consolas,Courier New,monospace"
                         IsVisible="{Binding IsListenerGuidBoxVisible}"
                         ToolTip.Tip="{DynamicResource ToolTip_ListenerGuid}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_ListenerGuid}"
                         AutomationProperties.Name="{DynamicResource PropertyRow_ListenerGuid}"/>

                <!-- ── Display (HideSpeaker moved here from Voice) ─────── -->
                <TextBlock Classes="group-header" Text="{DynamicResource Label_GroupDisplay}"/>

                <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_DisplayType}"/>
                <ComboBox Classes="detail-combo"
                          ItemsSource="{x:Static vm:NodeDetailViewModel.DisplayTypeOptions}"
                          SelectedItem="{Binding DisplayType, Mode=TwoWay}"
                          ToolTip.Tip="{DynamicResource ToolTip_DisplayType}"
                          AutomationProperties.HelpText="{DynamicResource ToolTip_DisplayType}"
                          AutomationProperties.Name="{DynamicResource PropertyRow_DisplayType}"/>

                <Border Background="{DynamicResource Brush.Bark.Detail.Background}" BorderBrush="{DynamicResource Brush.Bark.Detail.Border}" BorderThickness="1"
                        CornerRadius="2" Padding="6,4" Margin="0,0,0,4"
                        IsVisible="{Binding BarkWarnings.Count, Converter={StaticResource CountToVis}}"
                        ToolTip.Tip="{DynamicResource ToolTip_BarkWarnings}">
                    <ItemsControl ItemsSource="{Binding BarkWarnings}">
                        <ItemsControl.ItemTemplate>
                            <DataTemplate>
                                <TextBlock Text="{Binding}" Foreground="{DynamicResource Brush.Bark.Detail.Text}"
                                           FontSize="{DynamicResource FontSize.Small}" TextWrapping="Wrap"/>
                            </DataTemplate>
                        </ItemsControl.ItemTemplate>
                    </ItemsControl>
                </Border>

                <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_Persistence}"/>
                <ComboBox Classes="detail-combo"
                          ItemsSource="{x:Static vm:NodeDetailViewModel.PersistenceOptions}"
                          SelectedItem="{Binding Persistence, Mode=TwoWay}"
                          ToolTip.Tip="{DynamicResource ToolTip_Persistence}"
                          AutomationProperties.HelpText="{DynamicResource ToolTip_Persistence}"
                          AutomationProperties.Name="{DynamicResource PropertyRow_Persistence}"/>

                <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_ActorDirection}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding ActorDirection, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         ToolTip.Tip="{DynamicResource ToolTip_ActorDirection}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_ActorDirection}"
                         AutomationProperties.Name="{DynamicResource PropertyRow_ActorDirection}"/>

                <CheckBox Classes="detail-check"
                          IsChecked="{Binding HideSpeaker, Mode=TwoWay}"
                          Content="{DynamicResource PropertyRow_HideSpeaker}"
                          ToolTip.Tip="{DynamicResource ToolTip_HideSpeaker}"
                          AutomationProperties.HelpText="{DynamicResource ToolTip_HideSpeaker}"/>

                <!-- ── Voice ───────────────────────────────────────────── -->
                <TextBlock Classes="group-header" Text="{DynamicResource Label_GroupVoice}"/>

                <TextBlock Classes="field-label" Text="{DynamicResource PropertyRow_ExternalVO}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding ExternalVO, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         FontFamily="Consolas,Courier New,monospace"
                         ToolTip.Tip="{DynamicResource ToolTip_ExternalVO}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_ExternalVO}"
                         AutomationProperties.Name="{DynamicResource PropertyRow_ExternalVO}"/>

                <CheckBox Classes="detail-check"
                          IsChecked="{Binding HasVO, Mode=TwoWay}"
                          Content="{DynamicResource PropertyRow_HasVO}"
                          ToolTip.Tip="{DynamicResource ToolTip_HasVO}"
                          AutomationProperties.HelpText="{DynamicResource ToolTip_HasVO}"/>

                <!-- VO file status — PoE2 only; shown when HasVO or ExternalVO set -->
                <StackPanel Orientation="Horizontal" Margin="0,2,0,4"
                            IsVisible="{Binding HasVoStatus}"
                            ToolTip.Tip="{Binding VoStatusText}"
                            AutomationProperties.HelpText="{Binding VoStatusText}">
                    <TextBlock Text="{Binding VoStatusGlyph}"
                               Foreground="{Binding VoStatusIsFound, Converter={StaticResource BoolToVoStatusBrush}}"
                               FontSize="{DynamicResource FontSize.Small}"
                               VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <TextBlock Text="{Binding VoStatusText}"
                               Foreground="{Binding VoStatusIsFound, Converter={StaticResource BoolToVoStatusBrush}}"
                               FontSize="{DynamicResource FontSize.Small}"
                               VerticalAlignment="Center"/>
                    <!-- Play buttons — only when vgmstream-cli is present and the file is found -->
                    <Button Content="{Binding PlayPrimaryLabel}"
                            Command="{Binding PlayPrimaryCommand}"
                            IsVisible="{Binding CanPlayAudio}"
                            ToolTip.Tip="{Binding PlayPrimaryTooltip}"
                            AutomationProperties.HelpText="{Binding PlayPrimaryTooltip}"
                            Margin="8,0,0,0" Padding="6,2"/>
                    <Button Content="{Binding PlayFemLabel}"
                            Command="{Binding PlayFemCommand}"
                            IsVisible="{Binding CanPlayFem}"
                            ToolTip.Tip="{Binding PlayFemTooltip}"
                            AutomationProperties.HelpText="{Binding PlayFemTooltip}"
                            Margin="4,0,0,0" Padding="6,2"/>
                </StackPanel>

                <!-- Import button — visible for all PoE2 nodes (even before HasVO is set);
                     disabled when the project is unsaved so the tooltip explains why. -->
                <Button Content="🎤"
                        Command="{Binding ImportVoCommand}"
                        IsVisible="{Binding IsVoImportVisible}"
                        ToolTip.Tip="{Binding ImportVoTooltip}"
                        AutomationProperties.Name="{DynamicResource AutomationName_VoImport}"
                        AutomationProperties.HelpText="{Binding ImportVoTooltip}"
                        Margin="0,2,0,4" Padding="8,3"/>

                <!-- ── Logic (scripts + conditions, merged) ────────────── -->
                <TextBlock Classes="group-header" Text="{DynamicResource Label_GroupLogic}"/>
                <Grid ColumnDefinitions="*,Auto" Margin="0,2,0,0">
                    <TextBlock Grid.Column="0"
                               Text="{Binding ScriptSummary}"
                               Foreground="{DynamicResource Brush.Text.Muted}" FontSize="{DynamicResource FontSize.Small}"
                               VerticalAlignment="Center"
                               TextTrimming="CharacterEllipsis"/>
                    <Button Grid.Column="1"
                            Content="{DynamicResource Button_EditScripts}"
                            Click="EditScripts_Click"
                            Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.Muted.Light}" BorderThickness="0"
                            Padding="8,3" FontSize="{DynamicResource FontSize.Small}"
                            ToolTip.Tip="{DynamicResource ToolTip_EditScripts}"
                            AutomationProperties.HelpText="{DynamicResource ToolTip_EditScripts}"/>
                </Grid>

                <TextBlock Classes="group-header" Text="{DynamicResource Label_GroupConditions}"/>
                <Grid ColumnDefinitions="*,Auto" Margin="0,2,0,0">
                    <!-- Summary: show count + first condition names -->
                    <TextBlock Grid.Column="0"
                               Text="{Binding ConditionSummary}"
                               Foreground="{DynamicResource Brush.Text.Muted}" FontSize="{DynamicResource FontSize.Small}"
                               VerticalAlignment="Center"
                               TextTrimming="CharacterEllipsis"/>
                    <Button Grid.Column="1"
                            x:Name="EditConditionsButton"
                            Content="{DynamicResource Button_EditConditions}"
                            Click="EditConditions_Click"
                            Background="{DynamicResource Brush.Surface.Header}" Foreground="{DynamicResource Brush.Text.Muted.Light}" BorderThickness="0"
                            Padding="8,3" FontSize="{DynamicResource FontSize.Small}"
                            ToolTip.Tip="{DynamicResource ToolTip_EditConditions}"
                            AutomationProperties.HelpText="{DynamicResource ToolTip_EditConditions}"/>
                </Grid>

                <!-- ── Notes (comments + translator note) ──────────────── -->
                <TextBlock Classes="group-header" Text="{DynamicResource PropertyRow_Comments}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding Comments, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="30"
                         ToolTip.Tip="{DynamicResource ToolTip_Comments}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_Comments}"
                         AutomationProperties.Name="{DynamicResource PropertyRow_Comments}"/>

                <TextBlock Classes="group-header" Text="{DynamicResource NodeDetail_TranslatorNote}"/>
                <TextBox Classes="detail-field"
                         Text="{Binding TranslatorNote, Mode=TwoWay, UpdateSourceTrigger=LostFocus}"
                         AcceptsReturn="True" TextWrapping="Wrap" MinHeight="30"
                         ToolTip.Tip="{DynamicResource ToolTip_NodeDetail_TranslatorNote}"
                         AutomationProperties.HelpText="{DynamicResource ToolTip_NodeDetail_TranslatorNote}"
                         AutomationProperties.Name="{DynamicResource NodeDetail_TranslatorNote}"/>

            </StackPanel>
        </ScrollViewer>
```

Note: the `xmlns:models` namespace declaration on the `UserControl` root becomes unused once `ReadOnlyRowTemplate` is deleted — remove it too.

- [x] **Step 6: Build, run tests, grep for orphans**

```
dotnet build
```
Expected: success, no `AVLN` errors.

```
dotnet test --nologo
```
Expected: all pass, including the four new `NodeDetailViewModelPaneTests`.

```
grep -rn "PropertyGroups\|RefreshReadOnlyGroups\|ReadOnlyRowTemplate" --include="*.cs" --include="*.axaml" DialogEditor.ViewModels DialogEditor.Avalonia DialogEditor.Tests
```
Expected: no matches in `NodeDetailViewModel.cs` / `NodeDetailView.axaml`. If `NodeDetailViewModelTests.cs` asserts on `PropertyGroups` (check!), delete those specific assertions/tests — the node ID they verified now lives in `NodeHeaderSummary`, which is covered by the new tests.

- [x] **Step 7: Commit**

```bash
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs "DialogEditor.Avalonia/Views/NodeDetailView.axaml" "DialogEditor.Avalonia/Resources/Strings.axaml" DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs DialogEditor.Tests/ViewModels/NodeDetailViewModelTests.cs
git commit -m "refactor(detail-pane): hot-first reorder, node header, GUID toggles

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 2: Expanders with live summaries

**Files:**
- Test: `DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs` (extend)
- Modify: `DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs`
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml`
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

**Interfaces:**
- Consumes: Task 1's `NodeHeaderSummary` layout; existing `_voCheck`, `VoPresence`, `SpeakerNameService`, `Comments`, `TranslatorNote`, `_node.Conditions/.Scripts`.
- Produces (XAML binds these exact names):
  - `string IdentitySummary`, `DisplaySummary`, `VoiceSummary`, `LogicSummary`, `NotesSummary`.
  - `bool IsIdentityExpanded`, `IsDisplayExpanded`, `IsVoiceExpanded`, `IsLogicExpanded`, `IsNotesExpanded` — settable, backed by **static** fields (session-wide), default false.
  - `internal static void ResetExpanderStateForTests()`.

- [x] **Step 1: Write the failing tests**

Append to `NodeDetailViewModelPaneTests.cs` (inside the class):

```csharp
    // ── Expander summaries ───────────────────────────────────────────────

    [Fact]
    public void IdentitySummary_NoSpeakerData_ShowsCategoryOnly()
    {
        LoadNode();
        Assert.Equal("Speaker_Npc", _vm.IdentitySummary);
    }

    [Fact]
    public void DisplaySummary_ComposesDisplayTypeAndPersistence()
    {
        LoadNode();
        Assert.Equal($"Conversation{Sep}NodeDetail_PersistsPrefix None", _vm.DisplaySummary);
    }

    [Fact]
    public void VoiceSummary_NoVoStatus_ShowsNoneShort()
    {
        LoadNode(); // no GameRoot/ActiveGameId → VO not applicable
        Assert.Equal("NodeDetail_NoneShort", _vm.VoiceSummary);
    }

    [Fact]
    public void LogicSummary_CountsConditionsAndScripts()
    {
        LoadNode();
        Assert.Equal($"0 NodeDetail_ConditionsWord{Sep}0 NodeDetail_ScriptsWord", _vm.LogicSummary);
    }

    [Fact]
    public void NotesSummary_EmptyNotes_ShowsNoneShort()
    {
        LoadNode();
        Assert.Equal("NodeDetail_NoneShort", _vm.NotesSummary);
    }

    [Fact]
    public void NotesSummary_WithComment_ShowsCount()
    {
        LoadNode();
        _vm.Comments = "watch the pacing here";
        Assert.Equal("1 NodeDetail_NotesWord", _vm.NotesSummary);
    }

    // ── Session-static expander state ────────────────────────────────────

    [Fact]
    public void ExpanderState_SharedAcrossInstances_AndSurvivesLoad()
    {
        NodeDetailViewModel.ResetExpanderStateForTests();
        try
        {
            _vm.IsVoiceExpanded = true;
            LoadNode(id: 2); // selecting another node must not collapse it

            var second = new NodeDetailViewModel();
            Assert.True(second.IsVoiceExpanded);   // session-wide
            Assert.False(second.IsLogicExpanded);  // others untouched
        }
        finally
        {
            NodeDetailViewModel.ResetExpanderStateForTests();
        }
    }
```

- [x] **Step 2: Run tests to verify they fail**

Run: `dotnet test --nologo --filter "FullyQualifiedName~NodeDetailViewModelPaneTests"`
Expected: **build failure** — `error CS1061 ... 'IdentitySummary'`.

- [x] **Step 3: Implement the ViewModel changes**

Add to `NodeDetailViewModel.cs`, directly after the GUID-toggle block from Task 1:

```csharp
    // ── Expander summaries (collapsed headers still answer most glances) ──

    /// e.g. "NPC · Edér → Player" — speaker/listener names only when resolvable.
    public string IdentitySummary
    {
        get
        {
            if (_node is null) return string.Empty;
            var sep      = Loc.Get("NodeDetail_HeaderSeparator");
            var speaker  = SpeakerNameService.FindByGuid(_node.SpeakerGuid)?.Name;
            var listener = SpeakerNameService.FindByGuid(_node.ListenerGuid)?.Name;
            if (speaker is null) return SpeakerCategoryString;
            var pair = listener is null ? speaker : $"{speaker} → {listener}";
            return SpeakerCategoryString + sep + pair;
        }
    }

    /// e.g. "Conversation · persists: None".
    public string DisplaySummary => _node is null
        ? string.Empty
        : $"{DisplayType}{Loc.Get("NodeDetail_HeaderSeparator")}{Loc.Get("NodeDetail_PersistsPrefix")} {Persistence}";

    /// e.g. "✓ found · M+F" / "✗ missing" / "—" (VO not applicable).
    public string VoiceSummary => _voCheck switch
    {
        null or { Status: VoPresence.NotApplicable }            => Loc.Get("NodeDetail_NoneShort"),
        { Status: VoPresence.Found, FemaleVariantFound: true }  => Loc.Get("NodeDetail_VoFoundWithFem"),
        { Status: VoPresence.Found }                            => Loc.Get("NodeDetail_VoFound"),
        _                                                       => Loc.Get("NodeDetail_VoMissing"),
    };

    /// e.g. "2 conditions · 1 script" (words are localised fragments).
    public string LogicSummary => _node is null
        ? string.Empty
        : $"{_node.Conditions.Count} {Loc.Get("NodeDetail_ConditionsWord")}"
          + Loc.Get("NodeDetail_HeaderSeparator")
          + $"{_node.Scripts.Count} {Loc.Get("NodeDetail_ScriptsWord")}";

    /// e.g. "1 note(s)" counting non-empty comment + translator note, "—" when none.
    public string NotesSummary
    {
        get
        {
            if (_node is null) return string.Empty;
            var n = (string.IsNullOrWhiteSpace(Comments) ? 0 : 1)
                  + (string.IsNullOrWhiteSpace(TranslatorNote) ? 0 : 1);
            return n == 0
                ? Loc.Get("NodeDetail_NoneShort")
                : $"{n} {Loc.Get("NodeDetail_NotesWord")}";
        }
    }

    // ── Session-wide expander state ──────────────────────────────────────
    // Static so a VO pass keeps Voice open across node selections; resets on
    // app restart (deliberately NOT persisted to AppSettings — YAGNI per spec).
    private static bool _identityExpanded, _displayExpanded, _voiceExpanded,
                        _logicExpanded, _notesExpanded;

    public bool IsIdentityExpanded
    {
        get => _identityExpanded;
        set { _identityExpanded = value; OnPropertyChanged(); }
    }
    public bool IsDisplayExpanded
    {
        get => _displayExpanded;
        set { _displayExpanded = value; OnPropertyChanged(); }
    }
    public bool IsVoiceExpanded
    {
        get => _voiceExpanded;
        set { _voiceExpanded = value; OnPropertyChanged(); }
    }
    public bool IsLogicExpanded
    {
        get => _logicExpanded;
        set { _logicExpanded = value; OnPropertyChanged(); }
    }
    public bool IsNotesExpanded
    {
        get => _notesExpanded;
        set { _notesExpanded = value; OnPropertyChanged(); }
    }

    /// Test hook: static state leaks across serially-run tests otherwise.
    internal static void ResetExpanderStateForTests()
        => _identityExpanded = _displayExpanded = _voiceExpanded
         = _logicExpanded = _notesExpanded = false;
```

Then wire refresh notifications:

**(a)** At the end of `NotifyAllProxies()` (after the Task 1 `NodeHeaderSummary` line) add:

```csharp
        OnPropertyChanged(nameof(IdentitySummary));
        OnPropertyChanged(nameof(DisplaySummary));
        OnPropertyChanged(nameof(VoiceSummary));
        OnPropertyChanged(nameof(LogicSummary));
        OnPropertyChanged(nameof(NotesSummary));
```

**(b)** In `OnNodePropertyChanged`, inside the existing `Conditions` and `Scripts` branches, add `OnPropertyChanged(nameof(LogicSummary));` to each.

**(c)** In the `TranslatorNote` setter, after `OnPropertyChanged();` add `OnPropertyChanged(nameof(NotesSummary));`.

Note: `InternalsVisibleTo` — check `DialogEditor.ViewModels.csproj`/`AssemblyInfo` for an existing `InternalsVisibleTo("DialogEditor.Tests")`; if absent, make `ResetExpanderStateForTests` `public` instead (with the same doc comment).

- [x] **Step 4: Add the Task 2 strings**

Append to the pane-rework block in `Strings.axaml`:

```xml
    <sys:String x:Key="Label_GroupNotes">Notes</sys:String>
    <sys:String x:Key="NodeDetail_PersistsPrefix">persists:</sys:String>
    <sys:String x:Key="NodeDetail_VoFound">✓ found</sys:String>
    <sys:String x:Key="NodeDetail_VoFoundWithFem">✓ found · M+F</sys:String>
    <sys:String x:Key="NodeDetail_VoMissing">✗ missing</sys:String>
    <sys:String x:Key="NodeDetail_NoneShort">—</sys:String>
    <sys:String x:Key="NodeDetail_ConditionsWord">conditions</sys:String>
    <sys:String x:Key="NodeDetail_ScriptsWord">scripts</sys:String>
    <sys:String x:Key="NodeDetail_NotesWord">note(s)</sys:String>
    <sys:String x:Key="ToolTip_GroupExpander">Expand or collapse this section. The open/closed state is remembered while the app runs.</sys:String>
```

- [x] **Step 5: Wrap the five groups in Expanders**

In `NodeDetailView.axaml`, add one style to `UserControl.Styles`:

```xml
        <Style Selector="Expander.detail-group">
            <Setter Property="Margin"  Value="0,6,0,0"/>
            <Setter Property="Padding" Value="4"/>
            <Setter Property="HorizontalAlignment" Value="Stretch"/>
        </Style>
```

Then replace each of the five secondary sections from Task 1 with an `Expander`. The pattern, shown in full for Voice — **apply identically to all five**, with the group's own header key, summary property, expanded property, and unchanged inner content:

```xml
                <Expander Classes="detail-group"
                          IsExpanded="{Binding IsVoiceExpanded, Mode=TwoWay}"
                          ToolTip.Tip="{DynamicResource ToolTip_GroupExpander}"
                          AutomationProperties.Name="{DynamicResource Label_GroupVoice}"
                          AutomationProperties.HelpText="{Binding VoiceSummary}">
                    <Expander.Header>
                        <Grid ColumnDefinitions="Auto,*">
                            <TextBlock Text="{DynamicResource Label_GroupVoice}"
                                       FontWeight="SemiBold" FontSize="{DynamicResource FontSize.Caption}"
                                       Foreground="{DynamicResource Brush.Text.Muted}"/>
                            <TextBlock Grid.Column="1" Text="{Binding VoiceSummary}"
                                       FontSize="{DynamicResource FontSize.Caption}"
                                       Foreground="{DynamicResource Brush.Text.Caption}"
                                       HorizontalAlignment="Right" Margin="8,0,0,0"
                                       TextTrimming="CharacterEllipsis"/>
                        </Grid>
                    </Expander.Header>
                    <StackPanel>
                        <!-- unchanged inner content of the group from Task 1 -->
                    </StackPanel>
                </Expander>
```

The five mappings (delete each group's `group-header` TextBlock — the Expander header replaces it):

| Group (Task 1 section) | Header key | Summary binding | IsExpanded binding |
|---|---|---|---|
| Speaker & Identity | `Label_GroupIdentity` | `IdentitySummary` | `IsIdentityExpanded` |
| Display | `Label_GroupDisplay` | `DisplaySummary` | `IsDisplayExpanded` |
| Voice (incl. status row + 🎤 button) | `Label_GroupVoice` | `VoiceSummary` | `IsVoiceExpanded` |
| Logic (scripts row + conditions row; keep the two inner `group-header`/`field-label` captions for Scripts vs Conditions — change the two inner `group-header` TextBlocks to `field-label` class) | `Label_GroupLogic` | `LogicSummary` | `IsLogicExpanded` |
| Notes (comments + translator note; change their two `group-header` TextBlocks to `field-label` class) | `Label_GroupNotes` | `NotesSummary` | `IsNotesExpanded` |

- [x] **Step 6: Build, test, commit**

```
dotnet build && dotnet test --nologo
```
Expected: build success, all tests pass (new summary + expander tests green).

```bash
git add DialogEditor.ViewModels/ViewModels/NodeDetailViewModel.cs "DialogEditor.Avalonia/Views/NodeDetailView.axaml" "DialogEditor.Avalonia/Resources/Strings.axaml" DialogEditor.Tests/ViewModels/NodeDetailViewModelPaneTests.cs
git commit -m "feat(detail-pane): collapsible groups with live summary headers

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 3: Link cards

**Files:**
- Test: `DialogEditor.Tests/ViewModels/ConnectionViewModelTests.cs` (extend; create if absent — check first)
- Modify: `DialogEditor.ViewModels/ViewModels/ConnectionViewModel.cs`
- Modify: `DialogEditor.Avalonia/Views/NodeDetailView.axaml` (links `ItemsControl` template only)
- Modify: `DialogEditor.Avalonia/Resources/Strings.axaml`

**Interfaces:**
- Consumes: `ConnectionViewModel.ConditionCount` / `HasConditions` (existing, already raise change notifications), `Target.Owner.TextPreview` (existing `NodeViewModel` property).
- Produces: `string ConditionCountLabel` on `ConnectionViewModel` — `"<glyph> <count>"`, refreshed whenever `ConditionCount` changes.

- [x] **Step 1: Write the failing test**

Check for an existing `ConnectionViewModelTests.cs` under `DialogEditor.Tests` (`ls DialogEditor.Tests/ViewModels | grep -i connection`); extend it, or create with this shape (adjust construction to match how existing tests build a `ConnectionViewModel` — look at any test referencing it before inventing a constructor):

```csharp
    [Fact]
    public void ConditionCountLabel_ComposesGlyphAndCount()
    {
        Loc.Configure(new StubStringProvider());
        var conn = /* construct a ConnectionViewModel the same way existing tests do */;
        Assert.Equal("Link_ConditionGlyph 0", conn.ConditionCountLabel);
    }
```

If no test currently constructs a `ConnectionViewModel` in isolation, put the test in `NodeDetailViewModelPaneTests` instead, driving it through `_vm.Links` after wiring a canvas — whichever existing pattern is cheapest. The assertion stays the same.

- [x] **Step 2: Run test to verify it fails**

Run: `dotnet test --nologo --filter "FullyQualifiedName~ConditionCountLabel"`
Expected: **build failure** — `'ConnectionViewModel' does not contain a definition for 'ConditionCountLabel'`.

- [x] **Step 3: Implement**

In `ConnectionViewModel.cs`, add near `ConditionCount`:

```csharp
    /// Button face for the link-conditions editor: localised glyph + live count.
    public string ConditionCountLabel => $"{Loc.Get("Link_ConditionGlyph")} {ConditionCount}";
```

and in the existing block that raises `OnPropertyChanged(nameof(ConditionCount));` (line ~43) add:

```csharp
                   OnPropertyChanged(nameof(ConditionCountLabel));
```

(Add `using DialogEditor.ViewModels.Resources;` if the file lacks it.)

In `Strings.axaml` (pane-rework block):

```xml
    <sys:String x:Key="Link_ConditionGlyph">⚙</sys:String>
    <sys:String x:Key="Link_DisplayLabel">Display</sys:String>
```

In `NodeDetailView.axaml`, replace the links `DataTemplate` content (the outer `Grid RowDefinitions="Auto,Auto"` from Task 1) with the card:

```xml
                        <DataTemplate DataType="vm:ConnectionViewModel">
                            <!-- Link card: accent left edge, target text snippet, labelled controls -->
                            <Border BorderBrush="{DynamicResource Brush.Border.Strong}" BorderThickness="1"
                                    CornerRadius="3" Margin="0,2,0,2"
                                    Background="{DynamicResource Brush.Surface.Card}">
                                <Grid ColumnDefinitions="3,*">
                                    <Rectangle Fill="{DynamicResource Brush.Text.Info}"/>
                                    <StackPanel Grid.Column="1" Margin="6,4">
                                        <!-- Row 0: target id + text snippet + delete -->
                                        <Grid ColumnDefinitions="Auto,Auto,*,Auto">
                                            <TextBlock Grid.Column="0"
                                                       Text="{DynamicResource Link_Arrow}"
                                                       Foreground="{DynamicResource Brush.Text.Info}" FontSize="{DynamicResource FontSize.Small}"
                                                       VerticalAlignment="Center" Margin="0,0,4,0"/>
                                            <TextBlock Grid.Column="1"
                                                       Text="{Binding Target.Owner.NodeId}"
                                                       FontWeight="SemiBold"
                                                       Foreground="{DynamicResource Brush.Text.Info}" FontSize="{DynamicResource FontSize.Small}"
                                                       VerticalAlignment="Center" Margin="0,0,6,0"/>
                                            <TextBlock Grid.Column="2"
                                                       Text="{Binding Target.Owner.TextPreview}"
                                                       FontStyle="Italic"
                                                       Foreground="{DynamicResource Brush.Text.Secondary}" FontSize="{DynamicResource FontSize.Small}"
                                                       VerticalAlignment="Center"
                                                       TextTrimming="CharacterEllipsis"/>
                                            <Button Grid.Column="3"
                                                    Content="{DynamicResource Button_DeleteLink}"
                                                    Command="{Binding DataContext.DeleteLinkCommand,
                                                              RelativeSource={RelativeSource AncestorType=UserControl}}"
                                                    CommandParameter="{Binding}"
                                                    Background="Transparent" BorderThickness="0"
                                                    Foreground="{DynamicResource Brush.Text.Muted}" FontSize="{DynamicResource FontSize.Caption}" Padding="4,1"
                                                    ToolTip.Tip="{DynamicResource ToolTip_DeleteLink}"
                                                    AutomationProperties.HelpText="{DynamicResource ToolTip_DeleteLink}"/>
                                        </Grid>
                                        <!-- Row 1: labelled controls -->
                                        <Grid ColumnDefinitions="Auto,Auto,*,Auto,Auto" Margin="0,3,0,0">
                                            <Button Grid.Column="0"
                                                    Content="{Binding ConditionCountLabel}"
                                                    Click="LinkConditions_Click"
                                                    Tag="{Binding}"
                                                    Background="Transparent" BorderThickness="0"
                                                    FontSize="{DynamicResource FontSize.Small}" Padding="3,1" Margin="0,0,8,0"
                                                    Foreground="{Binding HasConditions,
                                                        Converter={StaticResource BoolToFemaleTextBrush}}"
                                                    ToolTip.Tip="{DynamicResource ToolTip_LinkConditions_None}"
                                                    AutomationProperties.Name="{DynamicResource AutomationName_LinkQTD}"
                                                    AutomationProperties.HelpText="{DynamicResource ToolTip_LinkConditions_None}"/>
                                            <TextBlock Grid.Column="1"
                                                       Text="{DynamicResource Link_DisplayLabel}"
                                                       Foreground="{DynamicResource Brush.Text.Disabled}" FontSize="{DynamicResource FontSize.Caption}"
                                                       VerticalAlignment="Center" Margin="0,0,4,0"/>
                                            <ComboBox Grid.Column="2"
                                                      SelectedItem="{Binding QuestionNodeTextDisplay, Mode=TwoWay}"
                                                      ItemsSource="{x:Static vm:ConnectionViewModel.QTDOptions}"
                                                      Background="{DynamicResource Brush.Surface.Card}" Foreground="{DynamicResource Brush.Text.Secondary}"
                                                      BorderBrush="{DynamicResource Brush.Border.Strong}" BorderThickness="1"
                                                      FontSize="{DynamicResource FontSize.Small}" Padding="3,1" Height="22"
                                                      ToolTip.Tip="{DynamicResource ToolTip_LinkQTD}"
                                                      AutomationProperties.HelpText="{DynamicResource ToolTip_LinkQTD}"
                                                      AutomationProperties.Name="{DynamicResource AutomationName_LinkQTD}">
                                                <ComboBox.ItemTemplate>
                                                    <DataTemplate>
                                                        <TextBlock Text="{Binding Converter={StaticResource QTDDisplay},
                                                                          ConverterParameter={DynamicResource Option_QTD_Default}}"/>
                                                    </DataTemplate>
                                                </ComboBox.ItemTemplate>
                                            </ComboBox>
                                            <TextBlock Grid.Column="3"
                                                       Text="{DynamicResource Label_LinkWeight}"
                                                       Foreground="{DynamicResource Brush.Text.Disabled}" FontSize="{DynamicResource FontSize.Caption}"
                                                       VerticalAlignment="Center" Margin="6,0,4,0"/>
                                            <NumericUpDown Grid.Column="4"
                                                           Value="{Binding RandomWeight, Mode=TwoWay, Converter={StaticResource FloatDecimal}}"
                                                           Minimum="0" Maximum="100"
                                                           Increment="0.1" FormatString="0.##"
                                                           Background="{DynamicResource Brush.Surface.Card}" Foreground="{DynamicResource Brush.Text.Secondary}"
                                                           BorderBrush="{DynamicResource Brush.Border.Strong}" BorderThickness="1"
                                                           FontSize="{DynamicResource FontSize.Small}" Padding="2,0" Width="72"
                                                           ShowButtonSpinner="False"
                                                           ClipValueToMinMax="True"
                                                           ToolTip.Tip="{DynamicResource ToolTip_LinkWeight}"
                                                           AutomationProperties.HelpText="{DynamicResource ToolTip_LinkWeight}"
                                                           AutomationProperties.Name="{DynamicResource Label_LinkWeight}"/>
                                        </Grid>
                                    </StackPanel>
                                </Grid>
                            </Border>
                        </DataTemplate>
```

Note: the condition button's `AutomationProperties.Name` above reuses `AutomationName_LinkQTD` as written — that is wrong; add and use a dedicated key instead:

```xml
    <sys:String x:Key="AutomationName_LinkConditions">Edit conditions on this link</sys:String>
```

and set `AutomationProperties.Name="{DynamicResource AutomationName_LinkConditions}"` on the condition button.

- [x] **Step 4: Build, test, commit**

```
dotnet build && dotnet test --nologo
```
Expected: build success, all tests pass.

```bash
git add DialogEditor.ViewModels/ViewModels/ConnectionViewModel.cs "DialogEditor.Avalonia/Views/NodeDetailView.axaml" "DialogEditor.Avalonia/Resources/Strings.axaml" DialogEditor.Tests
git commit -m "feat(detail-pane): link cards with target snippet and labelled controls

Co-Authored-By: Claude Fable 5 <noreply@anthropic.com>"
```

---

### Task 4: Manual verification

**Files:** none (fixes go into the earlier tasks' files if a check fails).

- [ ] **Step 1: Run the app** — `dotnet run --project DialogEditor.Avalonia`, open a project with a loaded conversation.

- [ ] **Step 2: Walk the checklist**

- [ ] Header line shows `#<id> · <category> · <type>` (+ speaker name on PoE2) and updates when node type/speaker changes
- [ ] Git attribution renders as the small muted line under the header (when available)
- [ ] Text boxes and Links sit above all groups; groups are collapsed on first launch
- [ ] Expander summaries read correctly collapsed: identity pair, display/persistence, VO state (`✓ found · M+F` on a node with both files), logic counts, notes count
- [ ] Expanding Voice, selecting another node → Voice stays expanded; restarting the app → collapsed again
- [ ] GUID toggle: PoE2 (speaker data) — GUID boxes hidden until `{}` pressed; PoE1/no data — boxes always visible, no toggle shown
- [ ] Link cards: target snippet shows and trims; `⚙ n` accent state and click-through to the conditions window; QTD and Weight edit correctly; delete and add-link work
- [ ] Play `▶ M`/`▶ F` and 🎤 import still work inside the Voice expander
- [ ] Keyboard: tab order top-to-bottom sensible; expanders toggle with Space/Enter; every control still shows a tooltip

- [ ] **Step 3: Report results** — any failure: fix in the owning task's files, `dotnet build && dotnet test --nologo`, re-verify, commit as `fix(detail-pane): …`.
