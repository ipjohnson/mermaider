using AwesomeAssertions;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Rendering;

public class ZenUmlRendererTests
{
	private const string Source =
		"""
		zenuml
		title Checkout
		accTitle: Checkout flow
		@Actor Customer
		group Backend { Shop Payments }
		Customer->Shop.checkout(cart) {
		  // charge first
		  receipt = Payments.charge(total)
		  while(retry) {
		    Shop->Mailer: send receipt
		  }
		  par {
		    Shop->Audit: log
		    Shop->Stock: release
		  }
		  try {
		    Shop.archive()
		  } catch {
		    Shop->Ops: alert
		  }
		}
		== later ==
		""";

	[Test]
	public void Draws_the_title_participants_messages_and_frames()
	{
		var svg = MermaidRenderer.RenderSvg(Source);

		svg.Should().StartWith("<svg").And.EndWith("</svg>");
		svg.Should().Contain("aria-roledescription=\"ZenUML sequence diagram\"");
		svg.Should().Contain("class=\"diagram-title\"");
		foreach (var block in new[] { "loop", "par", "try" })
			svg.Should().Contain($"data-type=\"{block}\"");
		foreach (var text in new[]
		{
			"Checkout",
			"Customer",
			"Backend",
			"checkout(cart)",
			"charge(total)",
			"receipt",
			"send receipt",
			"charge first",
			"later",
			"catch"
		})
			svg.Should().Contain(text);
	}

	[Test]
	public void A_sync_call_is_activated_until_its_last_nested_message()
	{
		var (sequence, ends) = ZenUmlLayout.ToSequence(ZenUmlParser.Parse("zenuml\nA.m() {\n  B.n()\n  A->C: x\n}"));

		sequence
			.Messages
			.Select(m => (m.From, m.To, m.LineStyle, m.ArrowHead, m.Activate))
			.Should()
			.Equal(
				("_STARTER_", "A", SequenceLineStyle.Solid, SequenceArrowHead.Filled, true),
				("A", "B", SequenceLineStyle.Solid, SequenceArrowHead.Filled, true),
				("A", "C", SequenceLineStyle.Solid, SequenceArrowHead.Open, false)
			);
		ends.Should().Equal(("B", 1), ("A", 2));
		ZenUmlLayout.Layout(ZenUmlParser.Parse("zenuml\nA.m() {\n  B.n()\n  A->C: x\n}")).Activations.Should().HaveCount(2);
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
	public void Refused_input_raises_a_parse_exception()
	{
		var act = () => MermaidRenderer.RenderSvg("zenuml\nA.m() {");
		act.Should().Throw<MermaidParseException>();
	}
}
