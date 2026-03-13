// Represents a single colored triangle in the generated preview/export mesh.
namespace VectorPress.Core.Models;

public sealed record MeshTriangle(
	float Ax,
	float Ay,
	float Az,
	float Bx,
	float By,
	float Bz,
	float Cx,
	float Cy,
	float Cz,
	RgbaColor Color);
