using System.Runtime.InteropServices;

namespace DialogEditor.UiaMcp;

/// <summary>
/// Synthetic input and window helpers. A real mouse click is needed because Avalonia's
/// top-level MenuItems implement neither Invoke nor ExpandCollapse.
/// </summary>
internal static class Win32
{
    [DllImport("user32.dll")] internal static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] internal static extern void SetCursorPos(int x, int y);
    [DllImport("user32.dll")] internal static extern void mouse_event(uint f, uint dx, uint dy, uint d, UIntPtr e);

    internal static void Click(int x, int y)
    {
        SetCursorPos(x, y);
        mouse_event(0x0002, 0, 0, 0, UIntPtr.Zero);   // left down
        mouse_event(0x0004, 0, 0, 0, UIntPtr.Zero);   // left up
    }
}
