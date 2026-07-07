using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

/// Guards that the regenerated catalogue covers every script/condition signature
/// actually used in shipped conversations (fixture: Fixtures/catalogue-usage.txt,
/// produced by tools/catalogue-gen). This is the regression net that would have
/// caught the systemic incompleteness behind B-010/B-011.
public class CatalogueCoverageTests
{
    // Signatures present in shipped conversation data but NOT in the current
    // decompiled sources — genuine version skew between the shipped build and the
    // decompiled assembly we generate from. Documented and allowed; every OTHER
    // used signature must be covered.
    private static readonly HashSet<string> KnownVersionSkew = new(StringComparer.Ordinal)
    {
        // return type changed bool -> Void in the shipped build
        "Void InteractionSelectPartyMemberAbility(Int32, Guid)",
        // param serialized by its GameData type name, not Guid
        "Void ApplySevereInjury(Guid, AfflictionGameData)",
        // ship-combat participant serialized as "Participant", not the CLR type
        "Boolean IsCurrentHullHealthValue(Participant, Operator, Int32)",
        // parameter list differs from the current decompiled signature
        "Void RandomizeGlobalValueWithGlobal(String, Int32, String, Operator, Int32)",
        // absent from the current decompiled sources entirely
        "Void AdjustPartyFatigueWithSkillCheck(Int32, Guid, Operator, Int32)",
    };

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DialogEditor.slnx")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    private static HashSet<string> CatalogueFullNames()
    {
        var set = new HashSet<string>(StringComparer.Ordinal);
        foreach (var e in ConditionCatalogue.Instance.All) set.Add(e.ReflectionFullName);
        foreach (var e in ScriptCatalogue.Instance.All)    set.Add(e.ReflectionFullName);
        return set;
    }

    [Fact]
    public void EveryShippedSignature_IsCoveredOrKnownSkew()
    {
        var fixture = Path.Combine(RepoRoot(), "DialogEditor.Tests", "Fixtures", "catalogue-usage.txt");
        var used = File.ReadAllLines(fixture)
            .Select(l => l.Trim()).Where(l => l.Length > 0).ToList();
        var known = CatalogueFullNames();

        var missing = used
            .Where(fn => !known.Contains(fn) && !KnownVersionSkew.Contains(fn))
            .OrderBy(x => x, StringComparer.Ordinal)
            .ToList();

        Assert.True(missing.Count == 0,
            $"{missing.Count} shipped signatures missing from the catalogue " +
            $"(add to the generator, or to KnownVersionSkew if genuinely version-skewed):\n" +
            string.Join("\n", missing.Take(40)));
    }

    [Fact]
    public void KnownVersionSkew_StaysMinimal()
        => Assert.True(KnownVersionSkew.Count <= 8,
            "Version-skew allow-list is growing — the decompiled sources may be stale.");
}
