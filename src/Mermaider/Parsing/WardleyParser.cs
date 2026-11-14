using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Parses <c>wardley-beta</c> maps with the grammar of mermaid 12.3.0 (<c>packages/parser/src/language/wardley</c>). The
/// lexer tries mermaid's terminals in mermaid's order, so a name that starts with a keyword such as <c>note</c> or a
/// strategy such as <c>buy</c> is split the same way and refused the same way. Front matter and init directives are
/// blanked first, so line numbers are the source's. Input mermaid refuses throws a <see cref="MermaidParseException"/>
/// naming the line; input mermaid accepts but does not draw is listed in <see cref="WardleyDiagram.Unused"/>.
/// </summary>
internal static partial class WardleyParser
{
	private const int TimeoutMs = 2000;

	[GeneratedRegex(@"\G[0-9]+\.[0-9]+", RegexOptions.None, TimeoutMs)]
	private static partial Regex WardleyNumber();

	[GeneratedRegex(@"\G(?:-->|-\.->|>|\+'[^']*'<>|\+'[^']*'<|\+'[^']*'>)", RegexOptions.None, TimeoutMs)]
	private static partial Regex LinkArrow();

	[GeneratedRegex(@"\G(?!title\s|accTitle|accDescr)[A-Za-z](?:[A-Za-z0-9_()&]|-(?!>))*(?:[ \t]+[A-Za-z(](?:[A-Za-z0-9_()&]|-(?!>))*)*", RegexOptions.None, TimeoutMs)]
	private static partial Regex NameWithSpaces();

	[GeneratedRegex(@"\GaccDescr(?:[\t ]*:([^\n\r]*?(?=%%)|[^\n\r]*)|\s*\{([^}]*)\})", RegexOptions.None, TimeoutMs)]
	private static partial Regex AccDescr();

	[GeneratedRegex(@"\GaccTitle[\t ]*:(?:[^\n\r]*?(?=%%)|[^\n\r]*)", RegexOptions.None, TimeoutMs)]
	private static partial Regex AccTitle();

	[GeneratedRegex(@"\Gtitle(?:[\t ]([^\n\r]*?)(?=%%)|[\t ]([^\n\r]*)|)", RegexOptions.None, TimeoutMs)]
	private static partial Regex Title();

	[GeneratedRegex(@"\G(?:0|[1-9][0-9]*(?!\.))", RegexOptions.None, TimeoutMs)]
	private static partial Regex Int();

	[GeneratedRegex(@"\G(?:""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')", RegexOptions.None, TimeoutMs)]
	private static partial Regex QuotedString();

	[GeneratedRegex(@"\G[A-Za-z0-9_](?:[-A-Za-z0-9_]*[A-Za-z0-9_])?", RegexOptions.None, TimeoutMs)]
	private static partial Regex Id();

	[GeneratedRegex(@"[\t ]{2,}", RegexOptions.None, TimeoutMs)]
	private static partial Regex RunOfSpaces();

	private static readonly string[] Ports = ["+<>", "+>", "+<"];

	private static readonly string[] Strategies = ["build", "buy", "outsource", "market"];

	private static readonly string[] Keywords =
	[
		"wardley-beta",
		"size",
		"evolution",
		"anchor",
		"component",
		"label",
		"inertia",
		"evolve",
		"pipeline",
		"note",
		"annotations",
		"annotation",
		"accelerator",
		"deaccelerator",
	];

	private enum T
	{
		Punct,
		Number,
		Arrow,
		LinkPort,
		LinkArrow,
		LinkLabel,
		Strategy,
		Keyword,
		Name,
		AccDescr,
		AccTitle,
		Title,
		Int,
		String,
		Id,
		Newline,
		Eof,
	}

	private readonly record struct Token(T Kind, string Text, int Line, string? Value = null)
	{
		public bool Is(T kind, string text) => Kind == kind && Text == text;

		public bool IsName => Kind is T.Name or T.String or T.Id;
	}

	internal static WardleyDiagram Parse(string source)
	{
		try
		{
			var (text, _) = DiagramPreprocessor.BlankConfig(source, "wardley-beta");
			var lines = text.Split('\n');
			var statements = new Reader(Lex(text), lines).Read();
			return Build(statements, lines);
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException($"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.", ex);
		}
	}

