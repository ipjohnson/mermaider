using AwesomeAssertions;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

public class RailroadRendererTests
{
	private const string Source =
		"""
		railroad-abnf-beta
		title "Address"
		accTitle: Address grammar
		address = local-part "@" domain ;
		local-part = 1*( ALPHA / DIGIT / "." ) ;
		domain = label *( "." label ) [ ":" 1*5DIGIT ] ;
		""";

	[Test]
	public void Draws_every_rule_box_marker_and_bound()
	{
		var svg = MermaidRenderer.RenderSvg(Source);

		svg.Should().StartWith("<svg").And.EndWith("</svg>");
		svg.Should().Contain("aria-roledescription=\"railroad diagram\"");
		foreach (var rule in new[] { "address", "local-part", "domain" })
			svg.Should().Contain($"data-rule=\"{rule}\"").And.Contain($">{rule} =</text>");
		foreach (var kind in new[] { "terminal", "nonterminal" })
			svg.Should().Contain($"data-kind=\"{kind}\"");
		foreach (var text in new[] { "Address", "ALPHA", "DIGIT", "label", ">@<", ">:<", "×1–5" })
			svg.Should().Contain(text);
		System.Text.RegularExpressions.Regex.Matches(svg, "class=\"railroad-track\"").Count.Should().Be(3);
	}

	[Test]
	public void A_special_box_is_dashed()
	{
		var svg = MermaidRenderer.RenderSvg("railroad-ebnf-beta\nc = ? any character ? ;");
		svg.Should().Contain("data-kind=\"special\"").And.Contain("stroke-dasharray").And.Contain(">any character<");
	}

	[Test]
	public void An_empty_diagram_draws_an_empty_canvas()
	{
		var svg = MermaidRenderer.RenderSvg("railroad-peg-beta");
		svg.Should().Contain("viewBox=\"0 0 200 100\"").And.NotContain("railroad-rule");
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
	public void Each_header_is_detected_and_case_is_checked_by_the_parser()
	{
		foreach (var header in new[] { "railroad-beta", "railroad-ebnf-beta", "railroad-abnf-beta", "railroad-peg-beta" })
			Mermaider.Parsing.DiagramDetector.Detect(header + "\n").Should().Be(DiagramType.Railroad);

		var act = () => MermaidRenderer.RenderSvg("RAILROAD-EBNF-BETA\na = \"x\" ;");
		act.Should().Throw<MermaidParseException>();
	}

	[Test]
	public void Nesting_past_the_limit_raises_a_resource_limit_exception()
	{
		var act = () => MermaidRenderer.RenderSvg("railroad-ebnf-beta\na = x" + new string('?', 100) + " ;");
		act.Should().Throw<MermaidResourceLimitException>();
	}
}
