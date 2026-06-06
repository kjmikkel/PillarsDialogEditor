using System.ComponentModel;
using DialogEditor.Patch.Diff;
using Xunit;

namespace DialogEditor.Tests.Patch.Diff;

public class ProcessGitRunnerTests
{
    [Fact]
    public void ClassifyStartFailure_ExecutableNotFound_IsGitMissing()
    {
        // Win32 error 2 == ERROR_FILE_NOT_FOUND (git not on PATH).
        var ex = ProcessGitRunner.ClassifyStartFailure(new Win32Exception(2));
        Assert.Equal(DiffExceptionKind.GitMissing, ex.Kind);
    }

    [Fact]
    public void ClassifyStartFailure_OtherError_IsUnknown()
    {
        var ex = ProcessGitRunner.ClassifyStartFailure(new InvalidOperationException("boom"));
        Assert.Equal(DiffExceptionKind.Unknown, ex.Kind);
    }
}
