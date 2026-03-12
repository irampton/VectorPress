namespace VectorPress.Core.Models;

public sealed record SvgDocumentInfo(
    string FilePath,
    string FileName,
    string RawSvg,
    IReadOnlyList<SvgColorLayer> ColorLayers);