	private static MermaidParseException Error(int line, string message, string? near = null) =>
		new($"Wardley map, line {line}: {message}" + (near is { Length: > 0 } ? $" (near '{near}')" : ""));

	// ------------------------------------------------------------------
	// Lexer: mermaid's punctuation keywords first (a '-' gives way to a longer arrow), then the wardley terminals in
	// grammar order, then the common ones. Spaces, tabs and %% comments are hidden.
	// ------------------------------------------------------------------

	private static List<Token> Lex(string text)
	{
		var tokens = new List<Token>();
		var line = 1;
		var p = 0;
		while (p < text.Length)
		{
			var c = text[p];
			if (c is ' ' or '\t')
			{
				p++;
				continue;
			}

			if (c == '%' && p + 1 < text.Length && text[p + 1] == '%')
			{
				while (p < text.Length && text[p] != '\n' && text[p] != '\r')
					p++;
				continue;
			}

			if (c == '\n')
			{
				tokens.Add(new Token(T.Newline, "\n", line));
				p++;
				line++;
				continue;
			}

			var start = line;
			var (kind, length, value) = Next(text, p);
			if (length == 0)
				throw Error(line, $"unexpected character '{c}'");
			var lexeme = text.Substring(p, length);
			tokens.Add(new Token(kind, lexeme, start, value));
			foreach (var ch in lexeme)
				if (ch == '\n')
					line++;
			p += length;
		}

		tokens.Add(new Token(T.Eof, "", line));
		return tokens;
	}

	private static (T Kind, int Length, string? Value) Next(string text, int p)
	{
		var c = text[p];
		if (c is '[' or ']' or ',' or '(' or ')' or '{' or '}' or '@' or '/')
			return (T.Punct, 1, null);
		if (c == '-')
		{
			if (Starts(text, p, "->"))
				return (T.Arrow, 2, null);
			var arrow = LinkArrow().Match(text, p);
			return arrow.Success ? (T.LinkArrow, arrow.Length, null) : (T.Punct, 1, null);
		}

		Match m;
		if ((m = WardleyNumber().Match(text, p)).Success)
			return (T.Number, m.Length, null);
		foreach (var port in Ports)
			if (Starts(text, p, port))
				return (T.LinkPort, port.Length, null);
		if ((m = LinkArrow().Match(text, p)).Success)
			return (T.LinkArrow, m.Length, null);
		if (c == ';')
		{
			var end = p;
			while (end < text.Length && text[end] is not ('\n' or '\r'))
				end++;
			if (end > p + 1)
				return (T.LinkLabel, end - p, text[(p + 1)..end].Trim());
		}

		foreach (var s in Strategies)
			if (Starts(text, p, s))
				return (T.Strategy, s.Length, s);
		foreach (var k in Keywords)
			if (Starts(text, p, k))
				return (T.Keyword, k.Length, k);
		if ((m = NameWithSpaces().Match(text, p)).Success)
			return (T.Name, m.Length, m.Value);
		if ((m = AccDescr().Match(text, p)).Success)
			return (T.AccDescr, m.Length, null);
		if ((m = AccTitle().Match(text, p)).Success)
			return (T.AccTitle, m.Length, null);
		if ((m = Title().Match(text, p)).Success)
		{
			var title = m.Groups[1].Success ? m.Groups[1].Value : m.Groups[2].Value;
			return (T.Title, m.Length, RunOfSpaces().Replace(title.Trim(), " "));
		}

		if ((m = Int().Match(text, p)).Success)
			return (T.Int, m.Length, m.Value);
		if ((m = QuotedString().Match(text, p)).Success)
			return (T.String, m.Length, Unquote(m.Value));
		if ((m = Id().Match(text, p)).Success)
			return (T.Id, m.Length, m.Value);
		return (T.Punct, 0, null);
	}

	private static bool Starts(string text, int p, string s) => string.CompareOrdinal(text, p, s, 0, s.Length) == 0;

	/// <summary>Langium's STRING conversion: drop the quotes and read each backslash escape.</summary>
	private static string Unquote(string quoted)
	{
		var sb = new StringBuilder(quoted.Length);
		for (var i = 1; i < quoted.Length - 1; i++)
		{
			var c = quoted[i];
			if (c != '\\' || i + 1 >= quoted.Length - 1)
			{
				_ = sb.Append(c);
				continue;
			}

			var e = quoted[++i];
			_ = sb.Append(e switch
			{
				'b' => '\b',
				'f' => '\f',
				'n' => '\n',
				'r' => '\r',
				't' => '\t',
				'v' => '\v',
				'0' => '\0',
				_ => e,
			});
		}

		return sb.ToString();
	}

