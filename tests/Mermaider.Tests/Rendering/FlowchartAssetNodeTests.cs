using System.Globalization;
using System.Text;
using System.Xml.Linq;
using AwesomeAssertions;
using Mermaider.Icons;
using Mermaider.Models;

namespace Mermaider.Tests.Rendering;

/// <summary>Flowchart icon and image nodes: <c>@{ icon: ... }</c> and <c>@{ img: ... }</c>.</summary>
public class FlowchartAssetNodeTests
{
	internal static readonly string SvgDataUri = DataUri(
		"svg+xml",
		"""<svg xmlns="http://www.w3.org/2000/svg" width="80" height="40" viewBox="0 0 80 40"><rect width="80" height="40" rx="6" fill="#3b82f6" /></svg>"""
	);

	private const string PngDataUri = "data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

	private static string DataUri(string type, string svg) => $"data:image/{type};base64," + Convert.ToBase64String(Encoding.UTF8.GetBytes(svg));

	private static (string Svg, List<MermaidDiagnostic> Diagnostics) Render(string source, SanitizeMode mode = SanitizeMode.Strip)
	{
		var diagnostics = new List<MermaidDiagnostic>();
		var violations = new List<SvgViolation>();
		var svg = MermaidRenderer.RenderSvg(
			source,
			new RenderOptions { SanitizeMode = mode, OnDiagnostics = diagnostics.AddRange, OnSanitized = violations.AddRange }
		);
		violations.Should().BeEmpty("the renderer emits only what the sanitizer keeps");
		return (svg, diagnostics);
	}

	private static XElement NodeGroup(string svg, string id) =>
		XDocument.Parse(svg).Descendants().Single(e => e.Name.LocalName == "g" && (string?)e.Attribute("data-id") == id);

	private static double Y(XElement e) => double.Parse((string)e.Attribute("y")!, CultureInfo.InvariantCulture);

	[Test]
	public void Registered_icon_draws_its_artwork_with_the_label()
	{
		const string name = "test:flow-node-icon";
		IconRegistry.Register(name, """<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><circle cx="5" cy="5" r="4" /></svg>""");
		try
		{
			var (svg, diagnostics) = Render($"flowchart TD\n  A@{{ icon: \"{name}\", form: \"square\", label: \"Mine\", h: 60 }}");

			var node = NodeGroup(svg, "A");
			var image = node.Descendants().Single(e => e.Name.LocalName == "image");
			((string)image.Attribute("href")!).Should().Be(DataUri("svg+xml", IconRegistry.Resolve(name)));
			((string)image.Attribute("width")!).Should().Be("60");
			node.Descendants().Should().Contain(e => e.Name.LocalName == "text" && e.Value == "Mine");
			diagnostics.Should().BeEmpty();
		}
		finally
		{
			_ = IconRegistry.Unregister(name);
		}
	}

	[Test]
	public void Unknown_icon_draws_a_placeholder_glyph_and_reports_it()
	{
		var (svg, diagnostics) = Render("flowchart TD\n  A@{ icon: \"fa:user\", label: \"User\" }");

		NodeGroup(svg, "A")
			.Elements()
			.Should()
			.Contain(e => e.Name.LocalName == "g" && ((string?)e.Attribute("transform") ?? "").Contains("scale("), "the generic glyph stands in for the icon");
		diagnostics.Should().ContainSingle().Which.Should().Match<MermaidDiagnostic>(d => d.Kind == MermaidDiagnosticKind.UnknownIcon && d.ElementId == "A");
	}

	[Test]
	[Arguments("square", "rect")]
	[Arguments("rounded", "rect")]
	[Arguments("circle", "circle")]
	public void Icon_form_draws_its_frame(string form, string element)
	{
		var (svg, _) = Render($"flowchart TD\n  A@{{ icon: \"server\", form: \"{form}\" }}");

		NodeGroup(svg, "A").Elements().First().Name.LocalName.Should().Be(element);
	}

	[Test]
	[Arguments("t", true)]
	[Arguments("b", false)]
	public void Pos_puts_the_label_above_or_below_the_image(string pos, bool above)
	{
		var (svg, _) = Render($"flowchart TD\n  A@{{ img: \"{PngDataUri}\", label: \"Dot\", pos: \"{pos}\", w: 40, h: 40 }}");

		var node = NodeGroup(svg, "A");
		var label = Y(node.Descendants().Single(e => e.Name.LocalName == "text"));
		var image = Y(node.Descendants().Single(e => e.Name.LocalName == "image"));
		(label < image).Should().Be(above);
	}

