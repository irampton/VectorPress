// Generates smooth 2.5D triangle meshes from extracted SVG polygon regions.
using SkiaSharp;
using VectorPress.Core.Models;

namespace VectorPress.Core.Services;

public sealed class SvgExtrusionMeshService
{
	private const float ColorMergeDistance = 60f;
	private const float Epsilon = 0.0001f;

	public TriangleMesh BuildMesh(SvgDocumentInfo document, IReadOnlyList<ExtrusionLayerSettings> settings)
	{
		ArgumentNullException.ThrowIfNull(document);
		ArgumentNullException.ThrowIfNull(settings);

		var enabledSettings = settings
			.Where(static layer => layer.IsEnabled && layer.HeightMm > 0f)
			.ToArray();
		if (enabledSettings.Length == 0 || document.Regions.Count == 0)
		{
			return CreateEmptyMesh();
		}

		var triangles = new List<MeshTriangle>(4096);
		foreach (var region in document.Regions)
		{
			if (!TryResolveHeight(region, enabledSettings, out var color, out var heightMm))
			{
				continue;
			}

			AppendRegionMesh(triangles, region, color, heightMm);
		}

		return triangles.Count == 0 ? CreateEmptyMesh() : new TriangleMesh(triangles, CalculateBounds(triangles));
	}

	private static bool TryResolveHeight(
		SvgPolygonRegion region,
		IReadOnlyList<ExtrusionLayerSettings> settings,
		out RgbaColor color,
		out float heightMm)
	{
		if (region.IsTransparentRegion)
		{
			foreach (var setting in settings)
			{
				if (setting.Color.A != 0)
				{
					continue;
				}

				color = setting.Color;
				heightMm = setting.HeightMm;
				return true;
			}

			color = default;
			heightMm = 0f;
			return false;
		}

		var bestDistance = float.MaxValue;
		ExtrusionLayerSettings? bestMatch = null;
		foreach (var setting in settings)
		{
			if (setting.Color.A == 0)
			{
				continue;
			}

			var distance = CalculateColorDistance(region.Color, setting.Color);
			if (distance > ColorMergeDistance || distance >= bestDistance)
			{
				continue;
			}

			bestDistance = distance;
			bestMatch = setting;
		}

		if (bestDistance == float.MaxValue || bestMatch is null)
		{
			color = default;
			heightMm = 0f;
			return false;
		}

		color = bestMatch.Color;
		heightMm = bestMatch.HeightMm;
		return true;
	}

	private static void AppendRegionMesh(List<MeshTriangle> triangles, SvgPolygonRegion region, RgbaColor color, float heightMm)
	{
		var mergedPolygon = MergeHoles(region.OuterContour, region.Holes);
		if (mergedPolygon.Count < 3)
		{
			return;
		}

		var indices = Triangulate(mergedPolygon);
		for (var index = 0; index < indices.Count; index += 3)
		{
			var a = mergedPolygon[indices[index]];
			var b = mergedPolygon[indices[index + 1]];
			var c = mergedPolygon[indices[index + 2]];
			triangles.Add(new MeshTriangle(a.X, a.Y, heightMm, b.X, b.Y, heightMm, c.X, c.Y, heightMm, color));
			triangles.Add(new MeshTriangle(a.X, a.Y, 0f, c.X, c.Y, 0f, b.X, b.Y, 0f, color));
		}

		AppendContourWalls(triangles, region.OuterContour, color, heightMm);
		foreach (var hole in region.Holes)
		{
			AppendContourWalls(triangles, hole, color, heightMm);
		}
	}

	private static void AppendContourWalls(List<MeshTriangle> triangles, IReadOnlyList<SKPoint> contour, RgbaColor color, float heightMm)
	{
		for (var index = 0; index < contour.Count; index++)
		{
			var current = contour[index];
			var next = contour[(index + 1) % contour.Count];
			triangles.Add(new MeshTriangle(current.X, current.Y, 0f, next.X, next.Y, 0f, next.X, next.Y, heightMm, color));
			triangles.Add(new MeshTriangle(current.X, current.Y, 0f, next.X, next.Y, heightMm, current.X, current.Y, heightMm, color));
		}
	}

