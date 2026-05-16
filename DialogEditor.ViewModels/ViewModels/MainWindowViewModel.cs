using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Backup;
using DialogEditor.Core.GameData;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

// Poe1/Poe2 provider types are only referenced via IGameDataProvider interface;
// GameDataProviderFactory.Detect returns the concrete type but we only care about the interface.

namespace DialogEditor.ViewModels;

public partial class MainWindowViewModel : ObservableObject
{
    private readonly IFolderPicker _folderPicker;

    public GameBrowserViewModel  Browser { get; }
    public ConversationViewModel Canvas  { get; }
    public NodeDetailViewModel   Detail  { get; } = new();

    private IGameDataProvider? _provider;
    private ConversationFile?  _currentFile;
    private string             _currentGameDirectory = string.Empty;

    [ObservableProperty] private string              _statusText          = Loc.Get("Status_OpenFolder");
    [ObservableProperty] private IReadOnlyList<string> _availableLanguages = [];
    [ObservableProperty] private string              _selectedLanguage    = string.Empty;
    [ObservableProperty] private bool                _isModified;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserFlyoutOpen))]
    private bool _isBrowserExpanded = AppSettings.BrowserPinned;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsBrowserFlyoutOpen))]
    private bool _isBrowserPinned = AppSettings.BrowserPinned;

    [ObservableProperty] private bool    _isDetailExpanded       = AppSettings.DetailExpanded;
    [ObservableProperty] private string? _currentConversationName;

    public bool IsBrowserFlyoutOpen => IsBrowserExpanded && !IsBrowserPinned;

    // ── Window title reflects dirty state ─────────────────────────────────
    public string WindowTitle =>
        IsModified && CurrentConversationName is not null
            ? $"● {CurrentConversationName}"
            : Loc.Get("App_Title");

    // ── Unsaved-changes navigation guard ──────────────────────────────────
    private ConversationFile? _pendingFile;
    public event Action? UnsavedChangesRequested;

    public void SaveAndProceed()
    {
        SaveCommand.Execute(null);
        if (_pendingFile is not null) LoadConversationFile(_pendingFile);
        _pendingFile = null;
    }

    public void DiscardAndProceed()
    {
        if (_pendingFile is not null) LoadConversationFile(_pendingFile);
        _pendingFile = null;
    }

    public void CancelPendingNavigation() => _pendingFile = null;

    // ── Partial hooks ─────────────────────────────────────────────────────
    partial void OnIsBrowserPinnedChanged(bool value)
    {
        AppSettings.BrowserPinned = value;
        IsBrowserExpanded = value;
    }

    partial void OnIsDetailExpandedChanged(bool value) => AppSettings.DetailExpanded = value;

    partial void OnCurrentConversationNameChanged(string? value)
        => OnPropertyChanged(nameof(WindowTitle));

    partial void OnIsModifiedChanged(bool value)
        => OnPropertyChanged(nameof(WindowTitle));

    // ── Constructor ───────────────────────────────────────────────────────
    public MainWindowViewModel(IDispatcher dispatcher, IFolderPicker folderPicker)
    {
        _folderPicker = folderPicker;
        Browser = new GameBrowserViewModel(dispatcher);
        Canvas  = new ConversationViewModel(dispatcher);

        Browser.ConversationSelected += OnConversationSelected;

        Canvas.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(ConversationViewModel.SelectedNode))
                Detail.Load(Canvas.SelectedNode);
            if (e.PropertyName == nameof(ConversationViewModel.IsModified))
            {
                IsModified = Canvas.IsModified;
                SaveCommand.NotifyCanExecuteChanged();
            }
        };

        var last = AppSettings.LastGameDirectory;
        if (!string.IsNullOrEmpty(last) && Directory.Exists(last))
            LoadDirectory(last);
    }

    partial void OnSelectedLanguageChanged(string value)
    {
        if (_provider is null || string.IsNullOrEmpty(value)) return;
        _provider.Language = value;
        AppSettings.LastLanguage = value;
        if (_currentFile is not null)
            OnConversationSelected(_currentFile);
    }

    // ── Open folder ───────────────────────────────────────────────────────
    [RelayCommand]
    private async Task OpenFolder()
    {
        var path = await _folderPicker.PickFolderAsync(Loc.Get("Dialog_SelectFolder"));
        if (path is null) return;
        LoadDirectory(path);
    }

    private void LoadDirectory(string path)
    {
        var provider = GameDataProviderFactory.Detect(path);
        if (provider is null)
        {
            AppLog.Warn($"Folder not recognised as PoE1 or PoE2 root: {path}");
            StatusText = Loc.Get("Status_FolderNotRecognized");
            return;
        }

        try
        {
            AppSettings.LastGameDirectory = path;
            _currentGameDirectory = path;
            _provider = provider;
            CurrentConversationName = null;
            SpeakerNameService.Register(provider.LoadSpeakerNames());
            AvailableLanguages = provider.AvailableLanguages;
            SelectedLanguage   = AppSettings.PickLanguage(AvailableLanguages, AppSettings.LastLanguage);
            Browser.Load(provider);
            AppLog.Info($"Loaded {provider.GameName} from {path}");
            StatusText = Loc.Format("Status_FolderLoaded", provider.GameName, path);

            if (!AppSettings.IsKnownGameDirectory(path))
                _ = OfferBackupAsync(path);
            AppSettings.MarkGameDirectoryKnown(path);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to initialise game data from {path}", ex);
            StatusText = Loc.Format("Status_LoadError", path, ex.Message);
        }
    }

    // ── Backup offer (first time per game folder) ─────────────────────────
    private async Task OfferBackupAsync(string gameDirectory)
    {
        var pick = await _folderPicker.PickFolderAsync(Loc.Get("Dialog_SelectBackupFolder"));
        if (pick is null) return;

        var timestamp  = DateTime.Now.ToString("yyyy-MM-ddTHH-mm");
        var backupRoot = Path.Combine(pick, timestamp);
        AppSettings.SetBackupPath(gameDirectory, pick);

        StatusText = Loc.Get("Status_BackupInProgress");
        try
        {
            await RunProviderBackupAsync(backupRoot, _provider!);
            AppLog.Info($"Backup written to {backupRoot}");
            StatusText = Loc.Format("Status_BackupComplete", backupRoot);
        }
        catch (Exception ex)
        {
            AppLog.Error("Backup failed", ex);
            StatusText = Loc.Format("Status_BackupError", ex.Message);
        }
    }

    private static async Task RunProviderBackupAsync(string backupRoot, IGameDataProvider provider)
    {
        var (convRoot, stRoot) = provider.GetBackupRoots();
        await BackupService.BackupAsync(convRoot, Path.Combine(backupRoot, "conversations"), default);
        await BackupService.BackupAsync(stRoot,   Path.Combine(backupRoot, "stringtables"),  default);
    }

    // ── Restore backup ────────────────────────────────────────────────────
    [RelayCommand]
    private async Task RestoreBackup()
    {
        var backupPick = AppSettings.GetBackupPath(_currentGameDirectory);
        if (backupPick is null)
        {
            StatusText = Loc.Get("Status_NoBackupFound");
            return;
        }

        // Find the most recent timestamped subfolder
        var subdirs = Directory.GetDirectories(backupPick)
            .OrderByDescending(d => d)
            .ToList();
        if (subdirs.Count == 0) { StatusText = Loc.Get("Status_NoBackupFound"); return; }

        var backupRoot = subdirs[0];
        StatusText     = Loc.Get("Status_RestoreInProgress");
        try
        {
            var (convRoot, stRoot) = _provider!.GetBackupRoots();
            await BackupService.RestoreAsync(
                Path.Combine(backupRoot, "conversations"), convRoot, default);
            await BackupService.RestoreAsync(
                Path.Combine(backupRoot, "stringtables"),  stRoot,  default);
            AppLog.Info("Backup restored");
            StatusText = Loc.Get("Status_RestoreComplete");
            if (_currentFile is not null)
                LoadConversationFile(_currentFile);
        }
        catch (Exception ex)
        {
            AppLog.Error("Restore failed", ex);
            StatusText = Loc.Format("Status_RestoreError", ex.Message);
        }
    }

    // ── Save ──────────────────────────────────────────────────────────────
    [RelayCommand(CanExecute = nameof(CanSave))]
    private void Save()
    {
        if (_provider is null || _currentFile is null) return;
        try
        {
            var snapshot = Canvas.BuildSnapshot();
            _provider.SaveConversation(_currentFile, snapshot);
            Canvas.IsModified = false;
            IsModified = false;
            SaveCommand.NotifyCanExecuteChanged();
            AppLog.Info($"Saved {_currentFile.Name}");
            StatusText = Loc.Format("Status_Saved", _currentFile.Name);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to save '{_currentFile?.Name}'", ex);
            StatusText = Loc.Format("Status_SaveError", _currentFile!.Name, ex.Message);
        }
    }

    private bool CanSave() => _provider is not null && _currentFile is not null && IsModified;

    // ── Conversation selection ────────────────────────────────────────────
    private void OnConversationSelected(ConversationFile file)
    {
        if (_provider is null) return;

        if (IsModified && _currentFile is not null)
        {
            _pendingFile = file;
            UnsavedChangesRequested?.Invoke();
            return;
        }

        LoadConversationFile(file);
    }

    private void LoadConversationFile(ConversationFile file)
    {
        if (_provider is null) return;
        try
        {
            _currentFile = file;
            var conversation = _provider.LoadConversation(file);
            Canvas.Load(conversation);
            Detail.Clear();
            IsModified = false;
            CurrentConversationName = file.Name;
            if (!IsBrowserPinned) IsBrowserExpanded = false;
            StatusText = Loc.Format("Status_ConversationLoaded", file.Name, conversation.Nodes.Count);
        }
        catch (Exception ex)
        {
            AppLog.Error($"Failed to load conversation '{file.Name}'", ex);
            StatusText = Loc.Format("Status_LoadError", file.Name, ex.Message);
        }
    }
}
