using AwesomeAssertions;

namespace Mermaider.Tests.Rendering;

public class ClassRendererTests
{
	private const string SimpleClass =
		"""
		classDiagram
		class Animal {
		+String name
		+eat() void
		}
		class Dog
		Animal <|-- Dog
		""";

	[Test]
	public void Renders_valid_svg()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().StartWith("<svg");
		svg.Should().EndWith("</svg>");
	}

	[Test]
	public void Contains_class_nodes()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().Contain("class=\"class-node\"");
		svg.Should().Contain("data-id=\"Animal\"");
		svg.Should().Contain("data-id=\"Dog\"");
	}

	[Test]
	public void Contains_relationship_lines()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().Contain("class=\"class-relationship\"");
		svg.Should().Contain("data-type=\"inheritance\"");
	}

	[Test]
	public void Contains_marker_defs()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		// the shared marker set (DesignSystem.Marker), one definition per shape used
		svg.Should().MatchRegex("<marker id=\"[^\"]*mk-triangle");
		svg.Should().NotMatchRegex("<marker id=\"[^\"]*mk-diamond", "only the markers a diagram uses are defined");
	}

	[Test]
	public void Renders_annotation()
	{
		var svg = MermaidRenderer.RenderSvg(
			"""
			classDiagram
			class Shape {
			<<abstract>>
			+draw() void
			}
			"""
		);

		svg.Should().Contain("data-annotation=\"abstract\"");
		svg.Should().Contain(">«abstract»<");
	}

	[Test]
	public void Uses_uniform_font()
	{
		var svg = MermaidRenderer.RenderSvg(SimpleClass);

		svg.Should().Contain("font-family: 'Inter'");
		svg.Should().Contain("class=\"mono\"");
	}

	[Test]
	public void Renders_relationship_with_label()
	{
		var svg = MermaidRenderer.RenderSvg("""
			classDiagram
			Customer "1" --> "*" Order : places
			""");

		svg.Should().Contain("data-label=\"places\"");
	}

	[Test]
	public void Links_draw_no_marker()
	{
		var svg = MermaidRenderer.RenderSvg("classDiagram\n  A -- B\n  C .. D");

		svg.Should().Contain("data-type=\"link\"");
		svg.Should().Contain("data-type=\"dashed-link\"");
		svg.Should().NotContain("marker-end=");
		svg.Should().NotContain("marker-start=");
		svg.Should().MatchRegex("data-type=\"dashed-link\"[^>]*stroke-dasharray=");
		svg.Should().NotMatchRegex("data-type=\"link\"[^>]*stroke-dasharray=");
	}

	[Test]
	public void Note_is_placed_clear_of_relation_labels()
	{
		var diagram = Mermaider
			.Parsing
			.ClassParser
			.Parse([
				"classDiagram",
				"direction LR",
				"namespace Execution {",
				"class LaunchPoint",
				"class Policy {",
				"+bool Allowed",
				"}",
				"}",
				"namespace Containers {",
				"class DockerEngine",
				"}",
				"LaunchPoint --> DockerEngine : launches",
				"DockerEngine --> Policy : reads",
				"note for Policy \"Restrictions\"",
			]);

		var positioned = Mermaider.Layout.LightweightClassLayoutEngine.Layout(diagram);

		var note = positioned.Notes.Should().ContainSingle().Subject;
		foreach (var rel in positioned.Relationships)
		{
			var lp = rel.LabelPosition!.Value;
			var labelW = Mermaider
				.Rendering
				.DesignSystem
				.LabelBoxWidth(
					Mermaider.Text.TextMetrics.MeasureTextWidth(rel.Label!, Mermaider.Rendering.DesignSystem.Px(Mermaider.Rendering.TypeRole.Caption), 500)
				);
			var clear = note.X >= lp.X + (labelW / 2) || note.X + note.Width <= lp.X - (labelW / 2) || note.Y >= lp.Y + 10 || note.Y + note.Height <= lp.Y - 10;
			clear.Should().BeTrue($"the note must not cover the '{rel.Label}' label");
		}
	}
}
