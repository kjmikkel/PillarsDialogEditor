using System.Globalization;
using Avalonia.Data.Converters;
using DialogEditor.Avalonia.Theming;
using DialogEditor.ViewModels.Models;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Maps a <see cref="PropertyValueStyle"/> to its syntax highlight brush; returns null
/// for non-style values so the binding falls back. Resolves Brush.Syntax.* (spec §7.7).
/// </summary>
public sealed class PropertyValueStyleToBrushConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is PropertyValueStyle style ? style switch
        {
            PropertyValueStyle.Condition => TokenBrushes.Resolve("Brush.Syntax.Condition"),
            PropertyValueStyle.Script    => TokenBrushes.Resolve("Brush.Syntax.Script"),
            PropertyValueStyle.Code      => TokenBrushes.Resolve("Brush.Syntax.Code"),
            _                            => TokenBrushes.Resolve("Brush.Syntax.Default"),
        } : null;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