	// ------------------------------------------------------------------
	// Statements, as mermaid's AST keeps them
	// ------------------------------------------------------------------

	private sealed record LabelOffset(int X, int Y);

	private sealed record Statements
	{
		public List<(int Line, string Title)> Titles { get; } = [];
		public List<(int Line, int Width, int Height)> Sizes { get; } = [];
		public List<(int Line, List<(string Name, double? Boundary)> Stages)> Evolutions { get; } = [];
		public List<(int Line, string Name, double Visibility, double Evolution)> Anchors { get; } = [];
		public List<ComponentStatement> Components { get; } = [];
		public List<LinkStatement> Links { get; } = [];
		public List<(int Line, string Name, double Target)> Evolves { get; } = [];
		public List<(int Line, string Parent, List<(int Line, string Name, double Evolution, LabelOffset? Label)> Components)> Pipelines { get; } = [];
		public List<(int Line, string Text, double Visibility, double Evolution)> Notes { get; } = [];
		public List<(int Line, double X, double Y)> AnnotationBoxes { get; } = [];
		public List<(int Line, int Number, double X, double Y, string Text)> Annotations { get; } = [];
		public List<(int Line, string Name, double X, double Y, bool Deaccelerator)> Forces { get; } = [];
	}

	private sealed record ComponentStatement(int Line, string Name, double Visibility, double Evolution, LabelOffset? Label, string? Strategy, bool Inertia);

	private sealed record LinkStatement(int Line, string From, string? FromPort, string? Arrow, string To, string? ToPort, string? Label);

	private sealed class Reader(List<Token> tokens, string[] lines)
	{
		private int _i;

		private Token Peek(int ahead = 0) => tokens[Math.Min(_i + ahead, tokens.Count - 1)];

		private Token Take() => tokens[_i++];

		private MermaidParseException Unexpected(string expected)
		{
			var t = Peek();
			var near = t.Kind switch
			{
				T.Eof => "end of input",
				T.Newline => "end of line",
				_ => t.Text.Trim(),
			};
			return Error(t.Line, $"expected {expected}", near.Length > 40 ? near[..40] + "…" : near);
		}

		private Token Expect(T kind, string? text, string expected)
		{
			var t = Peek();
			if (t.Kind != kind || (text is not null && t.Text != text))
				throw Unexpected(expected);
			return Take();
		}

		private void Punct(char c) => Expect(T.Punct, c.ToString(), $"'{c}'");

		private string Name(string what)
		{
			if (!Peek().IsName)
				throw Unexpected(what);
			return Take().Value!;
		}

		private double Number(string what) => double.Parse(Expect(T.Number, null, what).Text, CultureInfo.InvariantCulture);

		private int Integer(string what) => int.Parse(Expect(T.Int, null, what).Text, CultureInfo.InvariantCulture);

		private double Coordinate(string what)
		{
			var t = Peek();
			if (t.Kind is not (T.Number or T.Int))
				throw Unexpected(what);
			_ = Take();
			return double.Parse(t.Text, CultureInfo.InvariantCulture);
		}

		private void Eol()
		{
			if (Peek().Kind == T.Eof)
				return;
			if (Peek().Kind != T.Newline)
				throw Unexpected("end of line");
			while (Peek().Kind == T.Newline)
				_ = Take();
		}

		internal Statements Read()
		{
			var s = new Statements();
			while (Peek().Kind == T.Newline)
				_ = Take();
			var header = Peek();
			if (!header.Is(T.Keyword, "wardley-beta"))
			{
				var first = lines[Math.Clamp(header.Line - 1, 0, lines.Length - 1)].Trim();
				throw Error(header.Line, "a Wardley map starts with 'wardley-beta'", first);
			}

			_ = Take();
			while (Peek().Kind != T.Eof)
			{
				var t = Peek();
				switch (t.Kind)
				{
					case T.Newline:
						_ = Take();
						break;
					case T.Title:
						s.Titles.Add((t.Line, Take().Value!));
						Eol();
						break;
					case T.AccTitle or T.AccDescr:
						_ = Take();
						Eol();
						break;
					case T.Keyword:
						Statement(s);
						break;
					case var _ when t.IsName:
						s.Links.Add(Link());
						break;
					default:
						throw Unexpected("a statement");
				}
			}

			return s;
		}