	private static List<SKPoint> MergeHoles(IReadOnlyList<SKPoint> outer, IReadOnlyList<IReadOnlyList<SKPoint>> holes)
	{
		var polygon = outer.ToList();
		foreach (var hole in holes.Where(static candidate => candidate.Count >= 3))
		{
			var holeIndex = FindRightmostIndex(hole);
			var holePoint = hole[holeIndex];
			if (!TryFindBridgeVertex(polygon, hole, holePoint, out var bridgeIndex))
			{
				continue;
			}

			var merged = new List<SKPoint>(polygon.Count + hole.Count + 2);
			for (var index = 0; index <= bridgeIndex; index++)
			{
				merged.Add(polygon[index]);
			}

			merged.Add(holePoint);
			for (var step = 1; step < hole.Count; step++)
			{
				var sourceIndex = (holeIndex - step + hole.Count) % hole.Count;
				merged.Add(hole[sourceIndex]);
			}

			merged.Add(holePoint);
			merged.Add(polygon[bridgeIndex]);
			for (var index = bridgeIndex + 1; index < polygon.Count; index++)
			{
				merged.Add(polygon[index]);
			}

			polygon = RemoveSequentialDuplicates(merged);
		}

		return polygon;
	}

	private static bool TryFindBridgeVertex(
		IReadOnlyList<SKPoint> outer,
		IReadOnlyList<SKPoint> hole,
		SKPoint holePoint,
		out int bridgeIndex)
	{
		bridgeIndex = -1;
		var bestDistance = float.MaxValue;

		for (var index = 0; index < outer.Count; index++)
		{
			var candidate = outer[index];
			if (!IsVisibleBridge(candidate, holePoint, outer, hole))
			{
				continue;
			}

			var distance = Distance(candidate, holePoint);
			if (distance >= bestDistance)
			{
				continue;
			}

			bestDistance = distance;
			bridgeIndex = index;
		}

		return bridgeIndex >= 0;
	}

	private static bool IsVisibleBridge(
		SKPoint outerPoint,
		SKPoint holePoint,
		IReadOnlyList<SKPoint> outer,
		IReadOnlyList<SKPoint> hole)
	{
		if (MathF.Abs(outerPoint.X - holePoint.X) < Epsilon && MathF.Abs(outerPoint.Y - holePoint.Y) < Epsilon)
		{
			return false;
		}

		var midpoint = new SKPoint((outerPoint.X + holePoint.X) * 0.5f, (outerPoint.Y + holePoint.Y) * 0.5f);
		if (!ContainsPoint(outer, midpoint) || ContainsPoint(hole, midpoint))
		{
			return false;
		}

		return !IntersectsAnyEdge(outerPoint, holePoint, outer) && !IntersectsAnyEdge(outerPoint, holePoint, hole);
	}

	private static bool IntersectsAnyEdge(SKPoint a, SKPoint b, IReadOnlyList<SKPoint> polygon)
	{
		for (var index = 0; index < polygon.Count; index++)
		{
			var c = polygon[index];
			var d = polygon[(index + 1) % polygon.Count];
			if (SharesEndpoint(a, b, c, d))
			{
				continue;
			}

			if (SegmentsIntersect(a, b, c, d))
			{
				return true;
			}
		}

		return false;
	}

	private static List<int> Triangulate(IReadOnlyList<SKPoint> polygon)
	{
		var indices = Enumerable.Range(0, polygon.Count).ToList();
		var triangles = new List<int>((polygon.Count - 2) * 3);
		var guard = polygon.Count * polygon.Count;

		while (indices.Count > 2 && guard-- > 0)
		{
			var earFound = false;
			for (var index = 0; index < indices.Count; index++)
			{
				var previous = indices[(index - 1 + indices.Count) % indices.Count];
				var current = indices[index];
				var next = indices[(index + 1) % indices.Count];
				if (!IsEar(previous, current, next, indices, polygon))
				{
					continue;
				}

				triangles.Add(previous);
				triangles.Add(current);
				triangles.Add(next);
				indices.RemoveAt(index);
				earFound = true;
				break;
			}

			if (!earFound)
			{
				break;
			}
		}

		return triangles;
	}

	private static bool IsEar(int previous, int current, int next, IReadOnlyList<int> polygonIndices, IReadOnlyList<SKPoint> polygon)
	{
		var a = polygon[previous];
		var b = polygon[current];
		var c = polygon[next];
		if (Cross(a, b, c) <= Epsilon)
		{
			return false;
		}

		for (var index = 0; index < polygonIndices.Count; index++)
		{
			var candidateIndex = polygonIndices[index];
			if (candidateIndex == previous || candidateIndex == current || candidateIndex == next)
			{
				continue;
			}

			if (PointInTriangle(polygon[candidateIndex], a, b, c))
			{
				return false;
			}
		}

		return true;
	}

	private static bool PointInTriangle(SKPoint point, SKPoint a, SKPoint b, SKPoint c)
	{
		var ab = Cross(a, b, point);
		var bc = Cross(b, c, point);
		var ca = Cross(c, a, point);
		var hasNegative = ab < -Epsilon || bc < -Epsilon || ca < -Epsilon;
		var hasPositive = ab > Epsilon || bc > Epsilon || ca > Epsilon;
		return !(hasNegative && hasPositive);
	}

