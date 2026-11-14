using System.Globalization;
using System.Text.RegularExpressions;
using AwesomeAssertions;
using Mermaider.Rendering;
using Mermaider.Text;

namespace Mermaider.Tests.Rendering;

/// <summary>
/// Edges into and out of the shapes whose outline is not their bounding box end on the outline the SVG draws. The
/// outline is read back from the node's own SVG elements. Text and the open side of a brace draw nothing, so an end
/// there sits on the label's box padded by 7.5px, as mermaid.js's text box is.
/// </summary>
public partial class ShapeEdgeEndpointTests
{
	private const double Tolerance = 0.75;
	private const double LabelPadding = 7.5;

	public static IEnumerable<Func<(string Direction, string Shape)>> Cases()
	{
		foreach (var direction in new[] { "TD", "BT", "LR", "RL" })
		{
			foreach (var shape in new[] { "tri", "flip-tri", "person", "text", "comment", "brace-r", "braces" })
				yield return () => (direction, shape);
		}
	}

	[Test]
	[MethodDataSource(nameof(Cases))]
	public void EdgeEndsLieOnTheDrawnOutline(string direction, string shape)
	{
		var svg = MermaidRenderer.RenderSvg(
			$$"""
			flowchart {{direction}}
			  A --> S@{ shape: {{shape}}, label: "Shape" }
			  S --> B
			"""
		);

		var node = NodeMarkup(svg, "S");
		var outline = DrawnOutline(node);
		outline.AddRange(OpenSides(node, outline));
		outline.Should().NotBeEmpty();

		var into = EdgePoints(svg, "A", "S")[^1];
		var outOf = EdgePoints(svg, "S", "B")[0];
		foreach (var end in new[] { into, outOf })
		{
			DistanceTo(outline, end).Should().BeLessThan(Tolerance, $"the end ({end.X}, {end.Y}) of an edge at {shape} in {direction} lies on its outline");
		}
	}

	private static string NodeMarkup(string svg, string id)
	{
		var start = svg.IndexOf($"<g class=\"node\" data-id=\"{id}\"", StringComparison.Ordinal);
		start.Should().BeGreaterThanOrEqualTo(0);
		return svg[start..svg.IndexOf("</g>", start, StringComparison.Ordinal)];
	}

	private static (double X, double Y)[] EdgePoints(string svg, string from, string to)
	{
		var m = Regex.Match(svg, $"<path class=\"edge\" data-from=\"{from}\" data-to=\"{to}\"[^>]* d=\"([^\"]+)\"", RegexOptions.None, TimeSpan.FromSeconds(2));
		m.Success.Should().BeTrue($"an edge {from} --> {to} is drawn");
		var n = Numbers(m.Groups[1].Value);
		return [(n[0], n[1]), (n[^2], n[^1])];
	}

	/// <summary>The node's paths, circles and rects as polylines.</summary>
	private static List<List<(double X, double Y)>> DrawnOutline(string node)
	{
		var lines = new List<List<(double X, double Y)>>();
		foreach (Match m in PathData().Matches(node))
			lines.AddRange(PathPolylines(m.Groups[1].Value));
		foreach (Match m in CircleElement().Matches(node))
		{
			var (cx, cy, r) = (Num(m.Groups[1].Value), Num(m.Groups[2].Value), Num(m.Groups[3].Value));
			lines.Add([.. Enumerable.Range(0, 361).Select(i => (cx + (r * Math.Cos(i * Math.PI / 180)), cy + (r * Math.Sin(i * Math.PI / 180))))]);
		}
		foreach (Match m in RectElement().Matches(node))
		{
			var (x, y, w, h) = (Num(m.Groups[1].Value), Num(m.Groups[2].Value), Num(m.Groups[3].Value), Num(m.Groups[4].Value));
			var rx = m.Groups[5].Success ? Num(m.Groups[5].Value) : 0;
			var pts = new List<(double X, double Y)>();
			foreach (var (ccx, ccy, a0) in new[]
			{
				(x + w - rx, y + rx, -90.0),
				(x + w - rx, y + h - rx, 0.0),
				(x + rx, y + h - rx, 90.0),
				(x + rx, y + rx, 180.0)
			})
			{
				for (var i = 0; i <= 90; i++)
					pts.Add((ccx + (rx * Math.Cos((a0 + i) * Math.PI / 180)), ccy + (rx * Math.Sin((a0 + i) * Math.PI / 180))));
			}
			pts.Add(pts[0]);
			lines.Add(pts);
		}
		return lines;
	}

