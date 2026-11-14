using System.Text;
using Mermaider.Layout;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// Railroad diagram on the design system, at <see cref="RailroadLayout"/>'s geometry. Each rule is a row: its
/// <c>name =</c> label, a start terminal, the tracks in the edge colour, and an end terminal. Nonterminals are boxes in the
/// default cluster family, terminals pills in the next family, and special boxes neutral with a dashed outline. Repeat
/// bounds are meta text with a page halo on the loop's return line.
/// </summary>
internal static class RailroadSvgRenderer
{
	internal static string Render(RailroadDiagram diagram, SvgRenderContext context)
	{
		var sb = RenderToBuilder(diagram, context);
		try
		{
			return sb.ToString();
		}
		finally
		{
			_ = sb.Clear();
			SharedStringBuilderPool.Instance.Return(sb);
		}
	}

	internal static StringBuilder RenderToBuilder(RailroadDiagram diagram, SvgRenderContext context)
	{
		var layout = RailroadLayout.Layout(diagram);
		var sb = SharedStringBuilderPool.Instance.Get();
		var ds = DesignSystem.For(context);

		StyleBlock.AppendSvgOpenTag(
			sb,
			layout.Width,
			layout.Height,
			context.Styles.Colors,
			context.Styles.Transparent,
			context.Accessibility,
			context.DiagramType
		);
		StyleBlock.AppendStyleBlock(sb, context.Styles);

		if (diagram.Title is { Length: > 0 } title)
			ds.AppendTitle(sb, RailroadLayout.Margin, RailroadLayout.TitleCy, title);

		var nonTerminal = ds.Cluster(0);
		var terminal = ds.Cluster(1);
		foreach (var rule in layout.Rules)
		{
			_ = sb.Append("\n<g class=\"railroad-rule\" data-rule=\"");
			MultilineUtils.AppendEscapedAttr(sb, rule.Rule.Name);
			_ = sb.Append("\">\n  ");
			ds.AppendText(sb, rule.Rule.Name + " =", rule.NameX, rule.BaselineY, TypeRole.Label, "var(--_text)", anchor: "start", weight: 700);
			_ = sb
				.Append("\n  <path class=\"railroad-track\" d=\"")
				.Append(rule.Tracks)
				.Append("\" fill=\"none\" stroke=\"")
				.Append(DesignSystem.EdgeColor)
				.Append("\" stroke-width=\"")
				.Append(ds.EdgeWidth)
				.Append("\" stroke-linecap=\"round\" />\n  ");
			ds.AppendTerminal(sb, rule.StartX, rule.BaselineY, RailroadLayout.MarkerRadius, end: false);
			_ = sb.Append("\n  ");
			ds.AppendTerminal(sb, rule.EndX, rule.BaselineY, RailroadLayout.MarkerRadius, end: true);

			foreach (var box in rule.Boxes)
				AppendBox(
					sb,
					ds,
					box,
					box.Kind switch
					{
						RailroadBoxKind.Terminal => terminal,
						RailroadBoxKind.Special => ds.Neutral,
						_ => nonTerminal,
					}
				);

			foreach (var count in rule.Counts)
			{
				_ = sb.Append("\n  ");
				ds.AppendHaloText(sb, count.Text, count.Cx, count.Cy, TypeRole.Meta);
			}

			_ = sb.Append("\n</g>");
		}

		ds.Close(sb);
		return sb;
	}

	private static void AppendBox(StringBuilder sb, DesignSystem ds, RailroadBoxLayout box, ColorFamily family)
	{
		var kind = box.Kind switch
		{
			RailroadBoxKind.Terminal => "terminal",
			RailroadBoxKind.Special => "special",
			_ => "nonterminal",
		};
		_ = sb.Append("\n  <g class=\"node railroad-").Append(kind).Append("\" data-kind=\"").Append(kind).Append("\">\n    ");
		if (box.Kind == RailroadBoxKind.Special)
		{
			_ = sb
				.Append("<rect x=\"")
				.Append(DesignSystem.Num(box.X))
				.Append("\" y=\"")
				.Append(DesignSystem.Num(box.Y))
				.Append("\" width=\"")
				.Append(DesignSystem.Num(box.Width))
				.Append("\" height=\"")
				.Append(DesignSystem.Num(box.Height))
				.Append("\" rx=\"")
				.Append(DesignSystem.Num(ds.Spec.NodeRadius))
				.Append("\" ry=\"")
				.Append(DesignSystem.Num(ds.Spec.NodeRadius))
				.Append("\" fill=\"")
				.Append(family.Tint())
				.Append("\" stroke=\"")
				.Append(family.Edge)
				.Append("\" stroke-width=\"")
				.Append(ds.ContainerStrokeWidth)
				.Append("\" stroke-dasharray=\"")
				.Append(DesignSystem.DashArray)
				.Append("\" />");
		}
		else
		{
			// a terminal is a pill and a nonterminal a box with the preset's radius, at most a quarter of its height
			var radius = box.Kind == RailroadBoxKind.Terminal ? box.Height / 2 : Math.Min(ds.Spec.NodeRadius, box.Height / 4);
			ds.AppendBox(sb, Math.Round(box.X, 3), Math.Round(box.Y, 3), Math.Round(box.Width, 3), Math.Round(box.Height, 3), family, radius);
		}

		_ = sb.Append("\n    ");
		ds.AppendText(sb, box.Text, Math.Round(box.Cx, 3), Math.Round(box.Cy, 3), TypeRole.Label, family.Ink);
		_ = sb.Append("\n  </g>");
	}
}
