using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>Issue 177: at 2.0× on a 1366×768 screen, Settings, About and Theme Onboarding are
/// clamped in height; their bodies scroll, so the action button stays inside the window.</summary>
public class TallWindowsFitTests
{
    public TallWindowsFitTests() => Loc.Configure(new StubStringProvider());

    private static void AssertButtonVisible(Window w, string buttonName)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => new Size(1366, 768);
        try
        {
            new FontScaleApplier().Apply(2.0);
            w.Show();
            Dispatcher.UIThread.RunJobs();   // let SizeToContent and the clamp settle before measuring
            var button = w.FindControl<Button>(buttonName)!;
            var bottom = button.TranslatePoint(new Point(0, button.Bounds.Height), w)!.Value.Y;
            Assert.True(w.ClientSize.Height <= 736, $"window is {w.ClientSize.Height} tall");
            Assert.True(bottom <= w.ClientSize.Height, $"{buttonName} ends at {bottom}, window is {w.ClientSize.Height} tall");
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }

    [AvaloniaFact] public void Settings_CloseStaysVisible() => AssertButtonVisible(new SettingsWindow(), "CloseButton");
    [AvaloniaFact] public void About_CloseStaysVisible() => AssertButtonVisible(new AboutWindow(), "CloseButton");
    [AvaloniaFact] public void ThemeOnboarding_ContinueStaysVisible() =>
        AssertButtonVisible(new DialogEditor.Avalonia.Shared.ThemeOnboardingWindow(), "ContinueButton");
}
