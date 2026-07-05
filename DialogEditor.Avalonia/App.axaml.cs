using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Core.Resources;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia;

public partial class App : Application
{
    // One report window per exception type — avoids a cascade of windows for repeated errors.
    private readonly HashSet<string> _reportedExceptionTypes = [];

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            AppLog.Info("DialogEditor.Avalonia started");

            // Catch exceptions thrown on the Avalonia UI dispatcher thread.
            // Without this, any unhandled exception in event handlers (e.g. the
            // AutoCompleteBox selection-model crash) propagates all the way to Main
            // and kills the process. e.Handled = true keeps the application alive.
            Dispatcher.UIThread.UnhandledException += (_, e) =>
            {
                AppLog.Error("Unhandled UI dispatcher exception", e.Exception);
                ShowExceptionReport(e.Exception);
                e.Handled = true;
            };

            // Background/Task exceptions and OS-level throws that bypass the dispatcher.
            AppDomain.CurrentDomain.UnhandledException += (_, e) =>
            {
                var ex = e.ExceptionObject as Exception;
                AppLog.Error("Unhandled domain exception", ex);
                if (ex is not null)
                    Dispatcher.UIThread.Post(() => ShowExceptionReport(ex));
            };
            TaskScheduler.UnobservedTaskException += (_, e) =>
            {
                AppLog.Error("Unobserved task exception", e.Exception);
                e.SetObserved();
                Dispatcher.UIThread.Post(() => ShowExceptionReport(e.Exception));
            };
            Loc.Configure(new AvaloniaStringProvider());
            // Apply the persisted theme before the first window is shown (overrides the
            // design-time Dark default baked into App.axaml).
            new ThemeApplier().Apply(AppSettings.Theme);
            // Apply the persisted language before the first window is shown.
            new LanguageApplier(
                "avares://DialogEditor.Avalonia.Shared/Resources/SharedStrings.{0}.axaml",
                "avares://DialogEditor.Avalonia/Resources/Strings.{0}.axaml"
            ).Apply(AppSettings.UiLanguage);
            CoreLocale.SetCulture(AppSettings.UiLanguage);
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

    public void ShowExceptionReport(Exception ex)
    {
        // Show at most one window per exception type to avoid flooding the desktop.
        var typeName = ex.GetType().Name;
        if (!_reportedExceptionTypes.Add(typeName)) return;

        var vm = new ExceptionReportViewModel(ex, AppLog.LogPath, MainWindowViewModel.IssuesUrl);
        var window = new ExceptionReportWindow(vm);
        window.Closed += (_, _) => _reportedExceptionTypes.Remove(typeName);
        window.Show();
    }
}
