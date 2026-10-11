using Avalonia.Controls;
using DialogEditor.ViewModels;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.Avalonia.Shared;

/// Wires PatchManagerViewModel's confirmations to real dialogs. Shared so the editor's
/// Patch Manager window and the standalone app behave identically.
public static class PatchManagerDialogs
{
    private const int MaxListedFiles = 10;

    public static void Attach(PatchManagerViewModel vm, Window owner)
    {
        // Issue 147: one-button message, so no Cancel; OK also answers Esc.
        vm.ShowGameUpdateHelp = (title, message) => new ConfirmDialog(
            title, message, Loc.Get("Schema_TooNew_Ok"), details: null, showCancel: false).ShowAsync(owner);

        vm.ConfirmRemoveAllMods = () => new ConfirmDialog(
            Loc.Get("PatchManager_RemoveAllConfirm_Title"),
            Loc.Get("PatchManager_RemoveAllConfirm_Message"),
            Loc.Get("PatchManager_RemoveAllMods"),
            details: null).ShowAsync(owner);

        vm.ConfirmAcceptExternalChanges = paths =>
        {
            var shown = paths.Take(MaxListedFiles).ToList();
            if (paths.Count > MaxListedFiles)
                shown.Add(Loc.Format("PatchManager_ExternalChanges_More", paths.Count - MaxListedFiles));
            return new ConfirmDialog(
                Loc.Get("PatchManager_ExternalChanges_Title"),
                Loc.Get("PatchManager_ExternalChanges_Message"),
                Loc.Get("PatchManager_ExternalChanges_Confirm"),
                shown).ShowAsync(owner);
        };
    }
}
