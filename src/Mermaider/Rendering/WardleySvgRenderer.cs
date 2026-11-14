using System.Text;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Wardley map, drawn at mermaid's geometry: a 900 × 600 canvas (or <c>size</c>) with a 48px margin, evolution left to
/// right and visibility bottom to top. Components are page-coloured dots with a caption, anchors bold labels, pipeline
/// parents squares above their pipeline box. Sourcing rings use neutral stages, evolution trends the accent, and
/// accelerators and deaccelerators the success and warning roles.
/// </summary>
internal static class WardleySvgRenderer
{
	internal const double DefaultWidth = 900;
	internal const double DefaultHeight = 600;
	private const double Padding = 48;
	private const double NodeRadius = 6;
	private const double LabelOffset = 8;
	private const double SourcingOffset = 10;
	private const double SquareSize = NodeRadius * 1.6;
	private const double AnnotationRadius = 10;
	private const double ForceWidth = 60;
	private const double ForceHeight = 30;
	private const double ForceHead = 20;

	private static readonly string[] DefaultStages = ["Genesis", "Custom Built", "Product", "Commodity"];

	internal static string Render(WardleyDiagram map, SvgRenderContext context)
	{
		var sb = RenderToBuilder(map, context);
		try
		{
			return sb.ToString();
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	internal static StringBuilder RenderToBuilder(WardleyDiagram map, SvgRenderContext context)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		double width = map.Size?.Width ?? DefaultWidth;
		double height = map.Size?.Height ?? DefaultHeight;
		var chartWidth = width - (Padding * 2);
		var chartHeight = height - (Padding * 2);

		double X(double evolution) => Padding + (evolution / 100 * chartWidth);

		double Y(double visibility) => height - Padding - (visibility / 100 * chartHeight);

		var positions = new Dictionary<string, (double X, double Y)>(StringComparer.Ordinal);
		var byId = new Dictionary<string, WardleyNode>(StringComparer.Ordinal);
		foreach (var node in map.Nodes)
		{
			positions[node.Id] = (X(node.Position.Evolution), Y(node.Position.Visibility));
			byId[node.Id] = node;
		}

		foreach (var pipeline in map.Pipelines)
			if (PipelineBox(pipeline, positions) is { } box && positions.ContainsKey(pipeline.NodeId))
				positions[pipeline.NodeId] = ((box.MinX + box.MaxX) / 2, box.Top - (SquareSize / 6));

		// mermaid clips labels and notes that run past the right edge; the canvas grows to fit them instead
		var right = width;
		foreach (var node in map.Nodes)
			right = Math.Max(right, LabelRight(node, positions[node.Id].X));
		foreach (var note in map.Notes)
			right = Math.Max(right, X(note.Position.Evolution) + DesignSystem.XsWidth(note.Text, 700) + 4);

		StyleBlock.AppendSvgOpenTag(
			sb,
			Math.Ceiling(right),
			height,
			context.Styles.Colors,
			context.Styles.Transparent,
			context.Accessibility,
			context.DiagramType
		);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (map.Title is { Length: > 0 } title)
			ds.AppendTitle(sb, Padding, Padding / 2, title);

		AppendAxes(sb, ds, map, width, height, chartWidth, chartHeight);

		AppendPipelines(sb, ds, map, positions);
		AppendLinks(sb, ds, map, positions, byId);
		AppendTrends(sb, ds, map, positions, byId, X, Y);
		// a pipeline parent's square sits on its pipeline box, among the member labels, so it is drawn last
		foreach (var node in map.Nodes.OrderBy(n => n.IsPipelineParent))
			AppendNode(sb, ds, node, positions[node.Id]);
		AppendAnnotations(sb, ds, map, X, Y, width, height);

		foreach (var note in map.Notes)
		{
			_ = sb.Append("\n<g class=\"wardley-note\">\n  ");
			ds.AppendHaloText(
				sb,
				note.Text,
				X(note.Position.Evolution),
				Y(note.Position.Visibility) - 4,
				TypeRole.Caption,
				"var(--_text)",
				anchor: "start",
				weight: 700
			);
			_ = sb.Append("\n</g>");
		}

		foreach (var force in map.Forces)
			AppendForce(sb, ds, force, X(force.Position.Evolution), Y(force.Position.Visibility));

		ds.Close(sb);
		return sb;
	}

	private static void AppendAxes(StringBuilder sb, DesignSystem ds, WardleyDiagram map, double width, double height, double chartWidth, double chartHeight)
	{
		var bottom = height - Padding;
		_ = sb.Append("\n<g class=\"wardley-axes\">\n  ");
		DesignSystem.AppendAxisLine(sb, Padding, bottom, width - Padding, bottom);
		_ = sb.Append("\n  ");
		DesignSystem.AppendAxisLine(sb, Padding, Padding, Padding, bottom);
		_ = sb.Append("\n  ");
		ds.AppendChartText(sb, "Evolution", Padding + (chartWidth / 2), height - (Padding / 4) - 4, TypeRole.Caption, "var(--_text)", weight: 700);
		_ = sb.Append("\n  ");
		ds.AppendRotatedText(sb, "Visibility", Padding / 3, Padding + (chartHeight / 2), TypeRole.Caption, "var(--_text)");
		_ = sb.Append("\n</g>");

		IReadOnlyList<string> stages = map.Stages.Count > 0 ? map.Stages : DefaultStages;
		var custom = map.StageBoundaries.Count == stages.Count;
		_ = sb.Append("\n<g class=\"wardley-stages\">");
		var start = 0.0;
		for (var i = 0; i < stages.Count; i++)
		{
			var end = custom ? map.StageBoundaries[i] : (i + 1.0) / stages.Count;
			var startX = Padding + (start * chartWidth);
			var endX = Padding + (end * chartWidth);
			if (i > 0)
			{
				_ = sb
					.Append("\n  <line x1=\"")
					.Append(DesignSystem.Num(startX))
					.Append("\" y1=\"")
					.Append(DesignSystem.Num(Padding))
					.Append("\" x2=\"")
					.Append(DesignSystem.Num(startX))
					.Append("\" y2=\"")
					.Append(DesignSystem.Num(height - Padding))
					.Append("\" stroke=\"")
					.Append(DesignSystem.EdgeColor)
					.Append("\" stroke-width=\"")
					.Append(DesignSystem.Num(DesignSystem.GridWidth))
					.Append("\" stroke-dasharray=\"")
					.Append(DesignSystem.DashArray)
					.Append("\" />");
			}

			_ = sb.Append("\n  ");
			ds.AppendChartText(sb, stages[i], (startX + endX) / 2, height - (Padding / 1.5) - 4, TypeRole.Caption);
			start = end;
		}

		_ = sb.Append("\n</g>");
	}

	private readonly record struct Box(double MinX, double MaxX, double Top, IReadOnlyList<(double X, double Y)> Members);

	private const double PipelinePad = 15;
	private const double PipelineHeight = NodeRadius * 4;

	private static Box? PipelineBox(WardleyPipeline pipeline, Dictionary<string, (double X, double Y)> positions)
	{
		var members = pipeline.ComponentIds.Where(positions.ContainsKey).Select(id => positions[id]).OrderBy(p => p.X).ToList();
		if (members.Count == 0)
			return null;
		var y = positions[pipeline.ComponentIds.Last(positions.ContainsKey)].Y;
		return new Box(members[0].X, members[^1].X, y - (PipelineHeight / 2), members);
	}

	private static double LabelRight(WardleyNode node, double x)
	{
		if (node.Kind == WardleyNodeKind.Anchor)
			return x + (node.LabelOffsetX ?? 0) + (DesignSystem.MeasureRole(node.Label, TypeRole.Label, 700) / 2) + 4;
		var extra = node.Sourcing != WardleySourcing.None ? SourcingOffset : 0;
		return x + (node.LabelOffsetX ?? (LabelOffset + extra)) + DesignSystem.XsWidth(node.Label, 500) + 4;
	}

	private static void AppendPipelines(StringBuilder sb, DesignSystem ds, WardleyDiagram map, Dictionary<string, (double X, double Y)> positions)
	{
		foreach (var pipeline in map.Pipelines)
		{
			if (PipelineBox(pipeline, positions) is not { } box)
				continue;
			var (minX, maxX, top, members) = (box.MinX, box.MaxX, box.Top, box.Members);
			const double pad = PipelinePad;
			const double boxHeight = PipelineHeight;

			_ = sb.Append("\n<g class=\"subgraph wardley-pipeline\" data-id=\"");
			MultilineUtils.AppendEscapedAttr(sb, pipeline.NodeId);
			_ = sb
				.Append("\">\n  <rect x=\"")
				.Append(DesignSystem.Num(minX - pad))
				.Append("\" y=\"")
				.Append(DesignSystem.Num(top))
				.Append("\" width=\"")
				.Append(DesignSystem.Num(maxX - minX + (pad * 2)))
				.Append("\" height=\"")
				.Append(DesignSystem.Num(boxHeight))
				.Append("\" rx=\"")
				.Append(DesignSystem.Num(ds.PanelRadius))
				.Append("\" ry=\"")
				.Append(DesignSystem.Num(ds.PanelRadius))
				.Append("\" fill=\"")
				.Append(ds.Neutral.Tint())
				.Append("\" stroke=\"")
				.Append(ds.Neutral.Edge)
				.Append("\" stroke-width=\"")
				.Append(ds.ContainerStrokeWidth)
				.Append("\" />");
			for (var i = 0; i < members.Count - 1; i++)
				AppendLine(sb, members[i], members[i + 1], DesignSystem.EdgeColor, ds.EdgeWidth, DesignSystem.DotArray, "wardley-pipeline-link");
			_ = sb.Append("\n</g>");
		}
	}

	private static double Radius(WardleyNode node) => node.IsPipelineParent ? SquareSize / Math.Sqrt(2) : NodeRadius;

	private static void AppendLinks(
		StringBuilder sb,
		DesignSystem ds,
		WardleyDiagram map,
		Dictionary<string, (double X, double Y)> positions,
		Dictionary<string, WardleyNode> byId
	)
	{
		var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var pipeline in map.Pipelines)
			foreach (var id in pipeline.ComponentIds)
				parentOf.TryAdd(id, pipeline.NodeId);

		foreach (var link in map.Links)
		{
			if (!positions.TryGetValue(link.Source, out var s) || !positions.TryGetValue(link.Target, out var t))
				continue;
			if (parentOf.TryGetValue(link.Source, out var parent) && parent == link.Target)
				continue;
			var dx = t.X - s.X;
			var dy = t.Y - s.Y;
			var distance = Math.Sqrt((dx * dx) + (dy * dy));
			if (distance == 0)
				continue;

			var ux = dx / distance;
			var uy = dy / distance;
			var from = (s.X + (ux * Radius(byId[link.Source])), s.Y + (uy * Radius(byId[link.Source])));
			var to = (t.X - (ux * Radius(byId[link.Target])), t.Y - (uy * Radius(byId[link.Target])));

			_ = sb.Append("\n<g class=\"edge wardley-link\" data-from=\"");
			MultilineUtils.AppendEscapedAttr(sb, link.Source);
			_ = sb.Append("\" data-to=\"");
			MultilineUtils.AppendEscapedAttr(sb, link.Target);
			_ = sb.Append("\">");
			var end = link.Flow is WardleyFlow.Forward or WardleyFlow.Bidirectional ? ds.Marker(MarkerShape.Arrow) : null;
			var startMarker = link.Flow is WardleyFlow.Backward or WardleyFlow.Bidirectional ? ds.Marker(MarkerShape.Arrow, atStart: true) : null;
			AppendLine(sb, from, to, DesignSystem.EdgeColor, ds.EdgeWidth, link.Dashed ? DesignSystem.DashArray : null, null, startMarker, end);

			if (link.Label is { Length: > 0 } label)
			{
				const double offset = 8;
				var lx = ((s.X + t.X) / 2) + (uy * offset);
				var ly = ((s.Y + t.Y) / 2) - (ux * offset);
				var angle = Math.Atan2(dy, dx) * 180 / Math.PI;
				if (angle is > 90 or < -90)
					angle += 180;
				_ = sb
					.Append("\n  <g transform=\"rotate(")
					.Append(DesignSystem.Num(angle))
					.Append(' ')
					.Append(DesignSystem.Num(lx))
					.Append(' ')
					.Append(DesignSystem.Num(ly))
					.Append(")\">");
				ds.AppendHaloText(sb, label, lx, ly, TypeRole.Caption);
				_ = sb.Append("</g>");
			}

			_ = sb.Append("\n</g>");
		}
	}

