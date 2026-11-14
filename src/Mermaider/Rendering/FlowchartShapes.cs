using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Sizes and outlines of the flowchart shapes mermaid added in 11.3 (<c>A@{ shape: doc }</c>) and of collapsed
/// subgraphs. Every outline takes the fill, stroke and stroke width of the node's colour family, so the shapes follow
/// the style presets like the bracket shapes do.
/// </summary>
internal static class FlowchartShapes
{
	/// <summary>Depth of a document's wavy edge.</summary>
	private const double Wave = 5;

	/// <summary>Offset between the sheets of a stacked shape.</summary>
	private const double Stack = 5;

	/// <summary>Inset of the extra rule in lined, divided and window shapes.</summary>
	private const double Rule = 10;

	/// <summary>Height of a collapsed subgraph's indicator row, below its title.</summary>
	private const double IndicatorRow = 28;

	/// <summary>Part of a person's head radius that overlaps the top of the body.</summary>
	private const double HeadOverlap = 0.27;

	/// <summary>Space between a label and the outline edges clip to on a shape that draws no outline there.</summary>
	private const double OpenSidePadding = 7.5;

	/// <summary>Size of the shapes drawn as markers, without their label.</summary>
	internal static (double Width, double Height)? FixedSize(NodeShape shape) => shape switch
	{
		NodeShape.Hourglass => (36, 36),
		NodeShape.LightningBolt => (32, 48),
		NodeShape.FilledCircle => (16, 16),
		NodeShape.CrossedCircle => (36, 36),
		_ => null,
	};

	/// <summary>Grows the label box (text plus node padding) to the shape's outline.</summary>
	internal static (double Width, double Height) Grow(NodeShape shape, double width, double height) => shape switch
	{
		NodeShape.Document or NodeShape.TaggedDocument or NodeShape.Flag => (width, height + (2 * Wave)),
		NodeShape.LinedDocument => (width + Rule, height + (2 * Wave)),
		NodeShape.StackedDocument => (width + (2 * Stack), height + (2 * Stack) + (2 * Wave)),
		NodeShape.StackedRectangle => (width + (2 * Stack), height + (2 * Stack)),
		NodeShape.LinedRectangle => (width + Rule, height),
		NodeShape.DividedRectangle => (width, height + Rule),
		NodeShape.WindowPane => (width + Rule, height + Rule),
		NodeShape.SlopedRectangle => (width, height + Rule),
		NodeShape.NotchedPentagon or NodeShape.NotchedRectangle => (width + Rule, height),
		NodeShape.Delay or NodeShape.BowTieRectangle or NodeShape.CurvedTrapezoid => (width + 20, height),
		NodeShape.HorizontalCylinder => (width + 14, height),
		NodeShape.LinedCylinder => (width, height + 20),
		NodeShape.DataStore => (width, height),
		NodeShape.Folder => (width, height + 8),
		NodeShape.Bucket => (width, height + 14),
		NodeShape.Console or NodeShape.Browser => (width, height + 14),
		NodeShape.Person => (Math.Max(width, 100), height + ((2 - HeadOverlap) * HeadRadius(Math.Max(width, 100)))),
		NodeShape.Bang => (width + 32, height + 24),
		NodeShape.Cloud => (width + 28, height + 20),
		NodeShape.BraceLeft or NodeShape.BraceRight => (width + 12, height),
		NodeShape.Braces => (width + 24, height),
		NodeShape.Triangle or NodeShape.FlippedTriangle => (width + (height * 1.5), height * 1.5),
		NodeShape.CollapsedGroup => (Math.Max(width, 80), height + IndicatorRow),
		_ => (width, height),
	};

