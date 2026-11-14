using System.Globalization;
using System.Text;
using Mermaider.Icons;
using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Text;
using Mermaider.Theming;
using Sugiyama;

namespace Mermaider.Rendering;

/// <summary>
/// Draws <c>usecase-beta</c> diagrams: actors as figures with a caption, use cases as ellipses or boxes, system
/// boundaries as containers, notes, JSON tables and relationships, laid out by the shared Sugiyama engine with each
/// boundary as a subgraph.
/// </summary>
internal static class UsecaseSvgRenderer
{
	private const double Margin = 24;
	private const double FigureHeight = 44;
	private const double FigureGap = 6;
	private const double StereotypeLine = 16;
	private const double WrapWidth = 180;
	private const double MinEllipseW = 110;
	private const double MinEllipseH = 48;
	private const double MinRectW = 100;
	private const double MinRectH = 40;
	private const double JsonHeaderH = 32;
	private const double TabHeight = 14;

	private enum NodeKind
	{
		Actor,
		UseCase,
		Note,
		Json,
	}

	private sealed record Node(string Id, NodeKind Kind, string Text, string? Stereotype, double W, double H)
	{
		public double X { get; set; }
		public double Y { get; set; }
		public UsecaseActor? Actor { get; init; }
		public UsecaseCase? UseCase { get; init; }
		public UsecaseJsonTable? Json { get; init; }
		public double CaptionH { get; init; }
	}

	private sealed record Route(UsecaseRelationship? Relationship, UsecaseNote? Note, List<Point> Points, Point? LabelAt, string? Label);

