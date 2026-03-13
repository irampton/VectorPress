// Stores a single vector region and its holes for extrusion and export.
using SkiaSharp;

namespace VectorPress.Core.Models;

public sealed record SvgPolygonRegion(
	RgbaColor Color,
	IReadOnlyList<SKPoint> OuterContour,
	IReadOnlyList<IReadOnlyList<SKPoint>> Holes,
	bool IsTransparentRegion);
