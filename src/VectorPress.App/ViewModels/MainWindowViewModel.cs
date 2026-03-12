using System.Collections.ObjectModel;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VectorPress.App.Converters;
using VectorPress.App.Services;
using VectorPress.Core.Services;

namespace VectorPress.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogService;
    private readonly SvgDocumentService _svgDocumentService;
    private readonly SvgPreviewRenderer _svgPreviewRenderer;

    public MainWindowViewModel(
        IFileDialogService fileDialogService,
        SvgDocumentService svgDocumentService,
        SvgPreviewRenderer svgPreviewRenderer)
    {
        _fileDialogService = fileDialogService;
        _svgDocumentService = svgDocumentService;
        _svgPreviewRenderer = svgPreviewRenderer;
        OpenSvgCommand = new AsyncRelayCommand(OpenSvgAsync);
        ExportCommand = new RelayCommand(Export, () => CanExport);
    }

    public ObservableCollection<SidebarColorItemViewModel> ColorItems { get; } = [];

    [ObservableProperty]
    private Bitmap? previewImage;

    [ObservableProperty]
    private bool hasPreview;

    public bool HasNoPreview => !HasPreview;

    [ObservableProperty]
    private bool hasColorItems;

    public bool HasNoColorItems => !HasColorItems;

    public bool CanExport => HasPreview;

    [ObservableProperty]
    private string selectedFileName = "No SVG loaded";

    [ObservableProperty]
    private string statusText = "Load an SVG to inspect its paint groups.";

    public IAsyncRelayCommand OpenSvgCommand { get; }

    public IRelayCommand ExportCommand { get; }

    private async Task OpenSvgAsync()
    {
        var filePath = await _fileDialogService.PickSvgFileAsync();
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return;
        }

        try
        {
            var document = _svgDocumentService.Load(filePath);
            var preview = _svgPreviewRenderer.Render(filePath);

            PreviewImage?.Dispose();
            PreviewImage = preview;
            HasPreview = true;
            SelectedFileName = document.FileName;
            StatusText = document.ColorLayers.Count == 0
                ? "SVG loaded. No visible paint groups were detected."
                : $"SVG loaded. {document.ColorLayers.Count} grouped paint colors detected.";

            ColorItems.Clear();
            foreach (var layer in document.ColorLayers)
            {
                ColorItems.Add(new SidebarColorItemViewModel
                {
                    SwatchBrush = ColorToBrushConverter.ToBrush(layer.Color),
                    HexCode = ColorToBrushConverter.ToHex(layer.Color),
                    Label = layer.ShapeCount == 1 ? "1 shape" : $"{layer.ShapeCount} shapes"
                });
            }

            HasColorItems = ColorItems.Count > 0;
        }
        catch (Exception ex)
        {
            PreviewImage?.Dispose();
            PreviewImage = null;
            HasPreview = false;
            HasColorItems = false;
            ColorItems.Clear();
            StatusText = $"Failed to load SVG: {ex.Message}";
        }
    }

    private void Export()
    {
    }

    partial void OnHasPreviewChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoPreview));
        OnPropertyChanged(nameof(CanExport));
        ExportCommand.NotifyCanExecuteChanged();
    }

    partial void OnHasColorItemsChanged(bool value)
    {
        OnPropertyChanged(nameof(HasNoColorItems));
    }
}
