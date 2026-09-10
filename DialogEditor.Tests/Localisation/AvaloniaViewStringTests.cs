using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Avalonia.Views;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Tests.Localisation;

/// <summary>
/// The Avalonia layer's last hard-coded strings.
///
/// Three of them were the interesting kind: ConversationNameDialog and
/// UnsavedChangesDialog already looked their text up, then fell back to a hard-coded
/// English literal with `?? "New Conversation"`. That is worse than not localising at
/// all — a key that goes missing or gets renamed keeps rendering correct English
/// forever, so nobody notices until a translator asks why their string never appears.
/// The project's own AvaloniaStringProvider already fails loudly, returning "[Key]",
/// so these tests pin that the fallbacks are gone and the lookup is the only path.
/// </summary>
public class AvaloniaViewStringTests
{
    public AvaloniaViewStringTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void ConversationNameDialog_TitlesComeOnlyFromResources()
    {
        Assert.Equal("New Conversation",    new ConversationNameDialog().Title);
        Assert.Equal("Import Conversation", new ConversationNameDialog("city_market").Title);
    }

    [AvaloniaFact]
    public void UnsavedChangesDialog_MessageComesOnlyFromResources()
    {
        var dialog = new UnsavedChangesDialog("city_market");
        var block  = dialog.FindControl<TextBlock>("MessageBlock");

        Assert.NotNull(block);
        // The real resource carries a second paragraph the removed fallback never had —
        // proof of the drift these dual-source strings invite. Pin the substitution and
        // the fact that the full resource is what renders.
        Assert.StartsWith("'city_market' has unsaved changes.", block!.Text);
        Assert.Contains("Test Patch (F5)", block.Text);
    }

    [AvaloniaFact]
    public void UnsavedChangesSubjectFallbacks_ComeFromResources()
    {
        // Substituted for the conversation name when the prompt is about the whole
        // project, or about a conversation that has not been named yet. They land
        // inside "'{0}' has unsaved changes.", so they read as a subject.
        Assert.Equal("This conversation", Loc.Get("UnsavedChanges_ThisConversation"));
        Assert.Equal("This project",      Loc.Get("UnsavedChanges_ThisProject"));
    }

    [AvaloniaFact]
    public void FileTypeDescriptions_ComeFromResources()
    {
        // Shown in the OS save/open dialog's file-type dropdown.
        Assert.Equal("CSV files",   Loc.Get("FileType_CsvFiles"));
        Assert.Equal("Dialog Pack", Loc.Get("FileType_DialogPack"));
    }

    [AvaloniaFact]
    public void LinkConditionEditorTitle_NamesTheTargetNode()
    {
        Assert.Equal("Link \u2192 4", Loc.Format("Editor_LinkTitle", 4));
    }
}
