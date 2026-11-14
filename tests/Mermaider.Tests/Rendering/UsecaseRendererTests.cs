using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

public class UsecaseRendererTests
{
	private const string Source =
		"""
		usecase-beta
		direction LR
		accTitle: Ordering
		actor Customer
		actor Staff("Order staff")@{ type: hollow }
		systemBoundary ordering["Ordering system"]@{ type: package }
		  Browse("Browse products")
		  Review[Review order]
		end
		json Order@{ "status": "pending" }
		note for Browse "Shown first"
		Customer -- "browses" --> Browse
		Staff --x Review
		Browse ..> : include Review
		Review --> Order
		""";

	[Test]
	public void Draws_every_element_and_label()
	{
		var svg = MermaidRenderer.RenderSvg(Source);

		svg.Should().StartWith("<svg").And.EndWith("</svg>");
		svg.Should().Contain("aria-roledescription=\"use case diagram\"");
		foreach (var kind in new[] { "actor", "usecase", "usecase-rect", "boundary", "json", "note" })
			svg.Should().Contain($"data-kind=\"{kind}\"");
		foreach (var text in new[]
		{
			"Customer",
			"Order staff",
			"Ordering system",
			"Browse products",
			"Review order",
			"browses",
			"«include»",
			"Shown first",
			"pending"
		})
			svg.Should().Contain(text);
		svg.Should().Contain("data-type=\"package\"").And.Contain("stroke-dasharray=\"5 4\"").And.Contain("mk-cross");
	}

	[Test]
	public void Uses_no_literal_colours_in_any_preset()
	{
		foreach (var style in Enum.GetValues<DiagramStyle>())
		{
			foreach (var gradient in new[] { true, false })
			{
				// a family stage mixes the palette base, so only a hex outside color-mix counts
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
	public void Author_styles_win_unless_styling_is_strict()
	{
		const string styled = "usecase-beta\nA(\"Styled\")\nstyle A fill:#ff0000,stroke:#00ff00";

		MermaidRenderer.RenderSvg(styled).Should().Contain("fill=\"#ff0000\"").And.Contain("stroke=\"#00ff00\"");
		MermaidRenderer.RenderSvg(styled, new RenderOptions { Strict = new StrictStylingOptions { AllowedClasses = [] } }).Should().NotContain("#ff0000");
	}

	[Test]
	public void Refused_input_raises_a_parse_exception()
	{
		var act = () => MermaidRenderer.RenderSvg("usecase-beta\nactor A\nA ..> : include A");
		act.Should().Throw<MermaidParseException>();
	}
}
