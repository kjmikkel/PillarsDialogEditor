namespace DialogEditor.Tests.GameData;

/// <summary>
/// The theory form of <see cref="GameInstallFactAttribute"/>: skipped at discovery, with the
/// same reason, unless the environment variable names an existing game install.
/// </summary>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GameInstallTheoryAttribute : TheoryAttribute
{
    public GameInstallTheoryAttribute(string environmentVariable) =>
        Skip = GameInstallFactAttribute.SkipReason(environmentVariable);
}
