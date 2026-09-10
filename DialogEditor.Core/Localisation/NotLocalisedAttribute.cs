namespace DialogEditor.Core.Localisation;

/// <summary>
/// Marks a declaration whose string literals are deliberately NOT user-visible prose,
/// exempting them from the guard in NoHardcodedUiStringsInCodeTests.
///
/// Use it for text a translator must never touch: file formats the game parses back,
/// expression serialisations, command-line arguments, and developer diagnostics. It is
/// not an escape hatch for UI text that is merely inconvenient to localise — if a user
/// reads it in the app, it belongs in a resource dictionary.
///
/// The reason is a required constructor argument on purpose: the whole point of choosing
/// an opt-in marker over an allowlist file was that each exemption justifies itself at
/// the site, where the next reader will see it.
/// </summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum
    | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field
    | AttributeTargets.Constructor,
    AllowMultiple = false, Inherited = false)]
public sealed class NotLocalisedAttribute(string reason) : Attribute
{
    public string Reason { get; } = reason;
}