	/// <summary>Where the label's centre sits, relative to the centre of the box.</summary>
	internal static (double Dx, double Dy) LabelOffset(NodeShape shape, double w, double h) => shape switch
	{
		NodeShape.Document or NodeShape.TaggedDocument => (0, -Wave),
		NodeShape.LinedDocument => (Rule / 2, -Wave),
		NodeShape.StackedDocument => (-Stack, 0),
		NodeShape.StackedRectangle => (-Stack, Stack),
		NodeShape.LinedRectangle => (Rule / 2, 0),
		NodeShape.DividedRectangle or NodeShape.SlopedRectangle => (0, Rule / 2),
		NodeShape.WindowPane => (Rule / 2, Rule / 2),
		NodeShape.Folder => (0, 4),
		NodeShape.Bucket => (0, 3),
		NodeShape.Console or NodeShape.Browser => (0, 7),
		NodeShape.Person => (0, (2 - HeadOverlap) * HeadRadius(w) / 2),
		NodeShape.Triangle => (0, h / 6),
		NodeShape.FlippedTriangle => (0, -h / 6),
		NodeShape.CollapsedGroup => (0, -IndicatorRow / 2),
		_ => (0, 0),
	};

	/// <summary>Draws the outline of a shape this class owns. False for the bracket shapes, which SvgRenderer draws.</summary>
	internal static bool TryAppend(
		StringBuilder sb,
		DesignSystem ds,
		ColorFamily family,
		NodeShape shape,
		double x,
		double y,
		double w,
		double h,
		string fill,
		string stroke,
		string sw
	)
	{
		var (r, b, cx, cy) = (x + w, y + h, x + (w / 2), y + (h / 2));
		switch (shape)
		{
			case NodeShape.DataStore:
				Path(sb, new PathData().M(x, y).H(r).V(b).H(x).Z(), fill, "none", sw);
				Path(sb, new PathData().M(x, y).H(r).M(x, b).H(r), "none", stroke, sw);
				return true;
			case NodeShape.Folder:
				{
					var tab = Math.Min(w * 0.4, 48);
					Path(sb, new PathData().M(x, y).H(x + tab).L(x + tab + 6, y + 8).H(r).V(b).H(x).Z(), fill, stroke, sw);
					return true;
				}
			case NodeShape.Bucket:
				{
					const double ry = 6;
					var inset = Math.Min(w * 0.08, 10);
					Path(
						sb,
						new PathData().M(x, y + ry).L(x + inset, b - ry).A((w / 2) - inset, ry, false, x + w - inset, b - ry).L(r, y + ry),
						fill,
						stroke,
						sw
					);
					Ellipse(sb, cx, y + ry, w / 2, ry, fill, stroke, sw);
					return true;
				}
			case NodeShape.Console:
				Rect(sb, x, y, w, h, ds.Spec.NodeRadius, fill, stroke, sw);
				Path(sb, new PathData().M(x, y + 14).H(r).M(x + 8, y + 4).L(x + 12, y + 7).L(x + 8, y + 10).M(x + 14, y + 10).H(x + 19), "none", stroke, sw);
				return true;
			case NodeShape.Browser:
				Rect(sb, x, y, w, h, ds.Spec.NodeRadius, fill, stroke, sw);
				Path(sb, new PathData().M(x, y + 14).H(r), "none", stroke, sw);
				for (var i = 0; i < 3; i++)
					Circle(sb, x + 8 + (i * 7), y + 7, 2, stroke, "none", "0");
				return true;
			case NodeShape.Person:
				{
					var f = PersonFigure.Of(y, w, h);
					Rect(sb, x, f.BodyTop, w, b - f.BodyTop, f.BodyRadius, fill, stroke, sw);
					Circle(sb, cx, f.HeadCy, f.HeadRadius, fill, stroke, sw);
					return true;
				}
			case NodeShape.Bang:
				Path(sb, Star(cx, cy, w / 2, h / 2, 12, 0.78), fill, stroke, sw);
				return true;
			case NodeShape.Cloud:
				Path(sb, Scallops(cx, cy, (w / 2) - 4, (h / 2) - 4, 9), fill, stroke, sw);
				return true;
			case NodeShape.Text:
				return true;
			case NodeShape.NotchedRectangle:
				Path(sb, new PathData().M(x + 12, y).H(r).V(b).H(x).V(y + 12).Z(), fill, stroke, sw);
				return true;
			case NodeShape.LinedRectangle:
				Rect(sb, x, y, w, h, 0, fill, stroke, sw);
				Path(sb, new PathData().M(x + Rule, y).V(b), "none", stroke, sw);
				return true;
			case NodeShape.Hourglass:
				Path(sb, new PathData().M(x, y).H(r).L(x, b).H(r).Z(), fill, stroke, sw);
				return true;
			case NodeShape.BraceLeft:
				Path(sb, Brace(x + 2, y, b, left: true), "none", stroke, sw);
				return true;
			case NodeShape.BraceRight:
				Path(sb, Brace(r - 2, y, b, left: false), "none", stroke, sw);
				return true;
			case NodeShape.Braces:
				Path(sb, Brace(x + 2, y, b, left: true), "none", stroke, sw);
				Path(sb, Brace(r - 2, y, b, left: false), "none", stroke, sw);
				return true;
			case NodeShape.LightningBolt:
				Path(
					sb,
					new PathData().M(x + (w * 0.62), y).L(x, y + (h * 0.56)).H(x + (w * 0.45)).L(x + (w * 0.36), b).L(r, y + (h * 0.42)).H(x + (w * 0.56)).Z(),
					fill,
					stroke,
					sw
				);
				return true;
			case NodeShape.Document:
				Path(sb, Document(x, y, w, h), fill, stroke, sw);
				return true;
			case NodeShape.LinedDocument:
				Path(sb, Document(x, y, w, h), fill, stroke, sw);
				Path(sb, new PathData().M(x + Rule, y).V(b - Wave), "none", stroke, sw);
				return true;
			case NodeShape.TaggedDocument:
				Path(sb, Document(x, y, w, h), fill, stroke, sw);
				Path(sb, new PathData().M(r - 14, b - Wave - 2).L(r, b - Wave - 14), "none", stroke, sw);
				return true;
			case NodeShape.StackedDocument:
				Path(sb, Document(x + (2 * Stack), y, w - (2 * Stack), h - (2 * Stack)), fill, stroke, sw);
				Path(sb, Document(x + Stack, y + Stack, w - (2 * Stack), h - (2 * Stack)), fill, stroke, sw);
				Path(sb, Document(x, y + (2 * Stack), w - (2 * Stack), h - (2 * Stack)), fill, stroke, sw);
				return true;
			case NodeShape.Flag:
				Path(
					sb,
					new PathData()
						.M(x, y + Wave)
						.C(x + (w * 0.25), y + (3 * Wave), x + (w * 0.75), y - Wave, r, y + Wave)
						.V(b - Wave)
						.C(x + (w * 0.75), b - (3 * Wave), x + (w * 0.25), b + Wave, x, b - Wave)
						.Z(),
					fill,
					stroke,
					sw
				);
				return true;
			case NodeShape.Delay:
				Path(sb, new PathData().M(x, y).H(r - (h / 2)).A(h / 2, h / 2, true, r - (h / 2), b).H(x).Z(), fill, stroke, sw);
				return true;
			case NodeShape.HorizontalCylinder:
				{
					const double rx = 7;
					Path(sb, new PathData().M(x + rx, y).H(r - rx).A(rx, h / 2, true, r - rx, b).H(x + rx).A(rx, h / 2, true, x + rx, y).Z(), fill, stroke, sw);
					Ellipse(sb, r - rx, cy, rx, h / 2, fill, stroke, sw);
					return true;
				}
			case NodeShape.LinedCylinder:
				{
					const double ry = 7;
					Path(sb, new PathData().M(x, y + ry).V(b - ry).A(w / 2, ry, false, r, b - ry).V(y + ry), fill, stroke, sw);
					Ellipse(sb, cx, y + ry, w / 2, ry, fill, stroke, sw);
					Path(sb, new PathData().M(x, y + ry + 6).A(w / 2, ry, false, r, y + ry + 6), "none", stroke, sw);
					return true;
				}
			case NodeShape.CurvedTrapezoid:
				Path(sb, new PathData().M(x + (h / 3), y).H(r - (h / 2)).A(h / 2, h / 2, true, r - (h / 2), b).H(x + (h / 3)).L(x, cy).Z(), fill, stroke, sw);
				return true;
			case NodeShape.DividedRectangle:
				Rect(sb, x, y, w, h, 0, fill, stroke, sw);
				Path(sb, new PathData().M(x, y + Rule).H(r), "none", stroke, sw);
				return true;
			case NodeShape.Triangle:
				Path(sb, new PathData().M(cx, y).L(r, b).H(x).Z(), fill, stroke, sw);
				return true;
			case NodeShape.FlippedTriangle:
				Path(sb, new PathData().M(x, y).H(r).L(cx, b).Z(), fill, stroke, sw);
				return true;
			case NodeShape.WindowPane:
				Rect(sb, x, y, w, h, 0, fill, stroke, sw);
				Path(sb, new PathData().M(x + Rule, y).V(b).M(x, y + Rule).H(r), "none", stroke, sw);
				return true;
			case NodeShape.FilledCircle:
				Circle(sb, cx, cy, Math.Min(w, h) / 2, "var(--_line-strong)", "none", "0");
				return true;
			case NodeShape.NotchedPentagon:
				Path(sb, new PathData().M(x + Rule, y).H(r - Rule).L(r, y + Rule).V(b).H(x).V(y + Rule).Z(), fill, stroke, sw);
				return true;
			case NodeShape.SlopedRectangle:
				Path(sb, new PathData().M(x, y + Rule).L(r, y).V(b).H(x).Z(), fill, stroke, sw);
				return true;
			case NodeShape.StackedRectangle:
				Rect(sb, x + (2 * Stack), y, w - (2 * Stack), h - (2 * Stack), 0, fill, stroke, sw);
				Rect(sb, x + Stack, y + Stack, w - (2 * Stack), h - (2 * Stack), 0, fill, stroke, sw);
				Rect(sb, x, y + (2 * Stack), w - (2 * Stack), h - (2 * Stack), 0, fill, stroke, sw);
				return true;
			case NodeShape.BowTieRectangle:
				{
					const double rx = 8;
					Path(sb, new PathData().M(x + rx, y).H(r).A(rx, h / 2, false, r, b).H(x + rx).A(rx, h / 2, true, x + rx, y).Z(), fill, stroke, sw);
					return true;
				}
			case NodeShape.CrossedCircle:
				{
					var radius = Math.Min(w, h) / 2;
					var d = radius * Math.Sqrt(0.5);
					Circle(sb, cx, cy, radius, fill, stroke, sw);
					Path(sb, new PathData().M(cx - d, cy - d).L(cx + d, cy + d).M(cx + d, cy - d).L(cx - d, cy + d), "none", stroke, sw);
					return true;
				}
			case NodeShape.TaggedRectangle:
				Rect(sb, x, y, w, h, 0, fill, stroke, sw);
				Path(sb, new PathData().M(r - 14, b).L(r, b - 14), "none", stroke, sw);
				return true;
			case NodeShape.CollapsedGroup:
				{
					var rule = b - IndicatorRow;
					Rect(sb, x, y, w, h, ds.Spec.NodeRadius, ds.ContainerFill(family, 0), ds.ContainerStroke(family), ds.ContainerStrokeWidth);
					_ = sb
						.Append("\n<line x1=\"")
						.Append(DesignSystem.Num(x + 8))
						.Append("\" y1=\"")
						.Append(DesignSystem.Num(rule))
						.Append("\" x2=\"")
						.Append(DesignSystem.Num(r - 8))
						.Append("\" y2=\"")
						.Append(DesignSystem.Num(rule))
						.Append("\" stroke=\"")
						.Append(family.Edge)
						.Append("\" stroke-width=\"1\" stroke-dasharray=\"")
						.Append(DesignSystem.DashArray)
						.Append("\" />");
					for (var i = -1; i <= 1; i++)
						Circle(sb, cx + (i * 10), rule + (IndicatorRow / 2), 2.5, family.Stroke, "none", "0");
					return true;
				}
			default:
				return false;
		}
	}

