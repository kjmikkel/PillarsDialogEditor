using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;

namespace DialogEditor.WPF.ViewModels;

public partial class ConnectorViewModel : ObservableObject
{
    [ObservableProperty]
    private Point _anchor;
}