	[Test]
	[Arguments("svg")]
	[Arguments("png")]
	public void Data_uri_image_is_drawn_at_its_size(string type)
	{
		var uri = type == "svg" ? SvgDataUri : PngDataUri;
		var (svg, diagnostics) = Render($"flowchart TD\n  A@{{ img: \"{uri}\", label: \"Picture\", w: 80, h: 40 }}", SanitizeMode.Block);

		var image = NodeGroup(svg, "A").Descendants().Single(e => e.Name.LocalName == "image");
		((string)image.Attribute("href")!).Should().Be(uri);
		((string)image.Attribute("width")!).Should().Be("80");
		((string)image.Attribute("height")!).Should().Be("40");
		diagnostics.Should().BeEmpty();
	}

	[Test]
	public void Constrained_image_keeps_its_natural_aspect_ratio()
	{
		var (svg, _) = Render($"flowchart TD\n  A@{{ img: \"{SvgDataUri}\", h: 20, constraint: \"on\" }}");

		var image = NodeGroup(svg, "A").Descendants().Single(e => e.Name.LocalName == "image");
		((string)image.Attribute("width")!).Should().Be("40");
		((string)image.Attribute("height")!).Should().Be("20");
	}

	[Test]
	[Arguments("https://example.com/logo.png")]
	[Arguments("http://example.com/logo.png")]
	[Arguments("//example.com/logo.png")]
	[Arguments("javascript:alert(1)")]
	public void External_image_draws_a_placeholder_and_reports_it(string url)
	{
		var (svg, diagnostics) = Render($"flowchart TD\n  A@{{ img: \"{url}\", label: \"Remote\" }} --> B", SanitizeMode.Block);

		svg.Should().NotContain("example.com").And.NotContain("javascript").And.NotContain("<image");
		NodeGroup(svg, "A").Descendants().Should().Contain(e => e.Name.LocalName == "rect" && e.Attribute("stroke-dasharray") != null);
		NodeGroup(svg, "A").Descendants().Should().Contain(e => e.Name.LocalName == "text" && e.Value == "Remote");
		diagnostics.Should().ContainSingle().Which.Should().Match<MermaidDiagnostic>(d => d.Kind == MermaidDiagnosticKind.ExternalImage && d.ElementId == "A");
	}

	[Test]
	[Arguments("svg+xml;base64,PHN2ZyB4bWxucz0iaHR0cDovL3d3dy53My5vcmcvMjAwMC9zdmciPjxzY3JpcHQ+YWxlcnQoMSk8L3NjcmlwdD48L3N2Zz4=")]
	[Arguments("jpeg;base64,/9j/4AAQSkZJRgABAQ==")]
	[Arguments("png;base64,bm90IGEgcG5n")]
	[Arguments("svg+xml;utf8,<svg/>")]
	public void Image_the_sanitizer_would_strip_is_not_drawn_and_is_reported(string rest)
	{
		var (svg, diagnostics) = Render($"flowchart TD\n  A@{{ img: \"data:image/{rest}\", label: \"Bad\" }}", SanitizeMode.Block);

		svg.Should().NotContain("<image");
		diagnostics.Should().ContainSingle().Which.Kind.Should().Be(MermaidDiagnosticKind.UnsupportedImage);
	}

	[Test]
	public void Edge_into_a_top_labelled_icon_stops_at_the_label()
	{
		var (svg, _) = Render("flowchart TD\n  A --> B@{ icon: \"server\", form: \"square\", label: \"Server\", pos: \"t\" }");

		var label = Y(NodeGroup(svg, "B").Descendants().Single(e => e.Name.LocalName == "text"));
		var frame = Y(NodeGroup(svg, "B").Elements().First());
		var edge = XDocument.Parse(svg).Descendants().Single(e => e.Name.LocalName == "path" && (string?)e.Attribute("data-from") == "A");
		var end = ((string)edge.Attribute("d")!)
			.Split([' ', ',', 'M', 'L', 'Q', 'C'], StringSplitOptions.RemoveEmptyEntries)
			.Select(s => double.Parse(s, CultureInfo.InvariantCulture))
			.Last();
		end.Should().BeLessThan(frame).And.BeLessThan(label);
	}
}