	private static List<SKPoint> RemoveSequentialDuplicates(List<SKPoint> points)
	{
		var result = new List<SKPoint>(points.Count);
		foreach (var point in points)
		{
			if (result.Count == 0 || Distance(result[^1], point) > Epsilon)
			{
				result.Add(point);
			}
		}

		if (result.Count > 1 && Distance(result[0], result[^1]) < Epsilon)
		{
			result.RemoveAt(result.Count - 1);
		}

		return result;
	}

	private static int FindRightmostIndex(IReadOnlyList<SKPoint> polygon)
	{
		var bestIndex = 0;
		for (var index = 1; index < polygon.Count; index++)
		{
			var current = polygon[index];
			var best = polygon[bestIndex];
			if (current.X > best.X || (MathF.Abs(current.X - best.X) < Epsilon && current.Y < best.Y))
			{
				bestIndex = index;
			}
		}

		return bestIndex;
	}

	private static bool ContainsPoint(IReadOnlyList<SKPoint> polygon, SKPoint point)
	{
		var inside = false;
		for (var index = 0; index < polygon.Count; index++)
		{
			var current = polygon[index];
			var next = polygon[(index + 1) % polygon.Count];
			var intersects = ((current.Y > point.Y) != (next.Y > point.Y)) &&
				(point.X < (((next.X - current.X) * (point.Y - current.Y)) / ((next.Y - current.Y) + Epsilon)) + current.X);
			if (intersects)
			{
				inside = !inside;
			}
		}

		return inside;
	}

	private static bool SegmentsIntersect(SKPoint a, SKPoint b, SKPoint c, SKPoint d)
	{
		var ab1 = Cross(a, b, c);
		var ab2 = Cross(a, b, d);
		var cd1 = Cross(c, d, a);
		var cd2 = Cross(c, d, b);
		return (ab1 * ab2 < -Epsilon) && (cd1 * cd2 < -Epsilon);
	}

	private static bool SharesEndpoint(SKPoint a, SKPoint b, SKPoint c, SKPoint d)
	{
		return Distance(a, c) < Epsilon || Distance(a, d) < Epsilon || Distance(b, c) < Epsilon || Distance(b, d) < Epsilon;
	}

	private static float Cross(SKPoint a, SKPoint b, SKPoint c)
	{
		return ((b.X - a.X) * (c.Y - a.Y)) - ((b.Y - a.Y) * (c.X - a.X));
	}

	private static float Distance(SKPoint left, SKPoint right)
	{
		var dx = left.X - right.X;
		var dy = left.Y - right.Y;
		return MathF.Sqrt((dx * dx) + (dy * dy));
	}

	private static float CalculateColorDistance(RgbaColor left, RgbaColor right)
	{
		var dr = left.R - right.R;
		var dg = left.G - right.G;
		var db = left.B - right.B;
		return MathF.Sqrt((dr * dr) + (dg * dg) + (db * db));
	}

	private static MeshBounds CalculateBounds(IReadOnlyList<MeshTriangle> triangles)
	{
		var minX = float.MaxValue;
		var minY = float.MaxValue;
		var minZ = float.MaxValue;
		var maxX = float.MinValue;
		var maxY = float.MinValue;
		var maxZ = float.MinValue;

		foreach (var triangle in triangles)
		{
			minX = MathF.Min(minX, MathF.Min(triangle.Ax, MathF.Min(triangle.Bx, triangle.Cx)));
			minY = MathF.Min(minY, MathF.Min(triangle.Ay, MathF.Min(triangle.By, triangle.Cy)));
			minZ = MathF.Min(minZ, MathF.Min(triangle.Az, MathF.Min(triangle.Bz, triangle.Cz)));
			maxX = MathF.Max(maxX, MathF.Max(triangle.Ax, MathF.Max(triangle.Bx, triangle.Cx)));
			maxY = MathF.Max(maxY, MathF.Max(triangle.Ay, MathF.Max(triangle.By, triangle.Cy)));
			maxZ = MathF.Max(maxZ, MathF.Max(triangle.Az, MathF.Max(triangle.Bz, triangle.Cz)));
		}

		return new MeshBounds(minX, minY, minZ, maxX, maxY, maxZ);
	}

	private static TriangleMesh CreateEmptyMesh()
	{
		return new TriangleMesh([], new MeshBounds(0f, 0f, 0f, 1f, 1f, 1f));
	}
}
