using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Mermaider.Icons;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Sizes, outlines and draws flowchart icon and image nodes (<c>@{ icon: ... }</c>, <c>@{ img: ... }</c>) as mermaid.js
/// lays them out: the icon or image, in its frame, with the label above it for <c>pos: "t"</c> and below it otherwise.
/// An icon resolves through <see cref="IconRegistry"/>. An image reaches the SVG only when <see cref="SvgSanitizer"/>
/// keeps its href, which admits base64 <c>data:image/svg+xml</c> and <c>data:image/png</c> URIs; any other image draws
/// a placeholder box, and <see cref="Diagnose"/> reports it.
/// </summary>
internal static class NodeAssets
{
	/// <summary>Icon and image size when the source gives none, as in mermaid.js.</summary>
	private const double DefaultSize = 48;

	/// <summary>Largest side of an image drawn at its natural size.</summary>
	private const double MaxNaturalSize = 1000;

	/// <summary>Space between an icon and its square, rounded or circle frame, on each side.</summary>
	private const double FramePadding = 8;

	/// <summary>Space between the frame and the label, as in mermaid.js.</summary>
	private const double LabelGap = 8;

	/// <summary>An image as it can be drawn: the href to embed (null for a placeholder) and its natural size.</summary>
	internal readonly record struct ResolvedImage(string? Href, double Width, double Height, MermaidDiagnosticKind? Problem);

	/// <summary>Where each part of an icon or image node sits inside its box.</summary>
	internal readonly record struct Geometry(
		double FrameX,
		double FrameY,
		double FrameWidth,
		double FrameHeight,
		double ArtWidth,
		double ArtHeight,
		double LabelCx,
		double LabelCy,
		double LabelWidth,
		double LabelHeight
	);

	internal static ResolvedImage ResolveImage(string source)
	{
		if (SvgSanitizer.IsSafeImageHref(source))
		{
			var (w, h) = NaturalSize(source);
			return new ResolvedImage(source, w, h, null);
		}
		var problem = source.StartsWith("data:", StringComparison.OrdinalIgnoreCase)
			? MermaidDiagnosticKind.UnsupportedImage
			: MermaidDiagnosticKind.ExternalImage;
		return new ResolvedImage(null, DefaultSize, DefaultSize, problem);
	}

	/// <summary>One diagnostic per icon the registry lacks and per image drawn as a placeholder, in node order.</summary>
	internal static IEnumerable<MermaidDiagnostic> Diagnose(MermaidGraph graph)
	{
		var order = graph.NodeOrder.Count > 0 ? graph.NodeOrder : graph.Nodes.Keys.ToList();
		foreach (var id in order)
		{
			if (!graph.Nodes.TryGetValue(id, out var node) || node.Asset is not { } asset)
				continue;
			if (asset.Kind == NodeAssetKind.Icon)
			{
				if (!IconRegistry.TryGet(asset.Source, out _))
					yield return new MermaidDiagnostic(
						MermaidDiagnosticKind.UnknownIcon,
						id,
						$"Node '{id}': the icon '{asset.Source}' is not registered, so a placeholder glyph is drawn. Register it with IconRegistry.Register."
					);
				continue;
			}
			switch (ResolveImage(asset.Source).Problem)
			{
				case MermaidDiagnosticKind.ExternalImage:
					yield return new MermaidDiagnostic(
						MermaidDiagnosticKind.ExternalImage,
						id,
						$"Node '{id}': external images are never fetched, so a placeholder box is drawn. Use a base64 data:image/svg+xml or data:image/png URI."
					);
					break;
				case MermaidDiagnosticKind.UnsupportedImage:
					yield return new MermaidDiagnostic(
						MermaidDiagnosticKind.UnsupportedImage,
						id,
						$"Node '{id}': the image is not a valid base64 data:image/svg+xml or data:image/png URI, so a placeholder box is drawn."
					);
					break;
			}
		}
	}

	/// <summary>The node's box: the frame, and the label above or below it.</summary>
	internal static (double Width, double Height) Size(NodeAsset asset, string label)
	{
		var (fw, fh) = FrameSize(asset, ArtSize(asset));
		var (lw, lh) = LabelSize(label);
		return (Math.Max(fw, lw), fh + (lh > 0 ? lh + LabelGap : 0));
	}

	internal static Geometry Place(PositionedNode node, NodeAsset asset)
	{
		var art = ArtSize(asset);
		var (fw, fh) = FrameSize(asset, art);
		var (lw, lh) = LabelSize(node.Label);
		var cx = node.X + (node.Width / 2);
		var frameY = asset.LabelOnTop ? node.Y + node.Height - fh : node.Y;
		var labelCy = asset.LabelOnTop ? node.Y + (lh / 2) : node.Y + node.Height - (lh / 2);
		return new Geometry(cx - (fw / 2), frameY, fw, fh, art.Width, art.Height, cx, labelCy, lw, lh);
	}

