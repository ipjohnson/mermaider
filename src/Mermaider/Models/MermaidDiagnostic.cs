namespace Mermaider.Models;

/// <summary>Why a part of a diagram was drawn differently from what its source asks for.</summary>
public enum MermaidDiagnosticKind
{
	/// <summary>
	/// An icon node, a <c>fa:fa-name</c> label reference or a mindmap <c>::icon(...)</c> names an icon the
	/// <see cref="Icons.IconRegistry"/> does not have. It draws a placeholder glyph.
	/// </summary>
	UnknownIcon,

	/// <summary>An image node points at an <c>http(s)</c> or other external URL, which is never fetched. It draws a placeholder box.</summary>
	ExternalImage,

	/// <summary>An image node's data URI is not a valid base64 <c>data:image/svg+xml</c> or <c>data:image/png</c> image. It draws a placeholder box.</summary>
	UnsupportedImage
}

/// <summary>A note about one element of a rendered diagram. See <see cref="RenderOptions.OnDiagnostics"/>.</summary>
/// <param name="Kind">What happened.</param>
/// <param name="ElementId">
/// The id of the node or subgraph as written in the source, <c>A--&gt;B</c> for an edge, or a mindmap node's label.
/// </param>
/// <param name="Message">A sentence for a person, naming the element and what was drawn instead.</param>
public sealed record MermaidDiagnostic(MermaidDiagnosticKind Kind, string ElementId, string Message);