		private void Statement(Statements s)
		{
			var kw = Take();
			var line = kw.Line;
			switch (kw.Text)
			{
				case "size":
					{
						Punct('[');
						var w = Integer("a width");
						Punct(',');
						var h = Integer("a height");
						Punct(']');
						Eol();
						s.Sizes.Add((line, w, h));
						break;
					}
				case "evolution":
					{
						var stages = new List<(string, double?)> { Stage() };
						if (Peek().Kind != T.Arrow)
							throw Unexpected("'->' and another stage");
						while (Peek().Kind == T.Arrow)
						{
							_ = Take();
							stages.Add(Stage());
						}

						Eol();
						s.Evolutions.Add((line, stages));
						break;
					}
				case "anchor":
					{
						var name = Name("an anchor name");
						var (vis, evo) = Position();
						Eol();
						s.Anchors.Add((line, name, vis, evo));
						break;
					}
				case "component":
					{
						var name = Name("a component name");
						var (vis, evo) = Position();
						var label = Peek().Is(T.Keyword, "label") ? Label() : null;
						string? strategy = null;
						if (Peek().Is(T.Punct, "(") && Peek(1).Kind == T.Strategy)
						{
							_ = Take();
							strategy = Take().Value;
							Punct(')');
						}

						var inertia = false;
						if (Peek().Is(T.Keyword, "inertia"))
						{
							_ = Take();
							inertia = true;
						}
						else if (Peek().Is(T.Punct, "(") && Peek(1).Is(T.Keyword, "inertia"))
						{
							_ = Take();
							_ = Take();
							Punct(')');
							inertia = true;
						}

						Eol();
						s.Components.Add(new ComponentStatement(line, name, vis, evo, label, strategy, inertia));
						break;
					}
				case "evolve":
					{
						var name = Name("a component name");
						var target = Number("a decimal evolution such as 0.75");
						Eol();
						s.Evolves.Add((line, name, target));
						break;
					}
				case "pipeline":
					{
						var parent = Name("the pipeline's component name");
						Punct('{');
						if (Peek().Kind != T.Newline)
							throw Unexpected("a new line after '{'");
						while (Peek().Kind == T.Newline)
							_ = Take();
						var members = new List<(int, string, double, LabelOffset?)>();
						while (Peek().Is(T.Keyword, "component"))
						{
							var at = Take().Line;
							var name = Name("a component name");
							Punct('[');
							var evo = Number("a decimal evolution such as 0.5");
							Punct(']');
							var label = Peek().Is(T.Keyword, "label") ? Label() : null;
							Eol();
							members.Add((at, name, evo, label));
						}

						if (members.Count == 0)
							throw Unexpected("a pipeline 'component Name [evolution]'");
						Punct('}');
						Eol();
						s.Pipelines.Add((line, parent, members));
						break;
					}
				case "note":
					{
						var text = Expect(T.String, null, "a quoted note text").Value!;
						var (vis, evo) = Position();
						Eol();
						s.Notes.Add((line, text, vis, evo));
						break;
					}
				case "annotations":
					{
						Punct('[');
						var x = Coordinate("a number");
						Punct(',');
						var y = Coordinate("a number");
						Punct(']');
						Eol();
						s.AnnotationBoxes.Add((line, x, y));
						break;
					}
				case "annotation":
					{
						var number = Integer("an annotation number");
						Punct(',');
						Punct('[');
						var x = Coordinate("a number");
						Punct(',');
						var y = Coordinate("a number");
						Punct(']');
						var text = Expect(T.String, null, "a quoted annotation text").Value!;
						Eol();
						s.Annotations.Add((line, number, x, y, text));
						break;
					}
				case "accelerator" or "deaccelerator":
					{
						var name = Name("a name");
						var (x, y) = Position();
						Eol();
						s.Forces.Add((line, name, x, y, kw.Text == "deaccelerator"));
						break;
					}
				default:
					_i--;
					throw Unexpected("a statement");
			}
		}