	/// <summary>A document: square top, wavy bottom edge.</summary>
	private static PathData Document(double x, double y, double w, double h) =>
		new PathData().M(x, y).H(x + w).V(y + h - Wave).C(x + (w * 0.75), y + h - (3 * Wave), x + (w * 0.25), y + h + Wave, x, y + h - Wave).Z();

	/// <summary>A curly brace standing at <paramref name="edge"/>, its point facing away from the label.</summary>
	private static PathData Brace(double edge, double top, double bottom, bool left)
	{
		var s = left ? 1 : -1;
		var mid = (top + bottom) / 2;
		var arm = edge + (s * 5);
		return new PathData()
			.M(edge + (s * 10), top)
			.Q(arm, top, arm, top + 6)
			.V(mid - 6)
			.Q(arm, mid, edge, mid)
			.Q(arm, mid, arm, mid + 6)
			.V(bottom - 6)
			.Q(arm, bottom, edge + (s * 10), bottom);
	}

	/// <summary>
	/// The outline an edge ending at this shape stops on, as closed polygons in absolute coordinates, as mermaid.js
	/// intersects it. Null when the bounding box is the outline, or when the edge router already handles the shape.
	/// Open sides of the text and brace shapes stop at the label, padded like mermaid's text box.
	/// </summary>
	internal static IReadOnlyList<IReadOnlyList<(double X, double Y)>>? Outline(NodeShape shape, string label, double x, double y, double w, double h)
	{
		var (r, b, cx, cy) = (x + w, y + h, x + (w / 2), y + (h / 2));
		switch (shape)
		{
			case NodeShape.Triangle:
				return [[(cx, y), (r, b), (x, b)]];
			case NodeShape.FlippedTriangle:
				return [[(x, y), (r, y), (cx, b)]];
			case NodeShape.Person:
				{
					var f = PersonFigure.Of(y, w, h);
					return [CirclePoints(cx, f.HeadCy, f.HeadRadius), RoundedRectPoints(x, f.BodyTop, w, b - f.BodyTop, f.BodyRadius)];
				}
			case NodeShape.Text:
				{
					var (lx, ly, lr, lb) = PaddedLabel(label, x, y, w, h);
					return [[(lx, ly), (lr, ly), (lr, lb), (lx, lb)]];
				}
			case NodeShape.BraceLeft:
				{
					var (_, _, lr, _) = PaddedLabel(label, x, y, w, h);
					var pts = BracePoints(x + 2, y, b, left: true);
					pts.Add((lr, b));
					pts.Add((lr, y));
					return [pts];
				}
			case NodeShape.BraceRight:
				{
					var (lx, _, _, _) = PaddedLabel(label, x, y, w, h);
					var pts = BracePoints(r - 2, y, b, left: false);
					pts.Add((lx, b));
					pts.Add((lx, y));
					return [pts];
				}
			case NodeShape.Braces:
				{
					var pts = BracePoints(x + 2, y, b, left: true);
					var right = BracePoints(r - 2, y, b, left: false);
					right.Reverse();
					pts.AddRange(right);
					return [pts];
				}
			default:
				return null;
		}
	}

