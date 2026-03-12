using Avalonia.Media.Imaging;
using Svg.Skia;
using SkiaSharp;

namespace VectorPress.App.Services;

public sealed class SvgPreviewRenderer
{
    private const int MaxPreviewSize = 1600;

    public Bitmap Render(string filePath)
    {
        using var svg = new SKSvg();
        var picture = svg.Load(filePath) ?? throw new InvalidOperationException("Unable to load SVG.");
        var bounds = picture.CullRect;

        var width = Math.Max(1, (int)Math.Ceiling(bounds.Width));
        var height = Math.Max(1, (int)Math.Ceiling(bounds.Height));
        var scale = Math.Min(1d, MaxPreviewSize / (double)Math.Max(width, height));
        var scaledWidth = Math.Max(1, (int)Math.Ceiling(width * scale));
        var scaledHeight = Math.Max(1, (int)Math.Ceiling(height * scale));

        var imageInfo = new SKImageInfo(scaledWidth, scaledHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var surface = SKSurface.Create(imageInfo) ?? throw new InvalidOperationException("Unable to create preview surface.");
        var canvas = surface.Canvas;
        canvas.Clear(SKColors.Transparent);
        canvas.Scale((float)scale);
        canvas.Translate(-bounds.Left, -bounds.Top);
        canvas.DrawPicture(picture);
        canvas.Flush();

        using var image = surface.Snapshot();
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        return new Bitmap(stream);
    }
}
