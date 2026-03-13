// Holds editable per-color extrusion settings for the sidebar.
using Avalonia.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using VectorPress.Core.Models;

namespace VectorPress.App.ViewModels;

public partial class SidebarColorItemViewModel : ViewModelBase
{
    [ObservableProperty]
    private string extrusionHeightMm = "2.0";

    public required RgbaColor Color { get; init; }

    public required IBrush SwatchBrush { get; init; }

    public required string HexCode { get; init; }

    public required string Label { get; init; }
}
