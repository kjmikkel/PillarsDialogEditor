namespace DialogEditor.WPF.Services;

/// <summary>
/// Platform-agnostic string resource provider used by Loc.
/// WPF: backed by Application.Current.Resources (ResourceDictionary / Strings.xaml).
/// Avalonia: backed by Application.Current.FindResource or equivalent.
/// </summary>
public interface IStringProvider
{
    /// <summary>Returns the localised string for <paramref name="key"/>,
    /// or a visible "[key]" placeholder when the key is missing.</summary>
    string Get(string key);
}
