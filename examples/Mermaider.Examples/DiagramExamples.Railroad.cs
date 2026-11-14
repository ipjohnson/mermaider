namespace Mermaider.Examples;

public static partial class DiagramExamples
{
	private static DiagramExample[] CreateRailroadExamples() =>
		[
			new(
				"railroad-ebnf-csv",
				"CSV Record (EBNF)",
				DiagramCategory.Railroad,
				"""
			railroad-ebnf-beta
			title "CSV Record"
			(* ISO 14977 comment *)
			record = field { "," field } ;
			field = quoted | bare ;
			quoted = '"' { char - '"' | '""' } '"' ;
			bare = [ char { char } ] ;
			char = ? any character except a line break ? ;
			"""
			),
			new(
				"railroad-abnf-uri",
				"URI Scheme (ABNF)",
				DiagramCategory.Railroad,
				"""
			railroad-abnf-beta
			title "URI Scheme and Port"
			scheme = ALPHA *( ALPHA / DIGIT / "+" / "-" / "." ) ;
			port = *5DIGIT ;
			ipv4 = 1*3DIGIT 3( "." 1*3DIGIT ) ;
			hexdig = %x30-39 / %x41-46 ;
			"""
			),
			new(
				"railroad-peg-list",
				"Comma List (PEG)",
				DiagramCategory.Railroad,
				"""
			railroad-peg-beta
			title "Comma Separated List"
			List <- Item ("," Item)* ","? ;
			Item <- !"," (Word / Number) ;
			Word <- Letter+ ;
			Number <- Digit+ ("." Digit+)? ;
			Comment <- "#" (!EndOfLine .)* ;
			"""
			),
			new(
				"railroad-ir-statement",
				"Statement (constructors)",
				DiagramCategory.Railroad,
				"""
			railroad-beta
			title Statement
			statement = choice(
				sequence(terminal("if"), nonterminal("expr"), nonterminal("block"), optional(sequence(terminal("else"), nonterminal("block")))),
				sequence(terminal("while"), nonterminal("expr"), nonterminal("block")),
				sequence(nonterminal("expr"), terminal(";"))
			) ;
			block = sequence(terminal("{"), zeroOrMore(nonterminal("statement")), terminal("}")) ;
			"""
			),
		];
}
