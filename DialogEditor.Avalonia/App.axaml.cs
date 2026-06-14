using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia;

public partial class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLog.Info("DialogEditor.Avalonia started");
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
                AppLog.Error("Unhandled exception", e.ExceptionObject as Exception);
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLog.Error("Unobserved task exception", e.Exception);
                e.SetObserved();
            };
            Loc.Configure(new AvaloniaStringProvider());
            // Apply the persisted theme before the first window is shown (overrides the
            // design-time Dark default baked into App.axaml).
            new ThemeApplier().Apply(AppSettings.Theme);
            // Scale FontSize.* tokens before any window is constructed, so every
            // StaticResource FontSize binding (including dialogs opened later this
            // session) resolves the scaled value. Must run after ThemeApplier, which
            // reloads Tokens.axaml and would otherwise reset this. Restart-required:
            // changing the setting mid-session does not re-run this (Gaps item 6 part B).
            new FontScaleApplier().Apply(AppSettings.FontScale);

            if (!AppSettings.ThemeOnboardingSeen)
            {
                var onboarding = new ThemeOnboardingWindow();
                desktop.MainWindow = onboarding;
                onboarding.Closed += (_, _) =>
                {
                    AppSettings.ThemeOnboardingSeen = true;
                    var main = new MainWindow();
                    desktop.MainWindow = main;
                    main.Show();
                };
                onboarding.Show();
            }
            else
            {
                desktop.MainWindow = new MainWindow();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
