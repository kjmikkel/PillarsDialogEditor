using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.ViewModels;

public sealed class GuidedTourViewModelTests
{
    // Two-step fixture used by most tests.
    private static GuidedTourViewModel TwoStep() => new(
    [
        new GuidedTourStep("BrowserPanel", "Tour_Step1_Text"),
        new GuidedTourStep("CanvasView",   "Tour_Step2_Text"),
    ], GuidedTourViewModel.ReaderSteps);

    [Fact]
    public void Start_SetsIsVisibleTrue()
    {
        var vm = TwoStep();
        vm.Start(EditorMode.Editing);
        Assert.True(vm.IsVisible);
    }

    [Fact]
    public void Start_ResetsToStep0()
    {
        var vm = TwoStep();
        vm.Start(EditorMode.Editing);
        vm.NextCommand.Execute(null);
        vm.Start(EditorMode.Editing);                     // restart
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Start_WritesGuidedTourSeen()
    {
        AppSettings.GuidedTourSeen = false;
        TwoStep().Start(EditorMode.Editing);
        Assert.True(AppSettings.GuidedTourSeen);
    }

    [Fact]
    public void Start_RaisesStepChanged()
    {
        var vm    = TwoStep();
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.Start(EditorMode.Editing);
        Assert.True(fired);
    }

    [Fact]
    public void Next_AdvancesCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start(EditorMode.Editing);
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.CurrentIndex);
    }

    [Fact]
    public void Next_AtLastStep_Dismisses()
    {
        // Single-step tour: Next on the only step should end the tour.
        var vm = new GuidedTourViewModel([new GuidedTourStep("A", "K")], GuidedTourViewModel.ReaderSteps);
        vm.Start(EditorMode.Editing);
        vm.NextCommand.Execute(null);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Next_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start(EditorMode.Editing);
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.NextCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void Back_RetreatsCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start(EditorMode.Editing);
        vm.NextCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Back_AtStep0_IsNoOp()
    {
        var vm = TwoStep();
        vm.Start(EditorMode.Editing);
        vm.BackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Back_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start(EditorMode.Editing);
        vm.NextCommand.Execute(null);
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.BackCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void Dismiss_SetsIsVisibleFalse()
    {
        var vm = TwoStep();
        vm.Start(EditorMode.Editing);
        vm.DismissCommand.Execute(null);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Dismiss_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start(EditorMode.Editing);
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.DismissCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void IsLastStep_TrueOnlyAtFinalStep()
    {
        var vm = TwoStep();
        vm.Start(EditorMode.Editing);
        Assert.False(vm.IsLastStep);
        vm.NextCommand.Execute(null);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public void CurrentStep_ReflectsCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start(EditorMode.Editing);
        Assert.Equal("BrowserPanel", vm.CurrentStep.TargetName);
        vm.NextCommand.Execute(null);
        Assert.Equal("CanvasView", vm.CurrentStep.TargetName);
    }

    // The four onboarding targets, in the order a newcomer meets them: pick a
    // conversation, see it on the canvas, edit the selected node, find further help.
    // Pinned because the Browser/Details steps were once silently dropped when the
    // docking shell removed their named controls — the steps are the contract, and
    // MainWindow is responsible for resolving each target, not for defining the set.
    [Fact]
    public void DefaultSteps_CoverTheFourOnboardingTargets()
    {
        Assert.Equal(
            new[] { "BrowserPanel", "CanvasView", "DetailPanel", "HelpToggle" },
            GuidedTourViewModel.DefaultSteps.Select(s => s.TargetName));

        Assert.Equal(
            new[] { "Tour_Step1_Text", "Tour_Step2_Text", "Tour_Step3_Text", "Tour_Step4_Text" },
            GuidedTourViewModel.DefaultSteps.Select(s => s.DescriptionKey));
    }
}
