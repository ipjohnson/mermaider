using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Parses the four railroad headers of mermaid 12.3.0 into one <see cref="RailroadDiagram"/>. Each notation has its own
/// parser (<see cref="RailroadIrParser"/>, <see cref="RailroadEbnfParser"/>, <see cref="RailroadAbnfParser"/> and
/// <see cref="RailroadPegParser"/>), each a recursive-descent reader over the tokens of mermaid's Langium grammar for it.
/// Front matter and init directives are blanked first, so line numbers are the source's. Input mermaid refuses throws
/// <see cref="MermaidParseException"/> with the line; valid input the renderer does not draw is listed in
/// <see cref="RailroadDiagram.Unused"/>.
/// </summary>
internal static partial class RailroadParser
{
	internal const int TimeoutMs = 2000;

	/// <summary>Nesting depth of a definition when the caller gives no limit.</summary>
	internal const int DefaultMaxDepth = 64;

	internal static RailroadDiagram Parse(string source, int maxDepth = DefaultMaxDepth)
	{
		var (text, config) = DiagramPreprocessor.BlankConfig(source, "railroad");
		var notation = NotationOf(text);
		RailroadReader reader = notation switch
		{
			RailroadNotation.Ebnf => new RailroadEbnfParser(text, maxDepth),
			RailroadNotation.Abnf => new RailroadAbnfParser(text, maxDepth),
			RailroadNotation.Peg => new RailroadPegParser(text, maxDepth),
			_ => new RailroadIrParser(text, maxDepth),
		};
		foreach (var (line, key, value) in config)
			reader.AddUnused(new RailroadUnusedInput(line, $"{key}: {value}", ConfigReason(key)));
		RailroadDiagram diagram;
		try
		{
			diagram = reader.ParseDiagram();
		}
		catch (InsufficientExecutionStackException ex)
		{
			throw new MermaidResourceLimitException(nameof(ResourceLimits.MaxRecursionDepth), maxDepth + 1L, maxDepth, ex);
		}

		// postfixes nest without recursing in the parser, so the depth of each definition is checked once it is built
		foreach (var rule in diagram.Rules)
		{
			var depth = Depth(rule.Definition);
			if (depth > maxDepth)
				throw new MermaidResourceLimitException(nameof(ResourceLimits.MaxRecursionDepth), depth, maxDepth);
		}

		return diagram;
	}

	/// <summary>Number of expression nodes in every rule, for the element limit.</summary>
	internal static long CountNodes(RailroadDiagram diagram)
	{
		long count = 0;
		var stack = new Stack<RailroadExpression>(diagram.Rules.Select(r => r.Definition));
		while (stack.TryPop(out var node))
		{
			count++;
			foreach (var child in Children(node))
				stack.Push(child);
		}

		return count;
	}

	private static int Depth(RailroadExpression root)
	{
		var max = 0;
		var stack = new Stack<(RailroadExpression Node, int Depth)>();
		stack.Push((root, 1));
		while (stack.TryPop(out var item))
		{
			max = Math.Max(max, item.Depth);
			foreach (var child in Children(item.Node))
				stack.Push((child, item.Depth + 1));
		}

		return max;
	}

	private static IEnumerable<RailroadExpression> Children(RailroadExpression node) => node switch
	{
		RailroadSequence s => s.Items,
		RailroadChoice c => c.Alternatives,
		RailroadOptional o => [o.Item],
		RailroadRepetition r => [r.Item],
		_ => [],
	};

