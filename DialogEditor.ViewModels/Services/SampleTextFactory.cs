using DialogEditor.Patch;
using DialogEditor.ViewModels.Resources;

namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Builds SampleProjectService's prose from the string resources.
///
/// This lives in the ViewModels layer because DialogEditor.Patch references only
/// DialogEditor.Core and therefore cannot reach Loc. Keeping it a separate factory
/// rather than inlining it at the one call site is what lets the resource wiring be
/// tested directly — see SampleTextFactoryTests and SampleTextResourceEndToEndTests.
/// </summary>
public static class SampleTextFactory
{
    public static SampleTexts FromResources() => new(
        AuthorName:       Loc.Get("Sample_AuthorName"),
        EditedLineSuffix: Loc.Get("Sample_EditedLineSuffix"),
        AltLineSuffix:    Loc.Get("Sample_AltLineSuffix"),
        NewLineText:      Loc.Get("Sample_NewLineText"),
        TranslatorNote:   Loc.Get("Sample_TranslatorNote"),
        CommitInitial:    Loc.Get("Sample_CommitInitial"),
        CommitReshape:    Loc.Get("Sample_CommitReshape"),
        CommitExperiment: Loc.Get("Sample_CommitExperiment"));
}
