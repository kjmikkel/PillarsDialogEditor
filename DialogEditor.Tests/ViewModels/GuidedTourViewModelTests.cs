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
    ]);

    [Fact]
    public void Start_SetsIsVisibleTrue()
    {
        var vm = TwoStep();
        vm.Start();
        Assert.True(vm.IsVisible);
    }

    [Fact]
    public void Start_ResetsToStep0()
    {
        var vm = TwoStep();
        vm.Start();
        vm.NextCommand.Execute(null);
        vm.Start();                     // restart
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Start_WritesGuidedTourSeen()
    {
        AppSettings.GuidedTourSeen = false;
        TwoStep().Start();
        Assert.True(AppSettings.GuidedTourSeen);
    }

    [Fact]
    public void Start_RaisesStepChanged()
    {
        var vm    = TwoStep();
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.Start();
        Assert.True(fired);
    }

    [Fact]
    public void Next_AdvancesCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start();
        vm.NextCommand.Execute(null);
        Assert.Equal(1, vm.CurrentIndex);
    }

    [Fact]
    public void Next_AtLastStep_Dismisses()
    {
        // Single-step tour: Next on the only step should end the tour.
        var vm = new GuidedTourViewModel([new GuidedTourStep("A", "K")]);
        vm.Start();
        vm.NextCommand.Execute(null);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Next_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start();
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.NextCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void Back_RetreatsCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start();
        vm.NextCommand.Execute(null);
        vm.BackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Back_AtStep0_IsNoOp()
    {
        var vm = TwoStep();
        vm.Start();
        vm.BackCommand.Execute(null);
        Assert.Equal(0, vm.CurrentIndex);
    }

    [Fact]
    public void Back_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start();
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
        vm.Start();
        vm.DismissCommand.Execute(null);
        Assert.False(vm.IsVisible);
    }

    [Fact]
    public void Dismiss_RaisesStepChanged()
    {
        var vm    = TwoStep();
        vm.Start();
        var fired = false;
        vm.StepChanged += () => fired = true;
        vm.DismissCommand.Execute(null);
        Assert.True(fired);
    }

    [Fact]
    public void IsLastStep_TrueOnlyAtFinalStep()
    {
        var vm = TwoStep();
        vm.Start();
        Assert.False(vm.IsLastStep);
        vm.NextCommand.Execute(null);
        Assert.True(vm.IsLastStep);
    }

    [Fact]
    public void CurrentStep_ReflectsCurrentIndex()
    {
        var vm = TwoStep();
        vm.Start();
        Assert.Equal("BrowserPanel", vm.CurrentStep.TargetName);
        vm.NextCommand.Execute(null);
        Assert.Equal("CanvasView", vm.CurrentStep.TargetName);
    }
}
