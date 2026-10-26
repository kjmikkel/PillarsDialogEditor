using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Controls.Primitives;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Controls;
using DialogEditor.Core.Logging;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Behaviors;

/// Red wavy underlines under misspelt words in a TextBox, updating live as the writer
/// types (GitHub issue 9). The <see cref="FinderProperty"/> delegate says which spans are
/// misspelt; this behaviour only locates them on screen (TextPresenter.TextLayout) and
/// paints them on a non-hit-testable adorner, so typing, selection and the caret are never
/// affected. A spelling failure is logged and draws nothing.
///
/// Issue 312: re-running the finder on every keystroke underlined a word before the writer
/// had finished typing it. Now an edit only SHIFTS the cached spans along with the text
/// (<see cref="SpellSpanShift"/>; the edited word loses its squiggle at once, squiggles
/// elsewhere stay put instead of flickering away), and the full re-check runs once the
/// writer has paused for <see cref="CheckDelay"/>. A finder change still re-checks
/// immediately; layout and scroll changes only re-position the existing spans.
///
/// The squiggles are VISUAL ONLY. The spelling warning list in Node Details remains the
/// accessible channel (screen readers, suggestions, add-to-dictionary).
public static class SpellSquiggles
{
    public static readonly AttachedProperty<Func<string, IReadOnlyList<SpellSpan>>?> FinderProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Func<string, IReadOnlyList<SpellSpan>>?>(
            "Finder", typeof(SpellSquiggles));
    private static readonly AttachedProperty<Session?> SessionProperty =
        AvaloniaProperty.RegisterAttached<TextBox, Session?>("Session", typeof(SpellSquiggles));

    /// How long the writer must pause before the full spell check re-runs (issue 312).
    public static TimeSpan CheckDelay { get; } = TimeSpan.FromMilliseconds(500);

    /// Inspection hook (used by tests): a delayed re-check is waiting for the pause to elapse.
    public static bool HasPendingCheck(TextBox t) => t.GetValue(SessionProperty)?.HasPendingCheck ?? false;

    /// Test hook: run the pending delayed re-check immediately. No-op if none is pending.
    public static void CheckNow(TextBox t) => t.GetValue(SessionProperty)?.CheckNow();

    public static void SetFinder(TextBox t, Func<string, IReadOnlyList<SpellSpan>>? v) => t.SetValue(FinderProperty, v);
    public static Func<string, IReadOnlyList<SpellSpan>>? GetFinder(TextBox t) => t.GetValue(FinderProperty);

    /// Inspection hook (used by tests): the word rectangles currently underlined, in TextBox coordinates.
    public static IReadOnlyList<Rect> GetSquiggleRects(TextBox t)
        => t.GetValue(SessionProperty)?.Rects ?? [];

    static SpellSquiggles()
    {
        FinderProperty.Changed.AddClassHandler<TextBox>((t, e) =>
        {
            var session = t.GetValue(SessionProperty);
            if (e.NewValue is null)
            {
                session?.Dispose();
                t.SetValue(SessionProperty, null);
            }
            else if (session is null)
                t.SetValue(SessionProperty, new Session(t));
            else
                session.InvalidateSpans();
        });
    }

    private sealed class Session : IDisposable
    {
        private readonly TextBox _box;
        private readonly SquiggleAdorner _adorner = new() { IsHitTestVisible = false };
        private AdornerLayer? _layer;
        private readonly IDisposable _textSub;

        private IReadOnlyList<SpellSpan>? _spans;      // cached finder result for _spansText
        private string? _spansText;
        private string? _lastKey;
        private DispatcherTimer? _timer;

        public IReadOnlyList<Rect> Rects { get; private set; } = [];

        public Session(TextBox box)
        {
            _box = box;
            _textSub = box.GetObservable(TextBox.TextProperty).Subscribe(new Observer(_ => OnTextChanged()));
            box.LayoutUpdated += OnLayoutUpdated;
            box.AddHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
            box.DetachedFromVisualTree += OnDetached;
            Recompute();
        }

        private sealed class Observer(Action<string?> next) : IObserver<string?>
        {
            public void OnNext(string? value) => next(value);
            public void OnError(Exception error) { }
            public void OnCompleted() { }
        }

        public void InvalidateSpans()
        {
            StopTimer();
            _spans = null;
            _lastKey = null;
            Recompute();
        }

        public bool HasPendingCheck => _timer?.IsEnabled == true;

        public void CheckNow()
        {
            if (!HasPendingCheck) return;
            OnTimerTick(null, EventArgs.Empty);
        }

