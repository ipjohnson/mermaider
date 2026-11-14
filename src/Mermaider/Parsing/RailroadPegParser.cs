using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// <c>railroad-peg-beta</c>: parsing expression grammars as mermaid 12.3.0's grammar takes them. Rules are
/// <c>Name &lt;- definition ;</c>, ordered choice is <c>/</c>, sequence is whitespace, suffixes are <c>?</c>, <c>*</c> and
/// <c>+</c>, and <c>.</c> is any character. A predicate (<c>&amp;A</c>, <c>!A</c>) is drawn as one special box labelled with
/// its operator and operand, as mermaid draws it; an operand that is not a single item is labelled <c>(...)</c>, and its
/// text is listed in <see cref="RailroadDiagram.Unused"/>. Comments run from <c>#</c> to the end of the line.
/// </summary>
internal sealed partial class RailroadPegParser(string text, int maxDepth) : RailroadReader(
	text,
	"railroad-peg-beta",
	[new("<-"), new("/"), new("&"), new("!"), new("?"), new("*"), new("+"), new("("), new(")"), new("."), new(";")],
	Terminals(
		[new("PEG_ID", RailroadParser.Identifier()), new("PEG_STRING", RailroadParser.EscapedString())],
		[new("PEG_LINE_COMMENT", LineComment(), Hidden: true)]
	),
	maxDepth
)
{
	protected override RailroadNotation Notation => RailroadNotation.Peg;

	protected override string Name => "PEG railroad diagram";

	protected override RailroadRule ParseRule()
	{
		var name = Expect("PEG_ID", "a rule name");
		_ = Expect("<-");
		var definition = OrderedChoice();
		_ = Expect(";");
		return new RailroadRule(name.Text, definition, name.Line);
	}

	private RailroadExpression OrderedChoice()
	{
		using var _ = Nest();
		var alternatives = new List<RailroadExpression> { Sequence() };
		while (Accept("/"))
			alternatives.Add(Sequence());
		return ChoiceOf(alternatives);
	}

	private RailroadExpression Sequence()
	{
		var items = new List<RailroadExpression> { Prefix() };
		while (Peek.Kind is "&" or "!" or "PEG_STRING" or "PEG_ID" or "(" or ".")
			items.Add(Prefix());
		return SequenceOf(items);
	}

	private RailroadExpression Prefix()
	{
		if (Peek.Kind is not ("&" or "!"))
			return Suffix();

		var op = Next();
		var start = Peek;
		var inner = Suffix();
		var label = inner switch
		{
			RailroadTerminal t => $"\"{t.Value}\"",
			RailroadNonTerminal n => n.Name,
			RailroadSpecial s => s.Text,
			_ => null,
		};
		if (label is null)
		{
			var operand = SourceFrom(start);
			AddUnused(new RailroadUnusedInput(op.Line, op.Text + operand, $"the operand of '{op.Text}' is drawn as (...), not as its expression"));
			label = "(...)";
		}

		return new RailroadSpecial(op.Text + label);
	}

	private RailroadExpression Suffix()
	{
		var inner = Primary();
		if (Accept("?"))
			return new RailroadOptional(inner);
		if (Accept("*"))
			return new RailroadRepetition(inner, 0, null);
		return Accept("+") ? new RailroadRepetition(inner, 1, null) : inner;
	}

	private RailroadExpression Primary()
	{
		var t = Peek;
		switch (t.Kind)
		{
			case "PEG_STRING":
				_ = Next();
				return new RailroadTerminal(RailroadParser.Unescape(t.Text));
			case "PEG_ID":
				_ = Next();
				return new RailroadNonTerminal(t.Text);
			case ".":
				_ = Next();
				return new RailroadSpecial(".");
			case "(":
				{
					_ = Next();
					var inner = OrderedChoice();
					_ = Expect(")");
					return inner;
				}
			default:
				throw Unexpected("a string, a rule name, '.' or '('");
		}
	}

	[GeneratedRegex(@"\G#[^\n\r]*", RegexOptions.None, RailroadParser.TimeoutMs)]
	private static partial Regex LineComment();
}
