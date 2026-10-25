using System.Text.Json;
using DialogEditor.Patch;

namespace DialogEditor.Tests.Patch;

/// GitHub issue 249: a corrupt download used to surface as parser text ("'not json' is an
/// invalid JSON literal…"). These failures mean "damaged, or not a mod", and are reported so.
public class ModFileErrorsTests
{
    [Fact] public void BadJson_IsDamaged()          => Assert.True(ModFileErrors.IsDamaged(new JsonException("x")));
    [Fact] public void BadZip_IsDamaged()           => Assert.True(ModFileErrors.IsDamaged(new InvalidDataException("x")));
    [Fact] public void TruncatedFile_IsDamaged()    => Assert.True(ModFileErrors.IsDamaged(new EndOfStreamException("x")));
    [Fact] public void MissingFile_IsNotDamaged()   => Assert.False(ModFileErrors.IsDamaged(new FileNotFoundException("x")));
    [Fact] public void AccessDenied_IsNotDamaged()  => Assert.False(ModFileErrors.IsDamaged(new UnauthorizedAccessException("x")));
}
