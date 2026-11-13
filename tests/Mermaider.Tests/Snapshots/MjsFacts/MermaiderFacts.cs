using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Text;

namespace Mermaider.Tests.Snapshots.MjsFacts;

/// <summary>
/// Builds the facts scripts/mjs-facts.mjs writes for mermaid.js, from Mermaider's parsed model and rendered SVG. Structure
/// (ids, group members, edge ends, markers, line styles, class members, cardinalities) comes from the model; label text
/// comes from the SVG's <c>&lt;text&gt;</c> where an element can be found by its <c>data-*</c> attributes, so a label the
/// renderer draws differently from the model is caught too.
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
		var lines = MermaidRenderer.PreprocessLines(cleaned);
		var doc = XDocument.Parse(svg);
		var facts = new List<string> { "accept\tyes" };
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
		}

		return facts;
	}

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
			facts.Add(Fact("node", node.Id, drawn));
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
			var kind = node.Shape switch
			{
				NodeShape.StateStart or NodeShape.StateEnd => "pseudo",
				NodeShape.Diamond => "choice",
				NodeShape.ForkJoin => "fork",
				_ => "state",
			};
			var drawn = kind != "state" ? string.Empty : nodeGroups.TryGetValue(node.Id, out var g) ? DrawnText(g) : Display(node.Label);
			facts.Add(Fact("node", id, drawn, kind));
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
		return visibility + Display(text);
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

	[GeneratedRegex(@"\s+", RegexOptions.None, TimeoutMs)]
	private static partial Regex WhitespacePattern();

	[GeneratedRegex(@"<[^>]+>", RegexOptions.None, TimeoutMs)]
	private static partial Regex TagPattern();
}
