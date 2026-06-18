using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.Avalonia.Shared.Theming;

/// <summary>
/// Process-wide "the language just changed" ticker. <see cref="Revision"/> is bumped on
/// every overlay swap. ViewModels with computed string getters subscribe to
/// <see cref="PropertyChanged"/> and call <c>OnPropertyChanged(string.Empty)</c> to
/// re-evaluate their localized properties live.
/// Singleton because XAML binds <c>{x:Static}</c> to it and there is exactly one running app.
/// </summary>
public sealed partial class LocaleService : ObservableObject
{
    public static LocaleService Current { get; } = new();

    private LocaleService() { }

    [ObservableProperty] private int _revision;

    internal void Bump() => Revision++;
}
