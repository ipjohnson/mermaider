using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

internal static partial class FlowchartParser
{
	private const int TimeoutMs = 2000;

	// ========================================================================
	// Generated regex patterns
	// ========================================================================

	[GeneratedRegex(@"^(?:graph|flowchart)\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"^classDef\s+(\w+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassDefPattern();

	[GeneratedRegex(@"^class\s+([\w,-]+)\s+(\w+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassAssignPattern();

	[GeneratedRegex(@"^style\s+([\w,-]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex StylePattern();

	[GeneratedRegex(@"^linkStyle\s+(default|[\d,\s]+)\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex LinkStylePattern();

	[GeneratedRegex(@"^direction\s+(TD|TB|LR|BT|RL)\s*$", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex DirectionPattern();

	[GeneratedRegex(@"^subgraph\s+(.+)$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SubgraphPattern();

	[GeneratedRegex(@"^([\w-]+)\s*\[(.+)\]$", RegexOptions.None, TimeoutMs)]
	private static partial Regex SubgraphBracketPattern();

	[GeneratedRegex(@"^(<)?(-{2,}>|-{3,}|-\.+->|-\.+-|={2,}>|={3,}|~{3,})(?:\|(""[^""]*""|[^|]*)\|)?", RegexOptions.None, TimeoutMs)]
	private static partial Regex ArrowPattern();

	[GeneratedRegex(@"^(<)?(-{2}|={2}|-\.)\s+(.+?)\s+(-{2}>|={2}>|\.->)", RegexOptions.None, TimeoutMs)]
	private static partial Regex InlineTextArrowPattern();

	[GeneratedRegex(@"^:::([\w][\w-]*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex ClassShorthandPattern();

	[GeneratedRegex(@"^([\w-]+)\(\(\((""[^""]*""|.+?)\)\)\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex DoubleCirclePattern();

	[GeneratedRegex(@"^([\w-]+)\(\[(""[^""]*""|.+?)\]\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex StadiumPattern();

	[GeneratedRegex(@"^([\w-]+)\(\((""[^""]*""|.+?)\)\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex CirclePattern();

	[GeneratedRegex(@"^([\w-]+)\[\[(""[^""]*""|.+?)\]\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex SubroutinePattern();

	[GeneratedRegex(@"^([\w-]+)\[\((""[^""]*""|.+?)\)\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex CylinderPattern();

	[GeneratedRegex(@"^([\w-]+)\[/(""[^""]*""|.+?)\\\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex TrapezoidPattern();

	[GeneratedRegex(@"^([\w-]+)\[/(""[^""]*""|.+?)/\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex ParallelogramPattern();

	[GeneratedRegex(@"^([\w-]+)\[\\(""[^""]*""|.+?)\\\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex ParallelogramAltPattern();

	[GeneratedRegex(@"^([\w-]+)\[\\(""[^""]*""|.+?)/\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex TrapezoidAltPattern();

	[GeneratedRegex(@"^([\w-]+)>(""[^""]*""|.+?)\]", RegexOptions.None, TimeoutMs)]
	private static partial Regex AsymmetricPattern();

	[GeneratedRegex(@"^([\w-]+)\{\{(""[^""]*""|.+?)\}\}", RegexOptions.None, TimeoutMs)]
	private static partial Regex HexagonPattern();

	[GeneratedRegex(@"^([\w-]+)\[(""[^""]*""|.+?)\]", RegexOptions.Singleline, TimeoutMs)]
	private static partial Regex RectanglePattern();

	[GeneratedRegex(@"^([\w-]+)\((""[^""]*""|.+?)\)", RegexOptions.None, TimeoutMs)]
	private static partial Regex RoundedPattern();

	[GeneratedRegex(@"^([\w-]+)\{(""[^""]*""|.+?)\}", RegexOptions.None, TimeoutMs)]
	private static partial Regex DiamondPattern();

	[GeneratedRegex(@"^([\w]+(?:-[\w]+)*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex BareNodePattern();

	private static readonly (Func<Regex> Pattern, NodeShape Shape)[] NodePatterns =
	[
		(DoubleCirclePattern, NodeShape.DoubleCircle),
		(StadiumPattern, NodeShape.Stadium),
		(CirclePattern, NodeShape.Circle),
		(SubroutinePattern, NodeShape.Subroutine),
		(CylinderPattern, NodeShape.Cylinder),
		(ParallelogramPattern, NodeShape.Parallelogram),
		(ParallelogramAltPattern, NodeShape.ParallelogramAlt),
		(TrapezoidPattern, NodeShape.Trapezoid),
		(TrapezoidAltPattern, NodeShape.TrapezoidAlt),
		(AsymmetricPattern, NodeShape.Asymmetric),
		(HexagonPattern, NodeShape.Hexagon),
		(RectanglePattern, NodeShape.Rectangle),
		(RoundedPattern, NodeShape.Rounded),
		(DiamondPattern, NodeShape.Diamond),
	];

	// ========================================================================
	// Parse entry point
	// ========================================================================

	internal static MermaidGraph Parse(string[] lines)
	{
		try
		{
			return ParseCore(JoinMultilineLabels(lines));
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException($"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.", ex);
		}
	}

	private static string[] JoinMultilineLabels(string[] lines)
	{
		var result = new List<string>(lines.Length);
		for (var i = 0; i < lines.Length; i++)
		{
			var line = lines[i];
			if (line.Contains("[\"") && !line.Contains("\"]"))
			{
				while (++i < lines.Length)
				{
					line = line + "\n" + lines[i];
					if (lines[i].Contains("\"]"))
						break;
				}
			}
			else if (line.IndexOf("@{", StringComparison.Ordinal) is >= 0 and var open && line.IndexOf('}', open) < 0)
			{
				while (++i < lines.Length)
				{
					line = line + "\n" + lines[i];
					if (lines[i].Contains('}'))
						break;
				}
			}
			result.Add(line);
		}
		return result.ToArray();
	}

	private static MermaidGraph ParseCore(string[] lines)
	{
		var headerMatch = HeaderPattern().Match(lines[0]);
		if (!headerMatch.Success)
			throw new MermaidParseException($"Invalid mermaid header: \"{lines[0]}\". Expected \"graph TD\", \"flowchart LR\", etc.");

		var direction = Enum.Parse<Direction>(headerMatch.Groups[1].Value.ToUpperInvariant());

		var nodes = new Dictionary<string, MermaidNode>();
		var edges = new List<MermaidEdge>();
		var subgraphs = new List<MermaidSubgraph>();
		var classDefs = new Dictionary<string, IReadOnlyDictionary<string, string>>();
		var classAssignments = new Dictionary<string, string>();
		var nodeStyles = new Dictionary<string, Dictionary<string, string>>();
		var edgeStyles = new Dictionary<int, Dictionary<string, string>>();
		IReadOnlyDictionary<string, string>? defaultEdgeStyle = null;
		var subgraphStack = new Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)>();
		var collapsedIds = new HashSet<string>(StringComparer.Ordinal);

		for (var i = 1; i < lines.Length; i++)
		{
			var line = lines[i];

			var classDefMatch = ClassDefPattern().Match(line);
			if (classDefMatch.Success)
			{
				var name = classDefMatch.Groups[1].Value;
				var propsStr = classDefMatch.Groups[2].Value;
				classDefs[name] = ParseStyleProps(propsStr);
				continue;
			}

			var classAssignMatch = ClassAssignPattern().Match(line);
			if (classAssignMatch.Success)
			{
				var nodeIds = classAssignMatch.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
				var className = classAssignMatch.Groups[2].Value;
				foreach (var id in nodeIds)
					classAssignments[id] = className;
				continue;
			}

			var styleMatch = StylePattern().Match(line);
			if (styleMatch.Success)
			{
				var nodeIds = styleMatch.Groups[1].Value.Split(',', StringSplitOptions.TrimEntries);
				var props = ParseStyleProps(styleMatch.Groups[2].Value);
				foreach (var id in nodeIds)
				{
					if (!nodeStyles.TryGetValue(id, out var existing))
					{
						existing = [];
						nodeStyles[id] = existing;
					}
					foreach (var kvp in props)
						existing[kvp.Key] = kvp.Value;
				}
				continue;
			}

			var linkStyleMatch = LinkStylePattern().Match(line);
			if (linkStyleMatch.Success)
			{
				var indexPart = linkStyleMatch.Groups[1].Value.Trim();
				var props = ParseStyleProps(linkStyleMatch.Groups[2].Value);
				if (indexPart.Equals("default", StringComparison.OrdinalIgnoreCase))
				{
					defaultEdgeStyle = props;
				}
				else
				{
					var indices = indexPart.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
					foreach (var idxStr in indices)
					{
						if (int.TryParse(idxStr, out var idx))
						{
							if (!edgeStyles.TryGetValue(idx, out var existing))
							{
								existing = [];
								edgeStyles[idx] = existing;
							}
							foreach (var kvp in props)
								existing[kvp.Key] = kvp.Value;
						}
					}
				}
				continue;
			}

			var dirMatch = DirectionPattern().Match(line);
			if (dirMatch.Success && subgraphStack.Count > 0)
			{
				var current = subgraphStack.Pop();
				current.Dir = Enum.Parse<Direction>(dirMatch.Groups[1].Value.ToUpperInvariant());
				subgraphStack.Push(current);
				continue;
			}

			var subgraphMatch = SubgraphPattern().Match(line);
			if (subgraphMatch.Success)
			{
				var rest = subgraphMatch.Groups[1].Value.Trim();
				var bracketMatch = SubgraphBracketPattern().Match(rest);
				string id, label;
				if (bracketMatch.Success)
				{
					id = bracketMatch.Groups[1].Value;
					label = MultilineUtils.NormalizeBrTags(bracketMatch.Groups[2].Value);
				}
				else
				{
					label = MultilineUtils.NormalizeBrTags(rest);
					id = Regex.Replace(rest.Replace(' ', '_'), @"[^\w]", "", RegexOptions.None, TimeSpan.FromSeconds(2));
				}
				subgraphStack.Push((id, label, [], [], null));
				continue;
			}

			if (line == "end")
			{
				if (subgraphStack.Count > 0)
				{
					var completed = subgraphStack.Pop();
					var sg = new MermaidSubgraph
					{
						Id = completed.Id,
						Label = completed.Label,
						NodeIds = completed.NodeIds,
						Children = completed.Children,
						Direction = completed.Dir
					};
					if (subgraphStack.Count > 0)
					{
						var parent = subgraphStack.Pop();
						parent.Children.Add(sg);
						subgraphStack.Push(parent);
					}
					else
					{
						subgraphs.Add(sg);
					}
				}
				continue;
			}

			ParseEdgeLine(line, nodes, edges, classAssignments, subgraphStack, collapsedIds);
		}

		if (collapsedIds.Count > 0)
		{
			var owners = new Dictionary<string, string>(StringComparer.Ordinal);
			subgraphs = CollapseSubgraphs(subgraphs, collapsedIds, nodes, owners).Subgraphs;
			edgeStyles = RedirectIntoCollapsed(edges, edgeStyles, owners);
		}

		var subgraphMap = new Dictionary<string, MermaidSubgraph>();
		CollectSubgraphMap(subgraphs, subgraphMap);

		var redirections = new Dictionary<int, (string? SourceSg, string? TargetSg)>();
		for (var e = 0; e < edges.Count; e++)
		{
			var edge = edges[e];
			var src = ResolveSubgraphRef(edge.Source, subgraphMap);
			var tgt = ResolveSubgraphRef(edge.Target, subgraphMap);
			if (src != edge.Source || tgt != edge.Target)
			{
				redirections[e] = (src != edge.Source ? edge.Source : null, tgt != edge.Target ? edge.Target : null);
				edges[e] = new MermaidEdge(src, tgt, edge.Label, edge.Style, edge.HasArrowStart, edge.HasArrowEnd, edge.MinLength);
			}
		}

		foreach (var (sgId, sgDef) in subgraphMap)
		{
			// An empty subgraph has nothing to redirect an edge to, so the implicit node stays as the endpoint
			if (sgDef.NodeIds.Count == 0 && sgDef.Children.Count == 0)
				continue;
			if (nodes.TryGetValue(sgId, out var n) && n.Label == sgId && n.Shape == NodeShape.Rectangle)
				_ = nodes.Remove(sgId);
		}

		if (classDefs.ContainsKey("default"))
		{
			foreach (var nodeId in nodes.Keys)
			{
				_ = classAssignments.TryAdd(nodeId, "default");
			}
		}

		return new MermaidGraph
		{
			Direction = direction,
			Nodes = nodes,
			NodeOrder = BuildSubgraphFirstNodeOrder(nodes, subgraphMap),
			Edges = edges,
			Subgraphs = subgraphs,
			ClassDefs = classDefs,
			ClassAssignments = classAssignments,
			NodeStyles = nodeStyles.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyDictionary<string, string>)kvp.Value),
			EdgeStyles = edgeStyles.ToDictionary(kvp => kvp.Key, kvp => (IReadOnlyDictionary<string, string>)kvp.Value),
			DefaultEdgeStyle = defaultEdgeStyle,
			SubgraphEdgeRedirections = redirections,
			IsFlowchart = true
		};
	}

	// Subgraph members come first (alphabetical by subgraph ID), matching dagre's
	// node-insertion order so the DFS cycle-breaker picks the same back-edges.
	private static List<string> BuildSubgraphFirstNodeOrder(Dictionary<string, MermaidNode> nodes, Dictionary<string, MermaidSubgraph> subgraphMap)
	{
		var order = new List<string>(nodes.Count);
		var seen = new HashSet<string>(StringComparer.Ordinal);

		foreach (var sg in subgraphMap.Values.OrderBy(sg => sg.Id, StringComparer.Ordinal))
		{
			foreach (var nodeId in sg.NodeIds)
			{
				if (nodes.ContainsKey(nodeId) && seen.Add(nodeId))
					order.Add(nodeId);
			}
		}

		foreach (var nodeId in nodes.Keys)
		{
			if (seen.Add(nodeId))
				order.Add(nodeId);
		}

		return order;
	}

	// ========================================================================
	// Edge line parsing
	// ========================================================================

	private static void ParseEdgeLine(
		string line,
		Dictionary<string, MermaidNode> nodes,
		List<MermaidEdge> edges,
		Dictionary<string, string> classAssignments,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack,
		HashSet<string> collapsedIds
	)
	{
		var remaining = line.Trim();

		var (firstIds, rest) = ConsumeNodeGroup(remaining, nodes, classAssignments, subgraphStack, collapsedIds);
		if (firstIds.Count == 0)
			return;

		remaining = rest.Trim();
		var prevGroupIds = firstIds;

		while (remaining.Length > 0)
		{
			bool hasArrowStart;
			string arrowOp;
			string? edgeLabel;
			int consumedLength;

			var inlineMatch = InlineTextArrowPattern().Match(remaining);
			if (inlineMatch.Success)
			{
				hasArrowStart = inlineMatch.Groups[1].Success;
				arrowOp = inlineMatch.Groups[4].Value;
				var rawLabel = inlineMatch.Groups[3].Value.Trim();
				edgeLabel = rawLabel.Length > 0 ? MultilineUtils.NormalizeBrTags(rawLabel) : null;
				consumedLength = inlineMatch.Length;
			}
			else
			{
				var arrowMatch = ArrowPattern().Match(remaining);
				if (!arrowMatch.Success)
					break;

				hasArrowStart = arrowMatch.Groups[1].Success;
				arrowOp = arrowMatch.Groups[2].Value;
				var rawLabel = arrowMatch.Groups[3].Success ? arrowMatch.Groups[3].Value.Trim() : null;
				edgeLabel = rawLabel is { Length: > 0 } ? MultilineUtils.NormalizeBrTags(rawLabel) : null;
				consumedLength = arrowMatch.Length;
			}

			remaining = remaining[consumedLength..].Trim();

			var style = ArrowStyleFromOp(arrowOp);
			var hasArrowEnd = arrowOp.EndsWith('>');
			var minLength = 1; // mermaid.js v12: extra dashes (----> vs -->) are visual-only, not layout rank

			var (nextIds, nextRest) = ConsumeNodeGroup(remaining, nodes, classAssignments, subgraphStack, collapsedIds);
			if (nextIds.Count == 0)
				break;

			remaining = nextRest.Trim();

			foreach (var sourceId in prevGroupIds)
			{
				foreach (var targetId in nextIds)
				{
					edges.Add(new MermaidEdge(sourceId, targetId, edgeLabel, style, hasArrowStart, hasArrowEnd, minLength));
				}
			}

			prevGroupIds = nextIds;
		}
	}

	private static (List<string> Ids, string Remaining) ConsumeNodeGroup(
		string text,
		Dictionary<string, MermaidNode> nodes,
		Dictionary<string, string> classAssignments,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack,
		HashSet<string> collapsedIds
	)
	{
		var (firstId, rest) = ConsumeNode(text, nodes, classAssignments, subgraphStack, collapsedIds);
		if (firstId is null)
			return ([], text);

		var ids = new List<string> { firstId };
		var remaining = rest.Trim();

		while (remaining.StartsWith('&'))
		{
			remaining = remaining[1..].Trim();
			var (nextId, nextRest) = ConsumeNode(remaining, nodes, classAssignments, subgraphStack, collapsedIds);
			if (nextId is null)
				break;
			ids.Add(nextId);
			remaining = nextRest.Trim();
		}

		return (ids, remaining);
	}

	private static (string? Id, string Remaining) ConsumeNode(
		string text,
		Dictionary<string, MermaidNode> nodes,
		Dictionary<string, string> classAssignments,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack,
		HashSet<string> collapsedIds
	)
	{
		string? id = null;
		var remaining = text;

		foreach (var (patternFunc, shape) in NodePatterns)
		{
			var match = patternFunc().Match(text);
			if (match.Success)
			{
				id = match.Groups[1].Value;
				var (label, isMarkdown) = ReadLabel(match.Groups[2].Value);
				// mermaid.js lets the latest shaped definition set the label and shape, even after a bare reference.
				nodes[id] = new MermaidNode(id, label, shape, isMarkdown);
				AddToCurrentSubgraph(subgraphStack, id);
				remaining = text[match.Length..];
				break;
			}
		}

		if (id is null)
		{
			var bareMatch = BareNodePattern().Match(text);
			if (bareMatch.Success)
			{
				id = bareMatch.Groups[1].Value;
				if (nodes.ContainsKey(id))
					AddToCurrentSubgraph(subgraphStack, id);
				else
					RegisterNode(nodes, subgraphStack, new MermaidNode(id, id, NodeShape.Rectangle));
				remaining = text[bareMatch.Length..];

				if (remaining.StartsWith("@{", StringComparison.Ordinal) && ReadProperties(remaining) is { } parsed)
				{
					remaining = remaining[parsed.Length..];
					ApplyProperties(id, parsed.Props, nodes, collapsedIds);
				}
			}
		}

		if (id is null)
			return (null, text);

		var classMatch = ClassShorthandPattern().Match(remaining);
		if (classMatch.Success)
		{
			classAssignments[id] = classMatch.Groups[1].Value;
			remaining = remaining[classMatch.Length..];
		}

		return (id, remaining);
	}

	// ========================================================================
	// Helpers
	// ========================================================================

	private static void RegisterNode(
		Dictionary<string, MermaidNode> nodes,
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack,
		MermaidNode node
	)
	{
		_ = nodes.TryAdd(node.Id, node);
		AddToCurrentSubgraph(subgraphStack, node.Id);
	}

	private static void AddToCurrentSubgraph(
		Stack<(string Id, string Label, List<string> NodeIds, List<MermaidSubgraph> Children, Direction? Dir)> subgraphStack,
		string nodeId
	)
	{
		if (subgraphStack.Count > 0)
		{
			var current = subgraphStack.Peek();
			if (!current.NodeIds.Contains(nodeId))
				current.NodeIds.Add(nodeId);
		}
	}

	private static EdgeStyle ArrowStyleFromOp(string op)
	{
		if (op.Contains('~'))
			return EdgeStyle.Invisible;
		if (op.Contains('.'))
			return EdgeStyle.Dotted;
		if (op.Contains('='))
			return EdgeStyle.Thick;
		return EdgeStyle.Solid;
	}

	private static void CollectSubgraphMap(IReadOnlyList<MermaidSubgraph> sgs, Dictionary<string, MermaidSubgraph> map)
	{
		foreach (var sg in sgs)
		{
			map[sg.Id] = sg;
			CollectSubgraphMap(sg.Children, map);
		}
	}

	private static string ResolveSubgraphRef(string id, Dictionary<string, MermaidSubgraph> sgMap)
	{
		while (sgMap.TryGetValue(id, out var sg))
		{
			string? firstNonSg = null;
			foreach (var child in sg.NodeIds)
			{
				if (!sgMap.ContainsKey(child))
				{
					firstNonSg = child;
					break;
				}
			}
			if (firstNonSg != null)
				return firstNonSg;
			if (sg.NodeIds.Count > 0)
				id = sg.NodeIds[0];
			else if (sg.Children.Count > 0)
				id = sg.Children[0].Id;
			else
				break;
		}
		return id;
	}

	private static (string Label, bool IsMarkdown) ReadLabel(string rawLabel)
	{
		if (rawLabel.Length >= 2 && rawLabel[0] == '"' && rawLabel[^1] == '"')
			rawLabel = rawLabel[1..^1];
		var isMarkdown = rawLabel.Length >= 2 && rawLabel.StartsWith('`') && rawLabel.EndsWith('`');
		var label = isMarkdown
			? MultilineUtils.DecodeEntities(rawLabel[1..^1].Trim().Replace("\\n", "\n"))
			: MultilineUtils.NormalizeBrTags(rawLabel).Replace("\\n", "\n");
		return (label, isMarkdown);
	}

	/// <summary>
	/// Reads the <c>@{ key: value, ... }</c> block at the start of <paramref name="text"/>. Values may be quoted, and a
	/// quoted value may hold commas and braces. Null when the block is not closed.
	/// </summary>
	internal static (Dictionary<string, string> Props, int Length)? ReadProperties(string text)
	{
		var props = new Dictionary<string, string>(StringComparer.Ordinal);
		var entry = new System.Text.StringBuilder();
		var quote = '\0';
		for (var i = 2; i < text.Length; i++)
		{
			var c = text[i];
			if (quote != '\0')
			{
				if (c == quote)
					quote = '\0';
				_ = entry.Append(c);
				continue;
			}
			switch (c)
			{
				case '"' or '\'':
					quote = c;
					_ = entry.Append(c);
					break;
				case ',' or '\n':
					AddProperty(props, entry.ToString());
					_ = entry.Clear();
					break;
				case '}':
					AddProperty(props, entry.ToString());
					return (props, i + 1);
				default:
					_ = entry.Append(c);
					break;
			}
		}
		return null;
	}

	private static void AddProperty(Dictionary<string, string> props, string entry)
	{
		var colon = entry.IndexOf(':');
		if (colon <= 0)
			return;
		var key = entry[..colon].Trim().Trim('"', '\'');
		var value = entry[(colon + 1)..].Trim();
		if (value.Length >= 2 && value[0] == '\'' && value[^1] == '\'')
			value = value[1..^1];
		if (key.Length > 0)
			props[key] = value;
	}

	/// <summary>
	/// Applies <c>shape</c>, <c>label</c> and the icon and image keys (<c>icon</c>, <c>img</c>, <c>form</c>, <c>pos</c>,
	/// <c>w</c>, <c>h</c>, <c>constraint</c>) to the node, and records <c>view: collapsed</c>. As in mermaid.js, an image
	/// wins over an icon and both win over <c>shape</c>, and an icon or image node without a label drops its id as label.
	/// </summary>
	private static void ApplyProperties(string id, Dictionary<string, string> props, Dictionary<string, MermaidNode> nodes, HashSet<string> collapsedIds)
	{
		if (props.TryGetValue("view", out var view) && view.Trim('"') == "collapsed")
			_ = collapsedIds.Add(id);

		var hasShape = props.TryGetValue("shape", out var shapeName);
		var hasLabel = props.TryGetValue("label", out var rawLabel);
		var asset = ReadAsset(props, nodes[id].Asset);
		if (!hasShape && !hasLabel && asset is null)
			return;

		var node = nodes[id];
		var shape = hasShape && FlowchartShapeNames.TryGet(shapeName!.Trim('"'), out var named) ? named : node.Shape;
		var (label, isMarkdown) = hasLabel ? ReadLabel(rawLabel!) : (node.Label, node.IsMarkdown);
		if (asset is not null)
		{
			shape = asset.Kind == NodeAssetKind.Image ? NodeShape.Image : NodeShape.Icon;
			if (string.IsNullOrWhiteSpace(label) || (!hasLabel && label == id))
				(label, isMarkdown) = (string.Empty, false);
		}
		if (!FlowchartShapeNames.DrawsLabel(shape))
			(label, isMarkdown) = (string.Empty, false);
		nodes[id] = new MermaidNode(id, label, shape, isMarkdown) { Asset = asset };
	}

	/// <summary>Largest <c>w</c> or <c>h</c> an icon or image node takes, so a source cannot ask for a huge canvas.</summary>
	private const double MaxAssetSize = 1000;

	/// <summary>The icon or image the properties ask for, on top of the node's earlier one. Null when there is neither.</summary>
	private static NodeAsset? ReadAsset(Dictionary<string, string> props, NodeAsset? earlier)
	{
		NodeAsset? asset = earlier;
		if (props.TryGetValue("img", out var img) && Unquote(img) is { Length: > 0 } src)
			asset = new NodeAsset(NodeAssetKind.Image, src);
		else if (props.TryGetValue("icon", out var icon) && Unquote(icon) is { Length: > 0 } name && asset?.Kind != NodeAssetKind.Image)
			asset = new NodeAsset(NodeAssetKind.Icon, name);
		if (asset is null)
			return null;

		if (props.TryGetValue("form", out var form))
		{
			asset = asset with
			{
				Form = Unquote(form) switch
				{
					"square" => IconForm.Square,
					"circle" => IconForm.Circle,
					"rounded" => IconForm.Rounded,
					_ => IconForm.None,
				},
			};
		}
		if (props.TryGetValue("pos", out var pos))
			asset = asset with { LabelOnTop = Unquote(pos) == "t" };
		if (props.TryGetValue("constraint", out var constraint))
			asset = asset with { KeepAspectRatio = Unquote(constraint) == "on" };
		if (props.TryGetValue("w", out var w) && ReadSize(w) is { } width)
			asset = asset with { Width = width };
		if (props.TryGetValue("h", out var h) && ReadSize(h) is { } height)
			asset = asset with { Height = height };
		return asset;
	}

	private static string Unquote(string value) => value.Trim().Trim('"').Trim();

	private static double? ReadSize(string value) =>
		double.TryParse(
			Unquote(value),
			System.Globalization.NumberStyles.Float,
			System.Globalization.CultureInfo.InvariantCulture,
			out var size
		) && double.IsFinite(size) && size > 0 ? Math.Min(size, MaxAssetSize) : null;

	/// <summary>
	/// Replaces each collapsed subgraph with one <see cref="NodeShape.CollapsedGroup"/> node carrying its title, in the
	/// parent's place. A collapse inside a collapsed subgraph resolves to the outermost one. <paramref name="owners"/>
	/// gets every hidden node and subgraph id, mapped to the collapsed node that replaces it.
	/// </summary>
	private static (List<MermaidSubgraph> Subgraphs, List<string> Promoted) CollapseSubgraphs(
		IReadOnlyList<MermaidSubgraph> subgraphs,
		HashSet<string> collapsedIds,
		Dictionary<string, MermaidNode> nodes,
		Dictionary<string, string> owners
	)
	{
		var kept = new List<MermaidSubgraph>(subgraphs.Count);
		var promoted = new List<string>();
		foreach (var sg in subgraphs)
		{
			if (collapsedIds.Contains(sg.Id))
			{
				foreach (var hidden in Descendants(sg))
				{
					owners[hidden] = sg.Id;
					if (hidden != sg.Id)
						_ = nodes.Remove(hidden);
				}
				nodes[sg.Id] = new MermaidNode(sg.Id, sg.Label, NodeShape.CollapsedGroup);
				promoted.Add(sg.Id);
				continue;
			}
			var (children, fromChildren) = CollapseSubgraphs(sg.Children, collapsedIds, nodes, owners);
			var nodeIds = sg.NodeIds.Where(n => !owners.ContainsKey(n) || owners[n] == n).Concat(fromChildren).Distinct().ToList();
			kept.Add(sg with { Children = children, NodeIds = nodeIds });
		}
		return (kept, promoted);
	}

	private static IEnumerable<string> Descendants(MermaidSubgraph sg)
	{
		foreach (var id in sg.NodeIds)
			yield return id;
		foreach (var child in sg.Children)
		{
			yield return child.Id;
			foreach (var id in Descendants(child))
				yield return id;
		}
	}

	/// <summary>
	/// Points every edge end inside a collapsed subgraph at its collapsed node and drops the edges that end up inside one,
	/// then moves the <c>linkStyle</c> indexes with the edges that remain.
	/// </summary>
	private static Dictionary<int, Dictionary<string, string>> RedirectIntoCollapsed(
		List<MermaidEdge> edges,
		Dictionary<int, Dictionary<string, string>> edgeStyles,
		Dictionary<string, string> owners
	)
	{
		var restyled = new Dictionary<int, Dictionary<string, string>>();
		var kept = new List<MermaidEdge>(edges.Count);
		for (var i = 0; i < edges.Count; i++)
		{
			var edge = edges[i];
			var source = owners.GetValueOrDefault(edge.Source, edge.Source);
			var target = owners.GetValueOrDefault(edge.Target, edge.Target);
			if (source == target && (source != edge.Source || target != edge.Target))
				continue;
			if (edgeStyles.TryGetValue(i, out var style))
				restyled[kept.Count] = style;
			kept.Add(edge with { Source = source, Target = target });
		}
		edges.Clear();
		edges.AddRange(kept);
		return restyled;
	}

	private static IReadOnlyDictionary<string, string> ParseStyleProps(string propsStr)
	{
		var props = new Dictionary<string, string>();
		foreach (var pair in propsStr.Split(','))
		{
			var colonIdx = pair.IndexOf(':');
			if (colonIdx <= 0)
				continue;
			var key = pair[..colonIdx].Trim();
			var val = pair[(colonIdx + 1)..].Trim();
			if (key.Length > 0 && val.Length > 0)
				props[key] = val;
		}
		return props;
	}
}
