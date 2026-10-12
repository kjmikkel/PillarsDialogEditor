using DialogEditor.Core.GameData;
using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Builds the PoE2 mod once per test class and installs it with the patcher's own routine
/// into a fresh copy of the canonical install.
public sealed class BuiltPoe2Mod : IDisposable
{
    public string Root        { get; } = Path.Combine(Path.GetTempPath(), $"canonical built {Guid.NewGuid():N}");
    public string ProjectPath { get; }
    public string GameRoot    => Path.Combine(Root, "game");
    public ModView Applied    { get; }

    public BuiltPoe2Mod()
    {
        ProjectPath = CanonicalModBuilder.BuildAsync("poe2", Poe2Scenario.Steps, Path.Combine(Root, "mod")).GetAwaiter().GetResult();
        CanonicalFixture.CopyTo("poe2", GameRoot);
        var provider = new Poe2GameDataProvider(GameRoot);
        var project  = DialogProjectSerializer.LoadFromFile(ProjectPath);
        var voDir    = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
        var result   = PatchInstaller.Install(provider, GameRoot,
            [new InstallEntry(project, Directory.Exists(voDir) ? voDir : null)], new InstallOptions());
        Assert.IsType<InstallResult.Applied>(result);
        Applied = ModView.FromGame(provider, Poe2Scenario.Conversations, ["en", "de"]);
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ } }
}
