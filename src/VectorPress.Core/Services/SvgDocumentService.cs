// Loads SVG files, extracts smooth vector regions, and groups them into color layers.
using System.Globalization;
using System.Numerics;
using System.Xml.Linq;
using SkiaSharp;
using VectorPress.Core.Models;

namespace VectorPress.Core.Services;

public sealed class SvgDocumentService
{
	private const float ColorMergeDistance = 60f;
	private const float CurveSegmentLength = 6f;
	private static readonly RgbaColor TransparentRegionColor = new(0, 0, 0, 0);

	private static readonly HashSet<string> ShapeTags =
	[
		"path",
		"rect",
		"circle",
		"ellipse",
		"polygon",
		"polyline",
		"line"
	];

	public SvgDocumentInfo Load(string filePath)
	{
		var rawSvg = File.ReadAllText(filePath);
		var document = XDocument.Parse(rawSvg, LoadOptions.PreserveWhitespace);
		var classStyles = ParseClassStyles(document);
		var extractedPaths = new List<ExtractedPath>(128);
		var rootState = ParseState.Default;

		if (document.Root is not null)
		{
			ExtractPaths(document.Root, rootState, classStyles, extractedPaths);
		}

		var regions = BuildRegions(extractedPaths);
		var colorLayers = BuildColorLayers(regions);

		return new SvgDocumentInfo(
			filePath,
			Path.GetFileName(filePath),
			rawSvg,
			colorLayers,
			regions);
	}

	private static void ExtractPaths(
		XElement element,
		ParseState inheritedState,
		IReadOnlyDictionary<string, Dictionary<string, string>> classStyles,
		List<ExtractedPath> output)
	{
		var state = inheritedState.With(element, classStyles);
		var tagName = element.Name.LocalName;

		if (ShapeTags.Contains(tagName))
		{
			using var sourcePath = CreatePath(element);
			if (sourcePath is not null && !sourcePath.IsEmpty)
			{
				sourcePath.Transform(ToSkMatrix(state.Transform));
				AppendVisibleGeometry(sourcePath, state, output);
			}
		}

		foreach (var child in element.Elements())
		{
			ExtractPaths(child, state, classStyles, output);
		}
	}

	private static void AppendVisibleGeometry(SKPath sourcePath, ParseState state, List<ExtractedPath> output)
	{
		var fillColor = TryParseVisibleColor(state.Fill, state.Opacity * state.FillOpacity);
		if (fillColor is not null)
		{
			var fillPath = new SKPath(sourcePath)
			{
				FillType = state.FillRule
			};
			output.Add(new ExtractedPath(fillColor.Value, fillPath));
		}

		var strokeColor = TryParseVisibleColor(state.Stroke, state.Opacity * state.StrokeOpacity);
		if (strokeColor is null || state.StrokeWidth <= 0f)
		{
			return;
		}

		using var strokePaint = new SKPaint
		{
			Style = SKPaintStyle.Stroke,
			StrokeWidth = state.StrokeWidth,
			StrokeJoin = state.StrokeJoin,
			StrokeCap = state.StrokeCap,
			StrokeMiter = 4f,
			IsAntialias = false
		};
		var strokePath = new SKPath();
		if (strokePaint.GetFillPath(sourcePath, strokePath) && !strokePath.IsEmpty)
		{
			strokePath.FillType = SKPathFillType.Winding;
			output.Add(new ExtractedPath(strokeColor.Value, strokePath));
		}
	}

	private static IReadOnlyList<SvgPolygonRegion> BuildRegions(IReadOnlyList<ExtractedPath> paths)
	{
		var regions = new List<SvgPolygonRegion>(paths.Count * 2);

		foreach (var extracted in paths)
		{
			var contours = FlattenPathContours(extracted.Path);
			if (contours.Count == 0)
			{
				continue;
			}

			var nodes = BuildContourHierarchy(contours);
			foreach (var root in nodes.Where(static node => node.Parent is null))
			{
				CollectRegions(root, extracted.Color, regions);
			}
		}

		return regions;
	}

