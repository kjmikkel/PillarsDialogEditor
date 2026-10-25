using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(StatusText), nameof(HasStatus))]
    private bool _hasConflict;

    /// What is wrong with this entry, as visible text in its row (GitHub issue 259): it used to
    /// be only a hover tooltip on a ⚠ glyph, out of reach of keyboard and screen-reader users.
    public string StatusText =>
        LoadError is not null ? Loc.Format("PatchManager_EntryLoadErrorDetail", LoadError)
        : HasConflict          ? Loc.Get("PatchManager_EntryConflictDetail")
        : string.Empty;

    public bool HasStatus => StatusText.Length > 0;

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
