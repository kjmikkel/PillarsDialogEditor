using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DialogEditor.Core.Editing;
using DialogEditor.Core.Export;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.ViewModels;

public partial class ConversationExportItem : ObservableObject
{
    public string Name { get; }

    [ObservableProperty]
    private bool _isChecked;

    public ConversationExportItem(string name, bool isChecked = false)
    {
        Name = name;
        _isChecked = isChecked;
    }
}

public partial class ExportConversationsViewModel : ObservableObject
{
    private readonly Func<string, IReadOnlyList<NodeEditSnapshot>> _nodesFetch;
    private readonly IFilePicker   _filePicker;
    private readonly IFolderPicker _folderPicker;

    public ObservableCollection<ConversationExportItem> ConversationItems { get; }

    [ObservableProperty] private string _selectedFormat = "csv";
    [ObservableProperty] private string _statusText     = "";

    public ExportConversationsViewModel(
        IReadOnlyList<string> conversationNames,
        string? currentConversationName,
        Func<string, IReadOnlyList<NodeEditSnapshot>> nodesFetch,
        IFilePicker filePicker,
        IFolderPicker folderPicker)
    {
        _nodesFetch   = nodesFetch;
        _filePicker   = filePicker;
        _folderPicker = folderPicker;

        ConversationItems = new ObservableCollection<ConversationExportItem>(
            conversationNames.Select(n =>
                new ConversationExportItem(n, n == currentConversationName)));

        foreach (var item in ConversationItems)
            item.PropertyChanged += (_, _) => ExportCommand.NotifyCanExecuteChanged();
    }

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var item in ConversationItems)
            item.IsChecked = true;
    }

    [RelayCommand]
    private void SelectNone()
    {
        foreach (var item in ConversationItems)
            item.IsChecked = false;
    }

    [RelayCommand(CanExecute = nameof(CanExport))]
    private async Task Export()
    {
        var selected = ConversationItems.Where(i => i.IsChecked).ToList();
        var exporter = DialogExporterFactory.GetForFormat(SelectedFormat);
        if (exporter is null) return;

        try
        {
            if (selected.Count == 1)
            {
                var item = selected[0];
                var path = await _filePicker.PickSaveFileAsync(
                    "Export Conversation",
                    item.Name,
                    exporter.FileExtension,
                    exporter.FileExtension.TrimStart('.').ToUpperInvariant());
                if (path is null) return;
                exporter.Export(new ConversationExport(item.Name, _nodesFetch(item.Name)), path);
                StatusText = string.Format(
                    Loc.Get("Status_ExportConversationsSaved"), 1, path);
            }
            else
            {
                var folder = await _folderPicker.PickFolderAsync("Export Conversations");
                if (folder is null) return;
                foreach (var item in selected)
                {
                    var path = Path.Combine(folder, item.Name + exporter.FileExtension);
                    exporter.Export(
                        new ConversationExport(item.Name, _nodesFetch(item.Name)), path);
                }
                StatusText = string.Format(
                    Loc.Get("Status_ExportConversationsSaved"), selected.Count, folder);
            }
        }
        catch (Exception ex)
        {
            AppLog.Error($"Export conversations failed", ex);
            StatusText = string.Format(Loc.Get("Status_ExportConversationsError"), ex.Message);
        }
    }

    private bool CanExport() => ConversationItems.Any(i => i.IsChecked);
}
