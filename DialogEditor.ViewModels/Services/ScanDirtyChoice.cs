namespace DialogEditor.ViewModels.Services;

/// User's choice when starting a project-wide scan while the project has
/// unsaved changes (the scan reads saved state only).
public enum ScanDirtyChoice { SaveAndScan, ScanSavedOnly, Cancel }
