using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Media;
using DialogEditor.Patch.GitConflict;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class GitConflictResolutionWindow : Window
{
    private static readonly IBrush CommonBrush = new SolidColorBrush(Color.Parse("#e8e8e8"));
    private static readonly IBrush MineBrush   = new SolidColorBrush(Color.Parse("#9be39b"));
    private static readonly IBrush TheirsBrush = new SolidColorBrush(Color.Parse("#ff9c9c"));

    private readonly GitConflictResolutionViewModel? _vm;

    // Parameterless constructor required so the XAML compiler embeds this type
    // (avoids AVLN3000 wiping precompiled resources on a clean build).
    public GitConflictResolutionWindow() => InitializeComponent();

    public GitConflictResolutionWindow(GitConflictResolutionViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.RequestClose += Close;
        CancelButton.Click += (_, _) => Close();
        vm.PropertyChanged += OnVmPropertyChanged;
        UpdateDiff(vm.Selected);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_vm is not null)
        {
            _vm.RequestClose      -= Close;
            _vm.PropertyChanged   -= OnVmPropertyChanged;
        }
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(GitConflictResolutionViewModel.Selected)
            && DataContext is GitConflictResolutionViewModel vm)
            UpdateDiff(vm.Selected);
    }

    // Render the selected conflict's mine/theirs values with the differing run
    // highlighted. For non-field (structural) conflicts there is nothing to diff,
    // so the raw values are shown.
    private void UpdateDiff(ConflictRowViewModel? row)
    {
        var mine   = new InlineCollection();
        var theirs = new InlineCollection();

        if (row is { Kind: MergeConflictKind.FieldEdit })
        {
            foreach (var span in TextDiff.Diff(row.MineValue, row.TheirsValue))
            {
                switch (span.Kind)
                {
                    case DiffKind.Common:
                        mine.Add(MakeRun(span.Text, CommonBrush));
                        theirs.Add(MakeRun(span.Text, CommonBrush));
                        break;
                    case DiffKind.MineOnly:
                        mine.Add(MakeRun(span.Text, MineBrush));
                        break;
                    case DiffKind.TheirsOnly:
                        theirs.Add(MakeRun(span.Text, TheirsBrush));
                        break;
                }
            }
        }
        else if (row is not null)
        {
            mine.Add(MakeRun(row.MineValue, CommonBrush));
            theirs.Add(MakeRun(row.TheirsValue, CommonBrush));
        }

        MineDiffText.Inlines   = mine;
        TheirsDiffText.Inlines = theirs;
    }

    private static Run MakeRun(string text, IBrush brush) => new(text) { Foreground = brush };
}