	private static void AppendTrends(
		StringBuilder sb,
		DesignSystem ds,
		WardleyDiagram map,
		Dictionary<string, (double X, double Y)> positions,
		Dictionary<string, WardleyNode> byId,
		Func<double, double> x,
		Func<double, double> y
	)
	{
		foreach (var trend in map.Trends)
		{
			if (!positions.TryGetValue(trend.NodeId, out var origin))
				continue;
			var tx = x(trend.TargetEvolution);
			var ty = y(byId[trend.NodeId].Position.Visibility);
			var dx = tx - origin.X;
			var dy = ty - origin.Y;
			var distance = Math.Sqrt((dx * dx) + (dy * dy));
			const double shorten = NodeRadius + 2;
			var end = distance > shorten ? (tx - (dx / distance * shorten), ty - (dy / distance * shorten)) : (tx, ty);
			_ = sb.Append("\n<g class=\"wardley-trend\" data-id=\"");
			MultilineUtils.AppendEscapedAttr(sb, trend.NodeId);
			_ = sb.Append("\">");
			AppendLine(sb, origin, end, ColorFamily.AccentBase, "1.25", DesignSystem.DashArray, null, null, ds.Marker(MarkerShape.Arrow, accent: true));
			_ = sb.Append("\n</g>");
		}
	}