	/// <summary>The outline an edge stops on: the label box and the frame, as mermaid.js intersects them. Null when it is the node's box.</summary>
	internal static IReadOnlyList<IReadOnlyList<(double X, double Y)>>? Outline(PositionedNode node, NodeAsset asset)
	{
		var g = Place(node, asset);
		IReadOnlyList<(double X, double Y)> frame = asset.Form == IconForm.Circle && asset.Kind == NodeAssetKind.Icon
			? FlowchartShapes.CirclePoints(g.FrameX + (g.FrameWidth / 2), g.FrameY + (g.FrameHeight / 2), g.FrameWidth / 2)
			: Box(g.FrameX, g.FrameY, g.FrameWidth, g.FrameHeight);
		if (g.LabelHeight <= 0)
			return asset.Form == IconForm.Circle && asset.Kind == NodeAssetKind.Icon ? [frame] : null;
		var label = Box(g.LabelCx - (g.LabelWidth / 2), g.LabelCy - (g.LabelHeight / 2), g.LabelWidth, g.LabelHeight);
		return [frame, label];
	}

	/// <summary>Draws the frame and the icon or image. The label is drawn by the caller at <see cref="Geometry.LabelCx"/>.</summary>
	internal static void Append(
		StringBuilder sb,
		DesignSystem ds,
		ColorFamily family,
		PositionedNode node,
		NodeAsset asset,
		string fill,
		string stroke,
		string sw
	)
	{
		var g = Place(node, asset);
		var (cx, cy) = (g.FrameX + (g.FrameWidth / 2), g.FrameY + (g.FrameHeight / 2));
		if (asset.Kind == NodeAssetKind.Icon)
		{
			switch (asset.Form)
			{
				case IconForm.Square:
					Rect(sb, g.FrameX, g.FrameY, g.FrameWidth, g.FrameHeight, ds.Spec.NodeRadius, fill, stroke, sw);
					break;
				case IconForm.Rounded:
					Rect(sb, g.FrameX, g.FrameY, g.FrameWidth, g.FrameHeight, g.FrameWidth / 5, fill, stroke, sw);
					break;
				case IconForm.Circle:
					_ = sb
						.Append("<circle cx=\"")
						.Append(DesignSystem.Num(cx))
						.Append("\" cy=\"")
						.Append(DesignSystem.Num(cy))
						.Append("\" r=\"")
						.Append(DesignSystem.Num(g.FrameWidth / 2))
						.Append("\" fill=\"")
						.Append(fill)
						.Append("\" stroke=\"")
						.Append(stroke)
						.Append("\" stroke-width=\"")
						.Append(sw)
						.Append("\" />");
					break;
			}
			ArchitectureSvgRenderer.AppendIcon(sb, ds, asset.Source, cx, cy, g.ArtWidth, family.Ink);
			return;
		}

		var image = ResolveImage(asset.Source);
		if (image.Href is null)
		{
			_ = sb
				.Append("<rect x=\"")
				.Append(DesignSystem.Num(g.FrameX))
				.Append("\" y=\"")
				.Append(DesignSystem.Num(g.FrameY))
				.Append("\" width=\"")
				.Append(DesignSystem.Num(g.FrameWidth))
				.Append("\" height=\"")
				.Append(DesignSystem.Num(g.FrameHeight))
				.Append("\" fill=\"")
				.Append(family.Soft)
				.Append("\" stroke=\"")
				.Append(family.Stroke)
				.Append("\" stroke-width=\"1\" stroke-dasharray=\"")
				.Append(DesignSystem.DashArray)
				.Append("\" />");
			DesignSystem.AppendGlyph(sb, DesignSystem.Glyph.Generic, cx, cy, family.Ink, Math.Clamp(Math.Min(g.FrameWidth, g.FrameHeight) / 44, 0.4, 2));
			return;
		}

		_ = sb
			.Append("<image x=\"")
			.Append(DesignSystem.Num(g.FrameX))
			.Append("\" y=\"")
			.Append(DesignSystem.Num(g.FrameY))
			.Append("\" width=\"")
			.Append(DesignSystem.Num(g.FrameWidth))
			.Append("\" height=\"")
			.Append(DesignSystem.Num(g.FrameHeight))
			.Append("\" preserveAspectRatio=\"")
			.Append(asset.KeepAspectRatio ? "xMidYMid meet" : "none")
			.Append("\" href=\"")
			.Append(image.Href)
			.Append("\" />");
	}

