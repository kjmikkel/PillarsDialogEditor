using Avalonia.Headless.XUnit;
using DialogEditor.Avalonia.Shared.Services;
using DialogEditor.Tests.Helpers;
using DialogEditor.ViewModels.Resources;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Localisation;

/// <summary>Tier one: every field of SampleTexts is looked up, not hard-coded.</summary>
public class SampleTextFactoryTests
{
    public SampleTextFactoryTests() => Loc.Configure(new StubStringProvider());

    [Fact]
    public void FromResources_LooksUpEveryField()
    {
        var t = SampleTextFactory.FromResources();

        Assert.Equal("Sample_AuthorName",       t.AuthorName);
        Assert.Equal("Sample_EditedLineSuffix", t.EditedLineSuffix);
        Assert.Equal("Sample_AltLineSuffix",    t.AltLineSuffix);
        Assert.Equal("Sample_NewLineText",      t.NewLineText);
        Assert.Equal("Sample_TranslatorNote",   t.TranslatorNote);
        Assert.Equal("Sample_CommitInitial",    t.CommitInitial);
        Assert.Equal("Sample_CommitReshape",    t.CommitReshape);
        Assert.Equal("Sample_CommitExperiment", t.CommitExperiment);
    }
}

/// <summary>
/// Tier two: the real Strings.axaml. Pins that the keys exist and — for the two
/// suffixes, which are appended to an existing game line — that their leading
/// separator spaces survived xml:space="preserve".
/// </summary>
public class SampleTextResourceEndToEndTests
{
    public SampleTextResourceEndToEndTests() => Loc.Configure(new AvaloniaStringProvider());

    [AvaloniaFact]
    public void EveryKeyResolves()
    {
        var t = SampleTextFactory.FromResources();

        foreach (var value in new[]
                 {
                     t.AuthorName, t.EditedLineSuffix, t.AltLineSuffix, t.NewLineText,
                     t.TranslatorNote, t.CommitInitial, t.CommitReshape, t.CommitExperiment,
                 })
        {
            Assert.False(string.IsNullOrWhiteSpace(value));
            // AvaloniaStringProvider returns "[Key]" for a key it cannot find.
            Assert.DoesNotContain("[Sample_", value);
        }
    }

    [AvaloniaFact]
    public void LineSuffixes_KeepTheirLeadingSeparatorSpace()
    {
        var t = SampleTextFactory.FromResources();

        Assert.StartsWith(" ", t.EditedLineSuffix);
        Assert.StartsWith(" ", t.AltLineSuffix);
    }
}
