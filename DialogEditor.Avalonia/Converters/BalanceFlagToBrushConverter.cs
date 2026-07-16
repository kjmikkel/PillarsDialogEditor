using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using DialogEditor.Avalonia.Theming;
using DialogEditor.ViewModels.Services;

namespace DialogEditor.Avalonia.Converters;

/// <summary>
/// Maps a <see cref="BalanceFlag"/> to a semantic brush token so a flagged value reads
/// visually as well as textually. Over-favoured and never-checked read as errors, under-used
/// as a warning, normal as primary text. Resolves Brush.* tokens (no hex — NoStrayHexTests).
/// </summary>
public sealed class BalanceFlagToBrushConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
        => value is BalanceFlag flag
            ? TokenBrushes.Resolve(flag switch
            {
                BalanceFlag.Over    => "Brush.Severity.Error",
                BalanceFlag.Ignored => "Brush.Severity.Error",
                BalanceFlag.Under   => "Brush.Severity.Warning",
                _                   => "Brush.Text.Primary",
            })
            : TokenBrushes.Resolve("Brush.Text.Primary");

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
