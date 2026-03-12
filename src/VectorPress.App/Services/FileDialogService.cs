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
}