	private static void CollectRegions(ContourNode node, RgbaColor color, List<SvgPolygonRegion> regions)
	{
		var isTransparentRegion = (node.Depth & 1) == 1;
		var regionColor = isTransparentRegion ? TransparentRegionColor : color;
		var outer = EnsureOrientation(node.Points, clockwise: false);
		var holes = node.Children
			.Select(static child => (IReadOnlyList<SKPoint>)EnsureOrientation(child.Points, clockwise: true))
			.Where(static hole => hole.Count >= 3)
			.ToArray();

		if (outer.Count >= 3)
		{
			regions.Add(new SvgPolygonRegion(regionColor, outer, holes, isTransparentRegion));
		}

		foreach (var child in node.Children)
		{
			CollectRegions(child, color, regions);
		}
	}

	private static IReadOnlyList<SvgColorLayer> BuildColorLayers(IReadOnlyList<SvgPolygonRegion> regions)
	{
		var transparentCount = regions.Count(static region => region.IsTransparentRegion);
		var visibleLayers = regions
			.Where(static region => !region.IsTransparentRegion)
			.GroupBy(static region => region.Color)
			.Select(static group => new SvgColorLayer(group.Key, group.Count()))
			.ToArray();

		var mergedVisibleLayers = MergeSimilarColors(visibleLayers).ToList();
		if (transparentCount > 0)
		{
			mergedVisibleLayers.Add(new SvgColorLayer(TransparentRegionColor, transparentCount, true));
		}

		return mergedVisibleLayers
			.OrderByDescending(static layer => layer.ShapeCount)
			.ThenBy(static layer => layer.IsTransparentRegion ? 1 : 0)
			.ThenBy(static layer => layer.Color.R)
			.ThenBy(static layer => layer.Color.G)
			.ThenBy(static layer => layer.Color.B)
			.ToArray();
	}

	private static IReadOnlyList<List<SKPoint>> FlattenPathContours(SKPath path)
	{
		var contours = new List<List<SKPoint>>();
		using var iterator = path.CreateRawIterator();
		Span<SKPoint> points = stackalloc SKPoint[4];
		List<SKPoint>? contour = null;

		while (true)
		{
			var verb = iterator.Next(points);
			switch (verb)
			{
				case SKPathVerb.Move:
					FinishContour(contours, contour);
					contour = [ToCartesian(points[0])];
					break;
				case SKPathVerb.Line:
					contour ??= [ToCartesian(points[0])];
					AddPoint(contour, ToCartesian(points[1]));
					break;
				case SKPathVerb.Quad:
					contour ??= [ToCartesian(points[0])];
					AppendQuadratic(contour, ToCartesian(points[0]), ToCartesian(points[1]), ToCartesian(points[2]));
					break;
				case SKPathVerb.Conic:
					contour ??= [ToCartesian(points[0])];
					AppendConic(contour, ToCartesian(points[0]), ToCartesian(points[1]), ToCartesian(points[2]), iterator.ConicWeight());
					break;
				case SKPathVerb.Cubic:
					contour ??= [ToCartesian(points[0])];
					AppendCubic(contour, ToCartesian(points[0]), ToCartesian(points[1]), ToCartesian(points[2]), ToCartesian(points[3]));
					break;
				case SKPathVerb.Close:
					FinishContour(contours, contour);
					contour = null;
					break;
				case SKPathVerb.Done:
					FinishContour(contours, contour);
					return contours;
			}
		}
	}

	private static void FinishContour(List<List<SKPoint>> contours, List<SKPoint>? contour)
	{
		if (contour is null)
		{
			return;
		}

		if (contour.Count > 1 && NearlyEqual(contour[0], contour[^1]))
		{
			contour.RemoveAt(contour.Count - 1);
		}

		if (contour.Count >= 3 && MathF.Abs(SignedArea(contour)) > 0.0001f)
		{
			contours.Add(contour);
		}
	}

