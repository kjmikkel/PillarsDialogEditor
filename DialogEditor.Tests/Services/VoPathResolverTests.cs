using DialogEditor.ViewModels.Services;

namespace DialogEditor.Tests.Services;

public class VoPathResolverTests : IDisposable
{
    // Use a temp directory so tests can plant/remove real files
    private readonly string _voRoot;
    private readonly string _gameRoot;

    public VoPathResolverTests()
    {
        _gameRoot = Path.Combine(Path.GetTempPath(), $"VoTest_{Guid.NewGuid():N}");
        _voRoot   = Path.Combine(_gameRoot,
            "PillarsOfEternityII_Data", "StreamingAssets", "Audio", "Windows", "Voices", "English(US)");
        Directory.CreateDirectory(_voRoot);

        ChatterPrefixService.Register(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            { "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", "eder" }
        });
    }

    public void Dispose()
    {
        ChatterPrefixService.Clear();
        if (Directory.Exists(_gameRoot))
            Directory.Delete(_gameRoot, recursive: true);
    }

    // ── _vo/ fallback exposes a playable local path ───────────────────────

    [Fact]
    public void WithLocalVoFallback_GameCopyMissing_SetsLocalPrimaryWemPath()
    {
        // The canonical PrimaryWemPath must stay the game path (import/batch code
        // derives destination paths from it), so the playable local copy is
        // surfaced separately.
        var projectDir = Path.Combine(_gameRoot, "proj");
        var localDir   = Path.Combine(projectDir, "_vo", "eder");
        Directory.CreateDirectory(localDir);
        var localWem = Path.Combine(localDir, "conv_0001.wem");
        File.WriteAllText(localWem, "");
        var projectPath = Path.Combine(projectDir, "test.dialogproject");

        var missing = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "conv", _gameRoot, "poe2")!;
        Assert.Equal(VoPresence.Missing, missing.Status);

        var result = VoPathResolver.WithLocalVoFallback(missing, projectPath, _gameRoot);

        Assert.Equal(VoPresence.Found, result.Status);
        Assert.Equal(localWem, result.LocalPrimaryWemPath);
        Assert.Equal(missing.PrimaryWemPath, result.PrimaryWemPath);   // canonical path untouched
    }

    // ── Returns null for non-PoE2 ─────────────────────────────────────────

    [Fact]
    public void Check_Poe1GameId_ReturnsNull()
    {
        var result = VoPathResolver.Check("any-guid", true, "", 1, "conv", _gameRoot, "poe1");
        Assert.Null(result);
    }

    [Fact]
    public void Check_EmptyGameRoot_ReturnsNull()
    {
        var result = VoPathResolver.Check("any-guid", true, "", 1, "conv", "", "poe2");
        Assert.Null(result);
    }

    // ── NotApplicable ─────────────────────────────────────────────────────

    [Fact]
    public void Check_NoHasVO_NoExternalVO_ReturnsNotApplicable()
    {
        var result = VoPathResolver.Check("any-guid", false, "", 1, "conv", _gameRoot, "poe2");
        Assert.NotNull(result);
        Assert.Equal(VoPresence.NotApplicable, result!.Status);
    }

    // ── Missing (unknown speaker) ─────────────────────────────────────────

    [Fact]
    public void Check_HasVO_UnknownSpeaker_ReturnsMissing()
    {
        var result = VoPathResolver.Check("unknown-guid", true, "", 1, "conv", _gameRoot, "poe2");
        Assert.Equal(VoPresence.Missing, result!.Status);
    }

    // ── Narrator hardcoded path ───────────────────────────────────────────

    [Fact]
    public void Check_NarratorGuid_UseNarratorPrefix()
    {
        var narratorGuid = "6a99a109-0000-0000-0000-000000000000";
        // File does not exist — result should be Missing (not null, not NotApplicable)
        var result = VoPathResolver.Check(narratorGuid, true, "", 5, "test_conv", _gameRoot, "poe2");
        Assert.Equal(VoPresence.Missing, result!.Status);
    }

    [Fact]
    public void Check_NarratorGuid_FileExists_ReturnsFound()
    {
        var narratorGuid = "6a99a109-0000-0000-0000-000000000000";
        var dir  = Path.Combine(_voRoot, "narrator");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0005.wem"), "");

        var result = VoPathResolver.Check(narratorGuid, true, "", 5, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
        Assert.False(result.FemaleVariantFound);
    }

    // ── Standard path ─────────────────────────────────────────────────────

    [Fact]
    public void Check_HasVO_KnownSpeaker_FileExists_ReturnsFound()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
        Assert.False(result.FemaleVariantFound);
    }

    [Fact]
    public void Check_HasVO_KnownSpeaker_FileMissing_ReturnsMissing()
    {
        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");
        Assert.Equal(VoPresence.Missing, result!.Status);
    }

    [Fact]
    public void Check_FemVariantAlsoExists_FemaleVariantFoundIsTrue()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"),     "");
        File.WriteAllText(Path.Combine(dir, "test_conv_0001_fem.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
        Assert.True(result.FemaleVariantFound);
    }

    [Fact]
    public void Check_NodeIdPaddedToFourDigits()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0042.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 42, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
    }

    [Fact]
    public void Check_ConvNameLowercased()
    {
        // File uses lowercase name; conversationName passed in mixed case
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "my_conv_0001.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "My_Conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
    }

    // ── ExternalVO override ───────────────────────────────────────────────

    [Fact]
    public void Check_ExternalVO_OverridesStandardPath()
    {
        // ExternalVO = "eder/00_cv_test_0153" → looks for .../eder/00_cv_test_0153.wem
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00_cv_test_0153.wem"), "");

        var result = VoPathResolver.Check(
            "unknown-guid", false, "eder/00_cv_test_0153", 999, "anything", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
    }

    [Fact]
    public void Check_ExternalVO_FileAbsent_ReturnsMissing()
    {
        var result = VoPathResolver.Check(
            "any-guid", false, "eder/00_cv_missing_0001", 1, "conv", _gameRoot, "poe2");
        Assert.Equal(VoPresence.Missing, result!.Status);
    }

    [Fact]
    public void Check_ExternalVO_TakesPrecedenceOverHasVO()
    {
        // HasVO AND ExternalVO set: ExternalVO path is used
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "override_0001.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb",
            hasVO: true,
            externalVO: "eder/override_0001",
            nodeId: 99, conversationName: "conv",
            _gameRoot, "poe2");

        Assert.Equal(VoPresence.Found, result!.Status);
    }

    // ── PrimaryWemPath and FemWemPath ─────────────────────────────────────

    [Fact]
    public void Check_KnownSpeaker_PrimaryWemPathContainsExpectedFilename()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

        Assert.NotNull(result!.PrimaryWemPath);
        Assert.EndsWith("test_conv_0001.wem", result.PrimaryWemPath!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_NoFemVariant_FemWemPathIsNull()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

        Assert.Null(result!.FemWemPath);
    }

    [Fact]
    public void Check_FemVariantExists_FemWemPathSet()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "test_conv_0001.wem"),     "");
        File.WriteAllText(Path.Combine(dir, "test_conv_0001_fem.wem"), "");

        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

        Assert.NotNull(result!.FemWemPath);
        Assert.EndsWith("test_conv_0001_fem.wem", result.FemWemPath!,
            StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Check_NotApplicable_BothPathsNull()
    {
        var result = VoPathResolver.Check("any-guid", false, "", 1, "conv", _gameRoot, "poe2");
        Assert.Null(result!.PrimaryWemPath);
        Assert.Null(result.FemWemPath);
    }

    [Fact]
    public void Check_UnknownSpeaker_PrimaryWemPathIsNull()
    {
        // Unknown speaker → we cannot resolve the folder, so the path stays null
        var result = VoPathResolver.Check("unknown-guid", true, "", 1, "conv", _gameRoot, "poe2");
        Assert.Null(result!.PrimaryWemPath);
        Assert.Null(result.FemWemPath);
    }

    [Fact]
    public void Check_PrimaryFileMissing_PrimaryWemPathStillSet()
    {
        // Even when the file doesn't exist, PrimaryWemPath holds the expected location —
        // the player needs it to attempt playback (and fail gracefully).
        var result = VoPathResolver.Check(
            "9c5f12c9-e93d-4952-9f1a-726c9498f8fb", true, "", 1, "test_conv", _gameRoot, "poe2");

        Assert.Equal(VoPresence.Missing, result!.Status);
        Assert.NotNull(result.PrimaryWemPath);
    }

    [Fact]
    public void Check_ExternalVO_PrimaryWemPathSet()
    {
        var dir = Path.Combine(_voRoot, "eder");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "00_cv_test_0153.wem"), "");

        var result = VoPathResolver.Check(
            "unknown-guid", false, "eder/00_cv_test_0153", 999, "anything", _gameRoot, "poe2");

        Assert.NotNull(result!.PrimaryWemPath);
        Assert.EndsWith("00_cv_test_0153.wem", result.PrimaryWemPath!,
            StringComparison.OrdinalIgnoreCase);
    }
}
