using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Markup.Xaml;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Views;

/// Non-modal, read-only reputation/disposition balance report. The Source/Scope combo boxes
/// are populated in code-behind so their display text is localised while the selected value
/// stays the enum — the same "label + enum" pairing other windows use for enum choices.
public partial class RepDispositionBalanceWindow : Window
{
    // Parameterless ctor so the XAML compiler embeds the type (avoids AVLN3000).
    public RepDispositionBalanceWindow()
    {
        InitializeComponent();
        HintBar.AttachTo(this);
    }

    public RepDispositionBalanceWindow(RepDispositionBalanceViewModel vm) : this()
    {
        DataContext = vm;

        SourceBox.ItemsSource = new List<KeyValuePair<string, BalanceSource>>
        {
            new(Loc.Get("RepBalance_Source_ProjectChanges"),   BalanceSource.ProjectChanges),
            new(Loc.Get("RepBalance_Source_OnDiskPlusChanges"), BalanceSource.OnDiskPlusChanges),
        };
        SourceBox.DisplayMemberBinding = new Binding("Key");
        SourceBox.SelectedIndex = 0;
        SourceBox.SelectionChanged += (_, _) =>
        {
            if (SourceBox.SelectedItem is KeyValuePair<string, BalanceSource> kv)
                vm.Source = kv.Value;
        };

        ScopeBox.ItemsSource = new List<KeyValuePair<string, BalanceScope>>
        {
            new(Loc.Get("RepBalance_Scope_Current"), BalanceScope.Current),
            new(Loc.Get("RepBalance_Scope_All"),     BalanceScope.All),
        };
        ScopeBox.DisplayMemberBinding = new Binding("Key");
        ScopeBox.SelectedIndex = 0;
        ScopeBox.SelectionChanged += (_, _) =>
        {
            if (ScopeBox.SelectedItem is KeyValuePair<string, BalanceScope> kv)
                vm.Scope = kv.Value;
        };
    }
}
