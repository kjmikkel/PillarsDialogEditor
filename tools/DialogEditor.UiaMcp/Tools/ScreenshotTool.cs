using System.ComponentModel;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;

namespace DialogEditor.UiaMcp.Tools;

[McpServerToolType]
internal sealed class ScreenshotTool(EditorSession session)
{
    [McpServerTool, Description(
        "Capture the app window as a PNG and return it inline, so the result can actually be " +
        "looked at rather than trusted.")]
    public IEnumerable<ContentBlock> Screenshot(
        [Description("Window to capture: automationId, className (e.g. 'SettingsWindow') or title. Defaults to the main window.")] string? window = null)
    {
        // Capture the RESOLVED window's rect. The old code took Process.MainWindowHandle,
        // so a dialog outside the main window's bounds simply was not in the image (#16).
        if (!session.TryWindow(window, out var target, out _, out var windowError))
            return [new TextContentBlock { Text = windowError }];

        session.Foreground(target);

        if (!Win32.GetWindowRect(target.Handle, out var r))
            return [new TextContentBlock { Text = "Error(NotOperable): GetWindowRect failed." }];

        var width = r.Right - r.Left;
        var height = r.Bottom - r.Top;
        if (width <= 0 || height <= 0)
            return [new TextContentBlock
                { Text = $"Error(NotOperable): window has no area ({width}x{height}); it may be minimized." }];

        using var bmp = new Bitmap(width, height);
        using (var g = Graphics.FromImage(bmp))
            g.CopyFromScreen(r.Left, r.Top, 0, 0, bmp.Size);

        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);

        return
        [
            new TextContentBlock
                { Text = $"{width}x{height} capture of '{target.Title}' " +
                         $"(className='{target.ClassName}'{(target.IsModal ? ", modal" : "")})." },
            ImageContentBlock.FromBytes(ms.ToArray(), "image/png"),
        ];
    }
}
