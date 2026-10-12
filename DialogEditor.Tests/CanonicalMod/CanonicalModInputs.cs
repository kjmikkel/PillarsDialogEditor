namespace DialogEditor.Tests.CanonicalMod;

/// Inputs the canonical mod's scenario imports. Invented placeholder bytes, not audio.
internal static class CanonicalModInputs
{
    public static string Poe2Wem =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "CanonicalMod", "poe2-inputs", "canonical_mod_0011.wem");
}
