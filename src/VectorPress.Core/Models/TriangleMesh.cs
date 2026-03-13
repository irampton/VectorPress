// Contains the full generated triangle mesh used by the preview and STL export pipeline.
namespace VectorPress.Core.Models;

public sealed record TriangleMesh(
	IReadOnlyList<MeshTriangle> Triangles,
	MeshBounds Bounds)
{
	public bool HasGeometry => Triangles.Count > 0;
}
