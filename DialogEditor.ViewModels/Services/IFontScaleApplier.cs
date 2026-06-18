namespace DialogEditor.ViewModels.Services;

/// <summary>
/// Framework-agnostic seam for runtime font-scale switching. The Avalonia implementation
/// mutates the live FontSize.* resource tokens and bumps <c>FontScaleService.Revision</c>;
/// tests inject a stub. Mirrors <see cref="IThemeApplier"/>.
/// </summary>
public interface IFontScaleApplier
{
    void Apply(double scale);
}
