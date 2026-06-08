using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Media;

namespace DialogEditor.Tests.Theming;

public class TokenRegistryTests
{
    private static ISolidColorBrush Brush(string key)
    {
        Assert.True(
            Application.Current!.TryGetResource(key, Application.Current!.ActualThemeVariant, out var v),
            $"Brush key '{key}' is not defined");
        return Assert.IsAssignableFrom<ISolidColorBrush>(v);
    }

    [AvaloniaTheory]
    [InlineData("Brush.Node.Npc.Header",        0xFF, 0x7b, 0x24, 0x1c)]
    [InlineData("Brush.Node.Player.Header",      0xFF, 0x1a, 0x52, 0x76)]
    [InlineData("Brush.Node.Script.Body",        0xFF, 0xe0, 0xe0, 0xe0)]
    [InlineData("Brush.Node.Bark.Footer",        0xFF, 0xe8, 0xd0, 0x80)]
    [InlineData("Brush.Diff.Added.Fill",         0xFF, 0x3a, 0x7a, 0x3a)]
    [InlineData("Brush.Diff.Changed.Fill",       0xFF, 0xc0, 0x8a, 0x2a)]
    [InlineData("Brush.Severity.Error",          0xFF, 0xc0, 0x39, 0x2b)]
    [InlineData("Brush.Toolbar.Button.Background",0xFF, 0x33, 0x33, 0x33)]
    [InlineData("Brush.Surface.Window",          0xFF, 0x1e, 0x1e, 0x1e)]
    [InlineData("Brush.Border.Default",          0xFF, 0x33, 0x33, 0x33)]
    [InlineData("Brush.Text.Primary",            0xFF, 0xe8, 0xe8, 0xe8)]
    [InlineData("Brush.Text.Muted",              0xFF, 0x88, 0x88, 0x88)]
    [InlineData("Brush.Text.Female.Active",      0xFF, 0xe8, 0xe8, 0xe8)]
    [InlineData("Brush.Text.Status.Changed",     0xFF, 0xf0, 0xad, 0x4e)] // #e0a030 absorbed
    [InlineData("Brush.Syntax.Code",             0xFF, 0x9c, 0xdc, 0xfe)]
    [InlineData("Brush.Conflict.Mine.Foreground",   0xFF, 0x9c, 0xc4, 0xff)] // Mine = blue (Sky.300)
    [InlineData("Brush.Conflict.Theirs.Foreground", 0xFF, 0xff, 0x9c, 0x9c)] // Theirs = red (Red.300)
    [InlineData("Brush.Diff.Inline.Mine",         0xFF, 0x9b, 0xe3, 0x9b)] // preserved #9be39b
    [InlineData("Brush.Diff.Inline.Theirs",       0xFF, 0xff, 0x9c, 0x9c)]
    public void TokenResolvesToExpectedColor(string key, byte a, byte r, byte g, byte b)
        => Assert.Equal(Color.FromArgb(a, r, g, b), Brush(key).Color);

    // No-dangling sweep: every Brush.* the app declares must resolve. This list is the
    // public contract from spec §7; keep it in sync when tokens are added.
    public static readonly string[] AllTokens =
    {
        "Brush.Node.Npc.Header","Brush.Node.Npc.Body","Brush.Node.Npc.Footer",
        "Brush.Node.Player.Header","Brush.Node.Player.Body","Brush.Node.Player.Footer",
        "Brush.Node.Narrator.Header","Brush.Node.Narrator.Body","Brush.Node.Narrator.Footer",
        "Brush.Node.Script.Header","Brush.Node.Script.Body","Brush.Node.Script.Footer",
        "Brush.Node.Bark.Header","Brush.Node.Bark.Body","Brush.Node.Bark.Footer",
        "Brush.Diff.Added.Fill","Brush.Diff.Changed.Fill","Brush.Diff.Removed.Fill",
        "Brush.Severity.Info","Brush.Severity.Warning","Brush.Severity.Error",
        "Brush.Toolbar.Button.Background","Brush.Toolbar.Button.Foreground","Brush.Toolbar.Button.Hover",
        "Brush.Toolbar.Button.Pressed","Brush.Toolbar.Button.Checked","Brush.Toolbar.Button.CheckedHover",
        "Brush.Surface.Window","Brush.Surface.Panel","Brush.Surface.Card","Brush.Surface.Input",
        "Brush.Surface.Inset","Brush.Surface.Subtle","Brush.Surface.Header","Brush.Surface.Info","Brush.Surface.Overlay.Scrim",
        "Brush.Border.Default","Brush.Border.Subtle","Brush.Border.Strong","Brush.Border.Muted",
        "Brush.Border.OnDark","Brush.Border.Focus",
        "Brush.Text.Primary","Brush.Text.Emphasis","Brush.Text.Secondary","Brush.Text.Tertiary",
        "Brush.Text.Muted.Light","Brush.Text.Caption","Brush.Text.Muted","Brush.Text.Disabled",
        "Brush.Text.OnAccent","Brush.Text.Female.Active","Brush.Text.Female.Dim",
        "Brush.Text.OnLight","Brush.Text.OnLight.Muted",
        "Brush.Connection.Default","Brush.Connection.Always","Brush.Connection.Never",
        "Brush.Connection.Highlighted","Brush.Connection.Highlighted.Always","Brush.Connection.Highlighted.Never",
        "Brush.Text.Link","Brush.Text.Link.Subtle","Brush.Text.Info","Brush.Text.Highlight",
        "Brush.Accent.Badge",
        "Brush.Text.Status.New","Brush.Text.Status.Added","Brush.Text.Status.Changed",
        "Brush.Text.Status.Removed","Brush.Text.Status.Success","Brush.Text.Status.Error",
        "Brush.Text.Status.Pending","Brush.Text.Meta.Commit",
        "Brush.Syntax.Condition","Brush.Syntax.Script","Brush.Syntax.Code","Brush.Syntax.Default",
        "Brush.Diff.Inline.Mine","Brush.Diff.Inline.Theirs",
        "Brush.Conflict.Mine.Background","Brush.Conflict.Mine.Foreground",
        "Brush.Conflict.Theirs.Background","Brush.Conflict.Theirs.Foreground",
        "Brush.Button.Confirm.Background","Brush.Button.Caution.Background",
        "Brush.Button.Primary.Background","Brush.Button.Destructive.Background",
        "Brush.Bark.Detail.Background","Brush.Bark.Detail.Border","Brush.Bark.Detail.Text",
        "Brush.Node.Bark.Outline","Brush.Node.Quotidian.Note",
    };

    public static IEnumerable<object[]> AllTokenCases() => AllTokens.Select(t => new object[] { t });

    [AvaloniaTheory]
    [MemberData(nameof(AllTokenCases))]
    public void EveryDeclaredTokenResolves(string key) => Assert.NotNull(Brush(key));
}
