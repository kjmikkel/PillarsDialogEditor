using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public sealed partial class GuidedTourViewModel : ObservableObject
{
    // The steps that every running instance uses by default, in the order a newcomer
    // meets them: pick a conversation, see it on the canvas, edit the selected node,
    // find further help.
    //
    // TargetName is an opaque key, NOT a control name — this project stays Avalonia-free.
    // DialogEditor.Avalonia's Docking/TourTargetResolver owns the mapping from key to a
    // live Control, and MainWindow.axaml.cs drives it (OnTourStepChanged). That indirection is what lets the first three targets be
    // Dock-hosted tool content, which has no compile-time x:Name to look up: they are
    // resolved by view type from the live visual tree, and the tool is revealed first if
    // the user has closed or backgrounded its tab.
    public static readonly IReadOnlyList<GuidedTourStep> DefaultSteps =
    [
        new("BrowserPanel", "Tour_Step1_Text"),
        new("CanvasView",   "Tour_Step2_Text"),
        new("DetailPanel",  "Tour_Step3_Text"),
        new("HelpToggle",   "Tour_Step4_Text"),
    ];

    // The reading-mode tour (issue 234): what a reader needs, no editing. ModeBadge is the
    // always-visible mode control; TourTargetResolver finds it by name.
    public static readonly IReadOnlyList<GuidedTourStep> ReaderSteps =
    [
        new("BrowserPanel", "Tour_Reader_Step1_Text"),
        new("CanvasView",   "Tour_Reader_Step2_Text"),
        new("DetailPanel",  "Tour_Reader_Step3_Text"),
        new("ModeBadge",    "Tour_Reader_Step4_Text"),
    ];

    private readonly IReadOnlyList<GuidedTourStep> _editingSteps;
    private readonly IReadOnlyList<GuidedTourStep> _readerSteps;
    // The steps of the tour that is running (or ran last); Start(mode) picks them.
    private IReadOnlyList<GuidedTourStep> _steps;

    // Parameterless constructor for production use (MainWindowViewModel).
    public GuidedTourViewModel() : this(DefaultSteps, ReaderSteps) { }

    // Overload for tests — lets tests inject short step lists.
    public GuidedTourViewModel(IReadOnlyList<GuidedTourStep> editingSteps, IReadOnlyList<GuidedTourStep> readerSteps)
    {
        _editingSteps = editingSteps;
        _readerSteps  = readerSteps;
        _steps        = editingSteps;
    }

    /// <summary>Whether the tour for <paramref name="mode"/> has already been shown.</summary>
    public static bool HasSeen(EditorMode mode) =>
        mode == EditorMode.Reading ? AppSettings.ReaderTourSeen : AppSettings.GuidedTourSeen;

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
    /// Runs the tour for <paramref name="mode"/> from step 0, marks that mode's tour as seen in
    /// AppSettings so the auto-trigger does not fire again, and makes the bar visible.
    /// Safe to call multiple times — always restarts from step 0.
    /// </summary>
    public void Start(EditorMode mode)
    {
        if (mode == EditorMode.Reading) AppSettings.ReaderTourSeen = true;
        else                            AppSettings.GuidedTourSeen = true;
        _steps = mode == EditorMode.Reading ? _readerSteps : _editingSteps;
        CurrentIndex = 0;   // setter calls OnCurrentIndexChanged which notifies dependents
        // The step list may have changed under an unchanged index (0 -> 0 raises nothing).
        OnCurrentIndexChanged(0);
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
