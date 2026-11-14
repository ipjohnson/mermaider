using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class WardleyParserTests
{
	[Test]
	public void Parses_components_anchors_links_and_trends()
	{
		var map = WardleyParser.Parse(
			"""
			wardley-beta
			title Tea Shop
			size [1100, 800]
			anchor Business [0.95, 0.63]
			component Cup of Tea [0.79, 0.61] label [-50, 10] (build) (inertia)
			component "Hot Water" [0.52, 80.0]
			Business -> Cup of Tea
			Cup of Tea +'heat'> "Hot Water"
			Cup of Tea -.-> Hot Water; boils
			evolve Cup of Tea 0.9
			"""
		);

		map.Title.Should().Be("Tea Shop");
		map.Size.Should().Be(new WardleySize(1100, 800));
		map
			.Nodes
			.Should()
			.Equal(
				new WardleyNode("Business", "Business", WardleyNodeKind.Anchor, new WardleyPoint(63, 95)),
				new WardleyNode("Cup of Tea", "Cup of Tea", WardleyNodeKind.Component, new WardleyPoint(61, 79), -50, 10, true, WardleySourcing.Build),
				new WardleyNode("Hot Water", "Hot Water", WardleyNodeKind.Component, new WardleyPoint(80, 52))
			);
		map
			.Links
			.Should()
			.Equal(
				new WardleyLink("Business", "Cup of Tea"),
				new WardleyLink("Cup of Tea", "Hot Water", Label: "heat", Flow: WardleyFlow.Forward),
				new WardleyLink("Cup of Tea", "Hot Water", Dashed: true, Label: "boils")
			);
		map.Trends.Should().Equal(new WardleyTrend("Cup of Tea", 90));
		map.Unused.Should().BeEmpty();
	}

	[Test]
	public void Pipelines_take_the_parent_visibility_and_resolve_links_by_label()
	{
		var map = WardleyParser.Parse(
			"""
			wardley-beta
			component Database [0.40, 0.60]
			component App [0.80, 0.50]
			pipeline Database {
			  component "SQL DB" [0.50]
			  component "NoSQL" [0.70] label [5, -5]
			}
			App -> SQL DB
			"""
		);

		map.Pipelines.Should().ContainSingle().Which.Should().BeEquivalentTo(new WardleyPipeline("Database", ["Database_SQL DB", "Database_NoSQL"]));
		map.Nodes.Single(n => n.Id == "Database").IsPipelineParent.Should().BeTrue();
		map
			.Nodes
			.Single(n => n.Id == "Database_NoSQL")
			.Should()
			.Be(new WardleyNode("Database_NoSQL", "NoSQL", WardleyNodeKind.PipelineComponent, new WardleyPoint(70, 40), 5, -5));
		map.Links.Should().Equal(new WardleyLink("App", "Database_SQL DB"));
	}

	[Test]
	public void Evolution_stages_take_boundaries_and_second_names()
	{
		var map = WardleyParser.Parse(
			"""
			wardley-beta
			evolution Genesis@0.2 / Concept -> Custom@0.4 -> Product@0.75 -> Commodity@1.0
			"""
		);

		map.Stages.Should().Equal("Genesis / Concept", "Custom", "Product", "Commodity");
		map.StageBoundaries.Should().Equal(0.2, 0.4, 0.75, 1.0);
	}

	[Test]
	public void Notes_annotations_and_forces_read_visibility_first()
	{
		var map = WardleyParser.Parse(
			"""
			wardley-beta
			note "Watch this" [0.30, 0.49]
			annotations [0.10, 0.90]
			annotation 1,[0.60, 0.65] "Critical"
			accelerator "AI" [0.50, 0.25]
			deaccelerator Legacy [0.15, 0.75]
			"""
		);

		map.Notes.Should().Equal(new WardleyNote("Watch this", new WardleyPoint(49, 30)));
		map.AnnotationsBox.Should().Be(new WardleyPoint(90, 10));
		map.Annotations.Should().Equal(new WardleyAnnotation(1, new WardleyPoint(65, 60), "Critical"));
		map.Forces.Should().Equal(new WardleyForce("AI", new WardleyPoint(25, 50), false), new WardleyForce("Legacy", new WardleyPoint(75, 15), true));
	}

	[Test]
	public void Input_mermaid_accepts_but_does_not_draw_is_listed_as_unused()
	{
		var map = WardleyParser.Parse(
			"""
			wardley-beta
			size [800, 600]
			size [1000, 600]
			evolution A@0.5 -> B
			component Parent [0.5, 0.5]
			pipeline Parent {
			  component Child [0.3]
			}
			Parent -> Ghost
			Child -> Parent
			evolve Ghost 0.5
			annotations [0.1, 0.1]
			annotations [0.2, 0.2]
			"""
		);

		map
			.Unused
			.Select(u => (u.Line, u.Text))
			.Should()
			.Equal(
				(2, "size [800, 600]"),
				(4, "evolution A@0.5 -> B"),
				(9, "Parent -> Ghost"),
				(10, "Child -> Parent"),
				(11, "evolve Ghost 0.5"),
				(13, "annotations [0.2, 0.2]")
			);
		map.Unused.Single(u => u.Line == 9).Reason.Should().Contain("'Ghost' is not a component");
	}

	[Test]
	public void Front_matter_keeps_source_line_numbers()
	{
		var map = WardleyParser.Parse("---\nconfig:\n  theme: forest\n---\nwardley-beta\nA -> B\n");

		map.Unused.Should().ContainSingle().Which.Line.Should().Be(6);
	}

	[Test]
	[Arguments("wardley-beta\ncomponent A [1, 0.5]", "line 2: expected a decimal coordinate")]
	[Arguments("wardley-beta\ncomponent A [0.5, 150.5]", "line 2: Component \"A\" evolution must be between")]
	[Arguments("wardley-beta\nnote Plain text [0.5, 0.5]", "line 2: expected a quoted note text")]
	[Arguments("wardley-beta\ncomponent notebook [0.5, 0.5]\n", "line 2: expected a component name")]
	[Arguments("wardley-beta\nA -.- (0.5, 0.5)", "line 2: unexpected character '.'")]
	[Arguments("wardley-beta\npipeline Missing {\n  component X [0.5]\n}", "line 2: pipeline \"Missing\" must name")]
	[Arguments("Wardley-Beta\ncomponent A [0.5, 0.5]", "line 1: a Wardley map starts with 'wardley-beta'")]
	public void Input_mermaid_refuses_throws_with_the_line(string source, string message)
	{
		var act = () => WardleyParser.Parse(source);

		act.Should().Throw<MermaidParseException>().Which.Message.Should().Contain(message);
	}

	[Test]
	public void A_comment_after_a_statement_is_ignored()
	{
		var map = WardleyParser.Parse("wardley-beta\ncomponent Product [0.70, 0.60]        %% mid evolution\n");

		map.Nodes.Should().ContainSingle().Which.Label.Should().Be("Product");
	}
}