		private (string Name, double? Boundary) Stage()
		{
			var name = Name("a stage name").Trim();
			double? boundary = null;
			if (Peek().Is(T.Punct, "@"))
			{
				_ = Take();
				boundary = Number("a decimal stage boundary such as 0.25");
			}

			if (Peek().Is(T.Punct, "/"))
			{
				_ = Take();
				name = $"{name} / {Name("a second stage name").Trim()}";
			}

			return (name, boundary);
		}

		private (double First, double Second) Position()
		{
			Punct('[');
			var first = Number("a decimal coordinate such as 0.5");
			Punct(',');
			var second = Number("a decimal coordinate such as 0.5");
			Punct(']');
			return (first, second);
		}

		private LabelOffset Label()
		{
			_ = Take();
			Punct('[');
			var x = SignedInt();
			Punct(',');
			var y = SignedInt();
			Punct(']');
			return new LabelOffset(x, y);
		}

		private int SignedInt()
		{
			var negative = false;
			if (Peek().Is(T.Punct, "-"))
			{
				_ = Take();
				negative = true;
			}

			var v = Integer("a whole-number label offset");
			return negative ? -v : v;
		}

		private LinkStatement Link()
		{
			var line = Peek().Line;
			var from = Name("a component name");
			var fromPort = Peek().Kind == T.LinkPort ? Take().Text : null;
			var arrow = Peek().Kind is T.LinkArrow or T.Arrow ? Take().Text : null;
			var to = Name("'->' and a component name");
			var toPort = Peek().Kind == T.LinkPort ? Take().Text : null;
			var label = Peek().Kind == T.LinkLabel ? Take().Value : null;
			Eol();
			return new LinkStatement(line, from, fromPort, arrow, to, toPort, label);
		}
	}

	// ------------------------------------------------------------------
	// mermaid's populateDb and WardleyBuilder
	// ------------------------------------------------------------------

	private static double ToPercent(double value, int line, string context)
	{
		var normalized = value <= 1 ? value * 100 : value;
		return normalized is < 0 or > 100
			? throw Error(line, $"{context} must be between 0-1 (decimal) or 0-100 (percentage), not {value.ToString(CultureInfo.InvariantCulture)}")
			: normalized;
	}

	private sealed class NodeDraft(string id, string label, WardleyNodeKind kind, WardleyPoint position, int line)
	{
		public string Id { get; } = id;
		public string Label { get; set; } = label;
		public WardleyNodeKind Kind { get; set; } = kind;
		public WardleyPoint Position { get; set; } = position;
		public double? OffsetX { get; set; }
		public double? OffsetY { get; set; }
		public bool Inertia { get; set; }
		public WardleySourcing Sourcing { get; set; }
		public bool IsPipelineParent { get; set; }
		public int Line { get; set; } = line;
	}

