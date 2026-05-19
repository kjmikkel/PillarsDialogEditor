using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace DialogEditor.PatchManager;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var window = new MainWindow();
            desktop.MainWindow = window;

            // If a .patchlist was passed as a command-line argument, open it immediately
            var args = desktop.Args ?? [];
            var patchlist = args.FirstOrDefault(a =>
                a.EndsWith(".patchlist", StringComparison.OrdinalIgnoreCase)
                && File.Exists(a));
            if (patchlist is not null)
                window.LoadPatchList(patchlist);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
