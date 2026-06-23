using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public sealed partial class GuidedTourViewModel : ObservableObject
{
    // The four steps that every running instance uses by default.
    // MainWindow.axaml.cs maps TargetName to an actual named Control.
    public static readonly IReadOnlyList<GuidedTourStep> DefaultSteps =
    [
        new("BrowserPanel", "Tour_Step1_Text"),
        new("CanvasView",   "Tour_Step2_Text"),
        new("DetailPanel",  "Tour_Step3_Text"),
        new("HelpToggle",   "Tour_Step4_Text"),
    ];

    private readonly IReadOnlyList<GuidedTourStep> _steps;

    // Parameterless constructor for production use (MainWindowViewModel).
    public GuidedTourViewModel() : this(DefaultSteps) { }

    // Overload for tests — lets tests inject a short step list.
    public GuidedTourViewModel(IReadOnlyList<GuidedTourStep> steps) => _steps = steps;

    [ObservableProperty] private bool _isVisible;
    [ObservableProperty] private int  _currentIndex;

    public GuidedTourStep CurrentStep => _steps[CurrentIndex];
    public bool           IsLastStep  => CurrentIndex == _steps.Count - 1;

    // Computed display strings — resolved lazily so Loc is not required at construction time.
    public string CounterText      => Loc.Format("Tour_Counter", CurrentIndex + 1, _steps.Count);
    public string CurrentStepText  => Loc.Get(CurrentStep.DescriptionKey);
    public string NextButtonLabel   => IsLastStep ? Loc.Get("Tour_Finish")          : Loc.Get("Tour_Next");
    public string NextButtonTooltip => IsLastStep ? Loc.Get("ToolTip_Tour_Finish")  : Loc.Get("ToolTip_Tour_Next");

    // Raised after every step change (Next, Back, Dismiss, Start) so
    // MainWindow.axaml.cs can swap the adorner target.
    public event Action? StepChanged;

    /// <summary>
    /// Resets to step 0, marks the tour as seen in AppSettings so the
    /// auto-trigger does not fire again, and makes the bar visible.
    /// Safe to call multiple times — always restarts from step 0.
    /// </summary>
    public void Start()
    {
        AppSettings.GuidedTourSeen = true;
        CurrentIndex = 0;   // setter calls OnCurrentIndexChanged which notifies dependents
        IsVisible    = true;
        StepChanged?.Invoke();
    }

    [RelayCommand(CanExecute = nameof(CanBack))]
    private void Back()
    {
        // Guard defensively: CanExecute prevents the UI from calling Back at step 0,
        // but Execute() in tests bypasses CanExecute, so we guard here too.
        if (CurrentIndex <= 0) return;
        CurrentIndex--;
        StepChanged?.Invoke();
    }

    private bool CanBack() => CurrentIndex > 0;

    [RelayCommand]
    private void Next()
    {
        if (IsLastStep) { Dismiss(); return; }
        CurrentIndex++;
        StepChanged?.Invoke();
    }

    [RelayCommand]
    private void Dismiss()
    {
        IsVisible = false;
        StepChanged?.Invoke();
    }

    // Called by the [ObservableProperty] source generator whenever CurrentIndex changes.
    partial void OnCurrentIndexChanged(int value)
    {
        OnPropertyChanged(nameof(CurrentStep));
        OnPropertyChanged(nameof(IsLastStep));
        OnPropertyChanged(nameof(CounterText));
        OnPropertyChanged(nameof(CurrentStepText));
        OnPropertyChanged(nameof(NextButtonLabel));
        OnPropertyChanged(nameof(NextButtonTooltip));
        BackCommand.NotifyCanExecuteChanged();
    }
}
