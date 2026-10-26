using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

/// Backing model for the first-launch mode chooser (issue 234). Picking a mode saves it;
/// closing the window without picking saves Reading, the safe, read-only mode.
public sealed partial class ModeChooserViewModel : ObservableObject
{
    private bool _chosen;

    /// The mode that was saved: the pick, or Reading when the window was closed without one.
    public EditorMode Result { get; private set; } = EditorMode.Reading;

    /// Raised after a pick so the window can close itself.
    public event Action? CloseRequested;

    [RelayCommand]
    private void ChooseReading() => Choose(EditorMode.Reading);

    [RelayCommand]
    private void ChooseEditing() => Choose(EditorMode.Editing);

    /// Called by the window's Closed handler. Does nothing after a pick; otherwise the close
    /// itself (X, Esc, Alt+F4) is the answer, and the answer is Reading.
    public void OnClosedWithoutChoice()
    {
        if (_chosen) return;
        Choose(EditorMode.Reading, close: false);
    }

    private void Choose(EditorMode mode, bool close = true)
    {
        _chosen = true;
        Result = mode;
        AppSettings.Mode = mode;
        if (close) CloseRequested?.Invoke();
    }
}
