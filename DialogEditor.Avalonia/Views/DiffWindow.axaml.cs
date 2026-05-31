using System;
using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Interactivity;
using Avalonia.Media;
using DialogEditor.Patch.GitConflict;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

public partial class DiffWindow : Window
{
    private static readonly IBrush CommonBrush = new SolidColorBrush(Color.Parse("#e8e8e8"));
    private static readonly IBrush BeforeBrush = new SolidColorBrush(Color.Parse("#9be39b"));
    private static readonly IBrush AfterBrush  = new SolidColorBrush(Color.Parse("#ff9c9c"));

    private DiffViewModel? _vm;
    private DiffHelpWindow? _helpWindow;

    public DiffWindow() => InitializeComponent();

    public DiffWindow(DiffViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
        vm.PropertyChanged += OnVmPropertyChanged;
        UpdateDetail(vm.SelectedNodeDetail);
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (_vm is not null)
            _vm.PropertyChanged -= OnVmPropertyChanged;
    }

    private void OnVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(DiffViewModel.SelectedNodeDetail))
            UpdateDetail(_vm?.SelectedNodeDetail);
    }

    // Render the selected node's before/after text with the changed run highlighted.
    // Structural-only changes show the hint (bound in XAML) and clear the text rows.
    private void UpdateDetail(NodeDiffDetailViewModel? d)
    {
        if (d is null || d.IsStructuralOnly)
        {
            DefaultBeforeText.Inlines = new InlineCollection();
            DefaultAfterText.Inlines  = new InlineCollection();
            FemaleBeforeText.Inlines  = new InlineCollection();
            FemaleAfterText.Inlines   = new InlineCollection();
            return;
        }

        RenderPair(DefaultBeforeText, DefaultAfterText, d.DefaultBefore, d.DefaultAfter);

        if (d.HasFemaleRow)
            RenderPair(FemaleBeforeText, FemaleAfterText, d.FemaleBefore, d.FemaleAfter);
        else
        {
            FemaleBeforeText.Inlines = new InlineCollection();
            FemaleAfterText.Inlines  = new InlineCollection();
        }
    }

    private static void RenderPair(TextBlock beforeBlock, TextBlock afterBlock, string before, string after)
    {
        var beforeInlines = new InlineCollection();
        var afterInlines  = new InlineCollection();

        foreach (var span in TextDiff.Diff(before, after))
        {
            switch (span.Kind)
            {
                case DiffKind.Common:
                    beforeInlines.Add(MakeRun(span.Text, CommonBrush));
                    afterInlines.Add(MakeRun(span.Text, CommonBrush));
                    break;
                case DiffKind.MineOnly:
                    beforeInlines.Add(MakeRun(span.Text, BeforeBrush));
                    break;
                case DiffKind.TheirsOnly:
                    afterInlines.Add(MakeRun(span.Text, AfterBrush));
                    break;
            }
        }

        beforeBlock.Inlines = beforeInlines;
        afterBlock.Inlines  = afterInlines;
    }

    private static Run MakeRun(string text, IBrush brush) => new(text) { Foreground = brush };

    private void Help_Click(object? sender, RoutedEventArgs e)
    {
        if (_helpWindow is null || !_helpWindow.IsVisible)
            _helpWindow = new DiffHelpWindow();
        _helpWindow.Show();
        _helpWindow.Activate();
    }

    private void UndoBringIn_Click(object? sender, RoutedEventArgs e)
        => (DataContext as DiffViewModel)?.RequestUndoApply?.Invoke();
}
