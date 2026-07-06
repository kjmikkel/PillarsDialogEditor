using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Templates;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Behaviors;

/// IDE-style token/markup autocomplete on a TextBox. Every decision (context,
/// candidates, insertion) comes from TokenCompletionService; this behaviour only
/// shows a Popup and applies the service's computed edit.
/// Spec: docs/superpowers/specs/2026-07-06-token-autocomplete-design.md
public static class TokenCompletion
{
    private static readonly TokenCompletionService Service = new();

    public static readonly AttachedProperty<bool> IsEnabledProperty =
        AvaloniaProperty.RegisterAttached<TextBox, bool>("IsEnabled", typeof(TokenCompletion));
    public static readonly AttachedProperty<string> GameIdProperty =
        AvaloniaProperty.RegisterAttached<TextBox, string>("GameId", typeof(TokenCompletion), string.Empty);
    private static readonly AttachedProperty<Session?> SessionProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Session?>("Session", typeof(TokenCompletion));

    public static void SetIsEnabled(TextBox t, bool v) => t.SetValue(IsEnabledProperty, v);
    public static bool GetIsEnabled(TextBox t) => t.GetValue(IsEnabledProperty);
    public static void SetGameId(TextBox t, string v) => t.SetValue(GameIdProperty, v);
    public static string GetGameId(TextBox t) => t.GetValue(GameIdProperty);

    /// Inspection hook (used by tests): the completion popup attached to a TextBox.
    public static Popup? GetPopup(TextBox t) => t.GetValue(SessionProperty)?.Popup;

    static TokenCompletion()
    {
        IsEnabledProperty.Changed.AddClassHandler<TextBox>((t, e) =>
        {
            if (e.NewValue is true && t.GetValue(SessionProperty) is null)
                t.SetValue(SessionProperty, new Session(t));
        });
    }

    /// Per-TextBox popup state and event wiring.
    private sealed class Session
    {
        private readonly TextBox _box;
        private CompletionContext? _context;

        public Popup Popup { get; }
        private readonly ListBox _list;

        public Session(TextBox box)
        {
            _box = box;
            _list = new ListBox
            {
                MaxHeight = 200,
                MinWidth = 220,
                ItemTemplate = new FuncDataTemplate<TagEntry>((entry, _) =>
                    entry is null
                        ? new Control()
                        : new StackPanel
                        {
                            Children =
                            {
                                new TextBlock { Text = entry.Name, FontWeight = FontWeight.Bold },
                                new TextBlock
                                {
                                    Text = entry.Description,
                                    Opacity = 0.7,
                                    TextWrapping = TextWrapping.Wrap,
                                },
                            },
                        }),
            };
            Popup = new Popup
            {
                Child = _list,
                PlacementTarget = box,
                Placement = PlacementMode.BottomEdgeAlignedLeft,
                IsLightDismissEnabled = true,
            };
            ((ISetLogicalParent)Popup).SetParent(box);

            box.PropertyChanged += OnBoxPropertyChanged;
            box.AddHandler(InputElement.KeyDownEvent, OnKeyDown,
                RoutingStrategies.Tunnel | RoutingStrategies.Bubble);
            _list.DoubleTapped += (_, _) => Accept();
        }

        private void OnBoxPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
        {
            if (e.Property == TextBox.TextProperty || e.Property == TextBox.CaretIndexProperty)
                Refresh();
        }

        private void Refresh()
        {
            try
            {
                _context = Service.TryGetContext(_box.Text ?? "", _box.CaretIndex);
                if (_context is null) { Popup.IsOpen = false; return; }

                var candidates = Service.GetCandidates(_context, GetGameId(_box));
                if (candidates.Count == 0) { Popup.IsOpen = false; return; }

                _list.ItemsSource = candidates;
                _list.SelectedIndex = 0;
                Popup.IsOpen = true;
            }
            catch (Exception ex)
            {
                AppLog.Error("Token autocomplete refresh failed", ex);
                Popup.IsOpen = false;
            }
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (!Popup.IsOpen) return;
            switch (e.Key)
            {
                case Key.Escape: Popup.IsOpen = false; e.Handled = true; break;
                case Key.Enter or Key.Tab: Accept(); e.Handled = true; break;
                case Key.Down: Move(+1); e.Handled = true; break;
                case Key.Up: Move(-1); e.Handled = true; break;
            }
        }

        private void Move(int delta)
        {
            var n = _list.ItemCount;
            if (n == 0) return;
            _list.SelectedIndex = (_list.SelectedIndex + delta + n) % n;
        }

        private void Accept()
        {
            if (_context is null || _list.SelectedItem is not TagEntry entry) return;

            var edit = Service.ApplyCompletion(_context, entry);
            var text = _box.Text ?? "";
            _box.Text = text.Remove(edit.ReplaceStart, edit.ReplaceLength)
                            .Insert(edit.ReplaceStart, edit.InsertedText);
            _box.SelectionStart = edit.SelectionStart;
            _box.SelectionEnd = edit.SelectionStart + edit.SelectionLength;
            _box.CaretIndex = edit.SelectionStart + edit.SelectionLength;
            Popup.IsOpen = false;
        }
    }
}
