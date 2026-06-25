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
    // Paths for .dialogpack entries: VoFolder is the extracted vo/ subdirectory;
    // TempDir is its parent temp directory and must be deleted when the entry is removed.
    public string?        VoFolder     { get; }
    public string?        TempDir      { get; }

    [ObservableProperty] private bool _hasConflict;

    public PatchEntryViewModel(string fullPath, DialogProject project,
        string? voFolder = null, string? tempDir = null)
    {
        FullPath    = fullPath;
        DisplayPath = Path.GetFileName(fullPath);
        Project     = project;
        ProjectName = project.Name;
        PatchCount  = project.Patches.Count;
        VoFolder    = voFolder;
        TempDir     = tempDir;
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
