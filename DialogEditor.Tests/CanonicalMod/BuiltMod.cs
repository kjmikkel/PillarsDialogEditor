using DialogEditor.Patch;
using DialogEditor.Patch.Install;
using DialogEditor.Tests.Helpers;

namespace DialogEditor.Tests.CanonicalMod;

/// Builds a game's canonical mod once per test class and installs it with the patcher's own
/// routine into a fresh copy of the canonical install. xUnit class fixtures need a concrete
/// type with a parameterless constructor, hence one small subclass per game.
public abstract class BuiltMod : IDisposable
{
    public string Root        { get; } = Path.Combine(Path.GetTempPath(), $"canonical built {Guid.NewGuid():N}");
    public string ProjectPath { get; }
    public string GameRoot    => Path.Combine(Root, "game");
    public ModView Applied    { get; }

    protected BuiltMod(CanonicalGame game)
    {
        ProjectPath = CanonicalModBuilder.BuildAsync(game.Id, game.Steps, Path.Combine(Root, "mod")).GetAwaiter().GetResult();
        CanonicalFixture.CopyTo(game.Id, GameRoot);
        var provider = game.Provider(GameRoot);
        var project  = DialogProjectSerializer.LoadFromFile(ProjectPath);
        var voDir    = Path.Combine(Path.GetDirectoryName(ProjectPath)!, "_vo");
        var result   = PatchInstaller.Install(provider, GameRoot,
            [new InstallEntry(project, Directory.Exists(voDir) ? voDir : null)], new InstallOptions());
        Assert.IsType<InstallResult.Applied>(result);
        Applied = ModView.FromGame(provider, game.Conversations, game.Languages);
    }

    public void Dispose() { try { Directory.Delete(Root, true); } catch (Exception) { /* best-effort */ } }
}

public sealed class BuiltPoe2Mod() : BuiltMod(CanonicalGame.Poe2);
