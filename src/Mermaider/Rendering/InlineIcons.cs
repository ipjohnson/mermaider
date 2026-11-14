using System.Text;
using Mermaider.Icons;
using Mermaider.Models;
using Mermaider.Text;
using Mermaider.Theming;

namespace Mermaider.Rendering;

/// <summary>
/// FontAwesome references in flowchart and state labels (<c>fa:fa-twitter for peace</c>), drawn as mermaid.js draws
/// them: an icon one em wide inline with the text, and never the reference itself. Labels keep the reference through
/// parsing and layout. Measuring swaps each one for an em space (<see cref="ForMeasure"/>), and drawing writes that em
/// space in the text and puts the icon over it. An icon the <see cref="IconRegistry"/> lacks draws its placeholder glyph
/// and is reported by <see cref="Diagnose"/>.
/// </summary>
internal static class InlineIcons
{
	/// <summary>The em space a reference becomes in the drawn text. It holds the icon's room on the line.</summary>
	internal const char Gap = ' ';

	/// <summary>Where a reference sits in the text <see cref="Replace"/> returns: its line, and its index in that line.</summary>
	internal readonly record struct Slot(int Line, int Offset, IconReference Reference);

	/// <summary>The label as it is measured: each reference an em space. The label itself when it has none.</summary>
	internal static string ForMeasure(string? label) =>
		label is null ? string.Empty : IconReference.InLabel(label) ? IconReference.LabelPattern().Replace(label, Gap.ToString()) : label;

	/// <summary>The label as it is drawn, each reference an em space, and where each reference went.</summary>
	internal static string Replace(string label, out IReadOnlyList<Slot> slots)
	{
		if (!IconReference.InLabel(label))
		{
			slots = [];
			return label;
		}

		var found = new List<Slot>();
		var sb = new StringBuilder(label.Length);
		var (line, lineStart, last) = (0, 0, 0);
		foreach (System.Text.RegularExpressions.Match m in IconReference.LabelPattern().Matches(label))
		{
			for (var i = last; i < m.Index; i++)
			{
				_ = sb.Append(label[i]);
				if (label[i] == '\n')
					(line, lineStart) = (line + 1, sb.Length);
			}
			found.Add(new Slot(line, sb.Length - lineStart, IconReference.FromMatch(m)));
			_ = sb.Append(Gap);
			last = m.Index + m.Length;
		}
		_ = sb.Append(label, last, label.Length - last);
		slots = found;
		return sb.ToString();
	}

	/// <summary>Draws <paramref name="label"/> like <see cref="DesignSystem.AppendText"/>, with each reference as its icon.</summary>
	internal static void AppendText(
		StringBuilder sb,
		DesignSystem ds,
		string label,
		double x,
		double cy,
		TypeRole role,
		string? color = null,
		string anchor = "middle",
		int? weight = null
	)
	{
		var shown = Replace(label, out var slots);
		ds.AppendText(sb, shown, x, cy, role, color, anchor, weight);
		var (px, w) = (DesignSystem.Px(role), weight ?? ds.Weight(role));
		AppendIcons(sb, ds, shown, slots, x, cy, px, anchor, s => TextMetrics.MeasurePlainWidth(s, px, w), color ?? "var(--_text)");
	}

	/// <summary>
	/// Draws the icons of <paramref name="slots"/> over their em spaces in <paramref name="shown"/>, a text drawn at
	/// (<paramref name="x"/>, <paramref name="cy"/>) with <paramref name="anchor"/>, lines <paramref name="px"/> × the line
	/// height apart, and measured with <paramref name="width"/>.
	/// </summary>
	internal static void AppendIcons(
		StringBuilder sb,
		DesignSystem ds,
		string shown,
		IReadOnlyList<Slot> slots,
		double x,
		double cy,
		double px,
		string anchor,
		Func<string, double> width,
		string ink
	)
	{
		if (slots.Count == 0)
			return;
		var lines = shown.Split('\n');
		var padding = width(string.Empty);

		double Inner(string s) => s.Length == 0 ? 0 : width(s) - padding;

		foreach (var slot in slots)
		{
			var line = lines[slot.Line];
			var lineWidth = Inner(line);
			var start = anchor switch
			{
				"start" => x,
				"end" => x - lineWidth,
				_ => x - (lineWidth / 2),
			};
			var before = Inner(line[..slot.Offset]);
			var gap = Inner(line[..(slot.Offset + 1)]) - before;
			var iconCx = start + before + (gap / 2);
			var iconCy = cy + ((slot.Line - ((lines.Length - 1) / 2.0)) * px * TextMetrics.LineHeightRatio);
			_ = sb.Append("<g class=\"label-icon\" data-icon=\"");
			MultilineUtils.AppendEscapedAttr(sb, slot.Reference.Text.AsSpan());
			_ = sb.Append("\">");
			ArchitectureSvgRenderer.AppendIcon(sb, ds, slot.Reference.RegistryName ?? IconRegistry.FallbackName, iconCx, iconCy, px, ink);
			_ = sb.Append("</g>");
		}
	}

	/// <summary>One diagnostic per reference the registry has no icon for, in node, edge and subgraph order.</summary>
	internal static IEnumerable<MermaidDiagnostic> Diagnose(MermaidGraph graph)
	{
		var order = graph.NodeOrder.Count > 0 ? graph.NodeOrder : graph.Nodes.Keys.ToList();
		foreach (var id in order)
		{
			if (graph.Nodes.TryGetValue(id, out var node))
			{
				foreach (var d in DiagnoseLabel(id, "Node", node.Label))
					yield return d;
			}
		}
		foreach (var edge in graph.Edges)
		{
			foreach (var d in DiagnoseLabel($"{edge.Source}-->{edge.Target}", "Edge", edge.Label))
				yield return d;
		}
		foreach (var d in Subgraphs(graph.Subgraphs))
			yield return d;

		static IEnumerable<MermaidDiagnostic> Subgraphs(IEnumerable<MermaidSubgraph> subgraphs)
		{
			foreach (var sg in subgraphs)
			{
				foreach (var d in DiagnoseLabel(sg.Id, "Subgraph", sg.Label))
					yield return d;
				foreach (var d in Subgraphs(sg.Children))
					yield return d;
			}
		}
	}

	private static IEnumerable<MermaidDiagnostic> DiagnoseLabel(string id, string kind, string? label)
	{
		if (!IconReference.InLabel(label))
			yield break;
		foreach (System.Text.RegularExpressions.Match m in IconReference.LabelPattern().Matches(label!))
		{
			var reference = IconReference.FromMatch(m);
			if (reference.RegistryName is null)
				yield return Unknown(id, $"{kind} '{id}'", reference);
		}
	}

	/// <summary>The diagnostic for an icon reference the registry has no icon for.</summary>
	internal static MermaidDiagnostic Unknown(string id, string element, IconReference reference) =>
		new(
			MermaidDiagnosticKind.UnknownIcon,
			id,
			$"{element}: the icon '{reference.Text}' is not registered, so a placeholder glyph is drawn. Register it as '{(reference.Pack.Length > 0 ? reference.Pack + ":" : "")}{reference.Name}' with IconRegistry.Register."
		);
}