	private static void AppendQuadratic(List<SKPoint> contour, SKPoint p0, SKPoint p1, SKPoint p2)
	{
		var segments = EstimateSegments(p0, p1, p2);
		for (var step = 1; step <= segments; step++)
		{
			var t = step / (float)segments;
			var omt = 1f - t;
			AddPoint(contour, new SKPoint(
				(omt * omt * p0.X) + (2f * omt * t * p1.X) + (t * t * p2.X),
				(omt * omt * p0.Y) + (2f * omt * t * p1.Y) + (t * t * p2.Y)));
		}
	}

	private static void AppendConic(List<SKPoint> contour, SKPoint p0, SKPoint p1, SKPoint p2, float weight)
	{
		var segments = EstimateSegments(p0, p1, p2);
		for (var step = 1; step <= segments; step++)
		{
			var t = step / (float)segments;
			var omt = 1f - t;
			var denominator = (omt * omt) + (2f * weight * omt * t) + (t * t);
			AddPoint(contour, new SKPoint(
				((omt * omt * p0.X) + (2f * weight * omt * t * p1.X) + (t * t * p2.X)) / denominator,
				((omt * omt * p0.Y) + (2f * weight * omt * t * p1.Y) + (t * t * p2.Y)) / denominator));
		}
	}

	private static void AppendCubic(List<SKPoint> contour, SKPoint p0, SKPoint p1, SKPoint p2, SKPoint p3)
	{
		var length = Distance(p0, p1) + Distance(p1, p2) + Distance(p2, p3);
		var segments = Math.Clamp((int)MathF.Ceiling(length / CurveSegmentLength), 8, 96);
		for (var step = 1; step <= segments; step++)
		{
			var t = step / (float)segments;
			var omt = 1f - t;
			var omt2 = omt * omt;
			var omt3 = omt2 * omt;
			var t2 = t * t;
			var t3 = t2 * t;
			AddPoint(contour, new SKPoint(
				(omt3 * p0.X) + (3f * omt2 * t * p1.X) + (3f * omt * t2 * p2.X) + (t3 * p3.X),
				(omt3 * p0.Y) + (3f * omt2 * t * p1.Y) + (3f * omt * t2 * p2.Y) + (t3 * p3.Y)));
		}
	}

	private static int EstimateSegments(SKPoint p0, SKPoint p1, SKPoint p2)
	{
		var length = Distance(p0, p1) + Distance(p1, p2);
		return Math.Clamp((int)MathF.Ceiling(length / CurveSegmentLength), 6, 72);
	}

	private static void AddPoint(List<SKPoint> contour, SKPoint point)
	{
		if (contour.Count == 0 || !NearlyEqual(contour[^1], point))
		{
			contour.Add(point);
		}
	}

	private static List<ContourNode> BuildContourHierarchy(IReadOnlyList<List<SKPoint>> contours)
	{
		var nodes = contours
			.Select(static points => new ContourNode(points))
			.OrderByDescending(static node => MathF.Abs(node.Area))
			.ToList();

		for (var index = 0; index < nodes.Count; index++)
		{
			var node = nodes[index];
			for (var parentIndex = index - 1; parentIndex >= 0; parentIndex--)
			{
				var candidate = nodes[parentIndex];
				if (!ContainsPoint(candidate.Points, node.Points[0]))
				{
					continue;
				}

				node.Parent = candidate;
				candidate.Children.Add(node);
				node.Depth = candidate.Depth + 1;
				break;
			}
		}

		return nodes;
	}