	internal static string Render(UsecaseDiagram diagram, SvgRenderContext context)
	{
		var sb = RenderToBuilder(diagram, context);
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

	internal static StringBuilder RenderToBuilder(UsecaseDiagram diagram, SvgRenderContext context, CancellationToken ct = default)
	{
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);
		var nodes = BuildNodes(diagram);
		if (nodes.Count == 0)
		{
			StyleBlock.AppendSvgOpenTag(sb, 200, 100, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
			StyleBlock.AppendStyleBlock(sb, context.Styles);
			ds.Close(sb);
			return sb;
		}

		var (routes, groups, width, height) = Layout(diagram, nodes, ds, ct);

		// a package boundary's title tab sits above its frame
		var top = diagram.Boundaries.Any(b => b.Type == UsecaseBoundaryType.Package) ? TabHeight : 0;
		if (top > 0)
		{
			foreach (var n in nodes.Values)
				n.Y += top;
			for (var i = 0; i < groups.Count; i++)
				groups[i] = groups[i] with { Y = groups[i].Y + top };
			foreach (var r in routes)
			{
				for (var i = 0; i < r.Points.Count; i++)
					r.Points[i] = r.Points[i] with { Y = r.Points[i].Y + top };
			}
		}

		StyleBlock.AppendSvgOpenTag(sb, width, height + top, context.Styles.Colors, context.Styles.Transparent, context.Accessibility, context.DiagramType);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		var styles = context.Styles.Strict is null ? diagram.Styles : null;
		var palette = ClusterPalette.Build(
			nodes.Values.Select(n => new ClusterBox(n.Id, n.X, n.Y, n.W, n.H)).ToList(),
			diagram.Relationships.Select(r => (r.Source, r.Target)),
			groups.Select(g => new ClusterGroup(g.Id, g.X, g.Y, g.Width, g.Height, [])).ToList(),
			context.Styles.Colors
		).WithTint(ds.TintStrength);

		var boundaries = diagram.Boundaries.ToDictionary(b => b.Id, StringComparer.Ordinal);
		foreach (var g in groups)
			AppendBoundaryBody(sb, ds, g, boundaries[g.Id], palette.GroupFamily(g.Id), styles);

		foreach (var route in routes)
			AppendRoute(sb, ds, route, context.EdgeRadius, styles);

		foreach (var node in nodes.Values)
		{
			var family = palette.Family(node.Id);
			switch (node.Kind)
			{
				case NodeKind.Actor:
					AppendActor(sb, ds, node, family, styles);
					break;
				case NodeKind.UseCase:
					AppendUseCase(sb, ds, node, family, styles);
					break;
				case NodeKind.Json:
					AppendJson(sb, ds, node, family, styles);
					break;
				default:
					break;
			}
		}

		foreach (var route in routes.Where(r => r.Note is not null))
		{
			var note = nodes[route.Note!.Id];
			_ = sb.Append("\n<g data-id=\"");
			MultilineUtils.AppendEscapedAttr(sb, note.Id.AsSpan());
			_ = sb.Append("\" data-kind=\"note\" data-target=\"");
			MultilineUtils.AppendEscapedAttr(sb, route.Note.Target.AsSpan());
			_ = sb.Append("\">");
			Point? from = route.Points.Count > 1 ? route.Points[0] : null;
			Point? to = route.Points.Count > 1 ? route.Points[^1] : null;
			ds.AppendNote(sb, note.X, note.Y, note.W, note.H, note.Text, from, to);
			_ = sb.Append("\n</g>");
		}

		foreach (var g in groups)
			AppendBoundaryHeader(sb, ds, g, boundaries[g.Id], palette.GroupFamily(g.Id));

		foreach (var route in routes)
		{
			if (route.Label is { Length: > 0 } label && route.LabelAt is { } at)
				ds.AppendEdgeLabel(sb, at.X, at.Y, label, Style(styles, route.Relationship?.Id, "color"));
		}

		ds.Close(sb);
		return sb;
	}

	// ── sizes ────────────────────────────────────────────────────────────

	private static string Display(UsecaseLabel label) => label.IsMarkdown ? MultilineUtils.MarkdownToTags(label.Text) : Wrap(label.Text);

	private static (double W, double H) Measure(string text, TypeRole role) =>
		TextMetrics.MeasureMultiline(MultilineUtils.StripFormatTags(text).AsSpan(), DesignSystem.Px(role), 500) is var m ? (m.Width, m.Height) : default;

	/// <summary>Breaks a plain label between words so no line is wider than <see cref="WrapWidth"/>.</summary>
	private static string Wrap(string text)
	{
		var px = DesignSystem.Px(TypeRole.Label);
		var lines = new List<string>();
		foreach (var paragraph in text.Split('\n'))
		{
			var current = "";
			foreach (var word in paragraph.Split(' '))
			{
				var candidate = current.Length == 0 ? word : current + " " + word;
				if (current.Length > 0 && TextMetrics.MeasureTextWidth(candidate, px, 500) > WrapWidth)
				{
					lines.Add(current);
					current = word;
				}
				else
				{
					current = candidate;
				}
			}

			lines.Add(current);
		}

		return string.Join('\n', lines);
	}

	private static Dictionary<string, Node> BuildNodes(UsecaseDiagram diagram)
	{
		var nodes = new Dictionary<string, Node>(StringComparer.Ordinal);
		foreach (var a in diagram.Actors)
		{
			var text = Display(a.Label);
			var (tw, th) = Measure(text, TypeRole.Label);
			var stereo = a.Stereotype is null ? 0 : StereotypeLine;
			var sw = a.Stereotype is null ? 0 : Measure("«" + a.Stereotype + "»", TypeRole.Meta).W;
			var caption = th + stereo;
			nodes[a.Id] = new Node(a.Id, NodeKind.Actor, text, a.Stereotype, Math.Max(48, Math.Max(tw, sw) + 8), FigureHeight + FigureGap + caption)
			{
				Actor = a,
				CaptionH = caption,
			};
		}

		foreach (var u in diagram.UseCases)
		{
			var text = Display(u.Label);
			var (tw, th) = Measure(text, TypeRole.Label);
			var sw = u.Stereotype is null ? 0 : Measure("«" + u.Stereotype + "»", TypeRole.Meta).W;
			var contentW = Math.Max(tw, sw);
			var contentH = th + (u.Stereotype is null ? 0 : StereotypeLine);
			var (w, h) = u.Shape == UsecaseShape.Ellipse
				? (Math.Max(MinEllipseW, (contentW * 1.3) + 36), Math.Max(MinEllipseH, (contentH * 1.3) + 22))
				: (Math.Max(MinRectW, contentW + 32), Math.Max(MinRectH, contentH + 22));
			nodes[u.Id] = new Node(u.Id, NodeKind.UseCase, text, u.Stereotype, w, h) { UseCase = u };
		}

		foreach (var n in diagram.Notes)
		{
			var text = Display(n.Label);
			var (tw, th) = Measure(text, TypeRole.Body);
			nodes[n.Id] = new Node(n.Id, NodeKind.Note, text, null, Math.Max(80, tw + 28), Math.Max(32, th + 18));
		}

		foreach (var j in diagram.JsonTables)
		{
			var keyW = j.Rows.Count == 0 ? 0 : j.Rows.Max(r => KeyWidth(r.Key));
			var valueW = j.Rows.Count == 0 ? 0 : j.Rows.Max(r => TextMetrics.MeasureTextWidth(r.Value, DesignSystem.Px(TypeRole.Body), 400));
			var nameW = TextMetrics.MeasureTextWidth(j.Id, DesignSystem.Px(TypeRole.Heading), 700) + (EntityGrid.Pad * 2);
			var w = Math.Clamp(Math.Max(nameW, EntityGrid.Pad + keyW + EntityGrid.ColumnGap + valueW + EntityGrid.Pad + EntityGrid.Slack), 120, 420);
			var h = JsonHeaderH + (j.Rows.Count * DesignSystem.RowHeight);
			nodes[j.Id] = new Node(j.Id, NodeKind.Json, j.Id, null, w, h) { Json = j };
		}

		return nodes;
	}

	private static double KeyWidth(string key) =>
		Math.Max(
			TextMetrics.MeasureTextWidth(key, DesignSystem.Px(TypeRole.Meta), 600),
			TextMetrics.EstimateMonoTextWidth(key, DesignSystem.Px(TypeRole.Meta)) + 2
		);

	// ── layout ───────────────────────────────────────────────────────────

	private static (List<Route> Routes, List<PositionedGroup> Groups, double Width, double Height) Layout(
		UsecaseDiagram diagram,
		Dictionary<string, Node> nodes,
		DesignSystem ds,
		CancellationToken ct
	)
	{
		var layoutNodes = nodes
			.Values
			.Select(
				n => new LayoutNode(n.Id, n.W, n.H)
				{
					Outline = n.Kind == NodeKind.UseCase && n.UseCase!.Shape == UsecaseShape.Ellipse ? PortOutline.Ellipse : PortOutline.Rectangle,
				}
			)
			.ToList();

		var pending = new List<(UsecaseRelationship? Rel, UsecaseNote? Note, string? Label)>();
		var edges = new List<LayoutEdge>();
		foreach (var r in diagram.Relationships)
		{
			if (!nodes.ContainsKey(r.Source) || !nodes.ContainsKey(r.Target))
				continue;
			var label = r.Label is { } l ? (l.IsMarkdown ? MultilineUtils.MarkdownToTags(l.Text) : l.Text) : null;
			var (lw, lh) = label is null ? (0.0, 0.0) : ds.EdgeLabelSize(MultilineUtils.StripFormatTags(label));
			edges.Add(new LayoutEdge(r.Source, r.Target, lw, lh, r.MinLength));
			pending.Add((r, null, label));
		}

		foreach (var n in diagram.Notes)
		{
			edges.Add(new LayoutEdge(n.Id, n.Target));
			pending.Add((null, n, null));
		}

		var subgraphs = diagram.Boundaries.Select(b => new LayoutSubgraph(b.Id, b.Label.Text, b.Members.Where(nodes.ContainsKey).ToList(), [])).ToList();
		var direction = diagram.Direction switch
		{
			Direction.LR => LayoutDirection.LR,
			Direction.RL => LayoutDirection.RL,
			Direction.BT => LayoutDirection.BT,
			_ => LayoutDirection.TD,
		};
		var result = HierarchicalLayout.Compute(
			new LayoutGraph(direction, layoutNodes, edges, subgraphs),
			new LayoutOptions { Padding = Margin, NodeSpacing = 36, LayerSpacing = 64, NaturalBackEdgeRouting = true, CancellationToken = ct, }
		);

		foreach (var n in result.Nodes)
		{
			nodes[n.Id].X = n.X;
			nodes[n.Id].Y = n.Y;
		}

		var routes = new List<Route>(pending.Count);
		foreach (var e in result.Edges)
		{
			if (e.OriginalIndex < 0 || e.OriginalIndex >= pending.Count)
				continue;
			var (rel, note, label) = pending[e.OriginalIndex];
			var at = e.LabelPosition is { } lp ? new Point(lp.X, lp.Y) : (Point?)null;
			routes.Add(new Route(rel, note, e.Points.Select(p => new Point(p.X, p.Y)).ToList(), at, label));
		}

		var groups = new List<PositionedGroup>();

		void Flatten(IEnumerable<LayoutGroupResult> list)
		{
			foreach (var g in list)
			{
				groups.Add(new PositionedGroup { Id = g.Id, Label = g.Label, X = g.X, Y = g.Y, Width = g.Width, Height = g.Height, Children = [] });
				Flatten(g.Children);
			}
		}

		Flatten(result.Groups);
		return (routes, groups, result.Width, result.Height);
	}

	// ── drawing ──────────────────────────────────────────────────────────

	private static string? Style(IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? styles, string? id, string property)
	{
		if (styles is null || id is null || !styles.TryGetValue(id, out var s) || !s.TryGetValue(property, out var value))
			return null;
		return property == "stroke-width"
			? UsecaseParser.IsStrokeWidth(value) ? value.Trim() : null
			: SvgValueAllowlist.IsAllowedColor(value) ? value.Trim() : null;
	}

	private static void AppendAttr(StringBuilder sb, string name, string value)
	{
		_ = sb.Append(' ').Append(name).Append("=\"");
		MultilineUtils.AppendEscapedAttr(sb, value.AsSpan());
		_ = sb.Append('"');
	}

	private static void OpenGroup(StringBuilder sb, string cssClass, string id, string kind)
	{
		_ = sb.Append("\n<g class=\"").Append(cssClass).Append('"');
		AppendAttr(sb, "data-id", id);
		_ = sb.Append(" data-kind=\"").Append(kind).Append("\">");
	}

	private static void AppendBoundaryBody(
		StringBuilder sb,
		DesignSystem ds,
		PositionedGroup g,
		UsecaseBoundary boundary,
		ColorFamily family,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? styles
	)
	{
		var data = new StringBuilder();
		_ = data.Append("data-id=\"");
		MultilineUtils.AppendEscapedAttr(data, boundary.Id.AsSpan());
		_ = data.Append("\" data-kind=\"boundary\" data-type=\"").Append(boundary.Type == UsecaseBoundaryType.Package ? "package" : "rect").Append('"');

		if (boundary.Type == UsecaseBoundaryType.Package)
		{
			var tabW = Math.Min(g.Width / 2, Measure(boundary.Label.Text, TypeRole.Subheading).W + 32);
			_ = sb
				.Append("\n<rect class=\"boundary-tab\" x=\"")
				.Append(g.X)
				.Append("\" y=\"")
				.Append(g.Y - TabHeight)
				.Append("\" width=\"")
				.Append(tabW)
				.Append("\" height=\"")
				.Append(TabHeight + 2)
				.Append("\" rx=\"3\" fill=\"")
				.Append(Style(styles, boundary.Id, "fill") ?? family.Band)
				.Append("\" stroke=\"")
				.Append(Style(styles, boundary.Id, "stroke") ?? ds.ContainerStroke(family))
				.Append("\" stroke-width=\"")
				.Append(ds.ContainerStrokeWidth)
				.Append("\" />");
		}

		if (Style(styles, boundary.Id, "fill") is null && Style(styles, boundary.Id, "stroke") is null)
		{
			ds.AppendContainerBody(sb, g.X, g.Y, g.Width, g.Height, family, 0, "subgraph", data.ToString());
			return;
		}

		// author styling (non-strict) wins over the family
		_ = sb.Append("\n<g class=\"subgraph\" ").Append(data).Append(">\n  <rect x=\"");
		_ = sb
			.Append(g.X)
			.Append("\" y=\"")
			.Append(g.Y)
			.Append("\" width=\"")
			.Append(g.Width)
			.Append("\" height=\"")
			.Append(g.Height)
			.Append("\" rx=\"")
			.Append(DesignSystem.Num(ds.Spec.ContainerRadius))
			.Append("\" fill=\"")
			.Append(Style(styles, boundary.Id, "fill") ?? ds.ContainerFill(family, 0))
			.Append("\" stroke=\"")
			.Append(Style(styles, boundary.Id, "stroke") ?? ds.ContainerStroke(family))
			.Append("\" stroke-width=\"")
			.Append(Style(styles, boundary.Id, "stroke-width") ?? ds.ContainerStrokeWidth)
			.Append("\" />\n</g>");
	}

	private static void AppendBoundaryHeader(StringBuilder sb, DesignSystem ds, PositionedGroup g, UsecaseBoundary boundary, ColorFamily family) =>
		ds.AppendContainerHeader(sb, g.X, g.Y, g.Width, Display(boundary.Label), family);

	private static void AppendRoute(
		StringBuilder sb,
		DesignSystem ds,
		Route route,
		double radius,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? styles
	)
	{
		if (route.Points.Count < 2 || route.Relationship is not { } rel)
			return;

		var dashed = rel.Type is UsecaseRelationshipType.Include or UsecaseRelationshipType.Extend;
		_ = sb.Append("\n<path class=\"edge relationship\" data-type=\"").Append(rel.Type.ToString().ToLowerInvariant()).Append('"');
		AppendAttr(sb, "data-source", rel.Source);
		AppendAttr(sb, "data-target", rel.Target);
		_ = sb.Append(" d=\"");
		if (SvgRenderer.IsOrthogonal(route.Points))
			SvgRenderer.BuildOrthogonalPath(sb, route.Points, radius);
		else
			SvgRenderer.BuildRoundedPath(sb, route.Points, radius);
		_ = sb
			.Append("\" fill=\"none\" stroke=\"")
			.Append(Style(styles, rel.Id, "stroke") ?? DesignSystem.EdgeColor)
			.Append("\" stroke-width=\"")
			.Append(Style(styles, rel.Id, "stroke-width") ?? ds.EdgeWidth)
			.Append('"');
		if (dashed)
			_ = sb.Append(" stroke-dasharray=\"").Append(DesignSystem.DashArray).Append('"');
		if (MarkerOf(rel.StartEnd) is { } start)
			_ = sb.Append(" marker-start=\"").Append(ds.Marker(start, atStart: true)).Append('"');
		if (MarkerOf(rel.EndEnd) is { } end)
			_ = sb.Append(" marker-end=\"").Append(ds.Marker(end)).Append('"');
		_ = sb.Append(" />");
	}

	private static MarkerShape? MarkerOf(UsecaseEnd end) => end switch
	{
		UsecaseEnd.Arrow => MarkerShape.Arrow,
		UsecaseEnd.Circle => MarkerShape.Circle,
		UsecaseEnd.Cross => MarkerShape.Cross,
		UsecaseEnd.Triangle => MarkerShape.Triangle,
		_ => null,
	};

	private static void AppendCaption(StringBuilder sb, DesignSystem ds, Node node, double cx, double top, string? color)
	{
		var y = top;
		if (node.Stereotype is { } stereotype)
		{
			_ = sb.Append("\n  ");
			ds.AppendText(sb, "«" + stereotype + "»", cx, y + (StereotypeLine / 2), TypeRole.Meta);
			y += StereotypeLine;
		}

		var (_, th) = Measure(node.Text, TypeRole.Label);
		_ = sb.Append("\n  ");
		ds.AppendText(sb, node.Text, cx, y + (th / 2), TypeRole.Label, color);
	}

	private static void AppendActor(
		StringBuilder sb,
		DesignSystem ds,
		Node node,
		ColorFamily family,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? styles
	)
	{
		var actor = node.Actor!;
		var cx = node.X + (node.W / 2);
		var y = node.Y;
		var ink = Style(styles, actor.Id, "stroke") ?? family.Ink;
		var fill = Style(styles, actor.Id, "fill") ?? ds.NodeFill(family);
		var sw = Style(styles, actor.Id, "stroke-width") ?? "1.5";
		OpenGroup(sb, "node usecase-actor", actor.Id, "actor");
		var P = DesignSystem.Num;

		switch (actor.Type)
		{
			case UsecaseActorType.Icon:
				{
					var svg = IconRegistry.Resolve(actor.Icon);
					var base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));
					_ = sb
						.Append("\n  <image x=\"")
						.Append(cx - 20)
						.Append("\" y=\"")
						.Append(y + 2)
						.Append("\" width=\"40\" height=\"40\" href=\"data:image/svg+xml;base64,")
						.Append(base64)
						.Append("\" />");
					break;
				}
			case UsecaseActorType.Hollow:
			case UsecaseActorType.Awesome:
				{
					var solid = actor.Type == UsecaseActorType.Awesome;
					_ = sb.Append(
						CultureInfo.InvariantCulture,
						$"\n  <circle cx=\"{P(cx)}\" cy=\"{P(y + 9)}\" r=\"8\" fill=\"{(solid ? ink : fill)}\" stroke=\"{ink}\" stroke-width=\"{sw}\" />"
					).Append(
						CultureInfo.InvariantCulture,
						$"\n  <path d=\"M{P(cx - 14)},{P(y + FigureHeight)} L{P(cx - 14)},{P(y + 28)} Q{P(cx - 14)},{P(y + 20)} {P(cx)},{P(y + 20)} Q{P(cx + 14)},{P(y + 20)} {P(cx + 14)},{P(y + 28)} L{P(cx + 14)},{P(y + FigureHeight)} Z\" fill=\"{(solid ? ink : fill)}\" stroke=\"{ink}\" stroke-width=\"{sw}\" stroke-linejoin=\"round\" />"
					);
					break;
				}
			default:
				_ = sb.Append(
					CultureInfo.InvariantCulture,
					$"\n  <circle cx=\"{P(cx)}\" cy=\"{P(y + 7)}\" r=\"7\" fill=\"{fill}\" stroke=\"{ink}\" stroke-width=\"{sw}\" />"
				).Append(
					CultureInfo.InvariantCulture,
					$"\n  <path d=\"M{P(cx)},{P(y + 14)} L{P(cx)},{P(y + 30)} M{P(cx - 13)},{P(y + 20)} L{P(cx + 13)},{P(y + 20)} M{P(cx)},{P(y + 30)} L{P(cx - 11)},{P(y + FigureHeight)} M{P(cx)},{P(y + 30)} L{P(cx + 11)},{P(y + FigureHeight)}\" fill=\"none\" stroke=\"{ink}\" stroke-width=\"{sw}\" stroke-linecap=\"round\" />"
				);
				break;
		}

