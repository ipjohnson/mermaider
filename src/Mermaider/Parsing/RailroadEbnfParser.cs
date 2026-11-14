using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// <c>railroad-ebnf-beta</c>: W3C and ISO 14977 EBNF as mermaid 12.3.0's grammar takes it. Rules are
/// <c>name = definition ;</c> or <c>name ::= definition ;</c>. A sequence is items with optional commas between, a choice
/// is split by <c>|</c>, <c>[ ]</c> and postfix <c>?</c> are optional, <c>{ }</c> and postfix <c>*</c> repeat zero or more
/// times, postfix <c>+</c> one or more, <c>? text ?</c> is a special sequence, and <c>A - B</c> is drawn as the
/// sequence A, "-", B. Comments are <c>/* */</c> and <c>(* *)</c>.
/// </summary>
internal sealed partial class RailroadEbnfParser(string text, int maxDepth) : RailroadReader(
	text,
	"railroad-ebnf-beta",
	[
		new("="),
		new("::="),
		new("|"),
		new(","),
		new("(", "EBNF_ISO_COMMENT"),
		new(")"),
		new("["),
		new("]"),
		new("{"),
		new("}"),
		new("?", "EBNF_SPECIAL_SEQUENCE"),
		new("*"),
		new("+"),
		new("-"),
		new(";"),
	],
	Terminals(
		[new("EBNF_ID", RailroadParser.Identifier()), new("EBNF_STRING", RailroadParser.EscapedString()), new("EBNF_SPECIAL_SEQUENCE", SpecialSequence()),],
		[new("EBNF_BLOCK_COMMENT", BlockComment(), Hidden: true), new("EBNF_ISO_COMMENT", IsoComment(), Hidden: true)]
	),
	maxDepth
)
{
	protected override RailroadNotation Notation => RailroadNotation.Ebnf;

	protected override string Name => "EBNF railroad diagram";

	protected override RailroadRule ParseRule()
	{
		var name = Expect("EBNF_ID", "a rule name");
		if (!Accept("=") && !Accept("::="))
			throw Unexpected("'=' or '::='");
		var definition = Choice();
		_ = Expect(";");
		return new RailroadRule(name.Text, definition, name.Line);
	}

	private RailroadExpression Choice()
	{
		using var _ = Nest();
		var alternatives = new List<RailroadExpression> { Sequence() };
		while (Accept("|"))
			alternatives.Add(Sequence());
		return ChoiceOf(alternatives);
	}

	private RailroadExpression Sequence()
	{
		var items = new List<RailroadExpression> { Term() };
		while (true)
		{
			if (Accept(","))
			{
				items.Add(Term());
				continue;
			}

			if (!StartsPrimary)
				break;
			items.Add(Term());
		}

		return SequenceOf(items);
	}

	private bool StartsPrimary => Peek.Kind is "EBNF_STRING" or "EBNF_SPECIAL_SEQUENCE" or "EBNF_ID" or "(" or "[" or "{";

	private RailroadExpression Term()
	{
		var node = Primary();
		while (true)
		{
			if (Accept("?"))
				node = new RailroadOptional(node);
			else if (Accept("*"))
				node = new RailroadRepetition(node, 0, null);
			else if (Accept("+"))
				node = new RailroadRepetition(node, 1, null);
			else if (Accept("-"))
				node = new RailroadSequence([node, new RailroadTerminal("-"), Primary()]);
			else
				return node;
		}
	}

	private RailroadExpression Primary()
	{
		var t = Peek;
		switch (t.Kind)
		{
			case "EBNF_STRING":
				_ = Next();
				return new RailroadTerminal(RailroadParser.Unescape(t.Text));
			case "EBNF_SPECIAL_SEQUENCE":
				_ = Next();
				return new RailroadSpecial(t.Text[1..^1].Trim());
			case "EBNF_ID":
				_ = Next();
				return new RailroadNonTerminal(t.Text);
			case "(":
				return Enclosed(")", e => e);
			case "[":
				return Enclosed("]", e => new RailroadOptional(e));
			case "{":
				return Enclosed("}", e => new RailroadRepetition(e, 0, null));
			default:
				throw Unexpected("a terminal, a rule name, a special sequence or '(', '[' or '{'");
		}
	}

	private RailroadExpression Enclosed(string close, Func<RailroadExpression, RailroadExpression> wrap)
	{
		_ = Next();
		var inner = Choice();
		_ = Expect(close);
		return wrap(inner);
	}

	// A special sequence needs a non-space character between its delimiters and cannot contain ';'
	[GeneratedRegex(@"\G\?(?=[^?;]*[^?\s;][^?;]*\?)[^?;]*\?", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex SpecialSequence();

	[GeneratedRegex(@"\G/\*[\s\S]*?\*/", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex BlockComment();

	[GeneratedRegex(@"\G\(\*[\s\S]*?\*\)", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex IsoComment();
}