	/// <summary>The label's box grown by <see cref="OpenSidePadding"/>, kept inside the node.</summary>
	private static (double X, double Y, double Right, double Bottom) PaddedLabel(string label, double x, double y, double w, double h)
	{
		var m = TextMetrics.MeasureMultiline(NodeSizing.WrapLabel(label).AsSpan(), RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
		var (cx, cy) = (x + (w / 2), y + (h / 2));
		var hw = Math.Min(w / 2, (m.Width / 2) + OpenSidePadding);
		var hh = Math.Min(h / 2, (m.Height / 2) + OpenSidePadding);
		return (cx - hw, cy - hh, cx + hw, cy + hh);
	}

	/// <summary>Points along <see cref="Brace"/>, top to bottom.</summary>
	private static List<(double X, double Y)> BracePoints(double edge, double top, double bottom, bool left)
	{
		var s = left ? 1 : -1;
		var mid = (top + bottom) / 2;
		var arm = edge + (s * 5);
		var pts = new List<(double X, double Y)> { (edge + (s * 10), top) };
		Quad(pts, (arm, top), (arm, top + 6));
		pts.Add((arm, mid - 6));
		Quad(pts, (arm, mid), (edge, mid));
		Quad(pts, (arm, mid), (arm, mid + 6));
		pts.Add((arm, bottom - 6));
		Quad(pts, (arm, bottom), (edge + (s * 10), bottom));
		return pts;
	}

	/// <summary>Appends points along a quadratic curve from the last point in <paramref name="pts"/>.</summary>
	private static void Quad(List<(double X, double Y)> pts, (double X, double Y) c, (double X, double Y) end)
	{
		const int steps = 8;
		var start = pts[^1];
		for (var i = 1; i <= steps; i++)
		{
			var t = (double)i / steps;
			var (a, k, e) = ((1 - t) * (1 - t), 2 * (1 - t) * t, t * t);
			pts.Add(((a * start.X) + (k * c.X) + (e * end.X), (a * start.Y) + (k * c.Y) + (e * end.Y)));
		}
	}

	internal static List<(double X, double Y)> CirclePoints(double cx, double cy, double radius)
	{
		const int steps = 48;
		var pts = new List<(double X, double Y)>(steps);
		for (var i = 0; i < steps; i++)
		{
			var a = 2 * Math.PI * i / steps;
			pts.Add((cx + (radius * Math.Cos(a)), cy + (radius * Math.Sin(a))));
		}
		return pts;
	}

	private static List<(double X, double Y)> RoundedRectPoints(double x, double y, double w, double h, double radius)
	{
		const int steps = 8;
		var pts = new List<(double X, double Y)>(4 * (steps + 1));
		(double X, double Y, double Start)[] corners =
		[
			(x + w - radius, y + radius, -Math.PI / 2),
			(x + w - radius, y + h - radius, 0),
			(x + radius, y + h - radius, Math.PI / 2),
			(x + radius, y + radius, Math.PI),
		];
		foreach (var (ccx, ccy, start) in corners)
		{
			for (var i = 0; i <= steps; i++)
			{
				var a = start + (Math.PI / 2 * i / steps);
				pts.Add((ccx + (radius * Math.Cos(a)), ccy + (radius * Math.Sin(a))));
			}
		}
		return pts;
	}

	/// <summary>Head radius of a person <paramref name="w"/> wide, as mermaid.js sizes it.</summary>
	private static double HeadRadius(double w) => Math.Min(Math.Max(w * 0.23, 16), 56);

	/// <summary>A person as mermaid.js draws it: a head circle overlapping the top of a rounded body that holds the label.</summary>
	private readonly record struct PersonFigure(double HeadCy, double HeadRadius, double BodyTop, double BodyRadius)
	{
		internal static PersonFigure Of(double y, double w, double h)
		{
			var head = FlowchartShapes.HeadRadius(w);
			var bodyTop = y + ((2 - HeadOverlap) * head);
			return new PersonFigure(y + head, head, bodyTop, Math.Min(w * 0.177, (y + h - bodyTop) * 0.45));
		}
	}

	/// <summary>A star of <paramref name="points"/> spikes on an ellipse, its valleys at <paramref name="inner"/> of the radius.</summary>
	private static PathData Star(double cx, double cy, double rx, double ry, int points, double inner)
	{
		var d = new PathData();
		for (var i = 0; i < points * 2; i++)
		{
			var a = (Math.PI * i / points) - (Math.PI / 2);
			var k = i % 2 == 0 ? 1 : inner;
			var (px, py) = (cx + (rx * k * Math.Cos(a)), cy + (ry * k * Math.Sin(a)));
			_ = i == 0 ? d.M(px, py) : d.L(px, py);
		}
		return d.Z();
	}

	/// <summary>A cloud: <paramref name="bumps"/> outward arcs between points on an ellipse.</summary>
	private static PathData Scallops(double cx, double cy, double rx, double ry, int bumps)
	{
		var d = new PathData();

		(double X, double Y) Point(int i)
		{
			var a = (2 * Math.PI * i / bumps) - (Math.PI / 2);
			return (cx + (rx * Math.Cos(a)), cy + (ry * Math.Sin(a)));
		}

		var (sx, sy) = Point(0);
		_ = d.M(sx, sy);
		for (var i = 1; i <= bumps; i++)
		{
			var (px, py) = Point(i);
			var (qx, qy) = Point(i - 1);
			var chord = Math.Sqrt(((px - qx) * (px - qx)) + ((py - qy) * (py - qy)));
			_ = d.A(chord * 0.6, chord * 0.6, true, px, py);
		}
		return d.Z();
	}

	private static void Path(StringBuilder sb, PathData d, string fill, string stroke, string sw) =>
		sb
			.Append("\n<path d=\"")
			.Append(d)
			.Append("\" fill=\"")
			.Append(fill)
			.Append("\" stroke=\"")
			.Append(stroke)
			.Append("\" stroke-width=\"")
			.Append(sw)
			.Append("\" stroke-linejoin=\"round\" />");

	private static void Rect(StringBuilder sb, double x, double y, double w, double h, double rx, string fill, string stroke, string sw) =>
		sb
			.Append("\n<rect x=\"")
			.Append(DesignSystem.Num(x))
			.Append("\" y=\"")
			.Append(DesignSystem.Num(y))
			.Append("\" width=\"")
			.Append(DesignSystem.Num(w))
			.Append("\" height=\"")
			.Append(DesignSystem.Num(h))
			.Append("\" rx=\"")
			.Append(DesignSystem.Num(rx))
			.Append("\" fill=\"")
			.Append(fill)
			.Append("\" stroke=\"")
			.Append(stroke)
			.Append("\" stroke-width=\"")
			.Append(sw)
			.Append("\" />");

	private static void Circle(StringBuilder sb, double cx, double cy, double r, string fill, string stroke, string sw) =>
		sb
			.Append("\n<circle cx=\"")
			.Append(DesignSystem.Num(cx))
			.Append("\" cy=\"")
			.Append(DesignSystem.Num(cy))
			.Append("\" r=\"")
			.Append(DesignSystem.Num(r))
			.Append("\" fill=\"")
			.Append(fill)
			.Append("\" stroke=\"")
			.Append(stroke)
			.Append("\" stroke-width=\"")
			.Append(sw)
			.Append("\" />");

	private static void Ellipse(StringBuilder sb, double cx, double cy, double rx, double ry, string fill, string stroke, string sw) =>
		sb
			.Append("\n<ellipse cx=\"")
			.Append(DesignSystem.Num(cx))
			.Append("\" cy=\"")
			.Append(DesignSystem.Num(cy))
			.Append("\" rx=\"")
			.Append(DesignSystem.Num(rx))
			.Append("\" ry=\"")
			.Append(DesignSystem.Num(ry))
			.Append("\" fill=\"")
			.Append(fill)
			.Append("\" stroke=\"")
			.Append(stroke)
			.Append("\" stroke-width=\"")
			.Append(sw)
			.Append("\" />");

	/// <summary>SVG path data, written with three decimals at most.</summary>
	private sealed class PathData
	{
		private readonly StringBuilder _d = new();

		private PathData Cmd(char c, params ReadOnlySpan<double> values)
		{
			if (_d.Length > 0)
				_ = _d.Append(' ');
			_ = _d.Append(c);
			for (var i = 0; i < values.Length; i++)
				_ = _d.Append(i == 0 ? "" : i % 2 == 0 ? " " : ",").Append(DesignSystem.Num(values[i]));
			return this;
		}

		public PathData M(double x, double y) => Cmd('M', x, y);

		public PathData L(double x, double y) => Cmd('L', x, y);

		public PathData H(double x) => Cmd('H', x);

		public PathData V(double y) => Cmd('V', y);

		public PathData Q(double x1, double y1, double x, double y) => Cmd('Q', x1, y1, x, y);

		public PathData C(double x1, double y1, double x2, double y2, double x, double y) => Cmd('C', x1, y1, x2, y2, x, y);

		/// <summary>An elliptical arc; <paramref name="clockwise"/> is SVG's sweep flag.</summary>
		public PathData A(double rx, double ry, bool clockwise, double x, double y)
		{
			_ = _d
				.Append(" A")
				.Append(DesignSystem.Num(rx))
				.Append(',')
				.Append(DesignSystem.Num(ry))
				.Append(" 0 0 ")
				.Append(clockwise ? '1' : '0')
				.Append(' ')
				.Append(DesignSystem.Num(x))
				.Append(',')
				.Append(DesignSystem.Num(y));
			return this;
		}

		public PathData Z() => Cmd('Z');

		public override string ToString() => _d.ToString();
	}
}