		if (actor.Business)
			_ = sb.Append(
				CultureInfo.InvariantCulture,
				$"\n  <line class=\"business\" x1=\"{P(cx + 6)}\" y1=\"{P(y + FigureHeight)}\" x2=\"{P(cx + 18)}\" y2=\"{P(y + 26)}\" stroke=\"{ink}\" stroke-width=\"{sw}\" stroke-linecap=\"round\" />"
			);

		AppendCaption(sb, ds, node, cx, y + FigureHeight + FigureGap, Style(styles, actor.Id, "color"));
		_ = sb.Append("\n</g>");
	}

	private static void AppendUseCase(
		StringBuilder sb,
		DesignSystem ds,
		Node node,
		ColorFamily family,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? styles
	)
	{
		var u = node.UseCase!;
		var P = DesignSystem.Num;
		var fill = Style(styles, u.Id, "fill") ?? ds.NodeFill(family);
		var stroke = Style(styles, u.Id, "stroke") ?? (ds.Spec.OutlineWidth > 0 ? ds.NodeStroke(family) : DesignSystem.BoxOutline(family));
		var sw = Style(styles, u.Id, "stroke-width") ?? (ds.Spec.OutlineWidth > 0 ? ds.NodeStrokeWidth : "1");
		var (cx, cy) = (node.X + (node.W / 2), node.Y + (node.H / 2));
		OpenGroup(sb, "node usecase", u.Id, u.Shape == UsecaseShape.Ellipse ? "usecase" : "usecase-rect");
		if (u.Shape == UsecaseShape.Ellipse)
		{
			_ = sb.Append(
				CultureInfo.InvariantCulture,
				$"\n  <ellipse cx=\"{P(cx)}\" cy=\"{P(cy)}\" rx=\"{P(node.W / 2)}\" ry=\"{P(node.H / 2)}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{sw}\" />"
			);
			if (u.Business)
			{
				var (rx, ry) = (node.W / 2, node.H / 2);
				_ = sb.Append(
					CultureInfo.InvariantCulture,
					$"\n  <line class=\"business\" x1=\"{P(cx - (rx * 0.62))}\" y1=\"{P(cy + (ry * 0.78))}\" x2=\"{P(cx - (rx * 0.3))}\" y2=\"{P(cy - (ry * 0.95))}\" stroke=\"{stroke}\" stroke-width=\"{sw}\" />"
				);
			}
		}
		else
		{
			var r = P(ds.Spec.NodeRadius);
			_ = sb.Append(
				CultureInfo.InvariantCulture,
				$"\n  <rect x=\"{P(node.X)}\" y=\"{P(node.Y)}\" width=\"{P(node.W)}\" height=\"{P(node.H)}\" rx=\"{r}\" ry=\"{r}\" fill=\"{fill}\" stroke=\"{stroke}\" stroke-width=\"{sw}\" />"
			);
		}

		var (_, th) = Measure(node.Text, TypeRole.Label);
		var contentH = th + (node.Stereotype is null ? 0 : StereotypeLine);
		AppendCaption(sb, ds, node, cx, cy - (contentH / 2), Style(styles, u.Id, "color"));
		_ = sb.Append("\n</g>");
	}

	private static void AppendJson(
		StringBuilder sb,
		DesignSystem ds,
		Node node,
		ColorFamily family,
		IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>>? styles
	)
	{
		var table = node.Json!;
		var (x, y, w, h) = (node.X, node.Y, node.W, node.H);
		OpenGroup(sb, "node entity usecase-json", table.Id, "json");
		_ = sb.Append("\n  ");
		if (table.Rows.Count == 0)
		{
			ds.AppendHeaderOnlyEntity(sb, x, y, w, h, table.Id, family);
			_ = sb.Append("\n</g>");
			return;
		}

		ds.AppendEntityFrame(sb, x, y, w, h, JsonHeaderH, family);
		var rowTop = y + JsonHeaderH;
		for (var i = 0; i < table.Rows.Count; i++)
		{
			ds.AppendZebraRow(sb, x, rowTop, w, DesignSystem.RowHeight, i, i == table.Rows.Count - 1);
			if (i > 0)
				ds.AppendEntityRowDivider(sb, x, w, rowTop);
			rowTop += DesignSystem.RowHeight;
		}

		if (Style(styles, table.Id, "stroke") is { } stroke)
		{
			_ = sb.Append(
				CultureInfo.InvariantCulture,
				$"\n  <rect x=\"{DesignSystem.Num(x)}\" y=\"{DesignSystem.Num(y)}\" width=\"{DesignSystem.Num(w)}\" height=\"{DesignSystem.Num(h)}\" rx=\"{DesignSystem.Num(ds.Spec.NodeRadius)}\" fill=\"none\" stroke=\"{stroke}\" stroke-width=\"{Style(styles, table.Id, "stroke-width") ?? ds.NodeStrokeWidth}\" />"
			);
		}

		_ = sb.Append("\n  ");
		ds.AppendEntityName(sb, x, y, w, JsonHeaderH, table.Id, family);
		var keyW = table.Rows.Max(r => KeyWidth(r.Key));
		var valueX = x + EntityGrid.Pad + keyW + EntityGrid.ColumnGap;
		rowTop = y + JsonHeaderH;
		foreach (var row in table.Rows)
		{
			var cy = rowTop + (DesignSystem.RowHeight / 2);
			if (row.Key.Length > 0)
			{
				_ = sb.Append("\n  ");
				ds.AppendText(sb, row.Key, x + EntityGrid.Pad, cy, TypeRole.Meta, anchor: "start", weight: 600);
			}

			_ = sb.Append("\n  ");
			ds.AppendText(sb, row.Value, valueX, cy, TypeRole.Body, anchor: "start");
			rowTop += DesignSystem.RowHeight;
		}

		_ = sb.Append("\n</g>");
	}
}
