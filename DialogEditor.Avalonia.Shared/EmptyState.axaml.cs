using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;

namespace DialogEditor.Avalonia.Shared;

/// <summary>
/// The empty-list explanation shared by the tool windows (GitHub issue 254). See
/// EmptyState.axaml for why it hides itself and ignores the pointer.
/// </summary>
public partial class EmptyState : UserControl
{
    public static readonly StyledProperty<string?> MessageProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Message));

    public static readonly StyledProperty<string?> HintProperty =
        AvaloniaProperty.Register<EmptyState, string?>(nameof(Hint));

    public EmptyState()
    {
        InitializeComponent();
        Update();
    }

    /// <summary>Why the list is empty. Empty or null hides the whole control.</summary>
    public string? Message
    {
        get => GetValue(MessageProperty);
        set => SetValue(MessageProperty, value);
    }

    /// <summary>What to do to fill the list. Optional.</summary>
    public string? Hint
    {
        get => GetValue(HintProperty);
        set => SetValue(HintProperty, value);
    }

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);
        if (change.Property == MessageProperty || change.Property == HintProperty)
            Update();
    }

    private void Update()
    {
        var message = Message ?? string.Empty;
        var hint = Hint ?? string.Empty;
        MessageText.Text = message;
        HintText.Text = hint;
        HintText.IsVisible = hint.Length > 0;
        IsVisible = message.Length > 0;
        // One name for screen readers, so the reason and the remedy are read together.
        AutomationProperties.SetName(this, hint.Length > 0 ? $"{message}. {hint}" : message);
    }
}