	private static WardleyDiagram Build(Statements s, string[] lines)
	{
		var unused = new List<WardleyUnusedInput>();

		void Unused(int line, string reason) => unused.Add(new WardleyUnusedInput(line, lines[line - 1].Trim(), reason));

		for (var i = 0; i < s.Sizes.Count - 1; i++)
			Unused(s.Sizes[i].Line, "a later 'size' line replaces this one");
		var size = s.Sizes.Count > 0 ? new WardleySize(s.Sizes[^1].Width, s.Sizes[^1].Height) : null;

		for (var i = 0; i < s.Evolutions.Count - 1; i++)
			Unused(s.Evolutions[i].Line, "a later 'evolution' line replaces this one");
		IReadOnlyList<string> stages = [];
		IReadOnlyList<double> boundaries = [];
		if (s.Evolutions.Count > 0)
		{
			var (line, list) = s.Evolutions[^1];
			stages = [.. list.Select(st => st.Name)];
			boundaries = [.. list.Where(st => st.Boundary is not null).Select(st => st.Boundary!.Value)];
			if (boundaries.Count > 0 && boundaries.Count != stages.Count)
				Unused(line, "stage boundaries ('@') are used only when every stage has one");
		}

		var nodes = new Dictionary<string, NodeDraft>(StringComparer.Ordinal);
		var order = new List<NodeDraft>();

		NodeDraft AddNode(string id, string label, WardleyNodeKind kind, WardleyPoint position, int line)
		{
			if (nodes.TryGetValue(id, out var existing))
			{
				Unused(existing.Line, $"a later declaration of '{id}' at line {line} replaces this one");
				existing.Label = label;
				existing.Kind = kind;
				existing.Position = position;
				existing.Line = line;
				return existing;
			}

			var node = new NodeDraft(id, label, kind, position, line);
			nodes[id] = node;
			order.Add(node);
			return node;
		}

		foreach (var (line, name, vis, evo) in s.Anchors)
			_ = AddNode(
				name,
				name,
				WardleyNodeKind.Anchor,
				new WardleyPoint(ToPercent(evo, line, $"Anchor \"{name}\" evolution"), ToPercent(vis, line, $"Anchor \"{name}\" visibility")),
				line
			);

		foreach (var c in s.Components)
		{
			var node = AddNode(
				c.Name,
				c.Name,
				WardleyNodeKind.Component,
				new WardleyPoint(
					ToPercent(c.Evolution, c.Line, $"Component \"{c.Name}\" evolution"),
					ToPercent(c.Visibility, c.Line, $"Component \"{c.Name}\" visibility")
				),
				c.Line
			);
			if (c.Label is not null)
			{
				node.OffsetX = c.Label.X;
				node.OffsetY = c.Label.Y;
			}

			node.Inertia = c.Inertia;
			node.Sourcing = c.Strategy switch
			{
				"build" => WardleySourcing.Build,
				"buy" => WardleySourcing.Buy,
				"outsource" => WardleySourcing.Outsource,
				"market" => WardleySourcing.Market,
				_ => WardleySourcing.None,
			};
		}

		var notes = s
			.Notes
			.Select(
				n => new WardleyNote(
					n.Text,
					new WardleyPoint(
						ToPercent(n.Evolution, n.Line, $"Note \"{n.Text}\" evolution"),
						ToPercent(n.Visibility, n.Line, $"Note \"{n.Text}\" visibility")
					)
				)
			)
			.ToList();

		var pipelines = new List<(string NodeId, List<string> Members, int Line)>();
		foreach (var (line, parent, members) in s.Pipelines)
		{
			if (!nodes.TryGetValue(parent, out var parentNode))
				throw Error(line, $"pipeline \"{parent}\" must name a component or anchor declared with coordinates");

			var replaced = pipelines.FindIndex(p => p.NodeId == parent);
			if (replaced >= 0)
			{
				Unused(pipelines[replaced].Line, $"a later pipeline for '{parent}' at line {line} replaces this one");
				pipelines.RemoveAt(replaced);
			}

			parentNode.IsPipelineParent = true;
			var ids = new List<string>();
			foreach (var (at, name, evo, label) in members)
			{
				var id = $"{parent}_{name}";
				var node = AddNode(
					id,
					name,
					WardleyNodeKind.PipelineComponent,
					new WardleyPoint(ToPercent(evo, at, $"Pipeline component \"{name}\" evolution"), parentNode.Position.Visibility),
					at
				);
				if (label is not null)
				{
					node.OffsetX = label.X;
					node.OffsetY = label.Y;
				}

				ids.Add(id);
			}

			pipelines.Add((parent, ids, line));
		}

		string Resolve(string name)
		{
			if (nodes.ContainsKey(name))
				return name;
			return order.FirstOrDefault(n => n.Label == name)?.Id ?? name;
		}

		var parentOf = new Dictionary<string, string>(StringComparer.Ordinal);
		foreach (var (nodeId, members, _) in pipelines)
			foreach (var m in members)
				parentOf.TryAdd(m, nodeId);

		var links = new List<WardleyLink>();
		foreach (var l in s.Links)
		{
			var dashed = l.Arrow is not null && (l.Arrow.Contains("-.->", StringComparison.Ordinal) || l.Arrow.Contains(".-.", StringComparison.Ordinal));
			var flow = PortFlow(l.FromPort) ?? PortFlow(l.ToPort);
			var (arrowFlow, flowLabel) = ArrowFlow(l.Arrow);
			flow ??= arrowFlow;
			var link = new WardleyLink(Resolve(l.From), Resolve(l.To), dashed, flowLabel ?? l.Label, flow ?? WardleyFlow.None);
			links.Add(link);

			if (!nodes.ContainsKey(link.Source) || !nodes.ContainsKey(link.Target))
			{
				var missing = nodes.ContainsKey(link.Source) ? l.To : l.From;
				Unused(l.Line, $"the link is not drawn: '{missing}' is not a component, anchor or pipeline component");
			}
			else if (parentOf.TryGetValue(link.Source, out var p) && p == link.Target)
			{
				Unused(l.Line, "the link is not drawn: a pipeline component's link to its own pipeline is left out");
			}
			else if (link.Source == link.Target)
			{
				Unused(l.Line, "the link is not drawn: it starts and ends at the same component");
			}
		}

		var trends = new List<(WardleyTrend Trend, int Line)>();
		foreach (var (line, name, target) in s.Evolves)
		{
			if (!nodes.ContainsKey(name))
			{
				Unused(line, $"'{name}' is not a component or anchor, so there is nothing to evolve");
				continue;
			}

			var trend = new WardleyTrend(name, ToPercent(target, line, $"Evolve target for \"{name}\""));
			var earlier = trends.FindIndex(t => t.Trend.NodeId == name);
			if (earlier >= 0)
			{
				Unused(trends[earlier].Line, $"a later 'evolve {name}' at line {line} replaces this one");
				trends[earlier] = (trend, line);
			}
			else
			{
				trends.Add((trend, line));
			}
		}

		WardleyPoint? box = null;
		if (s.AnnotationBoxes.Count > 0)
		{
			var (line, x, y) = s.AnnotationBoxes[0];
			box = new WardleyPoint(ToPercent(y, line, "Annotations box evolution"), ToPercent(x, line, "Annotations box visibility"));
			foreach (var extra in s.AnnotationBoxes.Skip(1))
				Unused(extra.Line, "only the first 'annotations' position is used");
		}

		var annotations = new List<WardleyAnnotation>();
		foreach (var (line, number, x, y, text) in s.Annotations)
		{
			annotations.Add(
				new WardleyAnnotation(
					number,
					new WardleyPoint(ToPercent(y, line, $"Annotation {number} evolution"), ToPercent(x, line, $"Annotation {number} visibility")),
					text
				)
			);
			if (box is null && text.Length > 0)
				Unused(line, "the annotation's text is shown only in the annotations list, which needs an 'annotations [x, y]' line");
		}

		var forces = new List<WardleyForce>();
		foreach (var deaccelerator in (bool[])[false, true])
			foreach (var f in s.Forces.Where(f => f.Deaccelerator == deaccelerator))
			{
				var what = deaccelerator ? "Deaccelerator" : "Accelerator";
				forces.Add(
					new WardleyForce(
						f.Name,
						new WardleyPoint(ToPercent(f.Y, f.Line, $"{what} \"{f.Name}\" evolution"), ToPercent(f.X, f.Line, $"{what} \"{f.Name}\" visibility")),
						deaccelerator
					)
				);
			}

		var title = s.Titles.Count > 0 ? s.Titles[^1].Title : null;
		return new WardleyDiagram
		{
			Title = string.IsNullOrEmpty(title) ? null : title,
			Size = size,
			Stages = stages,
			StageBoundaries = boundaries,
			Nodes =
			[
				.. order.Select(n => new WardleyNode(n.Id, n.Label, n.Kind, n.Position, n.OffsetX, n.OffsetY, n.Inertia, n.Sourcing, n.IsPipelineParent)),
			],
			Links = links,
			Trends = [.. trends.Select(t => t.Trend)],
			Pipelines = [.. pipelines.Select(p => new WardleyPipeline(p.NodeId, p.Members))],
			Notes = notes,
			Annotations = annotations,
			AnnotationsBox = box,
			Forces = forces,
			Unused = [.. unused.OrderBy(u => u.Line)],
		};
	}

	private static WardleyFlow? PortFlow(string? port) => port switch
	{
		"+<>" => WardleyFlow.Bidirectional,
		"+<" => WardleyFlow.Backward,
		"+>" => WardleyFlow.Forward,
		_ => null,
	};

	private static (WardleyFlow? Flow, string? Label) ArrowFlow(string? arrow)
	{
		if (arrow is null || !arrow.StartsWith('+'))
			return (null, null);
		string? label = null;
		if (arrow.Length > 1 && arrow[1] == '\'')
		{
			var close = arrow.IndexOf('\'', 2);
			label = arrow[2..close];
		}

		return arrow.Contains("<>", StringComparison.Ordinal)
			? (WardleyFlow.Bidirectional, label)
			: arrow.Contains('<') ? (WardleyFlow.Backward, label) : arrow.Contains('>') ? (WardleyFlow.Forward, label) : (null, label);
	}
}
