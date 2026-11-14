using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// <c>railroad-beta</c>: mermaid 12.3.0's constructor notation. Rules are <c>name = expression ;</c> and an expression is
/// <c>terminal("x")</c>, <c>nonterminal("x")</c>, <c>special("x")</c>, <c>sequence(a, …)</c>, <c>choice(a, …)</c>,
/// <c>optional(a)</c>, <c>zeroOrMore(a)</c> or <c>oneOrMore(a)</c>. A constructor name is a keyword, so it cannot name
/// a rule. Comments are <c>/* */</c>.
/// </summary>
internal sealed partial class RailroadIrParser(string text, int maxDepth) : RailroadReader(
	text,
	"railroad-beta",
	[
		new("sequence", "RR_ID"),
		new("choice", "RR_ID"),
		new("optional", "RR_ID"),
		new("oneOrMore", "RR_ID"),
		new("zeroOrMore", "RR_ID"),
		new("terminal", "RR_ID"),
		new("nonterminal", "RR_ID"),
		new("special", "RR_ID"),
		new("="),
		new("("),
		new(")"),
		new(","),
		new(";"),
	],
	Terminals(
		[new("RR_ID", RailroadParser.Identifier()), new("RR_STRING", RailroadParser.EscapedString())],
		[new("RR_BLOCK_COMMENT", BlockComment(), Hidden: true)]
	),
	maxDepth
)
{
	protected override RailroadNotation Notation => RailroadNotation.Ir;

	protected override string Name => "Railroad diagram";

	protected override RailroadRule ParseRule()
	{
		var name = Expect("RR_ID", "a rule name");
		_ = Expect("=");
		var definition = Expression();
		_ = Expect(";");
		return new RailroadRule(name.Text, definition, name.Line);
	}

	private RailroadExpression Expression()
	{
		using var _ = Nest();
		return Peek.Kind switch
		{
			"terminal" => new RailroadTerminal(StringArgument()),
			"nonterminal" => new RailroadNonTerminal(StringArgument()),
			"special" => new RailroadSpecial(StringArgument()),
			"sequence" => SequenceOf(List()),
			"choice" => ChoiceOf(List()),
			"optional" => new RailroadOptional(Single()),
			"oneOrMore" => new RailroadRepetition(Single(), 1, null),
			"zeroOrMore" => new RailroadRepetition(Single(), 0, null),
			_ => throw Unexpected("terminal, nonterminal, special, sequence, choice, optional, oneOrMore or zeroOrMore"),
		};
	}

	private string StringArgument()
	{
		_ = Next();
		_ = Expect("(");
		var value = Expect("RR_STRING", "a quoted string");
		_ = Expect(")");
		return RailroadParser.Unescape(value.Text);
	}

	private List<RailroadExpression> List()
	{
		_ = Next();
		_ = Expect("(");
		var items = new List<RailroadExpression> { Expression() };
		while (Accept(","))
			items.Add(Expression());
		_ = Expect(")");
		return items;
	}

	private RailroadExpression Single()
	{
		_ = Next();
		_ = Expect("(");
		var item = Expression();
		_ = Expect(")");
		return item;
	}

	[GeneratedRegex(@"\G/\*[\s\S]*?\*/", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex BlockComment();
}
