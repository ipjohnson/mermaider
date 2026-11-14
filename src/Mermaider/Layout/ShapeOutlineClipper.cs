using Mermaider.Models;
using Mermaider.Rendering;

namespace Mermaider.Layout;

/// <summary>
/// Moves an edge end from the node's bounding box onto the drawn outline (<see cref="FlowchartShapes.Outline"/>), along
/// the edge's last segment. The router treats every node as its box, so the route itself does not change.
/// </summary>
internal static class ShapeOutlineClipper
{
	internal static void Clip(PositionedGraph positioned)
	{
		var outlines = new Dictionary<string, (PositionedNode Node, IReadOnlyList<IReadOnlyList<(double X, double Y)>> Polygons)>(StringComparer.Ordinal);
		foreach (var n in positioned.Nodes)
		{
			var polygons = n.Asset is { } asset ? NodeAssets.Outline(n, asset) : FlowchartShapes.Outline(n.Shape, n.Label, n.X, n.Y, n.Width, n.Height);
			if (polygons is not null)
				outlines[n.Id] = (n, polygons);
		}
		if (outlines.Count == 0)
			return;

		var edges = (List<PositionedEdge>)positioned.Edges;
		for (var i = 0; i < edges.Count; i++)
		{
			var e = edges[i];
			if (e.Points.Count < 2)
				continue;
			List<Point>? pts = null;
			if (outlines.TryGetValue(e.Source, out var s) && ClipEnd(e.Points, 0, 1, s.Node, s.Polygons) is { } start)
				(pts ??= [.. e.Points])[0] = start;
			if (outlines.TryGetValue(e.Target, out var t) && ClipEnd(e.Points, e.Points.Count - 1, -1, t.Node, t.Polygons) is { } end)
				(pts ??= [.. e.Points])[^1] = end;
			if (pts is not null)
				edges[i] = e with { Points = pts };
		}
	}

	/// <summary>Casts a ray from the end at <paramref name="index"/> into the node, along the segment it ends, onto the outline.</summary>
	private static Point? ClipEnd(IReadOnlyList<Point> pts, int index, int step, PositionedNode n, IReadOnlyList<IReadOnlyList<(double X, double Y)>> polygons)
	{
		var p = pts[index];
		// an end redirected to a subgraph border is not on this node
		const double tolerance = 1.5;
		var onBox = p.X >= n.X - tolerance && p.X <= n.X + n.Width + tolerance && p.Y >= n.Y - tolerance && p.Y <= n.Y + n.Height + tolerance;
		if (!onBox)
			return null;

		Point? from = null;
		for (var i = index + step; i >= 0 && i < pts.Count; i += step)
		{
			if (Math.Abs(pts[i].X - p.X) + Math.Abs(pts[i].Y - p.Y) > 0.01)
			{
				from = pts[i];
				break;
			}
		}
		if (from is not { } f)
			return null;

		var (dx, dy) = (p.X - f.X, p.Y - f.Y);
		var len = Math.Sqrt((dx * dx) + (dy * dy));
		(dx, dy) = (dx / len, dy / len);

		var best = double.MaxValue;
		foreach (var poly in polygons)
		{
			for (var i = 0; i < poly.Count; i++)
			{
				var a = poly[i];
				var b = poly[(i + 1) % poly.Count];
				if (RayHit(p.X, p.Y, dx, dy, a, b) is { } t && t < best)
					best = t;
			}
		}
		if (best == double.MaxValue)
			return null;
		// a vertical or horizontal run keeps its exact cross coordinate, so the route stays orthogonal
		var x = Math.Abs(dx) < 1e-9 ? p.X : Math.Round(p.X + (dx * best), 2);
		var y = Math.Abs(dy) < 1e-9 ? p.Y : Math.Round(p.Y + (dy * best), 2);
		return new Point(x, y);
	}

	/// <summary>Distance along the ray to segment <paramref name="a"/>–<paramref name="b"/>, from a little behind its origin.</summary>
	private static double? RayHit(double px, double py, double dx, double dy, (double X, double Y) a, (double X, double Y) b)
	{
		var (ex, ey) = (b.X - a.X, b.Y - a.Y);
		var denom = (dx * ey) - (dy * ex);
		if (Math.Abs(denom) < 1e-9)
			return null;
		var (wx, wy) = (a.X - px, a.Y - py);
		var t = ((wx * ey) - (wy * ex)) / denom;
		var u = ((wx * dy) - (wy * dx)) / denom;
		return t >= -0.5 && u is >= -1e-9 and <= 1 + 1e-9 ? t : null;
	}
}
