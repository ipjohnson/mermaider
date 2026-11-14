using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class RailroadParserTests
{
	private static RailroadTerminal T(string v) => new(v);

	private static RailroadNonTerminal N(string v) => new(v);

	private static RailroadSequence Seq(params RailroadExpression[] items) => new(items);

	private static RailroadChoice Alt(params RailroadExpression[] items) => new(items);

	private static void ShouldBe(RailroadExpression actual, RailroadExpression expected) => Show(actual).Should().Be(Show(expected));

	private static string Show(RailroadExpression e) => e switch
	{
		RailroadTerminal t => $"\"{t.Value}\"",
		RailroadNonTerminal n => $"<{n.Name}>",
		RailroadSpecial s => $"?{s.Text}?",
		RailroadSequence q => "(" + string.Join(' ', q.Items.Select(Show)) + ")",
		RailroadChoice c => "(" + string.Join(" | ", c.Alternatives.Select(Show)) + ")",
		RailroadOptional o => "[" + Show(o.Item) + "]",
		RailroadRepetition r => "{" + Show(r.Item) + "}" + r.Min + ".." + r.Max,
		_ => "!",
	};

	[Test]
	public void Ebnf_reads_both_styles_into_one_tree()
	{
		var d = RailroadParser.Parse(
			"""
			railroad-ebnf-beta
			title "Digits"
			(* ISO comment *)
			number ::= sign? digit+ , [ "." { digit } ] ;
			/* W3C comment */
			sign = "+" | "-" ;
			name = letter - "x" ? any letter ? ;
			"""
		);

		d.Notation.Should().Be(RailroadNotation.Ebnf);
		d.Title.Should().Be("Digits");
		d.Rules.Select(r => (r.Name, r.Line)).Should().Equal(("number", 4), ("sign", 6), ("name", 7));
		ShouldBe(
			d.Rules[0].Definition,
			Seq(
				new RailroadOptional(N("sign")),
				new RailroadRepetition(N("digit"), 1, null),
				new RailroadOptional(Seq(T("."), new RailroadRepetition(N("digit"), 0, null)))
			)
		);
		ShouldBe(d.Rules[1].Definition, Alt(T("+"), T("-")));
		ShouldBe(d.Rules[2].Definition, Seq(Seq(N("letter"), T("-"), T("x")), new RailroadSpecial("any letter")));
	}

	[Test]
	public void Abnf_reads_repeat_prefixes_and_numeric_values()
	{
		var d = RailroadParser.Parse("""
			railroad-abnf-beta
			rule = *a 1*b 2*4c 3d *1e [ "f" / %x30-39 ] ( g h ) ;
			""");

		d.Notation.Should().Be(RailroadNotation.Abnf);
		ShouldBe(
			d.Rules[0].Definition,
			Seq(
				new RailroadRepetition(N("a"), 0, null),
				new RailroadRepetition(N("b"), 1, null),
				new RailroadRepetition(N("c"), 2, 4),
				new RailroadRepetition(N("d"), 3, 3),
				new RailroadOptional(N("e")),
				new RailroadOptional(Alt(T("f"), T("%x30-39"))),
				Seq(N("g"), N("h"))
			)
		);
	}

	[Test]
	public void Peg_draws_predicates_as_special_boxes_and_lists_a_group_operand_as_unused()
	{
		var d = RailroadParser.Parse(
			"""
			railroad-peg-beta
			A <- !Keyword &"x" . B* / C+ ; # comment
			B <- !("a" / "b") C ;
			"""
		);

		d.Notation.Should().Be(RailroadNotation.Peg);
		ShouldBe(
			d.Rules[0].Definition,
			Alt(
				Seq(new RailroadSpecial("!Keyword"), new RailroadSpecial("&\"x\""), new RailroadSpecial("."), new RailroadRepetition(N("B"), 0, null)),
				new RailroadRepetition(N("C"), 1, null)
			)
		);
		ShouldBe(d.Rules[1].Definition, Seq(new RailroadSpecial("!(...)"), N("C")));
		d
			.Unused
			.Should()
			.ContainSingle()
			.Which
			.Should()
			.Be(new RailroadUnusedInput(3, "!(\"a\" / \"b\")", "the operand of '!' is drawn as (...), not as its expression"));
	}

	[Test]
	public void Constructors_collapse_single_item_lists()
	{
		var d = RailroadParser.Parse(
			"""
			railroad-beta
			title Expression Grammar
			e = sequence(nonterminal("t"), zeroOrMore(choice(terminal("+"), terminal("-"))), sequence(special("s"))) ;
			o = optional(oneOrMore(terminal("a\"b"))) ;
			"""
		);

		d.Notation.Should().Be(RailroadNotation.Ir);
		d.Title.Should().Be("Expression Grammar");
		ShouldBe(d.Rules[0].Definition, Seq(N("t"), new RailroadRepetition(Alt(T("+"), T("-")), 0, null), new RailroadSpecial("s")));
		ShouldBe(d.Rules[1].Definition, new RailroadOptional(new RailroadRepetition(T("a\"b"), 1, null)));
	}

	[Test]
	public void A_name_starting_with_title_is_read_as_a_title_as_mermaid_does()
	{
		var d = RailroadParser.Parse("railroad-ebnf-beta\ntitles = \"x\" ;");
		d.Title.Should().BeNull();
		d.Rules.Should().ContainSingle().Which.Name.Should().Be("s");
	}

	[Test]
	public void Railroad_config_keys_are_listed_as_unused()
	{
		var d = RailroadParser.Parse("---\nconfig:\n  railroad:\n    fontSize: 20\n---\nrailroad-beta\nr = terminal(\"x\") ;");
		d
			.Unused
			.Should()
			.ContainSingle()
			.Which
			.Should()
			.Be(
				new RailroadUnusedInput(
					4,
					"railroad.fontSize: 20",
					"config 'railroad.fontSize' is not used: text sizes come from the design system's type roles"
				)
			);
	}

	[Test]
	[Arguments("railroad-abnf-beta\nrule = b ; a comment\nnext = \"x\" ;", "line 3")]
	[Arguments("railroad-beta\nsequence = terminal(\"x\") ;", "line 2")]
	[Arguments("railroad-ebnf-beta\na = \"x\" | ;", "line 2")]
	[Arguments("railroad-ebnf-beta\na = \"x\"\n", "line 3")]
	[Arguments("railroad-peg-beta\nA <- [a-z] ;", "line 2: unexpected character '['")]
	[Arguments("railroad-abnf-beta\nr = 99999999999*b ;", "line 2: a repeat count must fit")]
	[Arguments("railroad-ebnf-beta\na = \"x\" ;\ntitle Late", "line 3")]
	[Arguments("Railroad-Beta\nr = terminal(\"x\") ;", "line 1")]
	public void Input_mermaid_refuses_throws_with_the_line(string source, string message)
	{
		var act = () => RailroadParser.Parse(source);
		act.Should().Throw<MermaidParseException>().WithMessage($"*{message}*");
	}

	[Test]
	public void Nesting_past_the_depth_limit_is_refused()
	{
		var deep = "railroad-ebnf-beta\na = " + new string('(', 100) + "x" + new string(')', 100) + " ;";
		var act = () => RailroadParser.Parse(deep);
		act.Should().Throw<MermaidResourceLimitException>();

		var postfixes = "railroad-ebnf-beta\na = x" + new string('?', 100) + " ;";
		var act2 = () => RailroadParser.Parse(postfixes);
		act2.Should().Throw<MermaidResourceLimitException>();
	}
}
