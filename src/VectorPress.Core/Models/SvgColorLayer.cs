// Describes a color-group row shown in the extrusion sidebar.
namespace VectorPress.Core.Models;

public sealed record SvgColorLayer(
	RgbaColor Color,
	int ShapeCount,
	bool IsTransparentRegion = false);
