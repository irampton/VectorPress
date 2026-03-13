// Describes the user-configurable extrusion settings for a single SVG color group.
namespace VectorPress.Core.Models;

public sealed record ExtrusionLayerSettings(
	RgbaColor Color,
	float HeightMm,
	bool IsEnabled);
