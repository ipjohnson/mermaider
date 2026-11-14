using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Parsing;

public class FlowchartParserTests
{
	[Test]
	public void ParsesSimpleFlowchart()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A --> B");

		graph.Direction.Should().Be(Direction.TD);
		graph.Nodes.Should().HaveCount(2);
		graph.Edges.Should().HaveCount(1);
		graph.Nodes.Should().ContainKey("A");
		graph.Nodes.Should().ContainKey("B");
	}

	[Test]
	public void ParsesDirectionLR()
	{
		var graph = MermaidRenderer.Parse("flowchart LR\n  X --> Y");

		graph.Direction.Should().Be(Direction.LR);
	}

	[Test]
	public void ParsesNodeShapes()
	{
		var graph = MermaidRenderer.Parse(
			"""
			graph TD
			  A[Rectangle]
			  B(Rounded)
			  C{Diamond}
			  D([Stadium])
			  E((Circle))
			  F --> A
			"""
		);

		graph.Nodes["A"].Shape.Should().Be(NodeShape.Rectangle);
		graph.Nodes["B"].Shape.Should().Be(NodeShape.Rounded);
		graph.Nodes["C"].Shape.Should().Be(NodeShape.Diamond);
		graph.Nodes["D"].Shape.Should().Be(NodeShape.Stadium);
		graph.Nodes["E"].Shape.Should().Be(NodeShape.Circle);
	}

	[Test]
	public void ParsesEdgeStyles()
	{
		var graph = MermaidRenderer.Parse("""
			graph TD
			  A --> B
			  B -.-> C
			  C ==> D
			""");

		graph.Edges.Should().HaveCount(3);
		graph.Edges[0].Style.Should().Be(EdgeStyle.Solid);
		graph.Edges[1].Style.Should().Be(EdgeStyle.Dotted);
		graph.Edges[2].Style.Should().Be(EdgeStyle.Thick);
	}

	[Test]
	public void ParsesEdgeLabels()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A -->|yes| B");

		graph.Edges.Should().HaveCount(1);
		graph.Edges[0].Label.Should().Be("yes");
	}

	[Test]
	public void ParsesSubgraphs()
	{
		var graph = MermaidRenderer.Parse(
			"""
			graph TD
			  subgraph sg [My Group]
			    A --> B
			  end
			  C --> A
			"""
		);

		graph.Subgraphs.Should().HaveCount(1);
		graph.Subgraphs[0].Id.Should().Be("sg");
		graph.Subgraphs[0].Label.Should().Be("My Group");
		graph.Subgraphs[0].NodeIds.Should().Contain("A");
		graph.Subgraphs[0].NodeIds.Should().Contain("B");
	}

	[Test]
	public void ParsesChainedEdges()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A --> B --> C");

		graph.Edges.Should().HaveCount(2);
		graph.Edges[0].Source.Should().Be("A");
		graph.Edges[0].Target.Should().Be("B");
		graph.Edges[1].Source.Should().Be("B");
		graph.Edges[1].Target.Should().Be("C");
	}

	[Test]
	public void ParsesParallelLinks()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A & B --> C");

		graph.Edges.Should().HaveCount(2);
		graph.Edges[0].Source.Should().Be("A");
		graph.Edges[0].Target.Should().Be("C");
		graph.Edges[1].Source.Should().Be("B");
		graph.Edges[1].Target.Should().Be("C");
	}

	[Test]
	public void ParsesParallelLinksOnBothSides()
	{
		var graph = MermaidRenderer.Parse("graph TD\n  A & B --> C & D");

		graph.Edges.Should().HaveCount(4);
	}

	[Test]
	public void ParsesClassDefAndAssignment()
	{
		var graph = MermaidRenderer.Parse(
			"""
			graph TD
			classDef red fill:#f00,color:#fff
			A --> B
			class A red
			"""
		);

		graph.ClassDefs.Should().ContainKey("red");
		graph.ClassAssignments.Should().ContainKey("A");
		graph.ClassAssignments["A"].Should().Be("red");
	}

	[Test]
	public void ParsesClassShorthand()
	{
		var graph = MermaidRenderer.Parse("""
			graph TD
			classDef highlight fill:#ff0
			A:::highlight --> B
			""");

		graph.ClassAssignments.Should().ContainKey("A");
		graph.ClassAssignments["A"].Should().Be("highlight");
	}

	[Test]
	public void ThrowsOnInvalidHeader()
	{
		var act = () => MermaidRenderer.Parse("invalid header");
		act.Should().Throw<MermaidParseException>();
	}

	[Test]
	public void ThrowsOnEmptyDiagram()
	{
		var act = () => MermaidRenderer.Parse("");
		act.Should().Throw<MermaidParseException>();
	}

	[Test]
	public void Quoted_labels_keep_closing_delimiters()
	{
		var graph = MermaidRenderer.Parse(
			"""
			flowchart LR
			  A["a ] b"] --> B
			  C --> D["d ] e"]
			  E -->|"x | y"| F
			  G(["s ] t"]) --> H("r ) s")
			"""
		);

		graph.Nodes.Should().HaveCount(8);
		graph.Nodes["A"].Label.Should().Be("a ] b");
		graph.Nodes["D"].Label.Should().Be("d ] e");
		graph.Nodes["G"].Label.Should().Be("s ] t");
		graph.Nodes["H"].Label.Should().Be("r ) s");
		graph.Edges.Should().HaveCount(4);
		graph.Edges[2].Source.Should().Be("E");
		graph.Edges[2].Target.Should().Be("F");
		graph.Edges[2].Label.Should().Be("x | y");
	}

	[Test]
	public void Node_labelled_after_its_first_bare_reference_takes_the_label()
	{
		var graph = MermaidRenderer.Parse("""
			flowchart LR
			  A --> T
			  T[Tee label] --> D
			""");

		graph.Nodes["T"].Label.Should().Be("Tee label");
		graph.Nodes["T"].Shape.Should().Be(NodeShape.Rectangle);
		graph.Nodes.Keys.Should().Equal("A", "T", "D");
	}

	[Test]
	public void Shape_properties_set_shape_and_label()
	{
		var graph = MermaidRenderer.Parse(
			"""
			flowchart LR
			  A@{ shape: rounded, label: "Rounded" } --> B@{ shape: database, label: "Store, with comma" }
			  B --> C@{ shape: manual-file }
			  D@{ label: "Only a label" }
			  E@{
			    shape: docs
			    label: "Multi-line block"
			  }
			"""
		);

		graph.Edges.Select(e => (e.Source, e.Target)).Should().Equal(("A", "B"), ("B", "C"));
		graph.Nodes["A"].Should().Be(new MermaidNode("A", "Rounded", NodeShape.Rounded));
		graph.Nodes["B"].Should().Be(new MermaidNode("B", "Store, with comma", NodeShape.Cylinder));
		graph.Nodes["C"].Should().Be(new MermaidNode("C", "C", NodeShape.FlippedTriangle));
		graph.Nodes["D"].Should().Be(new MermaidNode("D", "Only a label", NodeShape.Rectangle));
		graph.Nodes["E"].Should().Be(new MermaidNode("E", "Multi-line block", NodeShape.StackedDocument));
	}

	[Test]
	public void Marker_shapes_drop_their_label()
	{
		var graph = MermaidRenderer.Parse("flowchart TD\n  A@{ shape: f-circ, label: \"Junction\" } --> B@{ shape: sm-circ, label: \"Start\" }");

		graph.Nodes["A"].Should().Be(new MermaidNode("A", "", NodeShape.FilledCircle));
		graph.Nodes["B"].Should().Be(new MermaidNode("B", "", NodeShape.StateStart));
	}

	[Test]
	public void Every_shape_name_of_mermaid_12_3_resolves()
	{
		var names = Mermaider.Parsing.FlowchartShapeNames.All.ToList();

		names.Should().OnlyHaveUniqueItems();
		Mermaider.Parsing.FlowchartShapeNames.ShortNames.Should().HaveCount(53);
		Mermaider.Parsing.FlowchartShapeNames.ShortNames.Select(s => s.Shape).Should().OnlyHaveUniqueItems();
		foreach (var name in names)
		{
			var graph = MermaidRenderer.Parse($"flowchart TD\n  A@{{ shape: {name}, label: \"x\" }}");
			var shape = graph.Nodes["A"].Shape;
			(shape != NodeShape.Rectangle || name is "rect" or "proc" or "process" or "rectangle" or "squareRect").Should().BeTrue(name);
		}
	}

	[Test]
	public void Collapsed_subgraph_becomes_one_node()
	{
		var graph = MermaidRenderer.Parse(
			"""
			flowchart TD
			    Start --> one
			    subgraph one [My Group]
			        A --> B
			        subgraph inner
			            C
			        end
			        B --> C
			    end
			    one --> End
			    C --> End
			    one@{ view: collapsed }
			    linkStyle 4 stroke:red
			"""
		);

		graph.Subgraphs.Should().BeEmpty();
		graph.Nodes.Keys.Should().BeEquivalentTo("Start", "one", "End");
		graph.Nodes["one"].Should().Be(new MermaidNode("one", "My Group", NodeShape.CollapsedGroup));
		graph.Edges.Select(e => (e.Source, e.Target)).Should().Equal(("Start", "one"), ("one", "End"), ("one", "End"));
		graph.EdgeStyles.Keys.Should().Equal(2);
	}

	[Test]
	public void Icon_properties_make_an_icon_node()
	{
		var graph = MermaidRenderer.Parse("flowchart TD\n  A@{ icon: \"fa:user\", form: \"square\", label: \"User\", pos: \"t\", h: 60 }");

		graph
			.Nodes["A"]
			.Should()
			.Be(new MermaidNode("A", "User", NodeShape.Icon)
			{
				Asset = new NodeAsset(NodeAssetKind.Icon, "fa:user") { Form = IconForm.Square, LabelOnTop = true, Height = 60 },
			});
	}

	[Test]
	public void Image_properties_make_an_image_node_that_wins_over_icon_and_shape()
	{
		var graph = MermaidRenderer.Parse(
			"flowchart TD\n  A@{ shape: diam, icon: \"database\", img: \"https://example.com/a.png\", w: 60, h: 40, constraint: \"on\" }"
		);

		graph
			.Nodes["A"]
			.Should()
			.Be(new MermaidNode("A", "", NodeShape.Image)
			{
				Asset = new NodeAsset(NodeAssetKind.Image, "https://example.com/a.png") { Width = 60, Height = 40, KeepAspectRatio = true },
			});
	}

	[Test]
	public void Icon_node_keeps_an_explicit_label_and_drops_its_id()
	{
		var graph = MermaidRenderer.Parse("flowchart TD\n  A[Shown]\n  A@{ icon: \"server\" }\n  B@{ icon: \"server\" }");

		graph.Nodes["A"].Label.Should().Be("Shown");
		graph.Nodes["B"].Label.Should().BeEmpty();
	}

	[Test]
	public void Asset_sizes_that_are_not_positive_numbers_are_ignored_and_large_ones_capped()
	{
		var graph = MermaidRenderer.Parse("flowchart TD\n  A@{ icon: \"server\", w: -5, h: 1e9 }\n  B@{ icon: \"server\", w: \"wide\", h: NaN }");

		graph.Nodes["A"].Asset!.Width.Should().BeNull();
		graph.Nodes["A"].Asset!.Height.Should().Be(1000);
		graph.Nodes["B"].Asset!.Width.Should().BeNull();
		graph.Nodes["B"].Asset!.Height.Should().BeNull();
	}
}
