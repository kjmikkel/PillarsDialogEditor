using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using DialogEditor.Avalonia.Shared;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Shared.Theming;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// GitHub issue 264 (F-23, F-24). At 200% the Settings labels outgrew their fixed 140 px
/// minimum and ran into their controls, and the Patch Manager's "Load Order" heading ran into
/// its buttons. Settings also put Browse… ahead of the field it fills in the tab order.
/// </summary>
public class LabelFieldSpacingTests
{
    private const double Gap = 8;

    public LabelFieldSpacingTests() => Loc.Configure(new AvaloniaStringProvider());

    private static double Left(Visual v, Visual root)  => v.TranslatePoint(default, root)!.Value.X;
    private static double Right(Visual v, Visual root) => Left(v, root) + v.Bounds.Width;

    private static void AtDoubleSize(Window w, Action check)
    {
        WindowSizing.Register();
        var provider = WindowSizing.WorkingAreaProvider;
        WindowSizing.WorkingAreaProvider = _ => null;
        try
        {
            new FontScaleApplier().Apply(2.0);
            w.Show();
            Dispatcher.UIThread.RunJobs();
            check();
        }
        finally
        {
            w.Close();
            new FontScaleApplier().Apply(1.0);
            WindowSizing.WorkingAreaProvider = provider;
        }
    }

    [AvaloniaFact]
    public void Settings_LabelsEndBeforeTheirControls_AtDoubleSize()
    {
        var w = new SettingsWindow();
        AtDoubleSize(w, () =>
        {
            var labels = w.GetVisualDescendants().OfType<TextBlock>().Where(t => t.Classes.Contains("label")).ToList();
            Assert.NotEmpty(labels);
            foreach (var label in labels)
            {
                var row = (Panel)label.GetVisualParent()!;
                var control = row.Children.Where(c => c != label && c.IsVisible).MinBy(c => Left(c, w))!;
                Assert.True(Right(label, w) + Gap <= Left(control, w) + 0.5,
                    $"\"{label.Text}\" ends at {Right(label, w):0.#}, its control starts at {Left(control, w):0.#}");
            }
        });
    }

    [AvaloniaFact]
    public void PatchManager_LoadOrderHeading_EndsBeforeItsButtons_AtDoubleSize()
    {
        var vm = new PatchManagerViewModel(new StubFolderPicker(), new StubFilePicker());
        var view = new PatchManagerView { DataContext = vm };
        var w = new Window { Width = 640, Height = 580, Content = view };   // the standalone Patcher's size
        AtDoubleSize(w, () =>
        {
            var heading = w.GetVisualDescendants().OfType<TextBlock>()
                .Single(t => t.Text == Loc.Get("PatchManager_LoadOrder"));
            var save = w.GetVisualDescendants().OfType<Button>()
                .Single(b => b.Content as string == Loc.Get("PatchManager_SaveLoadOrder"));
            var top = heading.TranslatePoint(default, w)!.Value.Y;
            var buttonTop = save.TranslatePoint(default, w)!.Value.Y;
            // Either the buttons sit beside the heading with a gap, or they have wrapped below it.
            Assert.True(Right(heading, w) + Gap <= Left(save, w) + 0.5 || buttonTop >= top + heading.Bounds.Height,
                $"heading ends at {Right(heading, w):0.#}, Save load order starts at {Left(save, w):0.#} on the same line");
        });
    }

    // The theme and language pickers are shared by the editor's Settings, the Patcher's Settings
    // and theme onboarding. Their labels had a fixed Width="140" and no class, so the test above
    // never saw them: at 200% "App language" ran under its dropdown (GitHub issue 264).
    [AvaloniaTheory]
    [InlineData("theme")]
    [InlineData("language")]
    public void SharedPicker_LabelEndsBeforeItsDropdown_AtDoubleSize(string picker)
    {
        Control view = picker == "theme" ? new ThemePickerView() : new LanguagePickerView();
        var w = new Window { Width = 400, SizeToContent = SizeToContent.Height, Content = view };
        AtDoubleSize(w, () =>
        {
            var label = view.GetVisualDescendants().OfType<TextBlock>().First(t => t.GetVisualParent() is Panel);
            var combo = view.GetVisualDescendants().OfType<ComboBox>().Single();
            Assert.True(Right(label, w) + Gap <= Left(combo, w) + 0.5,
                $"\"{label.Text}\" ends at {Right(label, w):0.#}, its dropdown starts at {Left(combo, w):0.#}");
        });
    }

    [Fact]
    public void Settings_BackupField_ComesBeforeItsBrowseButton()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx"))) dir = dir.Parent;
        var doc = XDocument.Load(Path.Combine(dir!.FullName, "DialogEditor.Avalonia", "Views", "SettingsWindow.axaml"));
        var browse = doc.Descendants().Single(e => e.Name.LocalName == "Button"
            && e.Attribute("Command")?.Value == "{Binding BrowseBackupDirectoryCommand}");
        var siblings = browse.Parent!.Elements().ToList();
        var field = siblings.Single(e => e.Name.LocalName == "TextBox");
        Assert.True(siblings.IndexOf(field) < siblings.IndexOf(browse), "Browse… is declared, so tabbed to, before its field");
        Assert.Null(browse.Attribute("TabIndex"));
    }
}
