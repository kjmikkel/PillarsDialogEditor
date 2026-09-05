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
    public IEnumerable<ContentBlock> Screenshot()
    {
        session.Foreground();

        if (!Win32.GetWindowRect(session.WindowHandle, out var r))
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
            new TextContentBlock { Text = $"{width}x{height} capture of '{session.Status()}'." },
            ImageContentBlock.FromBytes(ms.ToArray(), "image/png"),
        ];
    }
}
