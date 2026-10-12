using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(DialogEditor.Tests.TestAppBuilder))]

namespace DialogEditor.Tests;

public class TestAppBuilder
{
    // Skia rendering instead of the headless drawing stub (issue 177): the stub measures every
    // glyph at a fixed width regardless of FontSize, which makes layout tests at a font scale
    // meaningless. Skia measures with the real fonts the app ships.
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DialogEditor.Avalonia.App>()
            .UseSkia()
            .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
}
