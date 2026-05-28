using Avalonia;
using Avalonia.Headless;

[assembly: AvaloniaTestApplication(typeof(DialogEditor.Tests.TestAppBuilder))]

namespace DialogEditor.Tests;

public class TestAppBuilder
{
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<DialogEditor.Avalonia.App>();
}
