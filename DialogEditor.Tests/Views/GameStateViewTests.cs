using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Conditions;
using DialogEditor.Core.Models;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.Views;

public class GameStateViewTests
{
    private static ConditionLeaf Leaf(string fullName, params string[] p) => new(fullName, p, false, "And");

    private static (Window Window, GameStateView View) Show(params ConditionNode[] conditions)
    {
        Loc.Configure(new StubStringProvider());
        var vm = new GameStateViewModel(new GameState(), (_, _) => null);
        vm.Load([conditions], "poe2");
        var view = new GameStateView { DataContext = vm };
        var window = new Window { Content = view, Width = 400, Height = 800 };
        window.Show();
        global::Avalonia.Threading.Dispatcher.UIThread.RunJobs();   // let the ItemsControls realise their rows
        return (window, view);
    }

    [AvaloniaFact]
    public void GameStateView_EveryInteractiveControlHasATooltip()
    {
        var (_, view) = Show(
            Leaf("Boolean IsGlobalValue(String, Operator, Int32)", "g", "EqualTo", "1"),
            Leaf("Boolean IsCompanionActiveInParty(Guid)", "guid-x"),
            Leaf("Boolean HasConversationNodeBeenPlayed(String, Int32)", "conv", "3"),
            Leaf("Boolean IsQuestActive(String)", "quest-a"),
            Leaf("Boolean IsInCombat()"));

        var controls = view.GetVisualDescendants()
            .Where(v => v is TextBox or ComboBox or Button or CheckBox)
            .Where(v => !v.GetVisualAncestors().OfType<ComboBox>().Any())   // a ComboBox's own template parts
            .Cast<Control>().ToList();
        Assert.True(controls.Count >= 7);   // clear + checkbox + 5 inputs
        foreach (var c in controls)
            Assert.True(ToolTip.GetTip(c) is not null, $"{c.GetType().Name} '{AutomationProperties.GetName(c)}' has no tooltip (dc {c.DataContext?.GetType().Name}, tip {ToolTip.GetTip(c)}, name {AutomationProperties.GetName(c)}, help {AutomationProperties.GetHelpText(c)})");
    }

    [AvaloniaFact]
    public void GameStateView_EveryInteractiveControlHasAnAutomationName()
    {
        var (_, view) = Show(
            Leaf("Boolean IsGlobalValue(String, Operator, Int32)", "g", "EqualTo", "1"),
            Leaf("Boolean IsCompanionActiveInParty(Guid)", "guid-x"),
            Leaf("Boolean HasConversationNodeBeenPlayed(String, Int32)", "conv", "3"),
            Leaf("Boolean IsQuestActive(String)", "quest-a"),
            Leaf("Boolean IsInCombat()"));

        var controls = view.GetVisualDescendants()
            .Where(v => v is TextBox or ComboBox or Button or CheckBox)
            .Where(v => !v.GetVisualAncestors().OfType<ComboBox>().Any())   // a ComboBox's own template parts
            .Cast<Control>().ToList();
        Assert.True(controls.Count >= 7);
        foreach (var c in controls)
            Assert.False(string.IsNullOrWhiteSpace(AutomationProperties.GetName(c)), $"{c.GetType().Name} has no automation name");
    }

    [AvaloniaFact]
    public void GameStateView_HidesEmptySections()
    {
        var (_, view) = Show(Leaf("Boolean IsGlobalValue(String, Operator, Int32)", "g", "EqualTo", "1"));

        Assert.True(view.FindControl<Control>("GlobalsSection")!.IsVisible);
        Assert.False(view.FindControl<Control>("CompanionsSection")!.IsVisible);
        Assert.False(view.FindControl<Control>("PlayedNodesSection")!.IsVisible);
        Assert.False(view.FindControl<Control>("QuestsSection")!.IsVisible);
        Assert.False(view.FindControl<Control>("CombatSection")!.IsVisible);
    }
}