	private static IReadOnlyList<SvgColorLayer> MergeSimilarColors(IReadOnlyList<SvgColorLayer> layers)
	{
		var groups = new List<ColorGroup>(layers.Count);

		foreach (var layer in layers)
		{
			var index = FindMatchingGroup(groups, layer.Color);
			if (index >= 0)
			{
				groups[index].Add(layer);
				continue;
			}

			groups.Add(new ColorGroup(layer));
		}

		return groups
			.Select(static group => group.ToLayer())
			.OrderByDescending(static layer => layer.ShapeCount)
			.ThenBy(static layer => layer.Color.R)
			.ThenBy(static layer => layer.Color.G)
			.ThenBy(static layer => layer.Color.B)
			.ToArray();
	}

	private static int FindMatchingGroup(IReadOnlyList<ColorGroup> groups, RgbaColor color)
	{
		var bestIndex = -1;
		var bestDistance = float.MaxValue;

		for (var i = 0; i < groups.Count; i++)
		{
			var distance = CalculateColorDistance(groups[i].Color, color);
			if (distance > ColorMergeDistance || distance >= bestDistance)
			{
				continue;
			}

			bestDistance = distance;
			bestIndex = i;
		}

		return bestIndex;
	}

	private static float CalculateColorDistance(RgbaColor left, RgbaColor right)
	{
		var dr = left.R - right.R;
		var dg = left.G - right.G;
		var db = left.B - right.B;
		return MathF.Sqrt((dr * dr) + (dg * dg) + (db * db));
	}

