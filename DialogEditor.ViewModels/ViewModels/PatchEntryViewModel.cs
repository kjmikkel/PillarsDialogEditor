using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Patch;

namespace DialogEditor.ViewModels;

public partial class PatchEntryViewModel : ObservableObject
{
    public string         DisplayPath  { get; }
    public string         FullPath     { get; }
    public string         ProjectName  { get; }
    public int            PatchCount   { get; }
    public DialogProject? Project      { get; }
    public string?        LoadError    { get; }

    [ObservableProperty] private bool _hasConflict;

    public PatchEntryViewModel(string fullPath, DialogProject project)
    {
        FullPath    = fullPath;
        DisplayPath = Path.GetFileName(fullPath);
        Project     = project;
        ProjectName = project.Name;
        PatchCount  = project.Patches.Count;
    }

    public PatchEntryViewModel(string fullPath, string errorMessage)
    {
        FullPath    = fullPath;
        DisplayPath = Path.GetFileName(fullPath);
        ProjectName = DisplayPath;
        LoadError   = errorMessage;
        PatchCount  = 0;
    }

    public bool IsLoaded => LoadError is null;
}
