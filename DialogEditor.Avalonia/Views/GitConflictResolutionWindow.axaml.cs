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
    // highlighted, for both the Default and Female text variants. The female blocks
    // are bound (in XAML) to HasFemaleRow, so they only show when relevant.
    private void UpdateDiff(ConflictRowViewModel? row)
    {
        MineDiffText.Inlines   = BuildInlines(row, isMine: true,  female: false);
        TheirsDiffText.Inlines = BuildInlines(row, isMine: false, female: false);

        MineFemaleDiffText.Inlines   = BuildInlines(row, isMine: true,  female: true);
        TheirsFemaleDiffText.Inlines = BuildInlines(row, isMine: false, female: true);
    }

    // Build the highlighted inlines for one cell. Field and translation (text) edits
    // get word-level highlighting; structural conflicts have nothing to diff, so the
    // raw value is shown.
    private static InlineCollection BuildInlines(ConflictRowViewModel? row, bool isMine, bool female)
    {
        var result = new InlineCollection();
        if (row is null)
            return result;

        var mineValue   = female ? row.MineFemaleValue   : row.MineValue;
        var theirsValue = female ? row.TheirsFemaleValue : row.TheirsValue;

        if (row.Kind is MergeConflictKind.FieldEdit or MergeConflictKind.TranslationEdit)
        {
            foreach (var span in TextDiff.Diff(mineValue, theirsValue))
            {
                switch (span.Kind)
                {
                    case DiffKind.Common:
                        result.Add(MakeRun(span.Text, CommonBrush));
                        break;
                    case DiffKind.MineOnly when isMine:
                        result.Add(MakeRun(span.Text, MineBrush));
                        break;
                    case DiffKind.TheirsOnly when !isMine:
                        result.Add(MakeRun(span.Text, TheirsBrush));
                        break;
                }
            }
        }
        else
        {
            result.Add(MakeRun(isMine ? mineValue : theirsValue, CommonBrush));
        }

        return result;
    }

    private static Run MakeRun(string text, IBrush brush) => new(text) { Foreground = brush };
}
