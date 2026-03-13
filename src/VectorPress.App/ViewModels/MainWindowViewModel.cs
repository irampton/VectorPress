// Coordinates SVG loading, extrusion configuration, 3D preview generation, and STL export.
using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using VectorPress.App.Converters;
using VectorPress.App.Services;
using VectorPress.Core.Models;
using VectorPress.Core.Services;

namespace VectorPress.App.ViewModels;

public partial class MainWindowViewModel : ViewModelBase
{
    private readonly IFileDialogService _fileDialogService;
    private readonly BinaryStlWriter _binaryStlWriter;
    private readonly SvgExtrusionMeshService _svgExtrusionMeshService;
    private readonly SvgDocumentService _svgDocumentService;
    private readonly SvgPreviewRenderer _svgPreviewRenderer;
    private SvgDocumentInfo? _document;

    public MainWindowViewModel(
        IFileDialogService fileDialogService,
        SvgDocumentService svgDocumentService,
        SvgPreviewRenderer svgPreviewRenderer,
        SvgExtrusionMeshService svgExtrusionMeshService,
        BinaryStlWriter binaryStlWriter)
    {
        _fileDialogService = fileDialogService;
        _svgDocumentService = svgDocumentService;
        _svgPreviewRenderer = svgPreviewRenderer;
        _svgExtrusionMeshService = svgExtrusionMeshService;
        _binaryStlWriter = binaryStlWriter;
        OpenSvgCommand = new AsyncRelayCommand(OpenSvgAsync);
        ExportCommand = new AsyncRelayCommand(ExportAsync, () => CanExport);
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

    [ObservableProperty]
    private TriangleMesh? previewMesh;

    public bool HasMeshPreview => PreviewMesh?.HasGeometry == true;

    public bool HasNoMeshPreview => !HasMeshPreview;

    public bool CanExport => HasMeshPreview;

    [ObservableProperty]
    private string selectedFileName = "No SVG loaded";

    [ObservableProperty]
    private string statusText = "Load an SVG to inspect its paint groups.";

    public IAsyncRelayCommand OpenSvgCommand { get; }

    public IAsyncRelayCommand ExportCommand { get; }

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
            _document = document;

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
                var item = new SidebarColorItemViewModel
                {
                    Color = layer.Color,
                    SwatchBrush = ColorToBrushConverter.ToBrush(layer.Color),
                    HexCode = ColorToBrushConverter.ToHex(layer.Color),
                    Label = layer.IsTransparentRegion
                        ? (layer.ShapeCount == 1 ? "1 unfilled region" : $"{layer.ShapeCount} unfilled regions")
                        : (layer.ShapeCount == 1 ? "1 shape" : $"{layer.ShapeCount} shapes")
                };
                item.PropertyChanged += OnColorItemPropertyChanged;
                ColorItems.Add(item);
            }

            HasColorItems = ColorItems.Count > 0;
            RebuildMeshPreview();
        }
        catch (Exception ex)
        {
            _document = null;
            PreviewImage?.Dispose();
            PreviewImage = null;
            HasPreview = false;
            HasColorItems = false;
            PreviewMesh = null;
            ColorItems.Clear();
            StatusText = $"Failed to load SVG: {ex.Message}";
        }
    }

    private async Task ExportAsync()
    {
        if (_document is null)
        {
            return;
        }

        RebuildMeshPreview();
        if (!HasMeshPreview || PreviewMesh is null)
        {
            StatusText = "Enter a positive extrusion height for at least one color group before exporting.";
            return;
        }

        var suggestedName = Path.GetFileNameWithoutExtension(_document.FileName) + ".stl";
        var savePath = await _fileDialogService.PickStlSavePathAsync(suggestedName);
        if (string.IsNullOrWhiteSpace(savePath))
        {
            return;
        }

        try
        {
            _binaryStlWriter.Write(savePath, PreviewMesh);
            StatusText = $"Exported STL to {Path.GetFileName(savePath)}.";
        }
        catch (Exception ex)
        {
            StatusText = $"Failed to export STL: {ex.Message}";
        }
    }

    private void RebuildMeshPreview()
    {
        if (_document is null)
        {
            PreviewMesh = null;
            return;
        }

        try
        {
            PreviewMesh = _svgExtrusionMeshService.BuildMesh(_document, BuildLayerSettings());
        }
        catch (Exception ex)
        {
            PreviewMesh = null;
            StatusText = $"Failed to build 3D preview: {ex.Message}";
        }
    }

    private ExtrusionLayerSettings[] BuildLayerSettings()
    {
        var settings = new ExtrusionLayerSettings[ColorItems.Count];
        for (var index = 0; index < ColorItems.Count; index++)
        {
            var item = ColorItems[index];
            var height = TryParseHeight(item.ExtrusionHeightMm, out var parsedHeight) ? parsedHeight : 0f;
            settings[index] = new ExtrusionLayerSettings(item.Color, height, height > 0f);
        }

        return settings;
    }

    private static bool TryParseHeight(string? rawHeight, out float height)
    {
        return float.TryParse(rawHeight, NumberStyles.Float, CultureInfo.InvariantCulture, out height) && height > 0f;
    }

    private void OnColorItemPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(SidebarColorItemViewModel.ExtrusionHeightMm))
        {
            RebuildMeshPreview();
        }
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

    partial void OnPreviewMeshChanged(TriangleMesh? value)
    {
        OnPropertyChanged(nameof(HasMeshPreview));
        OnPropertyChanged(nameof(HasNoMeshPreview));
        OnPropertyChanged(nameof(CanExport));
        ExportCommand.NotifyCanExecuteChanged();
    }
}
