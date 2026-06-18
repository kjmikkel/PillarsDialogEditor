using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml.Styling;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Runtime language overlay injection (UI Localisation items 1–3).
/// Stateless: uses a <c>_LanguageOverlayMarker</c> sentinel key to find and remove any
/// previously injected overlay before adding the new one. <c>Apply("en")</c> is a no-op —
/// English IS the base <c>Strings.axaml</c>; no overlay file exists for it.
///
/// Each overlay file must contain a <c>_LanguageOverlayMarker</c> string key so this
/// applier can locate and remove it:
/// <code>&lt;sys:String x:Key="_LanguageOverlayMarker"&gt;de&lt;/sys:String&gt;</code>
/// </summary>
public sealed class LanguageApplier : ILanguageApplier
{
    private const string OverlaySentinel = "_LanguageOverlayMarker";

    // TODO: add "Auto" (OS locale detection) + additional entries once a translation ships.
    private static readonly LanguageEntry[] Catalog =
    [
        new("en", "Settings_Language_English"),
    ];

    private readonly string[] _uriTemplates;

    /// <summary>
    /// Constructs a <see cref="LanguageApplier"/> for the calling app.
    /// </summary>
    /// <param name="uriTemplates">
    /// Format strings for the per-language overlay URIs.
    /// <c>{0}</c> is replaced with the language code.
    /// Example: <c>"avares://DialogEditor.Avalonia/Resources/Strings.{0}.axaml"</c>
    /// </param>
    public LanguageApplier(params string[] uriTemplates) => _uriTemplates = uriTemplates;

    public IReadOnlyList<LanguageOption> Available { get; } =
        Catalog.Select(e => new LanguageOption(e.Id, e.DisplayNameKey)).ToList();

    public void Apply(string id)
    {
        var entry = Catalog.FirstOrDefault(e => e.Id == id);
        if (entry is null)
        {
            AppLog.Warn($"LanguageApplier: unknown language id '{id}'. Falling back to English.");
            id = "en";
        }

        var app   = Application.Current
            ?? throw new InvalidOperationException("No Application is running.");
        var dicts = app.Resources.MergedDictionaries;

        // Remove any previously injected overlay (identified by the sentinel key).
        for (var i = dicts.Count - 1; i >= 0; i--)
            if (dicts[i].TryGetResource(OverlaySentinel, null, out _))
                dicts.RemoveAt(i);

        // English is the base — no overlay needed.
        if (id != "en")
        {
            var wrapper = new ResourceDictionary();
            foreach (var template in _uriTemplates)
                wrapper.MergedDictionaries.Add(
                    new ResourceInclude((Uri?)null)
                    {
                        Source = new Uri(string.Format(template, id)),
                    });
            dicts.Add(wrapper);
        }

        LocaleService.Current.Bump();
    }

    private sealed record LanguageEntry(string Id, string DisplayNameKey);
}
