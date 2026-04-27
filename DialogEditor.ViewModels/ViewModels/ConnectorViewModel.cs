using CommunityToolkit.Mvvm.ComponentModel;
using DialogEditor.Core.Models;

namespace DialogEditor.ViewModels;

public partial class ConnectorViewModel : ObservableObject
{
    [ObservableProperty]
    private LayoutPoint _anchor;
}
