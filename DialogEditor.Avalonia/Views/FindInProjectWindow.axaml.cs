using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

/// <summary>
/// Non-modal results window for the (Task 3) ProjectFindViewModel. Rows carry a
/// display wrapper (<see cref="FindResultDisplayRow"/>) rather than the raw
/// FindMatchRow directly, because FindMatchRow.Language is "" for the primary
/// language and needs to be shown as the localized "Default" label — resolving
/// that once here in code-behind (rather than a dedicated IValueConverter +
/// App.axaml registration) is the lighter of the two mechanisms the Task 4
/// brief allows, since it doesn't require a new globally-registered converter
/// for a single window's single column.
/// </summary>
public partial class FindInProjectWindow : Window
{
    private readonly ProjectFindViewModel? _vm;

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public FindInProjectWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public FindInProjectWindow(ProjectFindViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += Vm_PropertyChanged;
        RefreshResults();
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(ProjectFindViewModel.Results))
            RefreshResults();
    }

    private void RefreshResults()
    {
        if (_vm is null) return;
        ResultsList.ItemsSource = _vm.Results
            .Select(r => new FindResultDisplayRow(r,
                string.IsNullOrEmpty(r.Language) ? Loc.Get("FindInProject_PrimaryLanguage") : r.Language))
            .ToList();
    }

    // Wired from each row's DoubleTapped in the ItemTemplate.
    private void OnResultActivated(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if ((sender as Control)?.DataContext is FindResultDisplayRow display)
            _vm.NavigateTo(display.Row);
    }

    // Enter on the selected row activates it, mirroring double-click.
    private void ResultsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Key != Key.Enter) return;
        if (ResultsList.SelectedItem is FindResultDisplayRow display)
        {
            _vm.NavigateTo(display.Row);
            e.Handled = true;
        }
    }

    // Wraps a FindMatchRow with its pre-resolved language display label.
    private sealed record FindResultDisplayRow(FindMatchRow Row, string LanguageDisplay);
}
