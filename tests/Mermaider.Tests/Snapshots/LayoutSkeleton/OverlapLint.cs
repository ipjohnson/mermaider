using System.Globalization;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mermaider.Text;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Overlap lint for drawn class and sequence diagrams: labels on labels, labels on class or actor boxes, notes on
/// anything, sibling groups (namespaces, sequence boxes) that intersect, and boxes outside the canvas. It runs on
/// labelled boxes, read from Mermaider's rendered SVG by <see cref="FromMermaiderSvg"/> and from the mermaid.js
/// bounding boxes scripts/mjs-facts.mjs records by <see cref="FromMjs"/>, so both engines are judged by the same rules.
/// </summary>
internal static partial class OverlapLint
{
	private const int TimeoutMs = 2000;

	internal const string Canvas = "canvas";
	internal const string LabelOverLabel = "label-label";
	internal const string LabelOverNode = "label-node";
	internal const string NoteOverlap = "note-overlap";
	internal const string GroupOverlap = "group-overlap";

	internal static readonly string[] AllChecks = [Canvas, LabelOverLabel, LabelOverNode, NoteOverlap, GroupOverlap];

	internal const string Node = "node";
	internal const string Label = "label";
	internal const string Note = "note";
	internal const string Group = "group";

	/// <summary>A drawn element. Groups with the same <paramref name="Parent"/> are siblings.</summary>
	internal sealed record Box(string Kind, string Name, double X0, double Y0, double X1, double Y1, string Parent = "")
	{
		/// <summary>True when the two boxes share more than <paramref name="m"/> on both axes.</summary>
		internal bool Overlaps(Box o, double m = 1) => X0 < o.X1 - m && X1 > o.X0 + m && Y0 < o.Y1 - m && Y1 > o.Y0 + m;

		internal string Show => $"{Kind} '{Name}'";
	}

	internal sealed record Geometry(Box? CanvasBox, IReadOnlyList<Box> Boxes)
	{
		internal static readonly Geometry Empty = new(null, []);
	}

	/// <summary>A finding; <paramref name="Key"/> names the boxes by kind and text, so the two engines' findings can be matched.</summary>
	internal sealed record Finding(string Check, string Key);

	internal static List<Finding> Run(Geometry g)
	{
		var found = new List<Finding>();
		var boxes = g.Boxes;

		if (g.CanvasBox is { } c)
		{
			const double tol = 0.5;
			foreach (var b in boxes.Where(b => b.X0 < c.X0 - tol || b.Y0 < c.Y0 - tol || b.X1 > c.X1 + tol || b.Y1 > c.Y1 + tol))
				found.Add(new Finding(Canvas, b.Show));
		}

		for (var i = 0; i < boxes.Count; i++)
		{
			for (var j = i + 1; j < boxes.Count; j++)
			{
				var (a, b) = (boxes[i], boxes[j]);
				if (Check(a, b) is not { } check || !a.Overlaps(b))
					continue;
				var pair = new[] { a.Show, b.Show }.Order(StringComparer.Ordinal).ToArray();
				found.Add(new Finding(check, $"{pair[0]} / {pair[1]}"));
			}
		}

		return found;
	}

	private static string? Check(Box a, Box b) => (a.Kind, b.Kind) switch
	{
		(Label, Label) => LabelOverLabel,
		(Label, Node) or (Node, Label) => LabelOverNode,
		(Note, Note or Label or Node) or (Label or Node, Note) => NoteOverlap,
		(Group, Group) when a.Parent == b.Parent => GroupOverlap,
		_ => null,
	};

	/// <summary>The findings Mermaider has and mermaid.js does not, matched by check and key as multisets.</summary>
	internal static List<Finding> MermaiderOnly(IReadOnlyList<Finding> mermaider, IReadOnlyList<Finding> mjs)
	{
		var counts = mjs.GroupBy(f => f).ToDictionary(g => g.Key, g => g.Count());
		var result = new List<Finding>();
		foreach (var f in mermaider)
		{
			if (counts.TryGetValue(f, out var n) && n > 0)
				counts[f] = n - 1;
			else
				result.Add(f);
		}

		return result;
	}

	/// <summary>The <c>canvas</c> and <c>bbox</c> lines of a mermaid.js facts file.</summary>
	internal static Geometry FromMjs(IReadOnlyList<string> lines)
	{
		Box? canvas = null;
		var boxes = new List<Box>();
		foreach (var line in lines)
		{
			var p = line.Split('\t');
			if (p[0] == "canvas" && p.Length >= 5)
				canvas = new Box("canvas", string.Empty, D(p[1]), D(p[2]), D(p[3]), D(p[4]));
			else if (p[0] == "bbox" && p.Length >= 7)
				boxes.Add(new Box(p[1], p[2], D(p[3]), D(p[4]), D(p[5]), D(p[6]), p.Length > 7 ? p[7] : string.Empty));
		}

		return new Geometry(canvas, boxes);
	}

