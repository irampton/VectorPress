// Writes generated meshes to binary STL files in millimeter units.
using System.Buffers.Binary;
using System.Numerics;
using VectorPress.Core.Models;

namespace VectorPress.Core.Services;

public sealed class BinaryStlWriter
{
	private const int HeaderLength = 80;
	private const int TriangleRecordLength = 50;

	public void Write(string filePath, TriangleMesh mesh)
	{
		ArgumentNullException.ThrowIfNull(mesh);

		using var stream = File.Create(filePath);
		using var writer = new BinaryWriter(stream);
		writer.Write(new byte[HeaderLength]);
		writer.Write((uint)mesh.Triangles.Count);

		Span<byte> record = stackalloc byte[TriangleRecordLength];
		foreach (var triangle in mesh.Triangles)
		{
			record.Clear();
			WriteTriangle(record, triangle);
			writer.Write(record);
		}
	}

	private static void WriteTriangle(Span<byte> buffer, MeshTriangle triangle)
	{
		var a = new Vector3(triangle.Ax, triangle.Ay, triangle.Az);
		var b = new Vector3(triangle.Bx, triangle.By, triangle.Bz);
		var c = new Vector3(triangle.Cx, triangle.Cy, triangle.Cz);
		var normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
		if (float.IsNaN(normal.X))
		{
			normal = Vector3.Zero;
		}

		WriteVector(buffer, 0, normal);
		WriteVector(buffer, 12, a);
		WriteVector(buffer, 24, b);
		WriteVector(buffer, 36, c);
		BinaryPrimitives.WriteUInt16LittleEndian(buffer[48..50], 0);
	}

	private static void WriteVector(Span<byte> buffer, int offset, Vector3 value)
	{
		BinaryPrimitives.WriteSingleLittleEndian(buffer[offset..(offset + 4)], value.X);
		BinaryPrimitives.WriteSingleLittleEndian(buffer[(offset + 4)..(offset + 8)], value.Y);
		BinaryPrimitives.WriteSingleLittleEndian(buffer[(offset + 8)..(offset + 12)], value.Z);
	}
}
