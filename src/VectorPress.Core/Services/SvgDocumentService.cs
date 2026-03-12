using System.Xml.Linq;
using SkiaSharp;
using VectorPress.Core.Models;

namespace VectorPress.Core.Services;

public sealed class SvgDocumentService
{
    private const int ColorMergeDistance = 60;

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
        var layers = ExtractColorLayers(document);

        return new SvgDocumentInfo(
            filePath,
            Path.GetFileName(filePath),
            rawSvg,
            layers);
    }

    private static IReadOnlyList<SvgColorLayer> ExtractColorLayers(XDocument document)
    {
        var classStyles = ParseClassStyles(document);
        var counts = new Dictionary<RgbaColor, int>();

        foreach (var element in document.Descendants())
        {
            if (!ShapeTags.Contains(element.Name.LocalName))
            {
                continue;
            }

            foreach (var color in ResolvePaints(element, classStyles))
            {
                counts.TryGetValue(color, out var existingCount);
                counts[color] = existingCount + 1;
            }
        }

        var exactLayers = counts
            .OrderByDescending(static pair => pair.Value)
            .ThenBy(static pair => pair.Key.R)
            .ThenBy(static pair => pair.Key.G)
            .ThenBy(static pair => pair.Key.B)
            .Select(static pair => new SvgColorLayer(pair.Key, pair.Value))
            .ToArray();

        return MergeSimilarColors(exactLayers);
    }

    private static IEnumerable<RgbaColor> ResolvePaints(
        XElement element,
        IReadOnlyDictionary<string, StylePaint> classStyles)
    {
        var colors = new HashSet<RgbaColor>();
        var fillValue = ResolvePaintValue(element, classStyles, "fill");
        var strokeValue = ResolvePaintValue(element, classStyles, "stroke");

        var fillColor = TryParseVisibleColor(fillValue);
        if (fillColor is not null)
        {
            colors.Add(fillColor.Value);
        }

        var strokeColor = TryParseVisibleColor(strokeValue);
        if (strokeColor is not null)
        {
            colors.Add(strokeColor.Value);
        }

        return colors;
    }

    private static string? ResolvePaintValue(
        XElement element,
        IReadOnlyDictionary<string, StylePaint> classStyles,
        string propertyName)
    {
        for (var current = element; current is not null; current = current.Parent)
        {
            var directAttribute = current.Attribute(propertyName)?.Value;
            if (!string.IsNullOrWhiteSpace(directAttribute))
            {
                return directAttribute;
            }

            var inlineStyle = TryReadStyleValue(current.Attribute("style")?.Value, propertyName);
            if (!string.IsNullOrWhiteSpace(inlineStyle))
            {
                return inlineStyle;
            }

            var classValue = current.Attribute("class")?.Value;
            if (string.IsNullOrWhiteSpace(classValue))
            {
                continue;
            }

            foreach (var className in classValue.Split([" "], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!classStyles.TryGetValue(className, out var stylePaint))
                {
                    continue;
                }

                var classPaint = propertyName.Equals("fill", StringComparison.OrdinalIgnoreCase)
                    ? stylePaint.Fill
                    : stylePaint.Stroke;

                if (!string.IsNullOrWhiteSpace(classPaint))
                {
                    return classPaint;
                }
            }
        }

        return null;
    }

    private static RgbaColor? TryParseVisibleColor(string? paintValue)
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

        return new RgbaColor(color.Red, color.Green, color.Blue, color.Alpha);
    }

    private static Dictionary<string, StylePaint> ParseClassStyles(XDocument document)
    {
        var result = new Dictionary<string, StylePaint>(StringComparer.Ordinal);

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
                if (string.IsNullOrWhiteSpace(selectorBlock) || string.IsNullOrWhiteSpace(declarationBlock))
                {
                    continue;
                }

                var fill = TryReadStyleValue(declarationBlock, "fill");
                var stroke = TryReadStyleValue(declarationBlock, "stroke");

                foreach (var selector in selectorBlock.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    if (!selector.StartsWith(".", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var className = selector[1..].Trim();
                    if (className.Length == 0)
                    {
                        continue;
                    }

                    result[className] = new StylePaint(fill, stroke);
                }
            }
        }

        return result;
    }

    private static string? TryReadStyleValue(string? style, string propertyName)
    {
        if (string.IsNullOrWhiteSpace(style))
        {
            return null;
        }

        foreach (var chunk in style.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separatorIndex = chunk.IndexOf(':', StringComparison.Ordinal);
            if (separatorIndex <= 0)
            {
                continue;
            }

            var key = chunk[..separatorIndex].Trim();
            if (!key.Equals(propertyName, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            return chunk[(separatorIndex + 1)..].Trim();
        }

        return null;
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
        var bestDistance = double.MaxValue;

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

    private static double CalculateColorDistance(RgbaColor left, RgbaColor right)
    {
        var dr = left.R - right.R;
        var dg = left.G - right.G;
        var db = left.B - right.B;
        return Math.Sqrt((dr * dr) + (dg * dg) + (db * db));
    }

    private readonly record struct StylePaint(string? Fill, string? Stroke);

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

        public SvgColorLayer ToLayer()
        {
            return new SvgColorLayer(Color, ShapeCount);
        }
    }
}
