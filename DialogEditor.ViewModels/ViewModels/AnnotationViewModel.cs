using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Editing;

namespace DialogEditor.ViewModels;

public partial class AnnotationViewModel : ObservableObject
{
    private string _id       = Guid.NewGuid().ToString();
    private string _title    = string.Empty;
    private string _body     = string.Empty;
    private string _colorKey = "Yellow";
    private double _x;
    private double _y;
    private double _width  = 240;
    private double _height = 140;

    [ObservableProperty] private bool _isSelected;
    [ObservableProperty] private bool _isEditing;

    internal UndoRedoStack? UndoStack { get; set; }

    public string Id { get => _id; init => _id = value; }

    public string Title
    {
        get => _title;
        set => Push(_title, value, "Edit annotation title",
            v => { _title = v; OnPropertyChanged(nameof(Title)); });
    }

    public string Body
    {
        get => _body;
        set => Push(_body, value, "Edit annotation body",
            v => { _body = v; OnPropertyChanged(nameof(Body)); });
    }

    public string ColorKey
    {
        get => _colorKey;
        set => Push(_colorKey, value, "Change annotation color",
            v => { _colorKey = v; OnPropertyChanged(nameof(ColorKey)); });
    }

    // Position is not tracked through undo (same pattern as node Layout positions).
    public double X
    {
        get => _x;
        set { _x = value; OnPropertyChanged(nameof(X)); }
    }

    public double Y
    {
        get => _y;
        set { _y = value; OnPropertyChanged(nameof(Y)); }
    }

    // Minimum size enforced so the box is always readable.
    public double Width
    {
        get => _width;
        set { _width = Math.Max(120, value); OnPropertyChanged(nameof(Width)); }
    }

    public double Height
    {
        get => _height;
        set { _height = Math.Max(60, value); OnPropertyChanged(nameof(Height)); }
    }

    public static AnnotationViewModel FromSnapshot(AnnotationSnapshot s)
    {
        var vm = new AnnotationViewModel();
        vm._id       = s.Id;
        vm._title    = s.Title;
        vm._body     = s.Body;
        vm._colorKey = s.ColorKey;
        vm._x        = s.X;
        vm._y        = s.Y;
        vm._width    = Math.Max(120, s.Width);
        vm._height   = Math.Max(60, s.Height);
        return vm;
    }

    public AnnotationSnapshot ToSnapshot() =>
        new(_id, _title, _body, _colorKey, _x, _y, _width, _height);

    // Screen-space bounds (pixels) — recomputed by ConversationView on every viewport or
    // world-bounds change.  Not persisted; purely a view-side cache.
    private double _screenX, _screenY, _screenW, _screenH;

    public double ScreenX      { get => _screenX; private set { _screenX = value; OnPropertyChanged(nameof(ScreenX)); } }
    public double ScreenY      { get => _screenY; private set { _screenY = value; OnPropertyChanged(nameof(ScreenY)); } }
    public double ScreenWidth  { get => _screenW; private set { _screenW = value; OnPropertyChanged(nameof(ScreenWidth)); } }
    public double ScreenHeight { get => _screenH; private set { _screenH = value; OnPropertyChanged(nameof(ScreenHeight)); } }

    public void SyncScreen(double zoom, double vX, double vY)
    {
        ScreenX      = (X - vX) * zoom;
        ScreenY      = (Y - vY) * zoom;
        ScreenWidth  = Width  * zoom;
        ScreenHeight = Height * zoom;
    }

    private void Push<T>(T current, T value, string description, Action<T> apply)
    {
        if (EqualityComparer<T>.Default.Equals(current, value)) return;
        if (UndoStack is null) { apply(value); return; }
        UndoStack.Execute(new SetPropertyCommand<T>(description, apply, current, value));
    }
}
