using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Views;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Theming;

/// <summary>
/// Pins the resize behaviour added for Gaps item 6 part B: every dialog that was
/// previously CanResize="False" with a fixed Width is now resizable, with MinWidth
/// equal to its old fixed Width, and grows vertically via SizeToContent="Height". See
/// docs/superpowers/specs/2026-06-14-fontsize-scale-setting-design.md.
/// </summary>
public class ResizableDialogTests
{
    public ResizableDialogTests() => Loc.Configure(new StubStringProvider());


    private static void AssertResizable(Window window, double expectedMinWidth)
    {
        window.Show();
        Assert.True(window.CanResize);
        Assert.True(window.SizeToContent.HasFlag(SizeToContent.Height));
        Assert.Equal(expectedMinWidth, window.MinWidth);
    }

    [AvaloniaFact] public void AboutWindow_IsResizable() => AssertResizable(new AboutWindow(), 420);
    [AvaloniaFact] public void BranchNameDialog_IsResizable() => AssertResizable(new BranchNameDialog(), 400);
    [AvaloniaFact] public void CommitConsentDialog_IsResizable() => AssertResizable(new CommitConsentDialog(), 460);
    [AvaloniaFact] public void ConflictResolutionDialog_IsResizable() => AssertResizable(new ConflictResolutionDialog(), 520);
    [AvaloniaFact] public void ConversationNameDialog_IsResizable() => AssertResizable(new ConversationNameDialog(), 460);
    [AvaloniaFact] public void ExportConversationsWindow_IsResizable() => AssertResizable(new ExportConversationsWindow(), 480);
    [AvaloniaFact] public void FindReplaceWindow_IsResizable() => AssertResizable(new FindReplaceWindow(), 420);
    [AvaloniaFact] public void ForceDeleteDialog_IsResizable() => AssertResizable(new ForceDeleteDialog(), 420);
    [AvaloniaFact] public void ImportWarningsDialog_IsResizable() => AssertResizable(new ImportWarningsDialog(), 440);
    [AvaloniaFact] public void LanguageCodeDialog_IsResizable() => AssertResizable(new LanguageCodeDialog(), 340);
    [AvaloniaFact] public void SettingsWindow_IsResizable() => AssertResizable(new SettingsWindow(), 500);
    [AvaloniaFact] public void UnsavedChangesDialog_IsResizable() => AssertResizable(new UnsavedChangesDialog(), 420);
}
