using DialogEditor.Core.Editing;
using DialogEditor.ViewModels;
using Xunit;

namespace DialogEditor.Tests.ViewModels;

public class AnnotationViewModelTests
{
    // ── Default values ────────────────────────────────────────────────────

    [Fact]
    public void DefaultTitle_IsEmpty()
    {
        var vm = new AnnotationViewModel();
        Assert.Equal(string.Empty, vm.Title);
    }

    [Fact]
    public void DefaultColorKey_IsYellow()
    {
        var vm = new AnnotationViewModel();
        Assert.Equal("Yellow", vm.ColorKey);
    }

    [Fact]
    public void DefaultWidth_Is240()
    {
        var vm = new AnnotationViewModel();
        Assert.Equal(240, vm.Width);
    }

    [Fact]
    public void DefaultHeight_Is140()
    {
        var vm = new AnnotationViewModel();
        Assert.Equal(140, vm.Height);
    }

    // ── Minimum size clamping ─────────────────────────────────────────────

    [Fact]
    public void Width_BelowMinimum_ClampsTo120()
    {
        var vm = new AnnotationViewModel();
        vm.Width = 50;
        Assert.Equal(120, vm.Width);
    }

    [Fact]
    public void Width_AtMinimum_Accepted()
    {
        var vm = new AnnotationViewModel();
        vm.Width = 120;
        Assert.Equal(120, vm.Width);
    }

    [Fact]
    public void Width_AboveMinimum_Accepted()
    {
        var vm = new AnnotationViewModel();
        vm.Width = 300;
        Assert.Equal(300, vm.Width);
    }

    [Fact]
    public void Height_BelowMinimum_ClampsTo60()
    {
        var vm = new AnnotationViewModel();
        vm.Height = 10;
        Assert.Equal(60, vm.Height);
    }

    [Fact]
    public void Height_AtMinimum_Accepted()
    {
        var vm = new AnnotationViewModel();
        vm.Height = 60;
        Assert.Equal(60, vm.Height);
    }

    [Fact]
    public void Height_AboveMinimum_Accepted()
    {
        var vm = new AnnotationViewModel();
        vm.Height = 200;
        Assert.Equal(200, vm.Height);
    }

    // ── SyncScreen ────────────────────────────────────────────────────────

    [Fact]
    public void SyncScreen_ComputesScreenX_AsWorldMinusViewportTimesZoom()
    {
        var vm = new AnnotationViewModel { X = 100 };
        vm.SyncScreen(zoom: 2.0, vX: 20, vY: 0);
        Assert.Equal((100 - 20) * 2.0, vm.ScreenX);
    }

    [Fact]
    public void SyncScreen_ComputesScreenY_AsWorldMinusViewportTimesZoom()
    {
        var vm = new AnnotationViewModel { Y = 50 };
        vm.SyncScreen(zoom: 2.0, vX: 0, vY: 10);
        Assert.Equal((50 - 10) * 2.0, vm.ScreenY);
    }

    [Fact]
    public void SyncScreen_ComputesScreenWidth_AsWidthTimesZoom()
    {
        var vm = new AnnotationViewModel();
        vm.Width = 240;
        vm.SyncScreen(zoom: 1.5, vX: 0, vY: 0);
        Assert.Equal(240 * 1.5, vm.ScreenWidth);
    }

    [Fact]
    public void SyncScreen_ComputesScreenHeight_AsHeightTimesZoom()
    {
        var vm = new AnnotationViewModel();
        vm.Height = 140;
        vm.SyncScreen(zoom: 0.5, vX: 0, vY: 0);
        Assert.Equal(140 * 0.5, vm.ScreenHeight);
    }

    [Fact]
    public void SyncScreen_RaisesPropertyChangedForScreenProperties()
    {
        var vm = new AnnotationViewModel();
        var raised = new List<string?>();
        vm.PropertyChanged += (_, e) => raised.Add(e.PropertyName);
        vm.SyncScreen(2.0, 0, 0);
        Assert.Contains(nameof(AnnotationViewModel.ScreenX),      raised);
        Assert.Contains(nameof(AnnotationViewModel.ScreenY),      raised);
        Assert.Contains(nameof(AnnotationViewModel.ScreenWidth),  raised);
        Assert.Contains(nameof(AnnotationViewModel.ScreenHeight), raised);
    }

    // ── Snapshot round-trip ───────────────────────────────────────────────

    [Fact]
    public void ToSnapshot_ThenFromSnapshot_PreservesAllFields()
    {
        var original = new AnnotationViewModel
        {
            Title    = "Test",
            Body     = "Body text",
            ColorKey = "Red",
            X        = 10,
            Y        = 20,
            Width    = 300,
            Height   = 200,
        };

        var roundtripped = AnnotationViewModel.FromSnapshot(original.ToSnapshot());

        Assert.Equal(original.Title,    roundtripped.Title);
        Assert.Equal(original.Body,     roundtripped.Body);
        Assert.Equal(original.ColorKey, roundtripped.ColorKey);
        Assert.Equal(original.X,        roundtripped.X);
        Assert.Equal(original.Y,        roundtripped.Y);
        Assert.Equal(original.Width,    roundtripped.Width);
        Assert.Equal(original.Height,   roundtripped.Height);
    }

    [Fact]
    public void FromSnapshot_ClampsWidthAndHeightToMinimum()
    {
        var snap = new AnnotationSnapshot("id", "T", "B", "Yellow", 0, 0, Width: 10, Height: 5);
        var vm   = AnnotationViewModel.FromSnapshot(snap);
        Assert.Equal(120, vm.Width);
        Assert.Equal(60,  vm.Height);
    }

    // ── Undo integration ──────────────────────────────────────────────────

    [Fact]
    public void Title_WithNoUndoStack_SetsDirect()
    {
        var vm = new AnnotationViewModel();
        vm.Title = "Hello";
        Assert.Equal("Hello", vm.Title);
    }

    [Fact]
    public void Title_WithUndoStack_IsUndoable()
    {
        var stack = new UndoRedoStack();
        var vm    = new AnnotationViewModel { UndoStack = stack };
        vm.Title  = "Hello";
        Assert.Equal("Hello", vm.Title);
        stack.Undo();
        Assert.Equal(string.Empty, vm.Title);
    }

    [Fact]
    public void Title_SetSameValue_DoesNotPushUndo()
    {
        var stack = new UndoRedoStack();
        var vm    = new AnnotationViewModel { UndoStack = stack };
        vm.Title  = "Same";
        vm.Title  = "Same"; // no-op
        stack.Undo(); // undoes the first set
        Assert.Equal(string.Empty, vm.Title);
        Assert.False(stack.CanUndo); // nothing left on stack
    }
}
