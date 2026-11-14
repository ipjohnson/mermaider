using System.Globalization;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// <c>railroad-abnf-beta</c>: RFC 5234 ABNF as mermaid 12.3.0's grammar takes it. Rules are <c>name = definition ;</c>,
/// alternatives are split by <c>/</c>, concatenation is whitespace, <c>( )</c> groups and <c>[ ]</c> is optional. A
/// repeat prefix is <c>*</c>, <c>n*</c>, <c>*m</c>, <c>n*m</c> or an exact <c>n</c>; <c>*1</c> is an optional. Terminals
/// are double-quoted strings (no escapes) and numeric values such as <c>%x30-39</c>. A comment runs from <c>;</c> to the
/// end of the line, so a rule's closing <c>;</c> must end its line: followed by anything, even a space, it is a comment.
/// </summary>
internal sealed partial class RailroadAbnfParser(string text, int maxDepth) : RailroadReader(
	text,
	"railroad-abnf-beta",
	[new("="), new("/"), new("("), new(")"), new("["), new("]"), new(";", "ABNF_COMMENT")],
	Terminals(
		[
			new("ABNF_RULENAME", RuleName()),
			new("ABNF_STRING", QuotedString()),
			new("ABNF_NUMVAL", NumVal()),
			new("ABNF_REPEAT", Repeat()),
			new("ABNF_EXACT_REPEAT", ExactRepeat()),
		],
		[new("ABNF_COMMENT", Comment(), Hidden: true)]
	),
	maxDepth
)
{
	protected override RailroadNotation Notation => RailroadNotation.Abnf;

	protected override string Name => "ABNF railroad diagram";

	// mermaid's ABNF title converter removes the quotes without decoding escapes
	protected override string Unquote(string title) => title[1..^1];

	protected override RailroadRule ParseRule()
	{
		var name = Expect("ABNF_RULENAME", "a rule name");
		_ = Expect("=");
		var definition = Alternation();
		_ = Expect(";");
		return new RailroadRule(name.Text, definition, name.Line);
	}

	private RailroadExpression Alternation()
	{
		using var _ = Nest();
		var alternatives = new List<RailroadExpression> { Concatenation() };
		while (Accept("/"))
			alternatives.Add(Concatenation());
		return ChoiceOf(alternatives);
	}

	private RailroadExpression Concatenation()
	{
		var items = new List<RailroadExpression> { Element() };
		while (Peek.Kind is "ABNF_REPEAT" or "ABNF_EXACT_REPEAT" or "ABNF_STRING" or "ABNF_NUMVAL" or "ABNF_RULENAME" or "(" or "[")
			items.Add(Element());
		return SequenceOf(items);
	}

	private RailroadExpression Element()
	{
		if (Peek.Kind is not ("ABNF_REPEAT" or "ABNF_EXACT_REPEAT"))
			return Primary();

		var repeat = Next();
		var inner = Primary();
		var star = repeat.Text.IndexOf('*', StringComparison.Ordinal);
		var (min, max) = star < 0
			? (Count(repeat.Text, repeat.Line), Count(repeat.Text, repeat.Line))
			: (star == 0 ? 0 : Count(repeat.Text[..star], repeat.Line), star == repeat.Text.Length - 1
				? (int?)null
				: Count(repeat.Text[(star + 1)..], repeat.Line));
		return min == 0 && max == 1 ? new RailroadOptional(inner) : new RailroadRepetition(inner, min, max);
	}

	private int Count(string digits, int line) =>
		int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var n)
			? n
			: throw Error($"a repeat count must fit in a 32-bit integer, not {digits}", line);

	private RailroadExpression Primary()
	{
		var t = Peek;
		switch (t.Kind)
		{
			case "ABNF_STRING":
				_ = Next();
				return new RailroadTerminal(t.Text[1..^1]);
			case "ABNF_NUMVAL":
				_ = Next();
				return new RailroadTerminal(t.Text);
			case "ABNF_RULENAME":
				_ = Next();
				return new RailroadNonTerminal(t.Text);
			case "(":
				{
					_ = Next();
					var inner = Alternation();
					_ = Expect(")");
					return inner;
				}
			case "[":
				{
					_ = Next();
					var inner = Alternation();
					_ = Expect("]");
					return new RailroadOptional(inner);
				}
			default:
				throw Unexpected("a string, a numeric value, a rule name or '(' or '['");
		}
	}

	[GeneratedRegex(@"\G[A-Za-z][A-Za-z0-9-]*", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex RuleName();

	[GeneratedRegex(@"\G""[^""]*""", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex QuotedString();

	[GeneratedRegex(@"\G%[xXdDbB][0-9A-Fa-f]+(?:-[0-9A-Fa-f]+|\.[0-9A-Fa-f]+)*", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex NumVal();

	[GeneratedRegex(@"\G[0-9]*\*[0-9]*", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex Repeat();

	[GeneratedRegex(@"\G[0-9]+", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex ExactRepeat();

	[GeneratedRegex(@"\G;[^\n\r]*", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex Comment();
}
