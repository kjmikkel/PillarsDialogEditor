using Avalonia.Controls;
using DialogEditor.ViewModels;

namespace DialogEditor.Avalonia.Views;

/// Non-modal, searchable reference of the dialog-text tag vocabulary
/// (tags.json via TagCatalogue). Opened from Help > Text Tag Reference;
/// MainWindow keeps one cached instance so reopening focuses it.
/// Spec: docs/superpowers/specs/2026-07-05-tag-reference-window-design.md
public partial class TagReferenceWindow : Window
{
    // Designer/runtime-loader constructor.
    public TagReferenceWindow() : this(new TagReferenceViewModel(string.Empty)) { }

    public TagReferenceWindow(TagReferenceViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
    }
}
