namespace DialogEditor.Tests.GameData;

/// <summary>
/// A test that needs a real game install, named by an environment variable (issue 117).
/// </summary>
/// <remarks>
/// xunit v2 has no runtime skip, so the attribute decides at discovery: when the variable
/// is unset or does not name an existing folder the test is reported as skipped with the
/// reason, never failed. A default <c>dotnet test</c> therefore stays green on any machine,
/// and game data never has to exist on a CI runner (CI also filters Category=GameData out).
/// See docs/game-data-tests.md.
/// </remarks>
[AttributeUsage(AttributeTargets.Method)]
public sealed class GameInstallFactAttribute : FactAttribute
{
    public const string Poe1Variable = "DIALOGEDITOR_POE1_DIR";
    public const string Poe2Variable = "DIALOGEDITOR_POE2_DIR";

    public GameInstallFactAttribute(string environmentVariable)
    {
        var dir = Environment.GetEnvironmentVariable(environmentVariable);
        if (string.IsNullOrWhiteSpace(dir))
            Skip = $"Set {environmentVariable} to a game install folder to run this test (docs/game-data-tests.md).";
        else if (!Directory.Exists(dir))
            Skip = $"{environmentVariable} is set but '{dir}' does not exist.";
    }

    public static string InstallDir(string environmentVariable) =>
        Environment.GetEnvironmentVariable(environmentVariable)!;
}
