namespace DialogEditor.Avalonia.Controls;

/// Which pane F6 / Shift+F6 moves to (GitHub issue 262). Panes are numbered in reading order;
/// -1 means focus is in none of them, so F6 starts at the first and Shift+F6 at the last.
public static class PaneCycle
{
    public static int Next(int current, int step, int count, bool backwards)
    {
        var start = current >= 0 ? current : backwards ? count : -1;
        var next  = (start + (backwards ? -step : step)) % count;
        return next < 0 ? next + count : next;
    }
}