	/// <summary>Polylines of a path written with M, L, H, V, Q and Z.</summary>
	private static List<List<(double X, double Y)>> PathPolylines(string d)
	{
		var lines = new List<List<(double X, double Y)>>();
		List<(double X, double Y)> line = [];
		var tokens = PathToken().Matches(d).Select(t => t.Value).ToList();
		var cur = (X: 0.0, Y: 0.0);
		for (var i = 0; i < tokens.Count;)
		{
			switch (tokens[i++])
			{
				case "M":
					if (line.Count > 1)
						lines.Add(line);
					cur = (Num(tokens[i++]), Num(tokens[i++]));
					line = [cur];
					break;
				case "L":
					cur = (Num(tokens[i++]), Num(tokens[i++]));
					line.Add(cur);
					break;
				case "H":
					cur = (Num(tokens[i++]), cur.Y);
					line.Add(cur);
					break;
				case "V":
					cur = (cur.X, Num(tokens[i++]));
					line.Add(cur);
					break;
				case "Q":
					{
						var c = (X: Num(tokens[i++]), Y: Num(tokens[i++]));
						var e = (X: Num(tokens[i++]), Y: Num(tokens[i++]));
						for (var s = 1; s <= 64; s++)
						{
							var t = s / 64.0;
							line.Add(
								(((1 - t) * (1 - t) * cur.X) + (2 * (1 - t) * t * c.X) + (t * t * e.X), ((1 - t) * (1 - t) * cur.Y) + (2 * (1 - t) * t * c.Y) +
									(t * t * e.Y))
							);
						}
						cur = e;
						break;
					}
				case "Z":
					line.Add(line[0]);
					break;
				default:
					throw new InvalidOperationException($"unexpected path command in '{d}'");
			}
		}
		if (line.Count > 1)
			lines.Add(line);
		return lines;
	}

	/// <summary>
	/// Where the shapes that draw nothing on a side are closed. Text: the label's box padded by
	/// <see cref="LabelPadding"/>. Braces: that box's vertical sides, and the lines joining the brace ends at the top and
	/// bottom.
	/// </summary>
	private static List<List<(double X, double Y)>> OpenSides(string node, List<List<(double X, double Y)>> drawn)
	{
		if (!node.Contains("data-shape=\"text\"") && !node.Contains("data-shape=\"brace"))
			return [];
		var m = TextElement().Match(node);
		m.Success.Should().BeTrue();
		var (cx, cy) = (Num(m.Groups[1].Value), Num(m.Groups[2].Value));
		var metrics = TextMetrics.MeasureMultiline("Shape", RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
		var (x0, x1) = (cx - (metrics.Width / 2) - LabelPadding, cx + (metrics.Width / 2) + LabelPadding);
		var (y0, y1) = (cy - (metrics.Height / 2) - LabelPadding, cy + (metrics.Height / 2) + LabelPadding);
		if (drawn.Count == 0)
			return [[(x0, y0), (x1, y0), (x1, y1), (x0, y1), (x0, y0)]];

		var pts = drawn.SelectMany(l => l).ToList();
		var (top, bottom) = (pts.Min(p => p.Y), pts.Max(p => p.Y));
		var (left, right) = (Math.Min(x0, pts.Min(p => p.X)), Math.Max(x1, pts.Max(p => p.X)));
		return [[(x0, top), (x0, bottom)], [(x1, top), (x1, bottom)], [(left, top), (right, top)], [(left, bottom), (right, bottom)]];
	}

	private static double DistanceTo(List<List<(double X, double Y)>> lines, (double X, double Y) p)
	{
		var best = double.MaxValue;
		foreach (var line in lines)
		{
			for (var i = 0; i < line.Count - 1; i++)
				best = Math.Min(best, SegmentDistance(line[i], line[i + 1], p));
		}
		return best;
	}

	private static double SegmentDistance((double X, double Y) a, (double X, double Y) b, (double X, double Y) p)
	{
		var (ex, ey) = (b.X - a.X, b.Y - a.Y);
		var len2 = (ex * ex) + (ey * ey);
		var t = len2 == 0 ? 0 : Math.Clamp((((p.X - a.X) * ex) + ((p.Y - a.Y) * ey)) / len2, 0, 1);
		var (qx, qy) = (a.X + (t * ex) - p.X, a.Y + (t * ey) - p.Y);
		return Math.Sqrt((qx * qx) + (qy * qy));
	}

	private static double[] Numbers(string s) => [.. PathToken().Matches(s).Select(t => t.Value).Where(t => t.Length > 1 || char.IsDigit(t[0])).Select(Num)];

	private static double Num(string s) => double.Parse(s, CultureInfo.InvariantCulture);

	[GeneratedRegex(@"[MLHVQZ]|-?\d+(?:\.\d+)?(?:e-?\d+)?", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex PathToken();

	[GeneratedRegex("<path d=\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex PathData();

	[GeneratedRegex("<circle cx=\"([^\"]+)\" cy=\"([^\"]+)\" r=\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex CircleElement();

	[GeneratedRegex("<rect x=\"([^\"]+)\" y=\"([^\"]+)\" width=\"([^\"]+)\" height=\"([^\"]+)\"(?: rx=\"([^\"]+)\")?", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex RectElement();

	[GeneratedRegex("<text x=\"([^\"]+)\" y=\"([^\"]+)\"", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	private static partial Regex TextElement();
}
