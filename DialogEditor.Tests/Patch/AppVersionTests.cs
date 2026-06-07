using System.Reflection;
using DialogEditor.Patch;
using Xunit;

namespace DialogEditor.Tests.Patch;

public class AppVersionTests
{
    [Fact]
    public void FromAssembly_ReturnsInformationalVersionOfThatAssembly()
    {
        var asm = typeof(AppVersionTests).Assembly;
        var expected = asm.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion ?? "unknown";

        Assert.Equal(expected, AppVersion.FromAssembly(asm));
    }

    [Fact]
    public void FromAssembly_Null_ReturnsUnknown()
    {
        Assert.Equal("unknown", AppVersion.FromAssembly(null));
    }
}