	private static (double Width, double Height) ArtSize(NodeAsset asset)
	{
		if (asset.Kind == NodeAssetKind.Icon)
		{
			var size = Math.Max(asset.Height ?? DefaultSize, asset.Width ?? DefaultSize);
			return (size, size);
		}

		var natural = ResolveImage(asset.Source);
		if (asset.KeepAspectRatio)
		{
			var aspect = natural.Width / natural.Height;
			if (asset.Height is { } h)
				return (h * aspect, h);
			if (asset.Width is { } w)
				return (w, w / aspect);
		}
		return (asset.Width ?? natural.Width, asset.Height ?? natural.Height);
	}

	private static (double Width, double Height) FrameSize(NodeAsset asset, (double Width, double Height) art)
	{
		if (asset.Kind == NodeAssetKind.Image)
			return art;
		return asset.Form switch
		{
			IconForm.Square or IconForm.Rounded => (art.Width + (2 * FramePadding), art.Height + (2 * FramePadding)),
			IconForm.Circle => ((art.Width * Math.Sqrt(2)) + FramePadding, (art.Width * Math.Sqrt(2)) + FramePadding),
			_ => art,
		};
	}

	private static (double Width, double Height) LabelSize(string label)
	{
		if (label.Length == 0)
			return (0, 0);
		var m = TextMetrics.MeasureMultiline(NodeSizing.WrapLabel(label).AsSpan(), RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);
		return (m.Width, m.Height);
	}

	/// <summary>The image's own size: a PNG's IHDR, or an SVG's width and height or viewBox. Scaled to fit <see cref="MaxNaturalSize"/>.</summary>
	private static (double Width, double Height) NaturalSize(string href)
	{
		var comma = href.IndexOf(',');
		var bytes = Convert.FromBase64String(href[(comma + 1)..]);
		var (w, h) = href.StartsWith("data:image/png", StringComparison.Ordinal) ? PngSize(bytes) : SvgSize(bytes);
		if (w <= 0 || h <= 0 || !double.IsFinite(w) || !double.IsFinite(h))
			return (DefaultSize, DefaultSize);
		var scale = Math.Min(1, MaxNaturalSize / Math.Max(w, h));
		return (w * scale, h * scale);
	}

	private static (double Width, double Height) PngSize(byte[] bytes) =>
		bytes.Length >= 24 ? (BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(16, 4)), BinaryPrimitives.ReadUInt32BigEndian(bytes.AsSpan(20, 4))) : (0, 0);

	private static (double Width, double Height) SvgSize(byte[] bytes)
	{
		XElement root;
		try
		{
			var settings = new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null };
			using var stream = new MemoryStream(bytes);
			using var reader = XmlReader.Create(stream, settings);
			root = XDocument.Load(reader).Root!;
		}
		catch (XmlException)
		{
			return (0, 0);
		}
		var (w, h) = (Length(root.Attribute("width")?.Value), Length(root.Attribute("height")?.Value));
		if (w > 0 && h > 0)
			return (w, h);
		var box = root.Attribute("viewBox")?.Value.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
		return box is { Length: 4 } ? (Length(box[2]), Length(box[3])) : (0, 0);
	}

	/// <summary>A length in px; 0 for a percentage or anything else that is not a plain number.</summary>
	private static double Length(string? value)
	{
		if (value is null)
			return 0;
		var number = value.Trim();
		if (number.EndsWith("px", StringComparison.Ordinal))
			number = number[..^2];
		return double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 0;
	}

	private static List<(double X, double Y)> Box(double x, double y, double w, double h) => [(x, y), (x + w, y), (x + w, y + h), (x, y + h)];

	private static void Rect(StringBuilder sb, double x, double y, double w, double h, double radius, string fill, string stroke, string sw) =>
		_ = sb
			.Append("<rect x=\"")
			.Append(DesignSystem.Num(x))
			.Append("\" y=\"")
			.Append(DesignSystem.Num(y))
			.Append("\" width=\"")
			.Append(DesignSystem.Num(w))
			.Append("\" height=\"")
			.Append(DesignSystem.Num(h))
			.Append("\" rx=\"")
			.Append(DesignSystem.Num(radius))
			.Append("\" ry=\"")
			.Append(DesignSystem.Num(radius))
			.Append("\" fill=\"")
			.Append(fill)
			.Append("\" stroke=\"")
			.Append(stroke)
			.Append("\" stroke-width=\"")
			.Append(sw)
			.Append("\" />");
}