	/// <summary>
	/// Boxes of a Mermaider class or sequence SVG: class and actor boxes, namespace and sequence boxes, relation labels and
	/// cardinalities, message labels, block titles and conditions, and notes. Text without a pill is measured with
	/// <see cref="TextMetrics"/> at the px of its <c>--fs-*</c> size.
	/// </summary>
	internal static Geometry FromMermaiderSvg(string svg)
	{
		var root = XDocument.Parse(svg).Root!;
		var vb = ((string?)root.Attribute("viewBox"))?.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
		var canvas = vb is { Length: 4 }
			? new Box("canvas", string.Empty, D(vb[0]), D(vb[1]), D(vb[0]) + D(vb[2]), D(vb[1]) + D(vb[3]))
			: new Box("canvas", string.Empty, 0, 0, D((string?)root.Attribute("width")), D((string?)root.Attribute("height")));

		var boxes = new List<Box>();
		foreach (var g in root.Descendants().Where(e => e.Name.LocalName == "g"))
		{
			var classes = ((string?)g.Attribute("class"))?.Split(' ') ?? [];
			var id = (string?)g.Attribute("data-id") ?? string.Empty;
			if (classes.Contains("class-node"))
			{
				if (Child(g, "circle") is { } circle)
				{
					var (cx, cy, r) = (D(circle, "cx"), D(circle, "cy"), D(circle, "r"));
					boxes.Add(new Box(Node, id, cx - r, cy - r, cx + r, cy + r));
				}
				else if (Child(g, "rect") is { } rect)
					boxes.Add(RectBox(Node, id, rect));
			}
			else if (classes.Contains("actor") && Child(g, "rect") is { } actor)
				boxes.Add(RectBox(Node, id, actor));
			else if ((classes.Contains("ns-box") || classes.Contains("box")) && Child(g, "rect") is { } frame)
				boxes.Add(RectBox(Group, id.Length > 0 ? id : (string?)g.Attribute("data-label") ?? string.Empty, frame));
			else if (classes.Contains("note") && Child(g, "rect") is { } note)
				boxes.Add(RectBox(Note, Text(g), note));
			else if (classes.Contains("edge-label") || classes.Contains("message"))
			{
				foreach (var pill in g.Elements().Where(e => e.Name.LocalName == "rect"))
				{
					var text = pill.ElementsAfterSelf().FirstOrDefault(e => e.Name.LocalName == "text");
					boxes.Add(RectBox(Label, text is null ? string.Empty : Text(text), pill));
				}
			}
			else if (classes.Contains("block"))
			{
				foreach (var text in g.Elements().Where(e => e.Name.LocalName == "text"))
					boxes.Add(TextBox(text));
			}
		}

		return new Geometry(canvas, boxes);
	}

	private static XElement? Child(XElement g, string name) => g.Elements().FirstOrDefault(e => e.Name.LocalName == name);

	private static Box RectBox(string kind, string name, XElement rect)
	{
		var (x, y) = (D(rect, "x"), D(rect, "y"));
		return new Box(kind, name, x, y, x + D(rect, "width"), y + D(rect, "height"));
	}

	/// <summary>A label drawn as bare text, centred on its y; the anchor sets where its x is.</summary>
	private static Box TextBox(XElement text)
	{
		var content = Text(text);
		var px = FontSizePattern().Match((string?)text.Attribute("font-size") ?? string.Empty) is { Success: true } m
			? m.Groups[1].Value switch
			{
				"xs" => 12.0,
				"s" => 14.0,
				"m" => 16.0,
				_ => 18.0,
			}
			: 14.0;
		var weight = int.TryParse((string?)text.Attribute("font-weight"), out var w) ? w : 400;
		var metrics = TextMetrics.MeasureMultiline(content.AsSpan(), px, weight);
		var (x, y) = (D(text, "x"), D(text, "y"));
		var x0 = (string?)text.Attribute("text-anchor") switch
		{
			"start" => x,
			"end" => x - metrics.Width,
			_ => x - (metrics.Width / 2),
		};
		return new Box(Label, content, x0, y - (metrics.Height / 2), x0 + metrics.Width, y + (metrics.Height / 2));
	}

	private static string Text(XElement e) =>
		WhitespacePattern().Replace(string.Join(' ', e.DescendantNodes().OfType<XText>().Select(t => t.Value)), " ").Trim();

	private static double D(XElement e, string attr) => D((string?)e.Attribute(attr));

	private static double D(string? s) => double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var v) ? v : 0;

	[GeneratedRegex(@"var\(--fs-(xs|s|m|l)\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex FontSizePattern();

	[GeneratedRegex(@"\s+", RegexOptions.None, TimeoutMs)]
	private static partial Regex WhitespacePattern();
}
