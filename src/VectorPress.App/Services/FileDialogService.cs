using Avalonia.Controls;
using Avalonia.Platform.Storage;

namespace VectorPress.App.Services;

public sealed class FileDialogService(Window owner) : IFileDialogService
{
    private static readonly FilePickerFileType SvgType = new("SVG")
    {
        Patterns = ["*.svg"],
        MimeTypes = ["image/svg+xml"]
    };

    private static readonly FilePickerFileType StlType = new("STL")
    {
        Patterns = ["*.stl"],
        MimeTypes = ["model/stl", "application/sla"]
    };

    public async Task<string?> PickSvgFileAsync()
    {
        var files = await owner.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = "Open SVG",
            AllowMultiple = false,
            FileTypeFilter = [SvgType]
        });

        return files.Count == 0 ? null : files[0].TryGetLocalPath();
    }

    public async Task<string?> PickStlSavePathAsync(string suggestedFileName)
    {
        var file = await owner.StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = "Export STL",
            SuggestedFileName = suggestedFileName,
            DefaultExtension = "stl",
            FileTypeChoices = [StlType]
        });

        return file?.TryGetLocalPath();
    }
}
