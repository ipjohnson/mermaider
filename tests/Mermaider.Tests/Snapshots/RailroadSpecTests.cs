using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class RailroadSpecTests
{
	[Test]
	public void Ebnf_choice_optional_and_repetition() =>
		MermaidRenderer
			.RenderSvg(
				"""
			railroad-ebnf-beta
			title "Signed Number"
			number = [ sign ] digit { digit } [ "." digit+ ] ;
			sign = "+" | "-" ;
			digit = "0" | "1" | "2" ;
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Abnf_repeat_bounds_and_numeric_values() =>
		MermaidRenderer
			.RenderSvg(
				"""
			railroad-abnf-beta
			title "IPv4 Address"
			address = dec-octet 3( "." dec-octet ) ;
			dec-octet = 1*3DIGIT ;
			DIGIT = %x30-39 ;
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Peg_predicates_and_any_character() =>
		MermaidRenderer
			.RenderSvg(
				"""
			railroad-peg-beta
			title "Strings"
			String <- '"' (!'"' .)* '"' ;
			Identifier <- !Keyword Letter (Letter / Digit)* ;
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Constructors_with_special_and_nested_choice() =>
		MermaidRenderer
			.RenderSvg(
				"""
			railroad-beta
			title Value
			value = choice(
				nonterminal("object"),
				sequence(terminal("["), optional(sequence(nonterminal("value"), zeroOrMore(sequence(terminal(","), nonterminal("value"))))), terminal("]")),
				special("number")
			) ;
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
