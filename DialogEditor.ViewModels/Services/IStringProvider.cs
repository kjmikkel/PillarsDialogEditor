namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Platform-agnostic string resource provider used by Loc.
/// Backed by Application.Current.FindResource (Strings.axaml).
/// </summary>
public interface IStringProvider
{
    /// <summary>Returns the localised string for <paramref name="key"/>,
    /// or a visible "[key]" placeholder when the key is missing.</summary>
    string Get(string key);
}
