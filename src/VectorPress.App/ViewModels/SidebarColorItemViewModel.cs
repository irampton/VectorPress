using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;

namespace VectorPress.App.ViewModels;

public partial class SidebarColorItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string extrusionHeightMm = string.Empty;

    public required IBrush SwatchBrush { get; init; }

    public required string HexCode { get; init; }

    public required string Label { get; init; }
}
