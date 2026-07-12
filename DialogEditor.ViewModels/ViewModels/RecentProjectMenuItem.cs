using System.Windows.Input;

namespace DialogEditor.ViewModels;

/// One row in the File ▸ Recent Projects submenu, bound via ItemsSource +
/// ItemContainerTheme. A tiny view-model so the ViewModels layer stays free of
/// Avalonia types. Kinds: a real recent-project entry, the disabled empty-state
/// placeholder, or the Clear action.
public sealed class RecentProjectMenuItem
{
    public required string Header { get; init; }
    public string? ToolTip { get; init; }
    public bool IsEnabled { get; init; } = true;
    public ICommand? Command { get; init; }
    public object? CommandParameter { get; init; }
}
