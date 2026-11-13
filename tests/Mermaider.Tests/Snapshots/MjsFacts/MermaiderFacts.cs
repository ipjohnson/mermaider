using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Text;

namespace Mermaider.Tests.Snapshots.MjsFacts;

/// <summary>
/// Builds the facts scripts/mjs-facts.mjs writes for mermaid.js, from Mermaider's parsed model and rendered SVG. Structure
/// (ids, group members, shapes, icons, edge ends, markers, line styles, class members, cardinalities, chart data) comes
/// from the model. For flowchart, state, class and sequence diagrams, label text comes from the SVG's
/// <c>&lt;text&gt;</c> where an element can be found by its <c>data-*</c> attributes, so a label the renderer draws
/// differently from the model is caught too. The other types write a <c>label</c> fact for each label of their model the
/// SVG shows (see <see cref="Labels"/>). Every type gets
/// <c>drawn</c> from the SVG. Each method mirrors the extractor of the same type in scripts/mjs-facts.mjs.
/// </summary>
internal static partial class MermaiderFacts
{
	private const int TimeoutMs = 2000;

	public static IReadOnlyList<string> For(string source)
	{
		string svg;
		try
		{
			svg = MermaidRenderer.RenderSvg(source);
		}
		catch (Exception e) when (e is MermaidParseException or OperationCanceledException)
		{
			return ["accept\tno"];
		}

		var (cleaned, _) = DiagramPreprocessor.Process(source);
		var (accessibility, lines) = AccessibilityParser.Extract(MermaidRenderer.PreprocessLines(cleaned));

		string[] Indented() => MermaidRenderer.PreprocessLinesPreserveIndent(cleaned, accessibility);

		var doc = XDocument.Parse(svg);
		var facts = new List<string> { "accept\tyes", Drawn(doc) };
		switch (DiagramDetector.Detect(cleaned.AsSpan()))
		{
			case DiagramType.Flowchart:
				Flowchart(FlowchartParser.Parse(lines), doc, facts);
				break;
			case DiagramType.State:
				State(StateParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Class:
				Class(ClassParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Sequence:
				Sequence(SequenceParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Er:
				Er(ErParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Pie:
				Pie(PieParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Quadrant:
				Quadrant(QuadrantParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Timeline:
				Timeline(TimelineParser.Parse(lines), doc, facts);
				break;
			case DiagramType.GitGraph:
				GitGraph(GitGraphParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Radar:
				Radar(RadarParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Treemap:
				Treemap(TreemapParser.Parse(Indented()), doc, facts);
				break;
			case DiagramType.Venn:
				Venn(VennParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Mindmap:
				Mindmap(MindmapParser.Parse(Indented()), doc, facts);
				break;
			case DiagramType.Gantt:
				Gantt(GanttParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Journey:
				Journey(JourneyParser.Parse(lines), doc, facts);
				break;
			case DiagramType.C4:
				C4(C4Parser.Parse(lines), doc, facts);
				break;
			case DiagramType.Sankey:
				Sankey(SankeyParser.Parse(lines), doc, facts);
				break;
			case DiagramType.XyChart:
				XyChart(XyChartParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Requirement:
				Requirement(RequirementParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Packet:
				Packet(PacketParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Kanban:
				Kanban(KanbanParser.Parse(Indented()), doc, facts);
				break;
			case DiagramType.Architecture:
				Architecture(ArchitectureParser.Parse(lines), doc, facts);
				break;
			case DiagramType.Block:
				Block(BlockParser.Parse(lines), doc, facts);
				break;
			case DiagramType.TreeView:
				TreeView(TreeViewParser.Parse(Indented()), doc, facts);
				break;
			default:
				throw new InvalidOperationException("MermaiderFacts has no extractor for this diagram type; add one with its scripts/mjs-facts.mjs twin");
		}

		return facts;
	}

	private static readonly HashSet<string> s_shapeElements = ["rect", "path", "circle", "ellipse", "polygon", "polyline", "line", "image", "text"];

	private static readonly HashSet<string> s_hiddenContainers = ["defs", "marker", "clipPath", "mask", "pattern", "symbol"];

	private static bool Hidden(XElement e) => e.Ancestors().Any(a => s_hiddenContainers.Contains(a.Name.LocalName));

	/// <summary>
	/// <c>drawn yes</c> when the SVG shows anything: a shape with a size or a non-empty text, outside defs and markers.
	/// mermaid.js's twin measures each element's box in the browser; without a browser, a zero width, height, radius or
	/// an empty path counts as nothing.
	/// </summary>
	internal static string Drawn(XDocument doc)
	{
		static bool Zero(XElement e, string name) =>
			Attr(e, name) is { } v && double.TryParse(v, System.Globalization.CultureInfo.InvariantCulture, out var d) && d == 0;

		var any = Elements(doc)
			.Where(e => s_shapeElements.Contains(e.Name.LocalName) && !Hidden(e))
			.Any(
				e =>
					e.Name.LocalName switch
					{
						"text" => DrawnText(e).Length > 0,
						"rect" or "image" => !Zero(e, "width") && !Zero(e, "height"),
						"circle" => !Zero(e, "r"),
						"path" => !string.IsNullOrWhiteSpace(Attr(e, "d")),
						_ => true,
					}
			);
		return Fact("drawn", any ? "yes" : "no");
	}

	/// <summary>
	/// A <c>label</c> fact for each model label the SVG shows, as the runner's labels() writes them: a label counts as shown
	/// when its letters and digits, case-folded, occur in all drawn text joined the same way, so upper-casing, wrapping and
	/// a value drawn beside the label do not matter. Longer labels are matched first and each match is used up.
	/// </summary>
	private static void Labels(XDocument doc, List<string> facts, IEnumerable<string?> labels)
	{
		static string Key(string s) => string.Concat(s.Where(char.IsLetterOrDigit)).ToLowerInvariant();

		var drawn = string.Concat(Elements(doc).Where(e => e.Name.LocalName == "text" && !Hidden(e)).Select(e => Key(e.Value)));
		var ordered = labels.Select(l => Norm(l)).Where(l => Key(l).Length > 0).OrderByDescending(l => Key(l).Length).ThenBy(l => l, StringComparer.Ordinal);
		foreach (var label in ordered)
		{
			var key = Key(label);
			var at = drawn.IndexOf(key, StringComparison.Ordinal);
			if (at < 0)
				continue;
			drawn = string.Concat(drawn.AsSpan(0, at), "\0", drawn.AsSpan(at + key.Length));
			facts.Add(Fact("label", label));
		}
	}

	private static string Num(double? v) => v is { } d ? Math.Round(d, 3).ToString(System.Globalization.CultureInfo.InvariantCulture) : string.Empty;

	private static string Int(int v) => v.ToString(System.Globalization.CultureInfo.InvariantCulture);

	/// <summary>
	/// A shape in mermaid's documented short names, the vocabulary scripts/mjs-facts.mjs maps mermaid's own shape names onto.
	/// </summary>
	private static string Shape(NodeShape shape) => shape switch
	{
		NodeShape.Rectangle => "rect",
		NodeShape.Rounded => "rounded",
		NodeShape.Diamond => "diam",
		NodeShape.Stadium => "stadium",
		NodeShape.Circle => "circle",
		NodeShape.Subroutine => "fr-rect",
		NodeShape.DoubleCircle => "dbl-circ",
		NodeShape.Hexagon => "hex",
		NodeShape.Cylinder => "cyl",
		NodeShape.Asymmetric => "odd",
		NodeShape.Trapezoid => "trap-b",
		NodeShape.TrapezoidAlt => "trap-t",
		NodeShape.Parallelogram => "lean-r",
		NodeShape.ParallelogramAlt => "lean-l",
		NodeShape.StateStart => "sm-circ",
		NodeShape.StateEnd => "fr-circ",
		NodeShape.ForkJoin => "fork",
		NodeShape.DataStore => "datastore",
		NodeShape.Text => "text",
		NodeShape.NotchedRectangle => "notch-rect",
		NodeShape.LinedRectangle => "lin-rect",
		NodeShape.BraceLeft => "brace",
		NodeShape.BraceRight => "brace-r",
		NodeShape.LightningBolt => "bolt",
		NodeShape.Document => "doc",
		NodeShape.HorizontalCylinder => "h-cyl",
		NodeShape.LinedCylinder => "lin-cyl",
		NodeShape.CurvedTrapezoid => "curv-trap",
		NodeShape.DividedRectangle => "div-rect",
		NodeShape.Triangle => "tri",
		NodeShape.WindowPane => "win-pane",
		NodeShape.FilledCircle => "f-circ",
		NodeShape.NotchedPentagon => "notch-pent",
		NodeShape.FlippedTriangle => "flip-tri",
		NodeShape.SlopedRectangle => "sl-rect",
		NodeShape.StackedDocument => "docs",
		NodeShape.StackedRectangle => "st-rect",
		NodeShape.BowTieRectangle => "bow-rect",
		NodeShape.CrossedCircle => "cross-circ",
		NodeShape.TaggedDocument => "tag-doc",
		NodeShape.TaggedRectangle => "tag-rect",
		NodeShape.LinedDocument => "lin-doc",
		NodeShape.CollapsedGroup => "collapsedGroup",
		_ => shape.ToString().ToLowerInvariant(),
	};

	private static string Fact(params string?[] parts) => string.Join('\t', parts.Select(p => Norm(p).Replace('\t', ' '))).TrimEnd('\t');

	private static string Norm(string? s) => WhitespacePattern().Replace(s ?? string.Empty, " ").Trim();

	/// <summary>A model label as Mermaider displays it: entities decoded, line breaks as spaces, formatting tags removed.</summary>
	private static string Display(string? label) => Norm(TagPattern().Replace(MultilineUtils.NormalizeBrTags(label ?? string.Empty), " "));

	/// <summary>The text an SVG element draws. A <c>tspan</c> with its own <c>x</c> or <c>dy</c> starts a new line.</summary>
	private static string DrawnText(XElement? element)
	{
		if (element is null)
			return string.Empty;
		var sb = new StringBuilder();
		foreach (var text in element.DescendantsAndSelf().Where(e => e.Name.LocalName == "text"))
		{
			_ = sb.Append(' ');
			Append(text, sb);
		}

		return Norm(sb.ToString());

		static void Append(XElement el, StringBuilder sb)
		{
			foreach (var node in el.Nodes())
			{
				if (node is XText t)
					_ = sb.Append(t.Value);
				else if (node is XElement child)
				{
					if (child.Name.LocalName == "tspan" && (child.Attribute("x") is not null || child.Attribute("dy") is not null))
						_ = sb.Append(' ');
					Append(child, sb);
				}
			}
		}
	}

	private static IEnumerable<XElement> Elements(XDocument doc) => doc.Descendants();

	private static string? Attr(XElement e, string name) => e.Attribute(name)?.Value;

	private static bool HasClass(XElement e, string cls) => (Attr(e, "class") ?? string.Empty).Split(' ').Contains(cls);

	/// <summary>The group's own texts, without those of nested groups such as a sequence message's autonumber badge.</summary>
	private static string OwnText(XElement group) => Norm(string.Join(' ', group.Elements().Where(e => e.Name.LocalName == "text").Select(DrawnText)));

	/// <summary>Edge label groups by (from, to), in drawing order, so parallel edges take their labels in turn.</summary>
	private static Func<string, string, string?> EdgeLabels(XDocument doc, string groupClass, Func<XElement, string>? text = null)
	{
		text ??= DrawnText;
		var queues = Elements(doc)
			.Where(e => e.Name.LocalName == "g" && HasClass(e, groupClass) && Attr(e, "data-from") is not null)
			.GroupBy(e => (Attr(e, "data-from"), Attr(e, "data-to")))
			.ToDictionary(g => g.Key, g => new Queue<XElement>(g));
		return (from, to) => queues.TryGetValue((from, to), out var q) && q.Count > 0 ? text(q.Dequeue()) : null;
	}

	private static void Flowchart(MermaidGraph graph, XDocument doc, List<string> facts)
	{
		var nodeGroups = Elements(doc)
			.Where(e => e.Name.LocalName == "g" && Attr(e, "data-id") is not null && !HasClass(e, "subgraph"))
			.GroupBy(e => Attr(e, "data-id")!)
			.ToDictionary(g => g.Key, g => g.First());
		var groupLabels = Elements(doc)
			.Where(e => e.Name.LocalName == "g" && HasClass(e, "subgraph"))
			.GroupBy(e => Attr(e, "data-id")!)
			.ToDictionary(g => g.Key, g => Display(Attr(g.First(), "data-label")));
		var subgraphIds = new HashSet<string>();
		foreach (var sg in AllSubgraphs(graph.Subgraphs))
		{
			_ = subgraphIds.Add(sg.Id);
			var members = sg.NodeIds.Concat(sg.Children.Select(c => c.Id)).Distinct().Order(StringComparer.Ordinal);
			facts.Add(Fact("group", sg.Id, groupLabels.GetValueOrDefault(sg.Id) ?? Display(sg.Label), string.Join(',', members)));
		}

		foreach (var node in graph.Nodes.Values.Where(n => !subgraphIds.Contains(n.Id)))
		{
			var drawn = nodeGroups.TryGetValue(node.Id, out var g) ? DrawnText(g) : Display(node.Label);
			facts.Add(Fact("node", node.Id, drawn, Shape(node.Shape)));
		}

		var label = EdgeLabels(doc, "edge-label");
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var e = graph.Edges[i];
			var (fromGroup, toGroup) = graph.SubgraphEdgeRedirections.GetValueOrDefault(i);
			var text = string.IsNullOrEmpty(e.Label) ? string.Empty : label(e.Source, e.Target) ?? Display(e.Label);
			facts.Add(
				Fact(
					"edge",
					fromGroup ?? e.Source,
					toGroup ?? e.Target,
					e.HasArrowStart ? "arrow" : "none",
					e.HasArrowEnd ? "arrow" : "none",
					Line(e.Style),
					text
				)
			);
		}
	}

	private static string Line(EdgeStyle style) => style switch
	{
		EdgeStyle.Dotted => "dotted",
		EdgeStyle.Thick => "thick",
		EdgeStyle.Invisible => "invisible",
		_ => "solid",
	};

	private static IEnumerable<MermaidSubgraph> AllSubgraphs(IEnumerable<MermaidSubgraph> roots) => roots.SelectMany(s => AllSubgraphs(s.Children).Prepend(s));

	private static void State(MermaidGraph graph, XDocument doc, List<string> facts)
	{
		var nodeGroups = Elements(doc)
			.Where(e => e.Name.LocalName == "g" && Attr(e, "data-id") is not null && Attr(e, "data-from") is null)
			.GroupBy(e => Attr(e, "data-id")!)
			.ToDictionary(g => g.Key, g => g.First());
		var parent = new Dictionary<string, string>();
		foreach (var sg in AllSubgraphs(graph.Subgraphs))
		{
			foreach (var id in sg.NodeIds)
				parent[id] = sg.Id;
			foreach (var child in sg.Children)
				parent[child.Id] = sg.Id;
		}

		var groups = AllSubgraphs(graph.Subgraphs).ToDictionary(s => s.Id);

		string Name(string id) =>
			graph.Nodes.TryGetValue(id, out var n) && n.Shape is NodeShape.StateStart or NodeShape.StateEnd
				? $"[*]{(n.Shape == NodeShape.StateStart ? "start" : "end")}@{parent.GetValueOrDefault(id, "root")}"
				: id;

		foreach (var sg in groups.Values)
		{
			var members = sg.NodeIds.Concat(sg.Children.Select(c => c.Id)).Select(Name).Distinct().Order(StringComparer.Ordinal);
			facts.Add(Fact("group", sg.Id, Display(sg.Label), string.Join(',', members)));
		}

		var seen = new HashSet<string>();
		foreach (var node in graph.Nodes.Values.Where(n => !groups.ContainsKey(n.Id)))
		{
			var id = Name(node.Id);
			if (!seen.Add(id))
				continue;
			// A plain state is drawn as a rounded rectangle, whatever shape the parser gives it.
			var shape = node.Shape is NodeShape.StateStart or NodeShape.StateEnd or NodeShape.Diamond or NodeShape.ForkJoin ? Shape(node.Shape) : "rounded";
			var drawn = shape != "rounded" ? string.Empty : nodeGroups.TryGetValue(node.Id, out var g) ? DrawnText(g) : Display(node.Label);
			facts.Add(Fact("node", id, drawn, shape));
		}

		foreach (var note in graph.Notes)
			facts.Add(Fact("note", note.TargetNodeId, Display(note.Text)));

		var label = EdgeLabels(doc, "edge-label");
		for (var i = 0; i < graph.Edges.Count; i++)
		{
			var e = graph.Edges[i];
			var (fromGroup, toGroup) = graph.SubgraphEdgeRedirections.GetValueOrDefault(i);
			var text = string.IsNullOrEmpty(e.Label) ? string.Empty : label(e.Source, e.Target) ?? Display(e.Label);
			facts.Add(
				Fact(
					"edge",
					Name(fromGroup ?? e.Source),
					Name(toGroup ?? e.Target),
					e.HasArrowStart ? "arrow" : "none",
					e.HasArrowEnd ? "arrow" : "none",
					"solid",
					text
				)
			);
		}
	}

	private static void Class(ClassDiagram diagram, XDocument doc, List<string> facts)
	{
		var nodeGroups = Elements(doc)
			.Where(e => e.Name.LocalName == "g" && HasClass(e, "class-node"))
			.GroupBy(e => Attr(e, "data-id")!)
			.ToDictionary(g => g.Key, g => g.First());
		foreach (var cls in diagram.Classes)
		{
			// The class box draws its annotation and name as plain texts and its members as mono texts.
			var titles = nodeGroups.TryGetValue(cls.Id, out var g)
				? g.Elements().Where(e => e.Name.LocalName == "text" && !HasClass(e, "mono")).Select(DrawnText).ToList()
				: [];
			facts.Add(Fact("class", cls.Id, titles.Count > 0 ? titles[^1] : Display(cls.Label)));
			if (cls.Annotation is not null)
				facts.Add(Fact("annotation", cls.Id, titles.Count > 1 ? titles[0] : $"«{cls.Annotation}»"));
			foreach (var a in cls.Attributes)
				facts.Add(Fact("attribute", cls.Id, Member(a)));
			foreach (var m in cls.Methods)
				facts.Add(Fact("method", cls.Id, Member(m)));
		}

		foreach (var ns in diagram.Namespaces)
			facts.Add(Fact("namespace", ns.Name, string.Join(',', ns.ClassIds.Order(StringComparer.Ordinal))));
		foreach (var note in diagram.Notes)
			facts.Add(Fact("note", note.TargetClassId ?? string.Empty, Display(note.Text)));

		// A relation's label group draws the label first, then its cardinalities.
		var label = EdgeLabels(doc, "edge-label", g => DrawnText(g.Elements().FirstOrDefault(e => e.Name.LocalName == "text")));
		foreach (var r in diagram.Relationships)
		{
			var marker = r.Type switch
			{
				ClassRelationType.Inheritance or ClassRelationType.Realization => "triangle",
				ClassRelationType.Composition => "diamond-filled",
				ClassRelationType.Aggregation => "diamond",
				ClassRelationType.Association or ClassRelationType.Dependency => "arrow",
				ClassRelationType.Lollipop => "lollipop",
				_ => "none",
			};
			var line = r.Type is ClassRelationType.Realization or ClassRelationType.Dependency or ClassRelationType.DashedLink ? "dashed" : "solid";
			var atFrom = r.MarkerAt == ClassMarkerAt.From;
			// mermaid.js draws a lollipop as an interface named by the end it is written at, not as that class.
			var from = r.Type == ClassRelationType.Lollipop && atFrom ? "()" + r.From : r.From;
			var to = r.Type == ClassRelationType.Lollipop && !atFrom ? "()" + r.To : r.To;
			var text = string.IsNullOrEmpty(r.Label) ? string.Empty : label(r.From, r.To) ?? Display(r.Label);
			facts.Add(
				Fact("relation", from, to, atFrom ? marker : "none", atFrom ? "none" : marker, line, text, Display(r.FromCardinality), Display(r.ToCardinality))
			);
		}
	}

	/// <summary>
	/// A member as mermaid.js displays it. Mermaider draws the visibility symbol and the type as their own texts; mermaid.js
	/// draws one text with the symbol joined to it, an attribute's type before its name and a method's return type after
	/// " : ". The static and abstract classifiers are styling on both sides, not text.
	/// </summary>
	private static string Member(ClassMember m)
	{
		var visibility = m.Visibility switch
		{
			ClassVisibility.Public => "+",
			ClassVisibility.Private => "-",
			ClassVisibility.Protected => "#",
			ClassVisibility.Package => "~",
			_ => string.Empty,
		};
		var text = m.IsMethod
			? $"{m.Name}({m.Params}){(string.IsNullOrEmpty(m.Type) ? string.Empty : " : " + m.Type)}"
			: string.IsNullOrEmpty(m.Type) ? m.Name : $"{m.Type} {m.Name}";
		return visibility + Norm(text);
	}

	private static void Sequence(SequenceDiagram diagram, XDocument doc, List<string> facts)
	{
		var actors = Elements(doc)
			.Where(e => e.Name.LocalName == "g" && HasClass(e, "actor") && !HasClass(e, "actor-bottom"))
			.GroupBy(e => Attr(e, "data-id")!)
			.ToDictionary(g => g.Key, g => DrawnText(g.First()));
		foreach (var a in diagram.Actors)
			facts.Add(Fact("actor", a.Id, actors.GetValueOrDefault(a.Id) ?? Display(a.Label), a.Type == SequenceActorType.Actor ? "actor" : "participant"));
		foreach (var box in diagram.Boxes)
			facts.Add(Fact("box", Display(box.Title), string.Join(',', box.ActorIds.Order(StringComparer.Ordinal))));

		var drawn = Elements(doc).Where(e => e.Name.LocalName == "g" && HasClass(e, "message")).ToList();
		for (var i = 0; i < diagram.Messages.Count; i++)
		{
			var m = diagram.Messages[i];
			var head = m.Bidirectional
				? "filled-both"
				: m.ArrowHead switch
				{
					SequenceArrowHead.Cross => "cross",
					SequenceArrowHead.Open => "open",
					_ => "filled",
				};
			var text = drawn.Count == diagram.Messages.Count ? OwnText(drawn[i]) : Display(m.Label);
			facts.Add(Fact("message", m.From, m.To, m.LineStyle == SequenceLineStyle.Dashed ? "dashed" : "solid", head, text));
		}

		foreach (var n in diagram.Notes)
			facts.Add(Fact("note", n.Position.ToString().ToLowerInvariant(), string.Join(',', n.ActorIds), Display(n.Text)));
		foreach (var b in diagram.Blocks)
		{
			facts.Add(Fact("block", b.Type.ToString().ToLowerInvariant(), b.Type == SequenceBlockType.Rect ? string.Empty : Display(b.Label)));
			foreach (var d in b.Dividers)
				facts.Add(Fact("divider", Display(d.Label)));
		}
	}

	private static string Card(ErCardinality c) => c switch
	{
		ErCardinality.One => "one",
		ErCardinality.ZeroOne => "zero-or-one",
		ErCardinality.Many => "one-or-more",
		_ => "zero-or-more",
	};

	private static void Er(ErDiagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?>();
		foreach (var e in diagram.Entities)
		{
			facts.Add(Fact("entity", e.Id, Display(e.Label)));
			shown.Add(Display(e.Label));
			foreach (var a in e.Attributes)
			{
				facts.Add(
					Fact(
						"attribute",
						e.Id,
						Norm(a.Type),
						Norm(a.Name),
						string.Join(',', a.Keys.Select(k => k.ToString()).Order(StringComparer.Ordinal)),
						Display(a.Comment)
					)
				);
				shown.AddRange([Norm(a.Type), Norm(a.Name), Display(a.Comment)]);
			}
		}

		foreach (var r in diagram.Relationships)
		{
			facts.Add(
				Fact(
					"relation",
					r.Entity1,
					r.Entity2,
					Card(r.Cardinality1),
					Card(r.Cardinality2),
					r.Identifying ? "identifying" : "non-identifying",
					Display(r.Label)
				)
			);
			shown.Add(Display(r.Label));
		}

		Labels(doc, facts, shown);
	}

	private static void Pie(PieChart chart, XDocument doc, List<string> facts)
	{
		if (!string.IsNullOrEmpty(chart.Title))
			facts.Add(Fact("title", Display(chart.Title)));
		if (chart.ShowData)
			facts.Add(Fact("showdata", "yes"));
		foreach (var s in chart.Slices)
			facts.Add(Fact("slice", Display(s.Label), Num(s.Value)));
		Labels(doc, facts, chart.Slices.Select(s => Display(s.Label)).Prepend(Display(chart.Title)));
	}

	private static void Quadrant(QuadrantChart chart, XDocument doc, List<string> facts)
	{
		var shown = new List<string?>();

		void Add(params string[] parts)
		{
			facts.Add(Fact(parts));
			shown.Add(parts[0] == "point" ? parts[1] : parts[^1]);
		}

		if (!string.IsNullOrEmpty(chart.Title))
			Add("title", Display(chart.Title));
		string?[] quadrants = [chart.Quadrant1, chart.Quadrant2, chart.Quadrant3, chart.Quadrant4];
		for (var i = 0; i < quadrants.Length; i++)
			if (!string.IsNullOrEmpty(quadrants[i]))
				Add("quadrant", Int(i + 1), Display(quadrants[i]));
		(string Name, string? Text)[] axes =
		[
			("x-left", chart.XAxisLeft),
			("x-right", chart.XAxisRight),
			("y-bottom", chart.YAxisBottom),
			("y-top", chart.YAxisTop)
		];
		foreach (var (name, text) in axes)
			if (!string.IsNullOrEmpty(text))
				Add("axis", name, Display(text));
		foreach (var p in chart.Points)
			Add(
				"point",
				Display(p.Label),
				Math.Round(p.X, 2).ToString(System.Globalization.CultureInfo.InvariantCulture),
				Math.Round(p.Y, 2).ToString(System.Globalization.CultureInfo.InvariantCulture)
			);
		Labels(doc, facts, shown);
	}

	private static void Timeline(TimelineDiagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?> { Display(diagram.Title) };
		if (!string.IsNullOrEmpty(diagram.Title))
			facts.Add(Fact("title", Display(diagram.Title)));
		foreach (var s in diagram.Sections)
		{
			if (s.Name is not null)
			{
				facts.Add(Fact("section", Display(s.Name)));
				shown.Add(Display(s.Name));
			}

			foreach (var p in s.Periods)
			{
				facts.Add(Fact("period", Display(s.Name), Display(p.Label)));
				shown.Add(Display(p.Label));
				foreach (var e in p.Events)
				{
					facts.Add(Fact("event", Display(p.Label), Display(e)));
					shown.Add(Display(e));
				}
			}
		}

		Labels(doc, facts, shown);
	}

	/// <summary>Replays the actions on a current branch, as mermaid.js's db does, to list the commits in order.</summary>
	private static void GitGraph(GitGraph graph, XDocument doc, List<string> facts)
	{
		var branch = "main";
		facts.Add(Fact("branch", branch));
		var shown = new List<string?> { branch };
		var index = 0;

		void Commit(string type, string? tag, string from, string? id)
		{
			facts.Add(Fact("commit", Int(index++), branch, type, tag, from, id));
			shown.AddRange([tag, id]);
		}

		static string Type(GitCommitType t) => t switch
		{
			GitCommitType.Highlight => "highlight",
			GitCommitType.Reverse => "reverse",
			_ => "normal",
		};

		foreach (var action in graph.Actions)
		{
			switch (action)
			{
				case GitCommitAction c:
					Commit(Type(c.Type), c.Tag, string.Empty, c.Id);
					break;
				case GitBranchAction b:
					facts.Add(Fact("branch", b.Name));
					shown.Add(b.Name);
					branch = b.Name;
					break;
				case GitCheckoutAction c:
					branch = c.Name;
					break;
				case GitMergeAction m:
					Commit("merge", m.Tag, m.Name, m.Id);
					break;
				case GitCherryPickAction:
					Commit("cherry-pick", null, string.Empty, null);
					break;
			}
		}

		Labels(doc, facts, shown);
	}

	private static void Radar(RadarChart chart, XDocument doc, List<string> facts)
	{
		if (!string.IsNullOrEmpty(chart.Title))
			facts.Add(Fact("title", Display(chart.Title)));
		foreach (var a in chart.Axes)
			facts.Add(Fact("axis", Display(a.Label)));
		foreach (var c in chart.Curves)
			facts.Add(Fact("curve", Display(c.Label), string.Join(',', c.Values.Select(v => Num(v)))));
		Labels(doc, facts, [Display(chart.Title), .. chart.Axes.Select(a => Display(a.Label)), .. chart.Curves.Select(c => Display(c.Label))]);
	}

	private static void Treemap(TreemapDiagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?>();

		void Walk(IEnumerable<TreemapNode> nodes, string trail)
		{
			foreach (var n in nodes)
			{
				var path = trail.Length == 0 ? Display(n.Label) : trail + " / " + Display(n.Label);
				facts.Add(Fact("node", path, n.Children.Count > 0 ? string.Empty : Num(n.Value)));
				shown.Add(Display(n.Label));
				Walk(n.Children, path);
			}
		}

		Walk(diagram.Roots, string.Empty);
		Labels(doc, facts, shown);
	}

	/// <summary>A size the source leaves out is written as mermaid.js's default, 10 over the square of the number of sets.</summary>
	private static void Venn(VennDiagram diagram, XDocument doc, List<string> facts)
	{
		foreach (var s in diagram.Sets)
			facts.Add(Fact("set", s.Id, Display(s.Label), Num(s.Size ?? 10)));
		foreach (var u in diagram.Unions)
			facts.Add(
				Fact(
					"union",
					string.Join(',', u.SetIds.Order(StringComparer.Ordinal)),
					Display(u.Label),
					Num(u.Size ?? 10.0 / (u.SetIds.Count * u.SetIds.Count))
				)
			);
		Labels(doc, facts, [.. diagram.Sets.Select(s => Display(s.Label)), .. diagram.Unions.Select(u => Display(u.Label))]);
	}

	private static void Mindmap(MindmapDiagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?>();

		void Walk(MindmapNode n, string trail)
		{
			var path = trail.Length == 0 ? Display(n.Label) : trail + " / " + Display(n.Label);
			var shape = n.Shape switch
			{
				MindmapShape.Default => "default",
				MindmapShape.Square => "rect",
				MindmapShape.Circle => "circle",
				MindmapShape.Bang => "bang",
				MindmapShape.Cloud => "cloud",
				MindmapShape.Hexagon => "hex",
				_ => "rounded",
			};
			facts.Add(Fact("node", path, shape));
			shown.Add(Display(n.Label));
			foreach (var c in n.Children)
				Walk(c, path);
		}

		Walk(diagram.Root, string.Empty);
		Labels(doc, facts, shown);
	}

	private static void Gantt(GanttDiagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?> { Display(diagram.Title) };
		if (!string.IsNullOrEmpty(diagram.Title))
			facts.Add(Fact("title", Display(diagram.Title)));

		static string Time(DateTime d) => d.ToString("yyyy-MM-dd HH:mm", System.Globalization.CultureInfo.InvariantCulture);

		foreach (var s in diagram.Sections)
		{
			if (s.Name is not null)
			{
				facts.Add(Fact("section", Display(s.Name)));
				shown.Add(Display(s.Name));
			}

			foreach (var t in s.Tasks)
			{
				var tags = new[]
				{
					(GanttTaskTags.Done, "done"),
					(GanttTaskTags.Active, "active"),
					(GanttTaskTags.Crit, "crit"),
					(GanttTaskTags.Milestone, "milestone")
				}.Where(x => t.Tags.HasFlag(x.Item1)).Select(x => x.Item2);
				facts.Add(Fact("task", Display(s.Name), Display(t.Name), t.Id, Time(t.Start), Time(t.End), string.Join(',', tags)));
				shown.Add(Display(t.Name));
			}
		}

		Labels(doc, facts, shown);
	}

	private static void Journey(JourneyDiagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?> { Display(diagram.Title) };
		if (!string.IsNullOrEmpty(diagram.Title))
			facts.Add(Fact("title", Display(diagram.Title)));
		foreach (var s in diagram.Sections)
		{
			if (s.Name is not null)
			{
				facts.Add(Fact("section", Display(s.Name)));
				shown.Add(Display(s.Name));
			}

			foreach (var t in s.Tasks)
			{
				facts.Add(
					Fact("task", Display(s.Name), Display(t.Name), Int(t.Score), string.Join(',', t.Actors.Select(Display).Order(StringComparer.Ordinal)))
				);
				shown.Add(Display(t.Name));
			}
		}

		var people = diagram.Sections.SelectMany(s => s.Tasks).SelectMany(t => t.Actors).Select(Display).Distinct();
		Labels(doc, facts, shown.Concat(people));
	}

	private static void C4(C4Diagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?> { Display(diagram.Title) };
		if (!string.IsNullOrEmpty(diagram.Title))
			facts.Add(Fact("title", Display(diagram.Title)));

		void Walk(IEnumerable<C4Node> nodes, string parent)
		{
			foreach (var n in nodes)
			{
				switch (n)
				{
					case C4Element e:
						facts.Add(
							Fact(
								"element",
								e.Alias,
								SnakePattern().Replace(e.Type.ToString(), "_$1").ToLowerInvariant(),
								e.External ? "external" : "internal",
								Display(e.Label),
								Display(e.Technology),
								Display(e.Description),
								parent
							)
						);
						shown.AddRange([Display(e.Label), Display(e.Technology), Display(e.Description)]);
						break;
					case C4Boundary b:
						facts.Add(Fact("boundary", b.Alias, b.IsDeploymentNode ? "node" : b.Type.ToString().ToLowerInvariant(), Display(b.Label), parent));
						shown.Add(Display(b.Label));
						Walk(b.Children, b.Alias);
						break;
				}
			}
		}

		Walk(diagram.RootNodes, string.Empty);
		foreach (var r in diagram.Relations)
		{
			facts.Add(Fact("rel", r.From, r.To, r.Bidirectional ? "both" : "one", Display(r.Label), Display(r.Technology)));
			shown.AddRange([Display(r.Label), Display(r.Technology)]);
		}

		Labels(doc, facts, shown);
	}

	private static void Sankey(SankeyDiagram diagram, XDocument doc, List<string> facts)
	{
		foreach (var l in diagram.Links)
			facts.Add(Fact("flow", Display(l.Source), Display(l.Target), Num(l.Value)));
		Labels(doc, facts, diagram.Links.SelectMany(l => new[] { Display(l.Source), Display(l.Target) }).Distinct());
	}

	private static void XyChart(XyChart chart, XDocument doc, List<string> facts)
	{
		if (!string.IsNullOrEmpty(chart.Title))
			facts.Add(Fact("title", Display(chart.Title)));
		if (chart.Horizontal)
			facts.Add(Fact("orientation", "horizontal"));
		facts.Add(Fact("x-axis", Display(chart.XAxisTitle), string.Join(',', (chart.XCategories ?? []).Select(Display))));
		facts.Add(Fact("y-axis", Display(chart.YAxisTitle)));
		foreach (var s in chart.Series)
			facts.Add(Fact("series", s.Type.ToString().ToLowerInvariant(), string.Join(',', s.Values.Select(v => Num(v)))));
		Labels(doc, facts, [Display(chart.Title), Display(chart.XAxisTitle), Display(chart.YAxisTitle), .. (chart.XCategories ?? []).Select(Display)]);
	}

	private static void Requirement(RequirementDiagram diagram, XDocument doc, List<string> facts)
	{
		static string Kebab(string s) => SnakePattern().Replace(s, "-$1").ToLowerInvariant();

		foreach (var r in diagram.Requirements)
			facts.Add(
				Fact(
					"requirement",
					r.Name,
					Kebab(r.Kind.ToString()),
					Display(r.Id),
					Display(r.Text),
					r.Risk == RequirementRisk.Unspecified ? string.Empty : Kebab(r.Risk.ToString()),
					r.VerifyMethod == RequirementVerifyMethod.Unspecified ? string.Empty : Kebab(r.VerifyMethod.ToString())
				)
			);
		foreach (var e in diagram.Elements)
			facts.Add(Fact("element", e.Name, Display(e.Type), Display(e.DocRef)));
		foreach (var r in diagram.Relations)
			facts.Add(Fact("relation", r.Source, r.Target, r.Type.ToString().ToLowerInvariant()));
		Labels(
			doc,
			facts,
			[
				.. diagram.Requirements.SelectMany(r => new[] { r.Name, Display(r.Id), Display(r.Text) }),
				.. diagram.Elements.SelectMany(e => new[] { e.Name, Display(e.Type), Display(e.DocRef) }),
				.. diagram.Relations.Select(r => r.Type.ToString().ToLowerInvariant()),
			]
		);
	}

	private static void Packet(PacketDiagram diagram, XDocument doc, List<string> facts)
	{
		if (!string.IsNullOrEmpty(diagram.Title))
			facts.Add(Fact("title", Display(diagram.Title)));
		foreach (var f in diagram.Fields)
			facts.Add(
				Fact(
					"field",
					f.Start.ToString(System.Globalization.CultureInfo.InvariantCulture),
					f.End.ToString(System.Globalization.CultureInfo.InvariantCulture),
					Display(f.Label)
				)
			);
		Labels(doc, facts, diagram.Fields.Select(f => Display(f.Label)).Prepend(Display(diagram.Title)));
	}

	private static void Kanban(KanbanDiagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?>();
		foreach (var c in diagram.Columns)
		{
			facts.Add(Fact("column", c.Id, Display(c.Title)));
			shown.Add(Display(c.Title));
			foreach (var t in c.Tasks)
			{
				facts.Add(Fact("card", c.Id, t.Id, Display(t.Title), Display(t.Assigned), Display(t.Ticket), Display(t.Priority)));
				shown.AddRange([Display(t.Title), Display(t.Assigned), Display(t.Ticket)]);
			}
		}

		Labels(doc, facts, shown);
	}

	private static void Architecture(ArchitectureDiagram diagram, XDocument doc, List<string> facts)
	{
		static string Side(ArchitectureSide? s) => s switch
		{
			ArchitectureSide.Left => "L",
			ArchitectureSide.Right => "R",
			ArchitectureSide.Top => "T",
			ArchitectureSide.Bottom => "B",
			_ => string.Empty,
		};

		foreach (var g in diagram.Groups)
		{
			facts.Add(Fact("group", g.Id, Display(g.Title), g.ParentId));
			if (!string.IsNullOrEmpty(g.Icon))
				facts.Add(Fact("icon", g.Id, g.Icon));
		}

		foreach (var s in diagram.Services)
		{
			facts.Add(Fact("service", s.Id, Display(s.Title), s.GroupId));
			if (!string.IsNullOrEmpty(s.Icon))
				facts.Add(Fact("icon", s.Id, s.Icon));
		}

		foreach (var j in diagram.Junctions)
			facts.Add(Fact("junction", j.Id, j.GroupId));
		foreach (var e in diagram.Edges)
			facts.Add(
				Fact("edge", e.SourceId, Side(e.SourceSide), e.TargetId, Side(e.TargetSide), e.SourceArrow ? "arrow" : "none", e.TargetArrow ? "arrow" : "none")
			);
		Labels(doc, facts, [.. diagram.Groups.Select(g => Display(g.Title)), .. diagram.Services.Select(s => Display(s.Title))]);
	}

	private static void Block(BlockDiagram diagram, XDocument doc, List<string> facts)
	{
		foreach (var b in diagram.Nodes)
		{
			var span = Int(b.Span);
			facts.Add(b.IsSpace ? Fact("space", string.Empty, span) : Fact("block", b.Id, Display(b.Label), b.Rounded ? "rounded" : "rect", span));
		}

		foreach (var e in diagram.Edges)
			facts.Add(Fact("edge", e.From, e.To, "none", "arrow", Display(e.Label)));
		Labels(doc, facts, [.. diagram.Nodes.Where(b => !b.IsSpace).Select(b => Display(b.Label)), .. diagram.Edges.Select(e => Display(e.Label))]);
	}

	private static void TreeView(TreeViewDiagram diagram, XDocument doc, List<string> facts)
	{
		var shown = new List<string?>();

		void Walk(IEnumerable<TreeViewNode> nodes, string trail)
		{
			foreach (var n in nodes)
			{
				var path = trail.Length == 0 ? Display(n.Label) : trail + " / " + Display(n.Label);
				facts.Add(Fact("node", path, n.IsDirectory ? "dir" : "file", Display(n.Description)));
				shown.AddRange([Display(n.Label), Display(n.Description)]);
				if (!string.IsNullOrEmpty(n.Icon))
					facts.Add(Fact("icon", path, n.Icon));
				Walk(n.Children, path);
			}
		}

		Walk(diagram.Roots, string.Empty);
		Labels(doc, facts, shown);
	}

	[GeneratedRegex(@"(?<=[a-z])([A-Z])", RegexOptions.None, TimeoutMs)]
	private static partial Regex SnakePattern();

	[GeneratedRegex(@"\s+", RegexOptions.None, TimeoutMs)]
	private static partial Regex WhitespacePattern();

	[GeneratedRegex(@"<[^>]+>", RegexOptions.None, TimeoutMs)]
	private static partial Regex TagPattern();
}