	/// <summary>The notation named by the header: the first line that is not blank or a <c>%%</c> comment.</summary>
	private static RailroadNotation NotationOf(string text)
	{
		foreach (var raw in text.Split('\n'))
		{
			var line = raw.Trim();
			if (line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
				continue;
			var m = HeaderPattern().Match(line);
			if (!m.Success)
				break;
			return m.Groups[1].Value.ToLowerInvariant() switch
			{
				"ebnf-" => RailroadNotation.Ebnf,
				"abnf-" => RailroadNotation.Abnf,
				"peg-" => RailroadNotation.Peg,
				_ => RailroadNotation.Ir,
			};
		}

		throw new MermaidParseException("Railroad diagram: expected railroad-beta, railroad-ebnf-beta, railroad-abnf-beta or railroad-peg-beta");
	}

	private static string ConfigReason(string key) => key[(key.LastIndexOf('.') + 1)..] switch
	{
		var k when k.EndsWith("Fill", StringComparison.Ordinal) || k.EndsWith("Stroke", StringComparison.Ordinal) || k.EndsWith(
			"Color",
			StringComparison.Ordinal
		) => $"config '{key}' is not used: colours come from the theme and style preset",
		"fontSize" or "fontFamily" => $"config '{key}' is not used: text sizes come from the design system's type roles",
		"padding" or "verticalSeparation" or "horizontalSeparation" or "arcRadius" or "markerRadius" or "strokeWidth" =>
			$"config '{key}' is not used: the layout uses mermaid's default geometry",
		_ => $"config '{key}' is not used by the renderer",
	};

	[GeneratedRegex(@"^railroad-(ebnf-|abnf-|peg-)?beta", RegexOptions.IgnoreCase, TimeoutMs)]
	private static partial Regex HeaderPattern();

	// Terminals every notation shares, with mermaid's patterns.
	[GeneratedRegex(@"\G[\t \r\n]+", RegexOptions.None, TimeoutMs)]
	internal static partial Regex Whitespace();

	[GeneratedRegex(@"\Gtitle(?:[\t ][^\n\r]*?(?=%%)|[\t ][^\n\r]*|)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex Title();

	[GeneratedRegex(@"\GaccTitle[\t ]*:(?:[^\n\r]*?(?=%%)|[^\n\r]*)", RegexOptions.None, TimeoutMs)]
	internal static partial Regex AccTitle();

	[GeneratedRegex(@"\GaccDescr(?:[\t ]*:([^\n\r]*?(?=%%)|[^\n\r]*)|\s*\{([^}]*)\})", RegexOptions.None, TimeoutMs)]
	internal static partial Regex AccDescr();

	[GeneratedRegex(@"\G[\t ]*%%\{[\S\s]*?\}%%(?:\r?\n|(?!\S))", RegexOptions.None, TimeoutMs)]
	internal static partial Regex Directive();

	[GeneratedRegex(@"\G[\t ]*%%[^\n\r]*", RegexOptions.None, TimeoutMs)]
	internal static partial Regex LineComment();

	/// <summary>Identifiers of the EBNF, PEG and constructor notations.</summary>
	[GeneratedRegex(@"\G[A-Z_a-z][\w-]*", RegexOptions.None, TimeoutMs)]
	internal static partial Regex Identifier();

	/// <summary>Quoted strings of the EBNF, PEG and constructor notations, with backslash escapes.</summary>
	[GeneratedRegex(@"\G(?:""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*')", RegexOptions.None, TimeoutMs)]
	internal static partial Regex EscapedString();

	[GeneratedRegex(@"[\t ]{2,}", RegexOptions.None, TimeoutMs)]
	internal static partial Regex SpaceRun();

	/// <summary>The text of a quoted string without its quotes, with <c>\n</c>, <c>\r</c>, <c>\t</c> and <c>\x</c> decoded as mermaid does.</summary>
	internal static string Unescape(string quoted)
	{
		var content = quoted.AsSpan(1, quoted.Length - 2);
		if (!content.Contains('\\'))
			return content.ToString();
		var sb = new StringBuilder(content.Length);
		for (var i = 0; i < content.Length; i++)
		{
			var c = content[i];
			if (c == '\\' && i + 1 < content.Length)
			{
				i++;
				_ = sb.Append(content[i] switch
				{
					'n' => '\n',
					'r' => '\r',
					't' => '\t',
					var e => e,
				});
				continue;
			}

			_ = sb.Append(c);
		}

		return sb.ToString();
	}
}

/// <summary>A token: its kind (a keyword's own text, or a terminal's name), its text, line and offsets.</summary>
internal readonly record struct RailroadToken(string Kind, string Text, int Line, int Start, int End);

/// <summary>A terminal of a notation's grammar, tried in grammar order after the keywords.</summary>
internal sealed record RailroadTerminalRule(string Name, Regex Pattern, bool Hidden = false);

/// <summary>
/// A keyword, tried longest first before the terminals. As in Langium, a terminal that a keyword can start (a keyword's
/// "longer alternative") wins when it matches more text: EBNF's <c>?</c> yields to a special sequence, its <c>(</c> to an
/// ISO comment, ABNF's <c>;</c> to a comment running to the end of the line, and a word keyword to a longer identifier.
/// </summary>
internal sealed record RailroadKeyword(string Text, params string[] LongerAlternatives);

/// <summary>Lexer and cursor shared by the notation parsers; each one supplies its keywords, terminals and rules.</summary>
internal abstract class RailroadReader
{
	private readonly List<RailroadToken> _tokens;
	private readonly List<RailroadUnusedInput> _unused = [];
	private readonly int _maxDepth;
	private int _pos;
	private int _depth;
	private readonly string _text;

	protected RailroadReader(string text, string header, IReadOnlyList<RailroadKeyword> keywords, IReadOnlyList<RailroadTerminalRule> terminals, int maxDepth)
	{
		_text = text;
		_maxDepth = maxDepth;
		_tokens = Lex(text, header, keywords, terminals);
		Header = header;
	}

	protected string Header { get; }

	protected abstract RailroadNotation Notation { get; }

	/// <summary>Display name used in error messages, such as "EBNF railroad diagram".</summary>
	protected abstract string Name { get; }

	/// <summary>Reads a rule from its first token; the cursor stands on the rule's name.</summary>
	protected abstract RailroadRule ParseRule();

	/// <summary>The title's text after mermaid's common conversion; ABNF overrides how quotes are removed.</summary>
	protected virtual string Unquote(string title) => RailroadParser.Unescape(title);

	internal void AddUnused(RailroadUnusedInput input) => _unused.Add(input);

	internal RailroadDiagram ParseDiagram()
	{
		_ = Expect(Header);
		string? title = null;
		while (Peek.Kind is "TITLE" or "ACC_TITLE" or "ACC_DESCR")
		{
			var token = Next();
			if (token.Kind == "TITLE")
				title = TitleValue(token.Text);
		}

		var rules = new List<RailroadRule>();
		while (Peek.Kind != Eof)
			rules.Add(ParseRule());
		return new RailroadDiagram
		{
			Notation = Notation,
			Title = string.IsNullOrEmpty(title) ? null : title,
			Rules = rules,
			Unused = _unused.OrderBy(u => u.Line).ToList(),
		};
	}

	private string TitleValue(string token)
	{
		var value = RailroadParser.SpaceRun().Replace(token[5..].Trim(), " ");
		var quoted = value.Length > 0 && ((value[0] == '"' && value[^1] == '"') || (value[0] == '\'' && value[^1] == '\''));
		// a lone quote is both the first and the last character, and mermaid reads it as an empty title
		return !quoted ? value : value.Length == 1 ? string.Empty : Unquote(value);
	}

	// ── cursor ────────────────────────────────────────────────────────

	protected const string Eof = "EOF";

	protected RailroadToken Peek => _tokens[_pos];

	protected RailroadToken Next() => _tokens[_pos == _tokens.Count - 1 ? _pos : _pos++];

	protected bool At(string kind) => Peek.Kind == kind;

	protected bool Accept(string kind)
	{
		if (!At(kind))
			return false;
		_pos++;
		return true;
	}

	protected RailroadToken Expect(string kind, string? what = null)
	{
		if (At(kind))
			return Next();
		throw Unexpected(what ?? Describe(kind));
	}

	protected MermaidParseException Unexpected(string expected)
	{
		var t = Peek;
		var found = t.Kind == Eof ? "the end of the diagram" : $"'{t.Text}'";
		return Error($"expected {expected} but found {found}", t.Line);
	}

	protected MermaidParseException Error(string message, int line) => new($"{Name}, line {line}: {message}");

	private static string Describe(string kind) => kind switch
	{
		Eof => "the end of the diagram",
		_ when kind.Length > 0 && char.IsUpper(kind[0]) && kind.Contains('_', StringComparison.Ordinal) => kind.ToLowerInvariant().Replace('_', ' '),
		_ => $"'{kind}'",
	};

	/// <summary>The source text from <paramref name="start"/>'s first character to the end of the token before the cursor.</summary>
	protected string SourceFrom(RailroadToken start) => _text[start.Start.._tokens[Math.Max(0, _pos - 1)].End];

	/// <summary>Enters one level of nesting; dispose to leave it. Refuses definitions nested beyond the limit.</summary>
	protected DepthScope Nest()
	{
		if (++_depth > _maxDepth)
			throw new MermaidResourceLimitException(nameof(ResourceLimits.MaxRecursionDepth), _depth, _maxDepth);
		RuntimeHelpers.EnsureSufficientExecutionStack();
		return new DepthScope(this);
	}

	protected readonly struct DepthScope(RailroadReader reader) : IDisposable
	{
		public void Dispose() => reader._depth--;
	}

	// ── lexer ─────────────────────────────────────────────────────────

	private List<RailroadToken> Lex(string text, string header, IReadOnlyList<RailroadKeyword> keywords, IReadOnlyList<RailroadTerminalRule> terminals)
	{
		var ordered = keywords.Append(new RailroadKeyword(header)).OrderByDescending(k => k.Text.Length).ToList();
		var byName = terminals.ToDictionary(t => t.Name, StringComparer.Ordinal);
		var tokens = new List<RailroadToken>();
		var line = 1;
		var lineCountedTo = 0;
		var pos = 0;

		int LineAt(int at)
		{
			for (; lineCountedTo < at; lineCountedTo++)
			{
				if (text[lineCountedTo] == '\n')
					line++;
			}

			return line;
		}

		while (pos < text.Length)
		{
			var ws = RailroadParser.Whitespace().Match(text, pos);
			if (ws.Success)
			{
				pos += ws.Length;
				continue;
			}

			var matched = false;
			foreach (var keyword in ordered)
			{
				if (!IsKeywordAt(text, pos, keyword.Text, keyword.Text == header))
					continue;
				var kind = keyword.Text;
				var length = keyword.Text.Length;
				var hidden = false;
				foreach (var alt in keyword.LongerAlternatives)
				{
					var rule = byName[alt];
					var m = rule.Pattern.Match(text, pos);
					if (m.Success && m.Length > length)
					{
						(kind, length, hidden) = (rule.Name, m.Length, rule.Hidden);
						break;
					}
				}

				if (!hidden)
					tokens.Add(new RailroadToken(kind, text.Substring(pos, length), LineAt(pos), pos, pos + length));
				pos += length;
				matched = true;
				break;
			}

			if (matched)
				continue;

			foreach (var rule in terminals)
			{
				var m = rule.Pattern.Match(text, pos);
				if (!m.Success || m.Length == 0)
					continue;
				if (!rule.Hidden)
					tokens.Add(new RailroadToken(rule.Name, m.Value, LineAt(pos), pos, pos + m.Length));
				pos += m.Length;
				matched = true;
				break;
			}

			if (!matched)
				throw Error($"unexpected character '{text[pos]}'", LineAt(pos));
		}

		tokens.Add(new RailroadToken(Eof, string.Empty, LineAt(text.Length), text.Length, text.Length));
		return tokens;
	}

	/// <summary>Whether <paramref name="keyword"/> is at <paramref name="pos"/>; the header must be followed by space, <c>%%</c> or the end.</summary>
	private static bool IsKeywordAt(string text, int pos, string keyword, bool isHeader)
	{
		if (string.CompareOrdinal(text, pos, keyword, 0, keyword.Length) != 0)
			return false;
		if (!isHeader)
			return true;
		var after = pos + keyword.Length;
		return after >= text.Length || char.IsWhiteSpace(text[after]) || string.CompareOrdinal(text, after, "%%", 0, 2) == 0;
	}

	/// <summary>The common terminals in mermaid's order: title and accessibility first, then the notation's, then hidden ones.</summary>
	protected static List<RailroadTerminalRule> Terminals(IEnumerable<RailroadTerminalRule> own, IEnumerable<RailroadTerminalRule> hidden) =>
		[
			new("TITLE", RailroadParser.Title()),
			new("ACC_TITLE", RailroadParser.AccTitle()),
			new("ACC_DESCR", RailroadParser.AccDescr()),
			.. own,
			new("DIRECTIVE", RailroadParser.Directive(), Hidden: true),
			new("SINGLE_LINE_COMMENT", RailroadParser.LineComment(), Hidden: true),
			.. hidden,
		];

	/// <summary>A sequence, or its only item.</summary>
	protected static RailroadExpression SequenceOf(List<RailroadExpression> items) => items.Count == 1 ? items[0] : new RailroadSequence(items);

	/// <summary>A choice, or its only alternative.</summary>
	protected static RailroadExpression ChoiceOf(List<RailroadExpression> alternatives) =>
		alternatives.Count == 1 ? alternatives[0] : new RailroadChoice(alternatives);
}