        public void StopTimer()
        {
            if (_timer is null) return;
            _timer.Stop();
            _timer.Tick -= OnTimerTick;
            _timer = null;
        }

        // The pause is over: run the real check against the current text.
        private void OnTimerTick(object? sender, EventArgs e)
        {
            StopTimer();
            _spans = null;
            _lastKey = null;
            Recompute();
        }

        private void RestartTimer()
        {
            StopTimer();
            _timer = new DispatcherTimer { Interval = CheckDelay };
            _timer.Tick += OnTimerTick;
            _timer.Start();
        }

        // Keep the cached spans aligned with the current text without calling the finder.
        private void ShiftSpans(string text)
        {
            if (_spans is null || _spansText is null || _spansText == text) return;
            _spans = SpellSpanShift.Apply(_spans, _spansText, text);
            _spansText = text;
        }

        private void OnTextChanged()
        {
            if (_spans is not null && GetFinder(_box) is not null)
            {
                ShiftSpans(_box.Text ?? "");
                RestartTimer();
            }
            Recompute();
            // The presenter may pick the new text up a beat later; settle after layout.
            Dispatcher.UIThread.Post(Recompute, DispatcherPriority.Loaded);
        }

        private void OnLayoutUpdated(object? sender, EventArgs e) => Recompute();
        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e) => Recompute();
        private void OnDetached(object? sender, VisualTreeAttachmentEventArgs e)
        {
            StopTimer();
            RemoveAdorner();
        }

        private void EnsureAdorner()
        {
            if (_layer is not null) return;
            var layer = AdornerLayer.GetAdornerLayer(_box);
            if (layer is null) return;     // no layer (yet); rects are still computed
            AdornerLayer.SetAdornedElement(_adorner, _box);
            layer.Children.Add(_adorner);
            _layer = layer;
        }

        private void RemoveAdorner()
        {
            _layer?.Children.Remove(_adorner);
            _layer = null;
            _lastKey = null;    // re-attach and redraw if the box returns to a tree
        }

        private void Recompute()
        {
            try
            {
                var finder = GetFinder(_box);
                var presenter = _box.GetVisualDescendants().OfType<TextPresenter>().FirstOrDefault();
                var text = _box.Text ?? "";
                if (finder is null || presenter is null || (presenter.Text ?? "") != text)
                    return;     // template not applied yet / presenter lagging: a later layout retries

                EnsureAdorner();

                var origin = presenter.TranslatePoint(default, _box) ?? default;
                var key = $"{text.GetHashCode()}|{text.Length}|{presenter.Bounds.Size}|{origin}|{_box.Bounds.Size}";
                if (key == _lastKey) return;
                _lastKey = key;

                if (_spans is null)
                {
                    _spans = finder(text);
                    _spansText = text;
                }
                else ShiftSpans(text);

                var viewport = ViewportInBox(presenter);
                var layout = presenter.TextLayout;
                var rects = new List<Rect>();
                foreach (var span in _spans)
                {
                    if (span.Start < 0 || span.Length <= 0 || span.Start + span.Length > text.Length) continue;
                    foreach (var r in layout.HitTestTextRange(span.Start, span.Length))
                    {
                        var tl = presenter.TranslatePoint(r.TopLeft, _box);
                        if (tl is null) continue;
                        var clipped = new Rect(tl.Value, r.Size).Intersect(viewport);
                        if (clipped.Width > 0 && clipped.Height > 0) rects.Add(clipped);
                    }
                }
                Publish(rects);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _lastKey = null;
                AppLog.Warn($"Spell squiggles failed: {ex.Message}");
                Publish([]);
            }
        }

        private void Publish(IReadOnlyList<Rect> rects)
        {
            Rects = rects;
            _adorner.Rects = rects;
            _adorner.InvalidateVisual();
        }

        private Rect ViewportInBox(TextPresenter presenter)
        {
            var sv = presenter.GetVisualAncestors().OfType<ScrollViewer>().FirstOrDefault();
            if (sv is null) return new Rect(_box.Bounds.Size);
            var tl = sv.TranslatePoint(default, _box) ?? default;
            return new Rect(tl, sv.Bounds.Size);
        }

        public void Dispose()
        {
            StopTimer();
            _textSub.Dispose();
            _box.LayoutUpdated -= OnLayoutUpdated;
            _box.RemoveHandler(ScrollViewer.ScrollChangedEvent, OnScrollChanged);
            _box.DetachedFromVisualTree -= OnDetached;
            RemoveAdorner();
            Rects = [];
        }
    }
}
