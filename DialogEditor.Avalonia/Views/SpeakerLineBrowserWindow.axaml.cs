using System;
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
/// Non-modal results window for SpeakerLineBrowserViewModel. Rows are wrapped in a
/// display record that pre-resolves the localized origin badge and node/variant label
/// (mirrors FindInProjectWindow's language-label approach — cheaper than a
/// globally-registered converter for one window's columns). The window kicks off the
/// off-thread scan on construction and re-projects Rows whenever the VM raises the change.
/// </summary>
public partial class SpeakerLineBrowserWindow : Window
{
    private readonly SpeakerLineBrowserViewModel? _vm;

    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public SpeakerLineBrowserWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public SpeakerLineBrowserWindow(SpeakerLineBrowserViewModel vm) : this()
    {
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += Vm_PropertyChanged;
        RefreshResults();
        _ = vm.ScanAsync();   // starts the off-thread whole-game scan
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_vm is not null)
        {
            _vm.PropertyChanged -= Vm_PropertyChanged;
            _vm.CancelScanCommand.Execute(null);   // stop any scan still in flight
        }
    }

    private void Vm_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SpeakerLineBrowserViewModel.Rows))
            RefreshResults();
    }

    private void RefreshResults()
    {
        if (_vm is null) return;
        ResultsList.ItemsSource = _vm.Rows.Select(r => new DisplayRow(
            r,
            Loc.Get(r.Origin switch
            {
                LineOrigin.New    => "SpeakerLines_Origin_New",
                LineOrigin.Edited => "SpeakerLines_Origin_Edited",
                _                 => "SpeakerLines_Origin_Vanilla",
            }),
            NodeLabelFor(r))).ToList();
    }

    private static string NodeLabelFor(SpeakerLineRow r)
    {
        var label = $"{Loc.Get("SpeakerLines_Node")} {r.NodeId}";
        return r.Variant == LineVariant.Female
            ? $"{label} · {Loc.Get("SpeakerLines_Variant_Female")}"
            : label;
    }

    private void OnRowActivated(object? sender, RoutedEventArgs e)
    {
        if (_vm is null) return;
        if ((sender as Control)?.DataContext is DisplayRow display)
            _vm.NavigateTo(display.Row);
    }

    private void ResultsList_KeyDown(object? sender, KeyEventArgs e)
    {
        if (_vm is null || e.Key != Key.Enter) return;
        if (ResultsList.SelectedItem is DisplayRow display)
        {
            _vm.NavigateTo(display.Row);
            e.Handled = true;
        }
    }

    // Wraps a SpeakerLineRow with its pre-resolved origin badge and node/variant label.
    private sealed record DisplayRow(SpeakerLineRow Row, string OriginLabel, string NodeLabel);
}