	private static void AppendNode(StringBuilder sb, DesignSystem ds, WardleyNode node, (double X, double Y) p)
	{
		const string ink = "var(--_text)";
		var kind = node.Kind switch
		{
			WardleyNodeKind.Anchor => "anchor",
			WardleyNodeKind.PipelineComponent => "pipeline-component",
			_ => "component",
		};
		_ = sb.Append("\n<g class=\"node wardley-node\" data-id=\"");
		MultilineUtils.AppendEscapedAttr(sb, node.Id);
		_ = sb.Append("\" data-kind=\"").Append(kind).Append('"');
		if (node.Sourcing != WardleySourcing.None)
			_ = sb.Append(" data-sourcing=\"").Append(node.Sourcing.ToString().ToLowerInvariant()).Append('"');
		_ = sb.Append('>');

		var ring = node.Sourcing switch
		{
			WardleySourcing.Build => ds.Neutral.Tint(),
			WardleySourcing.Buy => ds.Neutral.Edge,
			WardleySourcing.Outsource => ds.Neutral.Stroke,
			WardleySourcing.Market => "var(--bg)",
			_ => null,
		};
		if (ring is not null)
			AppendCircle(sb, p.X, p.Y, NodeRadius * 2, ring, ink, 1);

		if (node.Sourcing == WardleySourcing.Market)
		{
			var r = NodeRadius * 1.2;
			var cos = r * Math.Cos(Math.PI / 6);
			var sin = r * Math.Sin(Math.PI / 6);
			(double, double)[] dots = [(p.X, p.Y - r), (p.X - cos, p.Y + sin), (p.X + cos, p.Y + sin)];
			for (var i = 0; i < dots.Length; i++)
				AppendLine(sb, dots[i], dots[(i + 1) % dots.Length], ink, "1", null, null);
			foreach (var (dx, dy) in dots)
				AppendCircle(sb, dx, dy, NodeRadius * 0.7, "var(--bg)", ink, 2);
		}
		else if (node.IsPipelineParent)
		{
			_ = sb
				.Append("\n  <rect x=\"")
				.Append(DesignSystem.Num(p.X - (SquareSize / 2)))
				.Append("\" y=\"")
				.Append(DesignSystem.Num(p.Y - (SquareSize / 2)))
				.Append("\" width=\"")
				.Append(DesignSystem.Num(SquareSize))
				.Append("\" height=\"")
				.Append(DesignSystem.Num(SquareSize))
				.Append("\" fill=\"var(--bg)\" stroke=\"")
				.Append(ink)
				.Append("\" stroke-width=\"1.25\" />");
		}
		else if (node.Kind != WardleyNodeKind.Anchor)
		{
			AppendCircle(sb, p.X, p.Y, NodeRadius, "var(--bg)", ink, 1.25);
		}

		if (node.Inertia)
		{
			var offset = (node.IsPipelineParent ? (SquareSize / 2) + 15 : NodeRadius + 15) + (node.Sourcing != WardleySourcing.None ? NodeRadius + 10 : 0);
			var half = node.IsPipelineParent ? SquareSize / 2 : NodeRadius;
			AppendLine(sb, (p.X + offset, p.Y - half), (p.X + offset, p.Y + half), ink, "6", null, "wardley-inertia");
		}

		_ = sb.Append("\n  ");
		if (node.Kind == WardleyNodeKind.Anchor)
		{
			ds.AppendHaloText(sb, node.Label, p.X + (node.LabelOffsetX ?? 0), p.Y + (node.LabelOffsetY ?? -3), TypeRole.Label, ink, weight: 700);
		}
		else
		{
			var extra = node.Sourcing != WardleySourcing.None ? SourcingOffset : 0;
			var lx = p.X + (node.LabelOffsetX ?? (LabelOffset + extra));
			var baseline = p.Y + (node.LabelOffsetY ?? -(LabelOffset + extra));
			ds.AppendHaloText(sb, node.Label, lx, baseline - 4, TypeRole.Caption, ink, anchor: "start");
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendAnnotations(
		StringBuilder sb,
		DesignSystem ds,
		WardleyDiagram map,
		Func<double, double> x,
		Func<double, double> y,
		double width,
		double height
	)
	{
		if (map.Annotations.Count == 0)
			return;

		_ = sb.Append("\n<g class=\"wardley-annotations\">");
		foreach (var a in map.Annotations)
		{
			var cx = x(a.Position.Evolution);
			var cy = y(a.Position.Visibility);
			_ = sb.Append("\n  <g class=\"wardley-annotation\">");
			AppendCircle(sb, cx, cy, AnnotationRadius, "var(--bg)", DesignSystem.AxisStroke, 1.5);
			_ = sb.Append("\n    ");
			ds.AppendChartText(sb, a.Number.ToString(System.Globalization.CultureInfo.InvariantCulture), cx, cy, TypeRole.Tag);
			_ = sb.Append("\n  </g>");
		}

		if (map.AnnotationsBox is { } at)
		{
			const double pad = 10;
			const double lineHeight = 18;
			var rows = map.Annotations.Where(a => a.Text.Length > 0).OrderBy(a => a.Number).Select(a => $"{a.Number}. {a.Text}").ToList();
			if (rows.Count > 0)
			{
				var boxWidth = rows.Max(r => DesignSystem.XsWidth(r, 500)) + (pad * 2);
				var boxHeight = (rows.Count * lineHeight) + (pad * 2) - 4;
				var bx = Math.Max(Padding, Math.Min(x(at.Evolution), width - Padding - boxWidth));
				var by = Math.Max(Padding, Math.Min(y(at.Visibility), height - Padding - boxHeight));
				_ = sb.Append("\n  <g class=\"wardley-annotations-box\">\n    ");
				ds.AppendPanel(sb, bx, by, boxWidth, boxHeight);
				for (var i = 0; i < rows.Count; i++)
				{
					_ = sb.Append("\n    ");
					ds.AppendChartText(
						sb,
						rows[i],
						bx + pad,
						by + pad + (lineHeight / 2) - 2 + (i * lineHeight),
						TypeRole.Caption,
						"var(--_text)",
						anchor: "start"
					);
				}

				_ = sb.Append("\n  </g>");
			}
		}

		_ = sb.Append("\n</g>");
	}

	private static void AppendForce(StringBuilder sb, DesignSystem ds, WardleyForce force, double x, double y)
	{
		var family = ds.Role(force.Deaccelerator ? ColorRole.Warning : ColorRole.Success);
		var top = y - (ForceHeight / 2);
		var bottom = y + (ForceHeight / 2);
		string d;
		if (force.Deaccelerator)
		{
			d =
				$"M{P(x + ForceWidth, top)} L{P(x + ForceHead, top)} L{P(x + ForceHead, top - 8)} L{P(x, y)} L{P(x + ForceHead, bottom + 8)} L{P(x + ForceHead, bottom)} L{P(x + ForceWidth, bottom)} Z";
		}
		else
		{
			var neck = x + ForceWidth - ForceHead;
			d = $"M{P(x, top)} L{P(neck, top)} L{P(neck, top - 8)} L{P(x + ForceWidth, y)} L{P(neck, bottom + 8)} L{P(neck, bottom)} L{P(x, bottom)} Z";
		}

		_ = sb.Append("\n<g class=\"wardley-force\" data-kind=\"").Append(force.Deaccelerator ? "deaccelerator" : "accelerator").Append("\">\n  <path d=\"");
		_ = sb
			.Append(d)
			.Append("\" fill=\"")
			.Append(family.Soft)
			.Append("\" stroke=\"")
			.Append(family.Edge)
			.Append("\" stroke-width=\"1.25\" stroke-linejoin=\"round\" />\n  ");
		ds.AppendHaloText(sb, force.Name, x + (ForceWidth / 2), bottom + 15 - 4, TypeRole.Caption, "var(--_text)", weight: 700);
		_ = sb.Append("\n</g>");
	}

	private static string P(double x, double y) => DesignSystem.Num(x) + "," + DesignSystem.Num(y);

	private static void AppendCircle(StringBuilder sb, double cx, double cy, double r, string fill, string stroke, double strokeWidth) =>
		sb
			.Append("\n  <circle cx=\"")
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
			.Append(DesignSystem.Num(strokeWidth))
			.Append("\" />");

	private static void AppendLine(
		StringBuilder sb,
		(double X, double Y) a,
		(double X, double Y) b,
		string stroke,
		string width,
		string? dash,
		string? cssClass,
		string? markerStart = null,
		string? markerEnd = null
	)
	{
		_ = sb.Append("\n  <line");
		if (cssClass is not null)
			_ = sb.Append(" class=\"").Append(cssClass).Append('"');
		_ = sb
			.Append(" x1=\"")
			.Append(DesignSystem.Num(a.X))
			.Append("\" y1=\"")
			.Append(DesignSystem.Num(a.Y))
			.Append("\" x2=\"")
			.Append(DesignSystem.Num(b.X))
			.Append("\" y2=\"")
			.Append(DesignSystem.Num(b.Y))
			.Append("\" stroke=\"")
			.Append(stroke)
			.Append("\" stroke-width=\"")
			.Append(width)
			.Append('"');
		if (dash is not null)
			_ = sb.Append(" stroke-dasharray=\"").Append(dash).Append('"');
		if (markerStart is not null)
			_ = sb.Append(" marker-start=\"").Append(markerStart).Append('"');
		if (markerEnd is not null)
			_ = sb.Append(" marker-end=\"").Append(markerEnd).Append('"');
		_ = sb.Append(" />");
	}
}
