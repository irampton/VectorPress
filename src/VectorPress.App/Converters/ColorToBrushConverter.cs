using Avalonia.Media;
using VectorPress.Core.Models;

namespace VectorPress.App.Converters;

public static class ColorToBrushConverter
{
    public static SolidColorBrush ToBrush(RgbaColor color)
        => color.A == 0
            ? new(Color.Parse("#FFE9E4DA"))
            : new(Color.FromArgb(color.A, color.R, color.G, color.B));

    public static string ToHex(RgbaColor color)
        => color.A == 0
            ? "Unfilled region"
            : $"#{color.R:X2}{color.G:X2}{color.B:X2}";
}
