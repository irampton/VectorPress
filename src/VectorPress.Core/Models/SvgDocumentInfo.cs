// Contains the loaded SVG source, grouped colors, and extracted vector regions.
namespace VectorPress.Core.Models;

public sealed record SvgDocumentInfo(
	string FilePath,
	string FileName,
	string RawSvg,
	IReadOnlyList<SvgColorLayer> ColorLayers,
	IReadOnlyList<SvgPolygonRegion> Regions);
