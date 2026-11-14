using System.Text;
using Mermaider.Layout;
using Mermaider.Models;

namespace Mermaider.Rendering;

/// <summary>ZenUML diagrams, drawn by the sequence renderer from <see cref="ZenUmlLayout"/>, with the title above.</summary>
internal static class ZenUmlSvgRenderer
{
	internal static StringBuilder RenderToBuilder(ZenUmlDiagram diagram, SvgRenderContext context) =>
		SequenceSvgRenderer.RenderToBuilder(ZenUmlLayout.Layout(diagram), context, diagram.Title);
}
