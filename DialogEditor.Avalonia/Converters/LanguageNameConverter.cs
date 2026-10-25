using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Shows a game language code ("fr", "pt-BR") as its localised name (GitHub issue 251). The item
/// itself, what SelectedItem writes back, stays the code; an unknown code is shown as is.
/// </summary>
public sealed class LanguageNameConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string code ? LanguageNameResolver.Resolve(code) : value;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException("Item text is display-only; the item is the language code.");
}