	private static Dictionary<string, Dictionary<string, string>> ParseClassStyles(XDocument document)
	{
		var result = new Dictionary<string, Dictionary<string, string>>(StringComparer.Ordinal);

		foreach (var styleElement in document.Descendants().Where(static element => element.Name.LocalName == "style"))
		{
			var content = styleElement.Value;
			if (string.IsNullOrWhiteSpace(content))
			{
				continue;
			}

			foreach (var rule in content.Split('}', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
			{
				var separatorIndex = rule.IndexOf('{', StringComparison.Ordinal);
				if (separatorIndex <= 0)
				{
					continue;
				}

				var selectorBlock = rule[..separatorIndex].Trim();
				var declarationBlock = rule[(separatorIndex + 1)..].Trim();
				var declarations = ParseDeclarations(declarationBlock);
				if (declarations.Count == 0)
				{
					continue;
				}

				foreach (var selector in selectorBlock.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
				{
					if (!selector.StartsWith(".", StringComparison.Ordinal))
					{
						continue;
					}

					result[selector[1..].Trim()] = declarations;
				}
			}
		}

		return result;
	}

	private static Dictionary<string, string> ParseDeclarations(string declarationBlock)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		foreach (var chunk in declarationBlock.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			var separatorIndex = chunk.IndexOf(':', StringComparison.Ordinal);
			if (separatorIndex <= 0)
			{
				continue;
			}

			result[chunk[..separatorIndex].Trim()] = chunk[(separatorIndex + 1)..].Trim();
		}

		return result;
	}

	private static SKPath? CreatePath(XElement element)
	{
		return element.Name.LocalName switch
		{
			"path" => CreatePathElement(element),
			"rect" => CreateRect(element),
			"circle" => CreateCircle(element),
			"ellipse" => CreateEllipse(element),
			"polygon" => CreatePoly(element, true),
			"polyline" => CreatePoly(element, false),
			"line" => CreateLine(element),
			_ => null
		};
	}

	private static SKPath? CreatePathElement(XElement element)
	{
		var data = element.Attribute("d")?.Value;
		return string.IsNullOrWhiteSpace(data) ? null : SKPath.ParseSvgPathData(data);
	}

	private static SKPath? CreateRect(XElement element)
	{
		var width = ParseLength(element.Attribute("width")?.Value);
		var height = ParseLength(element.Attribute("height")?.Value);
		if (width <= 0f || height <= 0f)
		{
			return null;
		}

		var x = ParseLength(element.Attribute("x")?.Value);
		var y = ParseLength(element.Attribute("y")?.Value);
		var rx = ParseLength(element.Attribute("rx")?.Value);
		var ry = ParseLength(element.Attribute("ry")?.Value);
		if (rx > 0f || ry > 0f)
		{
			rx = rx <= 0f ? ry : rx;
			ry = ry <= 0f ? rx : ry;
			return new SKPath().Tap(static (path, state) => path.AddRoundRect(SKRect.Create(state.X, state.Y, state.Width, state.Height), state.Rx, state.Ry), (X: x, Y: y, Width: width, Height: height, Rx: rx, Ry: ry));
		}

		return new SKPath().Tap(static (path, rect) => path.AddRect(rect), SKRect.Create(x, y, width, height));
	}

	private static SKPath? CreateCircle(XElement element)
	{
		var radius = ParseLength(element.Attribute("r")?.Value);
		if (radius <= 0f)
		{
			return null;
		}

		var cx = ParseLength(element.Attribute("cx")?.Value);
		var cy = ParseLength(element.Attribute("cy")?.Value);
		return new SKPath().Tap(static (path, state) => path.AddCircle(state.Cx, state.Cy, state.Radius), (Cx: cx, Cy: cy, Radius: radius));
	}

	private static SKPath? CreateEllipse(XElement element)
	{
		var rx = ParseLength(element.Attribute("rx")?.Value);
		var ry = ParseLength(element.Attribute("ry")?.Value);
		if (rx <= 0f || ry <= 0f)
		{
			return null;
		}

		var cx = ParseLength(element.Attribute("cx")?.Value);
		var cy = ParseLength(element.Attribute("cy")?.Value);
		return new SKPath().Tap(static (path, rect) => path.AddOval(rect), SKRect.Create(cx - rx, cy - ry, rx * 2f, ry * 2f));
	}

	private static SKPath? CreatePoly(XElement element, bool close)
	{
		var points = ParsePointList(element.Attribute("points")?.Value);
		if (points.Count < (close ? 3 : 2))
		{
			return null;
		}

		var path = new SKPath();
		path.MoveTo(points[0]);
		for (var index = 1; index < points.Count; index++)
		{
			path.LineTo(points[index]);
		}

		if (close)
		{
			path.Close();
		}

		return path;
	}

	private static SKPath? CreateLine(XElement element)
	{
		var x1 = ParseLength(element.Attribute("x1")?.Value);
		var y1 = ParseLength(element.Attribute("y1")?.Value);
		var x2 = ParseLength(element.Attribute("x2")?.Value);
		var y2 = ParseLength(element.Attribute("y2")?.Value);
		var path = new SKPath();
		path.MoveTo(x1, y1);
		path.LineTo(x2, y2);
		return path;
	}

	private static List<SKPoint> ParsePointList(string? value)
	{
		var numbers = ParseNumberList(value);
		var points = new List<SKPoint>(numbers.Count / 2);
		for (var index = 0; index + 1 < numbers.Count; index += 2)
		{
			points.Add(new SKPoint(numbers[index], numbers[index + 1]));
		}

		return points;
	}

	private static List<float> ParseNumberList(string? value)
	{
		var result = new List<float>();
		if (string.IsNullOrWhiteSpace(value))
		{
			return result;
		}

		var span = value.AsSpan();
		var start = -1;
		for (var index = 0; index <= span.Length; index++)
		{
			var isNumberChar = index < span.Length && (char.IsDigit(span[index]) || span[index] is '+' or '-' or '.' or 'e' or 'E');
			if (isNumberChar)
			{
				if (start < 0)
				{
					start = index;
				}

				continue;
			}

			if (start < 0)
			{
				continue;
			}

			if (float.TryParse(span[start..index], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
			{
				result.Add(parsed);
			}

			start = -1;
		}

		return result;
	}

	private static float ParseLength(string? value, float fallback = 0f)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return fallback;
		}

		var trimmed = value.Trim();
		var numberLength = 0;
		while (numberLength < trimmed.Length && (char.IsDigit(trimmed[numberLength]) || trimmed[numberLength] is '+' or '-' or '.' or 'e' or 'E'))
		{
			numberLength++;
		}

		if (numberLength == 0)
		{
			return fallback;
		}

		return float.TryParse(trimmed[..numberLength], NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed)
			? parsed
			: fallback;
	}

	private static SKMatrix ToSkMatrix(Matrix3x2 matrix)
	{
		return new SKMatrix
		{
			ScaleX = matrix.M11,
			SkewX = matrix.M21,
			TransX = matrix.M31,
			SkewY = matrix.M12,
			ScaleY = matrix.M22,
			TransY = matrix.M32,
			Persp0 = 0f,
			Persp1 = 0f,
			Persp2 = 1f
		};
	}

	private static RgbaColor? TryParseVisibleColor(string? paintValue, float opacityMultiplier)
	{
		if (string.IsNullOrWhiteSpace(paintValue))
		{
			return null;
		}

		if (paintValue.Equals("none", StringComparison.OrdinalIgnoreCase) ||
			paintValue.StartsWith("url(", StringComparison.OrdinalIgnoreCase))
		{
			return null;
		}

		if (!SKColor.TryParse(paintValue, out var color))
		{
			return null;
		}

		var alpha = (byte)Math.Clamp((int)MathF.Round(color.Alpha * opacityMultiplier), 0, 255);
		if (alpha == 0)
		{
			return null;
		}

		return new RgbaColor(color.Red, color.Green, color.Blue, alpha);
	}

	private static IReadOnlyList<SKPoint> EnsureOrientation(IReadOnlyList<SKPoint> points, bool clockwise)
	{
		var isClockwise = SignedArea(points) < 0f;
		if (isClockwise == clockwise)
		{
			return points.ToArray();
		}

		var reversed = points.ToArray();
		Array.Reverse(reversed);
		return reversed;
	}

	private static float SignedArea(IReadOnlyList<SKPoint> points)
	{
		var area = 0f;
		for (var index = 0; index < points.Count; index++)
		{
			var current = points[index];
			var next = points[(index + 1) % points.Count];
			area += (current.X * next.Y) - (next.X * current.Y);
		}

		return area * 0.5f;
	}

	private static bool ContainsPoint(IReadOnlyList<SKPoint> polygon, SKPoint point)
	{
		var inside = false;
		for (var i = 0; i < polygon.Count; i++)
		{
			var current = polygon[i];
			var next = polygon[(i + 1) % polygon.Count];
			var intersects = ((current.Y > point.Y) != (next.Y > point.Y)) &&
				(point.X < (((next.X - current.X) * (point.Y - current.Y)) / (next.Y - current.Y + 1e-6f)) + current.X);
			if (intersects)
			{
				inside = !inside;
			}
		}

		return inside;
	}

	private static bool NearlyEqual(SKPoint left, SKPoint right)
	{
		return MathF.Abs(left.X - right.X) < 0.001f && MathF.Abs(left.Y - right.Y) < 0.001f;
	}

	private static float Distance(SKPoint left, SKPoint right)
	{
		var dx = left.X - right.X;
		var dy = left.Y - right.Y;
		return MathF.Sqrt((dx * dx) + (dy * dy));
	}

	private static SKPoint ToCartesian(SKPoint point) => new(point.X, -point.Y);

	private readonly record struct ExtractedPath(RgbaColor Color, SKPath Path);

	private sealed class ContourNode(IReadOnlyList<SKPoint> points)
	{
		public IReadOnlyList<SKPoint> Points { get; } = points;

		public float Area { get; } = SignedArea(points);

		public int Depth { get; set; }

		public ContourNode? Parent { get; set; }

		public List<ContourNode> Children { get; } = [];
	}

	private readonly record struct ParseState(
		Matrix3x2 Transform,
		string? Fill,
		string? Stroke,
		float StrokeWidth,
		float Opacity,
		float FillOpacity,
		float StrokeOpacity,
		SKPathFillType FillRule,
		SKStrokeJoin StrokeJoin,
		SKStrokeCap StrokeCap)
	{
		public static ParseState Default => new(
			Matrix3x2.Identity,
			"#000000",
			null,
			1f,
			1f,
			1f,
			1f,
			SKPathFillType.Winding,
			SKStrokeJoin.Miter,
			SKStrokeCap.Butt);

		public ParseState With(XElement element, IReadOnlyDictionary<string, Dictionary<string, string>> classStyles)
		{
			var localDeclarations = ParseDeclarations(element.Attribute("style")?.Value ?? string.Empty);
			var classDeclarations = ResolveClassDeclarations(element, classStyles);

			string? Read(string name)
			{
				if (element.Attribute(name)?.Value is { Length: > 0 } attributeValue)
				{
					return attributeValue;
				}

				if (localDeclarations.TryGetValue(name, out var localValue))
				{
					return localValue;
				}

				return classDeclarations.TryGetValue(name, out var classValue) ? classValue : null;
			}

			var localTransform = ParseTransform(Read("transform"));
			return this with
			{
				Transform = Transform * localTransform,
				Fill = ResolveInherited(Read("fill"), Fill),
				Stroke = ResolveInherited(Read("stroke"), Stroke),
				StrokeWidth = ParseLength(ResolveInherited(Read("stroke-width"), null), StrokeWidth),
				Opacity = Opacity * ParseLength(ResolveInherited(Read("opacity"), null), 1f),
				FillOpacity = FillOpacity * ParseLength(ResolveInherited(Read("fill-opacity"), null), 1f),
				StrokeOpacity = StrokeOpacity * ParseLength(ResolveInherited(Read("stroke-opacity"), null), 1f),
				FillRule = ParseFillRule(ResolveInherited(Read("fill-rule"), null), FillRule),
				StrokeJoin = ParseStrokeJoin(ResolveInherited(Read("stroke-linejoin"), null), StrokeJoin),
				StrokeCap = ParseStrokeCap(ResolveInherited(Read("stroke-linecap"), null), StrokeCap)
			};
		}
	}

	private sealed class ColorGroup
	{
		private long _weightedR;
		private long _weightedG;
		private long _weightedB;
		private long _weightedA;

		public ColorGroup(SvgColorLayer layer)
		{
			Add(layer);
		}

		public int ShapeCount { get; private set; }

		public RgbaColor Color => new(
			(byte)(_weightedR / ShapeCount),
			(byte)(_weightedG / ShapeCount),
			(byte)(_weightedB / ShapeCount),
			(byte)(_weightedA / ShapeCount));

		public void Add(SvgColorLayer layer)
		{
			_weightedR += (long)layer.Color.R * layer.ShapeCount;
			_weightedG += (long)layer.Color.G * layer.ShapeCount;
			_weightedB += (long)layer.Color.B * layer.ShapeCount;
			_weightedA += (long)layer.Color.A * layer.ShapeCount;
			ShapeCount += layer.ShapeCount;
		}

		public SvgColorLayer ToLayer() => new(Color, ShapeCount);
	}

	private static Dictionary<string, string> ResolveClassDeclarations(
		XElement element,
		IReadOnlyDictionary<string, Dictionary<string, string>> classStyles)
	{
		var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
		var classValue = element.Attribute("class")?.Value;
		if (string.IsNullOrWhiteSpace(classValue))
		{
			return result;
		}

		foreach (var className in classValue.Split([" "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
		{
			if (!classStyles.TryGetValue(className, out var declarations))
			{
				continue;
			}

			foreach (var pair in declarations)
			{
				result[pair.Key] = pair.Value;
			}
		}

		return result;
	}

	private static string? ResolveInherited(string? value, string? fallback)
	{
		if (string.IsNullOrWhiteSpace(value) || value.Equals("inherit", StringComparison.OrdinalIgnoreCase))
		{
			return fallback;
		}

		return value;
	}

	private static SKPathFillType ParseFillRule(string? value, SKPathFillType fallback)
	{
		return value?.Trim().ToLowerInvariant() switch
		{
			"evenodd" => SKPathFillType.EvenOdd,
			"nonzero" => SKPathFillType.Winding,
			_ => fallback
		};
	}

	private static SKStrokeJoin ParseStrokeJoin(string? value, SKStrokeJoin fallback)
	{
		return value?.Trim().ToLowerInvariant() switch
		{
			"round" => SKStrokeJoin.Round,
			"bevel" => SKStrokeJoin.Bevel,
			"miter" => SKStrokeJoin.Miter,
			_ => fallback
		};
	}

	private static SKStrokeCap ParseStrokeCap(string? value, SKStrokeCap fallback)
	{
		return value?.Trim().ToLowerInvariant() switch
		{
			"round" => SKStrokeCap.Round,
			"square" => SKStrokeCap.Square,
			"butt" => SKStrokeCap.Butt,
			_ => fallback
		};
	}

	private static Matrix3x2 ParseTransform(string? value)
	{
		if (string.IsNullOrWhiteSpace(value))
		{
			return Matrix3x2.Identity;
		}

		var remaining = value.AsSpan().Trim();
		var matrix = Matrix3x2.Identity;

		while (!remaining.IsEmpty)
		{
			var openIndex = remaining.IndexOf('(');
			var closeIndex = remaining.IndexOf(')');
			if (openIndex <= 0 || closeIndex <= openIndex)
			{
				break;
			}

			var name = remaining[..openIndex].Trim().ToString().ToLowerInvariant();
			var args = ParseNumberList(remaining[(openIndex + 1)..closeIndex].ToString());
			matrix *= name switch
			{
				"matrix" when args.Count >= 6 => new Matrix3x2(args[0], args[1], args[2], args[3], args[4], args[5]),
				"translate" => Matrix3x2.CreateTranslation(args.ElementAtOrDefault(0), args.Count > 1 ? args[1] : 0f),
				"scale" => Matrix3x2.CreateScale(args.ElementAtOrDefault(0, 1f), args.Count > 1 ? args[1] : args.ElementAtOrDefault(0, 1f)),
				"rotate" => ParseRotation(args),
				"skewx" when args.Count >= 1 => Matrix3x2.CreateSkew(MathF.PI * args[0] / 180f, 0f),
				"skewy" when args.Count >= 1 => Matrix3x2.CreateSkew(0f, MathF.PI * args[0] / 180f),
				_ => Matrix3x2.Identity
			};

			remaining = remaining[(closeIndex + 1)..].Trim();
		}

		return matrix;
	}

	private static Matrix3x2 ParseRotation(IReadOnlyList<float> args)
	{
		if (args.Count == 0)
		{
			return Matrix3x2.Identity;
		}

		var radians = MathF.PI * args[0] / 180f;
		if (args.Count < 3)
		{
			return Matrix3x2.CreateRotation(radians);
		}

		var center = new Vector2(args[1], args[2]);
		return Matrix3x2.CreateTranslation(-center) *
			Matrix3x2.CreateRotation(radians) *
			Matrix3x2.CreateTranslation(center);
	}
}

internal static class SvgDocumentServiceExtensions
{
	public static T Tap<T, TState>(this T value, Action<T, TState> action, TState state)
	{
		action(value, state);
		return value;
	}

	public static float ElementAtOrDefault(this IReadOnlyList<float> values, int index, float fallback = 0f)
	{
		return index >= 0 && index < values.Count ? values[index] : fallback;
	}
}
