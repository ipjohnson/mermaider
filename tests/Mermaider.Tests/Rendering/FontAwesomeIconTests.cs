using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Icons;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

/// <summary>FontAwesome references: <c>fa:fa-name</c> in flowchart labels and <c>::icon(...)</c> in mindmaps.</summary>
public class FontAwesomeIconTests
{
	private static (XDocument Svg, List<MermaidDiagnostic> Diagnostics) Render(string source)
	{
		var diagnostics = new List<MermaidDiagnostic>();
		var violations = new List<SvgViolation>();
		var svg = MermaidRenderer.RenderSvg(source, new RenderOptions { OnDiagnostics = diagnostics.AddRange, OnSanitized = violations.AddRange });
		violations.Should().BeEmpty("the renderer emits only what the sanitizer keeps");
		return (XDocument.Parse(svg), diagnostics);
	}

	private static IEnumerable<string> Texts(XDocument svg) => svg.Descendants().Where(e => e.Name.LocalName == "text").Select(e => e.Value);

	private static List<XElement> Icons(XDocument svg, string cls) =>
		[.. svg.Descendants().Where(e => e.Name.LocalName == "g" && (string?)e.Attribute("class") == cls)];

	[Test]
	public void Known_references_draw_inline_icons_and_no_reference_text()
	{
		// docs-flowchart-114, with the edge written so that C is its own node
		var (svg, diagnostics) = Render(
			"""
			flowchart TD
			    B["fa:fa-twitter for peace"]
			    B --> C[fa:fa-ban forbidden]
			    B --> D(fa:fa-spinner)
			    B --> E(A fa:fa-camera-retro perhaps?)
			"""
		);

		Texts(svg).Should().NotContain(t => t.Contains("fa:"));
		var icons = Icons(svg, "label-icon");
		icons.Select(i => (string?)i.Attribute("data-icon")).Should().Equal("fa:fa-twitter", "fa:fa-ban", "fa:fa-spinner", "fa:fa-camera-retro");
		foreach (var icon in icons)
		{
			var href = (string)icon.Elements().Single(e => e.Name.LocalName == "image").Attribute("href")!;
			var name = "fa:" + ((string)icon.Attribute("data-icon")!)["fa:fa-".Length..];
			href.Should().EndWith(
				Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(IconRegistry.Resolve(name))),
				"the {0} placeholder glyph is drawn",
				name
			);
		}
		diagnostics.Should().BeEmpty();
	}

	[Test]
	public void Icon_sits_in_the_gap_its_reference_leaves_on_the_line()
	{
		var (svg, _) = Render("flowchart TD\n  E(A fa:fa-camera-retro perhaps?)");

		var text = svg.Descendants().Single(e => e.Name.LocalName == "text");
		text.Value.Should().Be("A   perhaps?");
		var image = Icons(svg, "label-icon").Single().Elements().Single();
		var (x, cx) = (double.Parse((string)image.Attribute("x")!), double.Parse((string)text.Attribute("x")!));
		x.Should().BeLessThan(cx, "the icon is left of the middle, after the A");
		x.Should().BeGreaterThan(cx - 40);
	}

	[Test]
	public void Other_fontawesome_packs_fall_back_to_the_fa_placeholders()
	{
		var (svg, diagnostics) = Render("flowchart LR\n  A[fas:fa-user far:fa-envelope fab:fa-twitter] -->|fal:fa-check ok| B");

		Icons(svg, "label-icon")
			.Select(i => (string?)i.Attribute("data-icon"))
			.Should()
			.BeEquivalentTo(["fas:fa-user", "far:fa-envelope", "fab:fa-twitter", "fal:fa-check"]);
		diagnostics.Should().BeEmpty();
	}

	[Test]
	public void Edge_and_subgraph_labels_draw_their_icons()
	{
		var (svg, _) = Render("flowchart TD\n  subgraph S [fa:fa-cloud Cloud]\n    A\n  end\n  A -->|fa:fa-check ok| B");

		Icons(svg, "label-icon").Select(i => (string?)i.Attribute("data-icon")).Should().BeEquivalentTo(["fa:fa-cloud", "fa:fa-check"]);
		Texts(svg).Should().NotContain(t => t.Contains("fa:"));
	}

	[Test]
	public void Unknown_reference_draws_a_placeholder_glyph_and_is_reported()
	{
		var (svg, diagnostics) = Render("flowchart TD\n  C[\"fab:fa-truck-bold a custom icon\"]");

		Texts(svg).Should().NotContain(t => t.Contains("fab:"));
		var icon = Icons(svg, "label-icon").Single();
		icon.Elements().Single().Attribute("transform")!.Value.Should().Contain("scale(", "the generic glyph stands in for the icon");
		var diagnostic = diagnostics.Should().ContainSingle().Which;
		diagnostic.Kind.Should().Be(MermaidDiagnosticKind.UnknownIcon);
		diagnostic.ElementId.Should().Be("C");
		diagnostic.Message.Should().Contain("'fab:fa-truck-bold'").And.Contain("'fab:truck-bold'");
	}

	[Test]
	public void Registered_pack_icon_wins_over_the_placeholder()
	{
		const string name = "fab:test-truck";
		IconRegistry.Register(name, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><rect width="10" height="10" /></svg>""");
		try
		{
			var (svg, diagnostics) = Render("flowchart TD\n  C[\"fab:fa-test-truck a custom icon\"]");

			var href = (string)Icons(svg, "label-icon").Single().Elements().Single().Attribute("href")!;
			href.Should().EndWith(Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(IconRegistry.Resolve(name))));
			diagnostics.Should().BeEmpty();
		}
		finally
		{
			_ = IconRegistry.Unregister(name);
		}
	}

	[Test]
	public void Reference_widens_the_node_by_its_icon_not_its_text()
	{
		static double Width(string label)
		{
			var (svg, _) = Render($"flowchart TD\n  A[\"{label}\"]");
			return double.Parse(
				(string)svg.Descendants().First(e => e.Name.LocalName == "rect" && e.Parent?.Attribute("data-id")?.Value == "A").Attribute("width")!
			);
		}

		Width("fa:fa-camera-retro some words").Should().BeLessThan(Width("camera-retro some words"));
	}

	[Test]
	public void Mindmap_icon_line_draws_the_icon_on_its_node()
	{
		// docs-mindmap-012
		var (svg, diagnostics) = Render("mindmap\n    Root\n        A\n        ::icon(fa fa-book)\n        B(B)\n        ::icon(mdi mdi-skull-outline)");

		svg
			.Descendants()
			.Where(e => (string?)e.Attribute("class") == "node mindmap-node")
			.Select(e => (string?)e.Attribute("data-label"))
			.Should()
			.Equal("Root", "A", "B");
		var icons = Icons(svg, "mindmap-icon");
		icons.Select(i => (string?)i.Attribute("data-icon")).Should().Equal("fa:fa-book", "mdi:mdi-skull-outline");
		icons[0].Parent!.Attribute("data-label")!.Value.Should().Be("A");
		Texts(svg).Should().NotContain(t => t.Contains("fa-book") || t.Contains("mdi"));
		var diagnostic = diagnostics.Should().ContainSingle().Which;
		diagnostic.Kind.Should().Be(MermaidDiagnosticKind.UnknownIcon);
		diagnostic.ElementId.Should().Be("B");
	}

	[Test]
	[Arguments("fa fa-book", "fa:fa-book", "fa", "book")]
	[Arguments("fa fa-fw fa-book", "fa:fa-book", "fa", "book")]
	[Arguments("mdi mdi-skull-outline", "mdi:mdi-skull-outline", "mdi", "skull-outline")]
	[Arguments("fas fa-music", "fas:fa-music", "fas", "music")]
	public void Mindmap_class_list_reads_as_mermaid_names_it(string classes, string text, string pack, string name) =>
		IconReference.FromClasses(classes).Should().Be(new IconReference(text, pack, name));
}
