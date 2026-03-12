using Avalonia.Media;
using VectorPress.Core.Models;

namespace VectorPress.App.Converters;

public static class ColorToBrushConverter
{
    public static SolidColorBrush ToBrush(RgbaColor color)
        => new(Color.FromArgb(color.A, color.R, color.G, color.B));

    public static string ToHex(RgbaColor color)
        => $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
