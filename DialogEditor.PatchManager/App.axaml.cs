using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.PatchManager;

public partial class App : Application
{
    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        Loc.Configure(new AvaloniaStringProvider());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Honour the theme the user picked in the editor (or here) on the previous run,
            // before the first window is built (overrides App.axaml's Dark default).
            new ThemeApplier().Apply(AppSettings.Theme);
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
