using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

public class WardleyRendererTests
{
	private const string Source =
		"""
		wardley-beta
		title Platform
		accTitle: Platform map
		evolution Genesis@0.25 -> Custom@0.5 -> Product@0.75 -> Commodity@1.0
		anchor Customer [0.90, 0.95]
		component App [0.75, 0.80] (build)
		component Gateway [0.65, 0.65] (buy) (inertia)
		component Auth [0.55, 0.55] (outsource)
		component Cloud [0.30, 0.95] (market)
		component Database [0.40, 0.60]
		pipeline Database {
		  component "SQL" [0.50]
		  component "NoSQL" [0.70]
		}
		Customer -> App
		App +<> Gateway
		Gateway +'tokens'> Auth
		Gateway -.-> Cloud
		evolve Gateway 0.85
		note "Move to managed services" [0.20, 0.30]
		annotations [0.10, 0.10]
		annotation 1,[0.70, 0.60] "Integration layer"
		accelerator "Cloud native" [0.20, 0.80]
		deaccelerator "Legacy data" [0.45, 0.35]
		""";

	[Test]
	public void Draws_every_element_and_label()
	{
		var svg = MermaidRenderer.RenderSvg(Source);

		svg.Should().StartWith("<svg").And.EndWith("</svg>");
		svg.Should().Contain("aria-roledescription=\"Wardley map\"");
		foreach (var kind in new[] { "anchor", "component", "pipeline-component" })
			svg.Should().Contain($"data-kind=\"{kind}\"");
		foreach (var sourcing in new[] { "build", "buy", "outsource", "market" })
			svg.Should().Contain($"data-sourcing=\"{sourcing}\"");
		foreach (var text in new[]
		{
			"Platform",
			"Customer",
			"Gateway",
			"NoSQL",
			"tokens",
			"Move to managed services",
			"1. Integration layer",
			"Cloud native",
			"Legacy data",
			"Commodity",
			"Evolution",
			"Visibility"
		})
			svg.Should().Contain(text);
		svg.Should().Contain("class=\"wardley-inertia\"").And.Contain("class=\"wardley-trend\"").And.Contain("wardley-pipeline");
		svg.Should().Contain("mk-arrow-s").And.Contain("mk-arrow-a");
	}

	[Test]
	public void Uses_no_literal_colours_in_any_preset()
	{
		foreach (var style in Enum.GetValues<DiagramStyle>())
		{
			foreach (var gradient in new[] { true, false })
			{
				var svg = DesignContract.Render(Source, style, gradient).Replace("color-mix(in srgb, #", "", StringComparison.Ordinal);
				DesignContract.HexColours(svg).Should().BeEmpty($"{style}, gradient {gradient}");
			}
		}
	}

	[Test]
	public void Layout_does_not_depend_on_the_preset()
	{
		static string ViewBox(string svg) => System.Text.RegularExpressions.Regex.Match(svg, "viewBox=\"[^\"]+\"").Value;

		var quiet = ViewBox(DesignContract.Render(Source, DiagramStyle.Quiet));
		ViewBox(DesignContract.Render(Source, DiagramStyle.Blueprint)).Should().Be(quiet);
		ViewBox(DesignContract.Render(Source, DiagramStyle.Tonal)).Should().Be(quiet);
	}

	[Test]
	public void The_canvas_follows_size_and_grows_for_a_label_past_the_edge()
	{
		MermaidRenderer.RenderSvg("wardley-beta\nsize [800, 400]\ncomponent A [0.5, 0.5]").Should().Contain("viewBox=\"0 0 800 400\"");
		MermaidRenderer.RenderSvg("wardley-beta\ncomponent A very long component name [0.5, 0.99]").Should().NotContain("viewBox=\"0 0 900 600\"");
	}

	[Test]
	public void Refused_input_raises_a_parse_exception()
	{
		var act = () => MermaidRenderer.RenderSvg("wardley-beta\ncomponent A [2, 0.5]");
		act.Should().Throw<MermaidParseException>();
	}
}
