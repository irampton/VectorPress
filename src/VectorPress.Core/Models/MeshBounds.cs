// Stores the axis-aligned bounds for a generated mesh.
namespace VectorPress.Core.Models;

public sealed record MeshBounds(
	float MinX,
	float MinY,
	float MinZ,
	float MaxX,
	float MaxY,
	float MaxZ)
{
	public float Width => MaxX - MinX;

	public float Height => MaxY - MinY;

	public float Depth => MaxZ - MinZ;

	public float CenterX => (MinX + MaxX) * 0.5f;

	public float CenterY => (MinY + MaxY) * 0.5f;

	public float CenterZ => (MinZ + MaxZ) * 0.5f;
}
