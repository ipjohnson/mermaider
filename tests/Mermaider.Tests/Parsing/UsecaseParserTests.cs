using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class UsecaseParserTests
{
	private static UsecaseDiagram Parse(string source) => UsecaseParser.Parse(DiagramPreprocessor.Process(source).CleanedText);

	[Test]
	public void Parses_actors_use_cases_and_a_boundary()
	{
		var d = Parse(
			"""
			usecase-beta
			direction TB
			actor Customer("Customer")
			systemBoundary "Order system"
			  Checkout("Place order")
			  Report[Generate report]
			end
			Customer --> Checkout
			"""
		);

		d.Direction.Should().Be(Direction.TD);
		d.Actors.Should().ContainSingle().Which.Label.Text.Should().Be("Customer");
		d
			.UseCases
			.Select(u => (u.Id, u.Label.Text, u.Shape, u.Boundary))
			.Should()
			.Equal(("Checkout", "Place order", UsecaseShape.Ellipse, "Order_system"), ("Report", "Generate report", UsecaseShape.Rect, "Order_system"));
		d.Boundaries.Should().ContainSingle().Which.Members.Should().Equal("Checkout", "Report");
		d.Relationships.Should().ContainSingle().Which.EndEnd.Should().Be(UsecaseEnd.Arrow);
	}

	[Test]
	public void An_endpoint_used_before_its_declaration_takes_the_declared_kind()
	{
		var d = Parse("""
			usecase-beta
			Customer --> Login
			actor Customer
			Login("Sign in")
			""");

		d.Actors.Select(a => a.Id).Should().Equal("Customer");
		d.UseCases.Should().ContainSingle().Which.Label.Text.Should().Be("Sign in");
	}

	[Test]
	public void Reads_every_association_operator_with_its_ends_and_length()
	{
		var d = Parse(
			"""
			usecase-beta
			actor User
			User --> A
			A <-- User
			A -- B
			User --o A
			A o-- User
			User --x B
			B x-- User
			User -- "longer" ----> B
			A <-- back label -- User
			"""
		);

		d
			.Relationships
			.Select(r => (r.StartEnd, r.EndEnd, r.Label?.Text, r.MinLength))
			.Should()
			.Equal(
				(UsecaseEnd.None, UsecaseEnd.Arrow, null, 1),
				(UsecaseEnd.Arrow, UsecaseEnd.None, null, 1),
				(UsecaseEnd.None, UsecaseEnd.None, null, 1),
				(UsecaseEnd.None, UsecaseEnd.Circle, null, 1),
				(UsecaseEnd.Circle, UsecaseEnd.None, null, 1),
				(UsecaseEnd.None, UsecaseEnd.Cross, null, 1),
				(UsecaseEnd.Cross, UsecaseEnd.None, null, 1),
				(UsecaseEnd.None, UsecaseEnd.Arrow, "longer", 3),
				(UsecaseEnd.Arrow, UsecaseEnd.None, "back label", 1)
			);
	}

	[Test]
	public void Reads_include_extend_and_generalization()
	{
		var d = Parse(
			"""
			usecase-beta
			actor Admin
			actor Person
			Admin --|> Person
			Checkout ..> : include Payment
			Coupon ..> : extend Checkout
			"""
		);

		d
			.Relationships
			.Select(r => (r.Type, r.Label?.Text))
			.Should()
			.Equal((UsecaseRelationshipType.Generalization, null), (UsecaseRelationshipType.Include, "«include»"), (UsecaseRelationshipType.Extend, "«extend»"));
	}

	[Test]
	public void Reads_metadata_stereotypes_markdown_and_entities()
	{
		var d = Parse(
			"""
			usecase-beta
			actor Broker("Broker")@{ type: hollow, business: true } <<Employee>>
			Quote("`**Prepare** quote`")@{ business: true } <<Core>>
			Refund("Refund #40;partial#41;")
			Broker --> Quote
			"""
		);

		d
			.Actors
			.Should()
			.ContainSingle()
			.Which
			.Should()
			.Be(new UsecaseActor("Broker", new UsecaseLabel("Broker"), UsecaseActorType.Hollow, null, true, "Employee"));
		var quote = d.UseCases.Single(u => u.Id == "Quote");
		quote.Label.Should().Be(new UsecaseLabel("**Prepare** quote", IsMarkdown: true));
		quote.Business.Should().BeTrue();
		quote.Stereotype.Should().Be("Core");
		d.UseCases.Single(u => u.Id == "Refund").Label.Text.Should().Be("Refund (partial)");
	}

	[Test]
	public void Flattens_a_json_table_in_source_order()
	{
		var d = Parse(
			"""
			usecase-beta
			json Payload@{
			  "2": "second",
			  "1": "first",
			  "colors": ["Red", "Green"],
			  "address": { "city": "Oslo" },
			  "items": [{ "name": "Book" }],
			  "empty": {}
			}
			"""
		);

		d
			.JsonTables
			.Should()
			.ContainSingle()
			.Which
			.Rows
			.Select(r => (r.Key, r.Value))
			.Should()
			.Equal(("2", "second"), ("1", "first"), ("colors", "Red"), ("", "Green"), ("address.city", "Oslo"), ("items[0].name", "Book"), ("empty", "{}"));
	}

	[Test]
	public void Resolves_styles_in_mermaid_order()
	{
		var d = Parse(
			"""
			usecase-beta
			A:::critical
			classDef default stroke:#111,fill:#eee
			classDef critical stroke:#c33
			style A stroke-width:4px
			"""
		);

		d.Styles["A"].Should().Equal(new Dictionary<string, string> { ["stroke"] = "#c33", ["fill"] = "#eee", ["stroke-width"] = "4px" });
	}

	[Test]
	public void Keeps_edge_animation_as_unused_input_with_its_line()
	{
		var d = Parse(
			"""
			usecase-beta
			actor Customer
			Customer opens@--> Checkout
			opens@{ animation: fast }
			"""
		);

		d.Unused.Should().ContainSingle().Which.Should().Be(new UsecaseUnusedInput(4, "animation: fast", "edge 'opens': animation is not drawn"));
	}

	[Test]
	[Arguments("usecase-beta\nA --> B C", "after a complete statement")]
	[Arguments("usecase-beta\nskinparam monochrome true", "PlantUML")]
	[Arguments("usecase-beta\nactor A\nA ..> : include B", "use-case endpoints")]
	[Arguments("usecase-beta\nactor A\nB --|> A", "Generalization")]
	[Arguments("usecase-beta\nsystemBoundary S\n  A --> B\nend", "only actor and use case declarations")]
	[Arguments("usecase-beta\nsystemBoundary S\n  A\n", "not closed")]
	[Arguments("usecase-beta\nactor A@{ fillColor: red }", "fillColor")]
	[Arguments("usecase-beta\nR[Rect]@{ business: true }", "Rectangular")]
	[Arguments("usecase-beta\nnote for Missing \"x\"", "unresolved")]
	[Arguments("usecase-beta\nclassDef c stroke:red;fill:blue", "semicolons")]
	[Arguments("usecase-beta\njson J@{ \"a\": 1, }", "invalid JSON")]
	public void Refuses_what_mermaid_refuses(string source, string reason)
	{
		var act = () => Parse(source);
		act.Should().Throw<MermaidParseException>().WithMessage($"*{reason}*");
	}
}
