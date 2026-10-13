using System.Xml.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Presenters;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Issue 179: secondary (non-primary) buttons borrowed Brush.Surface.Header with white
/// Brush.Text.OnAccent text (unreadable in Light, no edge in High Contrast) or a canvas
/// node-header colour. They now share the Button.secondary class (Resources/Buttons.axaml), whose
/// Brush.Button.Secondary.* tokens PaletteContrastTests gates in every theme.
/// </summary>
public class SecondaryButtonTests
{
    // ── The class ────────────────────────────────────────────────────────────────

    private static Color Token(string key)
    {
        Assert.True(Application.Current!.TryGetResource(key, null, out var r), key);
        return ((ISolidColorBrush)r!).Color;
    }

    private static Color ColorOf(IBrush? b) => Assert.IsAssignableFrom<ISolidColorBrush>(b).Color;

    private static void WithSecondaryButton(Action<Button> check)
    {
        var button = new Button { Content = "Close", Classes = { "secondary" } };
        var w = new Window { Content = button };
        try
        {
            w.Show();
            Dispatcher.UIThread.RunJobs();
            check(button);
        }
        finally { w.Close(); }
    }

    [AvaloniaFact]
    public void SecondaryClass_UsesTheSecondaryTokens()
        => WithSecondaryButton(b =>
        {
            Assert.Equal(Token("Brush.Button.Secondary.Background"), ColorOf(b.Background));
            Assert.Equal(Token("Brush.Button.Secondary.Foreground"), ColorOf(b.Foreground));
            Assert.Equal(Token("Brush.Button.Secondary.Border"),     ColorOf(b.BorderBrush));
            Assert.Equal(new Thickness(1), b.BorderThickness);
        });

    [AvaloniaFact]
    public void SecondaryClass_Hover_KeepsTokenColours()
        => WithSecondaryButton(b =>
        {
            // Without its own rule, Fluent's pale hover brushes take over as soon as the pointer
            // is on the button, which is the unreadable look issue 179 is about.
            ((IPseudoClasses)b.Classes).Add(":pointerover");
            Dispatcher.UIThread.RunJobs();
            var presenter = b.GetVisualDescendants().OfType<ContentPresenter>().First(p => p.Name == "PART_ContentPresenter");
            Assert.Equal(Token("Brush.Button.Secondary.Hover"),      ColorOf(presenter.Background));
            Assert.Equal(Token("Brush.Button.Secondary.Foreground"), ColorOf(presenter.Foreground));
            Assert.Equal(Token("Brush.Button.Secondary.Border"),     ColorOf(presenter.BorderBrush));
        });

    // ── The markup ───────────────────────────────────────────────────────────────

    private static string SolutionRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static IEnumerable<(string File, XElement Element)> Markup()
    {
        var root = SolutionRoot();
        foreach (var file in Directory.EnumerateFiles(root, "*.axaml", SearchOption.AllDirectories))
        {
            var segments = Path.GetRelativePath(root, file).Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (segments.Intersect(["bin", "obj", ".worktrees", "worktrees", ".claude"]).Any()) continue;
            foreach (var el in XDocument.Load(file).Descendants())
                yield return (Path.GetFileName(file), el);
        }
    }

    // A Button's fill, set either on the element or by a Style whose selector targets Buttons.
    private static IEnumerable<(string Where, string? Background, string? Foreground)> ButtonFills() =>
        Markup().SelectMany(m =>
        {
            var (file, el) = m;
            if (el.Name.LocalName == "Button")
                return [($"{file}: Button {(string?)el.Attribute("Content") ?? (string?)el.Attribute(XName.Get("Name", "http://schemas.microsoft.com/winfx/2006/xaml"))}",
                         (string?)el.Attribute("Background"), (string?)el.Attribute("Foreground"))];
            if (el.Name.LocalName == "Style" && ((string?)el.Attribute("Selector"))?.StartsWith("Button") == true)
            {
                string? Setter(string p) => el.Elements().Where(s => s.Name.LocalName == "Setter" && (string?)s.Attribute("Property") == p)
                                              .Select(s => (string?)s.Attribute("Value")).FirstOrDefault();
                return new[] { ($"{file}: Style {(string?)el.Attribute("Selector")}", Setter("Background"), Setter("Foreground")) };
            }
            return [];
        });

    [Fact]
    public void NoButtonBorrowsAHeaderFill()
    {
        // Surface.Header is a panel-header colour and Node.*.Header a canvas node colour: neither
        // is tested as a button fill. A secondary button takes Classes="secondary" instead.
        var offenders = ButtonFills()
            .Where(b => b.Background is { } bg && (bg.Contains("Brush.Surface.Header") ||
                        System.Text.RegularExpressions.Regex.IsMatch(bg, @"Brush\.Node\.\w+\.Header")))
            .Select(b => $"{b.Where}: Background={b.Background}")
            .ToList();
        Assert.True(offenders.Count == 0,
            "Use Classes=\"secondary\" (Resources/Buttons.axaml) instead of a borrowed header fill:\n" +
            string.Join("\n", offenders));
    }

    // Fills that Brush.Text.OnAccent (white) is tested against in PaletteContrastTests.
    private static readonly string[] AccentFills =
    [
        "Brush.Button.Primary.Background", "Brush.Button.Confirm.Background",
        "Brush.Button.Caution.Background", "Brush.Button.Destructive.Background",
        "Brush.Selection.Background", "Brush.Severity.",
    ];

    [Fact]
    public void OnAccentText_SitsOnAnAccentFill()
    {
        // White accent text is only readable on the accent fills it is tested against. A
        // transparent or unset fill shows whatever is behind it, which this markup check can't see.
        var offenders = Markup()
            .Where(m => ((string?)m.Element.Attribute("Foreground"))?.Contains("Brush.Text.OnAccent") == true)
            .Select(m => (m.File, Tag: m.Element.Name.LocalName, Bg: (string?)m.Element.Attribute("Background")))
            .Where(m => m.Bg is { } bg && bg != "Transparent" && !AccentFills.Any(bg.Contains))
            .Select(m => $"{m.File}: {m.Tag} Background={m.Bg}")
            .ToList();
        Assert.True(offenders.Count == 0,
            "Brush.Text.OnAccent on a fill that isn't an accent colour:\n" + string.Join("\n", offenders));
    }
}
