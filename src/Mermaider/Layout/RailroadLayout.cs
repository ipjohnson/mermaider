using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using Mermaider.Models;
using Mermaider.Rendering;
using Mermaider.Text;

namespace Mermaider.Layout;

internal enum RailroadBoxKind
{
	Terminal,
	NonTerminal,
	Special,
}

/// <summary>A terminal, nonterminal or special box at its absolute position.</summary>
internal sealed record RailroadBoxLayout(RailroadBoxKind Kind, string Text, double X, double Y, double Width, double Height)
{
	internal double Cx => X + (Width / 2);
	internal double Cy => Y + (Height / 2);
}

/// <summary>The bounds of a repetition (<c>×3</c>, <c>×2–4</c>, <c>×2+</c>) centred on the loop's return line.</summary>
internal sealed record RailroadCountLayout(string Text, double Cx, double Cy);

/// <summary>
/// One rule: its <c>name =</c> label, the start and end markers on the rule's main line, every track as one path, and the
/// boxes and repeat counts on it.
/// </summary>
internal sealed record RailroadRuleLayout(
	RailroadRule Rule,
	double NameX,
	double BaselineY,
	double StartX,
	double EndX,
	string Tracks,
	IReadOnlyList<RailroadBoxLayout> Boxes,
	IReadOnlyList<RailroadCountLayout> Counts
);

internal sealed record RailroadLayoutResult(double Width, double Height, IReadOnlyList<RailroadRuleLayout> Rules);

/// <summary>
/// Railroad layout at mermaid 12.3.0's geometry (its <c>railroadRenderer.ts</c>): box padding 10, 10 between items of a
/// sequence, 8 between alternatives and rules, arc radius 10 and marker radius 5. A sequence lines its items up on their
/// centre lines; a choice stacks its alternatives centred under one another and joins them with arcs; an optional adds a
/// bypass above; a repetition adds a return loop below and, when it may repeat zero times, a bypass above. Mermaider adds a
/// page margin and a title row, and draws the bounds of an ABNF repetition, which mermaid leaves out.
/// </summary>
internal static class RailroadLayout
{
	internal const double Padding = 10;
	internal const double HorizontalSeparation = 10;
	internal const double VerticalSeparation = 8;
	internal const double Arc = 10;
	internal const double MarkerRadius = 5;
	internal const double Margin = 20;
	internal const double TitleCy = 30;
	internal const double TitledTop = 56;

	/// <summary>Room under a repetition whose bounds are drawn on its return line.</summary>
	private const double CountSpace = 8;

	/// <summary>Weight text is measured at: the heaviest a preset sets a label in, so layout does not depend on the preset.</summary>
	private const int MeasureWeight = 600;

	private const int NameWeight = 700;

	private readonly record struct Size(double Width, double Up, double Down)
	{
		internal double Height => Up + Down;
	}

	internal static RailroadLayoutResult Layout(RailroadDiagram diagram)
	{
		var top = diagram.Title is { Length: > 0 } ? TitledTop : Margin;
		var width = diagram.Title is { Length: > 0 } title
			? Margin + DesignSystem.TitleIndent + DesignSystem.MeasureRole(title, TypeRole.Title, 700) + Margin
			: 0;

		if (diagram.Rules.Count == 0)
			return new RailroadLayoutResult(Math.Max(width, 200), Math.Max(top + Margin, 100), []);

		var sizes = new Dictionary<RailroadExpression, Size>(ReferenceEqualityComparer.Instance);
		var rules = new List<RailroadRuleLayout>(diagram.Rules.Count);
		var y = top;
		foreach (var rule in diagram.Rules)
		{
			var def = Measure(rule.Definition, sizes);
			var nameWidth = NameWidth(rule.Name) + 20;
			var definitionX = Margin + nameWidth + 20;
			var baseline = y + Math.Max(20, def.Up);

			var tracks = new StringBuilder();
			var boxes = new List<RailroadBoxLayout>();
			var counts = new List<RailroadCountLayout>();
			Place(rule.Definition, definitionX, baseline - def.Up, sizes, tracks, boxes, counts);

			var startX = Margin + nameWidth;
			var endX = definitionX + def.Width + 10;
			Line(tracks, startX + MarkerRadius, baseline, definitionX, baseline);
			Line(tracks, definitionX + def.Width, baseline, endX - MarkerRadius, baseline);

			rules.Add(new RailroadRuleLayout(rule, Margin, baseline, startX, endX, tracks.ToString().TrimEnd(), boxes, counts));
			width = Math.Max(width, endX + MarkerRadius + Margin);
			y += Math.Max(40, baseline - def.Up - y + def.Height + (Padding * 2)) + VerticalSeparation;
		}

		return new RailroadLayoutResult(Math.Ceiling(width), Math.Ceiling(y - VerticalSeparation + Margin), rules);
	}

	internal static double NameWidth(string name) => DesignSystem.MeasureRole(name + " =", TypeRole.Label, NameWeight);

	/// <summary>The bounds text of a repetition, or null for <c>*</c> and <c>+</c>, which the loop and bypass already show.</summary>
	internal static string? CountText(RailroadRepetition r)
	{
		if (r.Max is null && r.Min <= 1)
			return null;
		var min = r.Min.ToString(CultureInfo.InvariantCulture);
		return r.Max switch
		{
			null => $"×{min}+",
			var max when max == r.Min => $"×{min}",
			var max => $"×{min}–{max.Value.ToString(CultureInfo.InvariantCulture)}",
		};
	}

	private static Size Leaf(string text)
	{
		var m = TextMetrics.MeasureMultiline(text.AsSpan(), DesignSystem.Px(TypeRole.Label), MeasureWeight);
		var h = m.Height + (Padding * 2);
		// at least as wide as tall, so a one-character terminal is a circle rather than an upright ellipse
		return new Size(Math.Max(h, m.Width + (Padding * 2)), h / 2, h / 2);
	}

	private static Size Measure(RailroadExpression node, Dictionary<RailroadExpression, Size> sizes)
	{
		if (sizes.TryGetValue(node, out var known))
			return known;
		RuntimeHelpers.EnsureSufficientExecutionStack();
		Size size;
		switch (node)
		{
			case RailroadTerminal t:
				size = Leaf(t.Value);
				break;
			case RailroadNonTerminal n:
				size = Leaf(n.Name);
				break;
			case RailroadSpecial s:
				size = Leaf(s.Text);
				break;
			case RailroadSequence seq:
				{
					double w = 0, up = 0, down = 0;
					foreach (var item in seq.Items)
					{
						var m = Measure(item, sizes);
						w += m.Width;
						up = Math.Max(up, m.Up);
						down = Math.Max(down, m.Down);
					}

					size = new Size(w + ((seq.Items.Count - 1) * HorizontalSeparation), up, down);
					break;
				}
			case RailroadChoice choice:
				{
					double maxW = 0, total = 0;
					foreach (var alt in choice.Alternatives)
					{
						var m = Measure(alt, sizes);
						maxW = Math.Max(maxW, m.Width);
						total += m.Height;
					}

					total += (choice.Alternatives.Count - 1) * VerticalSeparation;
					size = new Size(maxW + (Arc * 4), total / 2, total / 2);
					break;
				}
			case RailroadOptional opt:
				{
					var inner = Measure(opt.Item, sizes);
					var up = (Arc * 2) + inner.Up;
					size = new Size(inner.Width + (Arc * 4), up, inner.Height + (Arc * 2) - up);
					break;
				}
			case RailroadRepetition rep:
				{
					var inner = Measure(rep.Item, sizes);
					var elemY = rep.Min == 0 ? Arc * 2 : 0;
					var height = inner.Height + (Arc * 2) + elemY + (CountText(rep) is null ? 0 : CountSpace);
					var up = elemY + inner.Up;
					size = new Size(inner.Width + (Arc * 4), up, height - up);
					break;
				}
			default:
				throw new InvalidOperationException($"Unknown railroad expression {node.GetType().Name}");
		}

		sizes[node] = size;
		return size;
	}

	/// <summary>Places <paramref name="node"/> with its top-left corner at (<paramref name="x"/>, <paramref name="y"/>).</summary>
	private static void Place(
		RailroadExpression node,
		double x,
		double y,
		Dictionary<RailroadExpression, Size> sizes,
		StringBuilder tracks,
		List<RailroadBoxLayout> boxes,
		List<RailroadCountLayout> counts
	)
	{
		var size = sizes[node];
		switch (node)
		{
			case RailroadTerminal t:
				boxes.Add(new RailroadBoxLayout(RailroadBoxKind.Terminal, t.Value, x, y, size.Width, size.Height));
				break;
			case RailroadNonTerminal n:
				boxes.Add(new RailroadBoxLayout(RailroadBoxKind.NonTerminal, n.Name, x, y, size.Width, size.Height));
				break;
			case RailroadSpecial s:
				boxes.Add(new RailroadBoxLayout(RailroadBoxKind.Special, s.Text, x, y, size.Width, size.Height));
				break;
			case RailroadSequence seq:
				{
					var cx = x;
					var lineY = y + size.Up;
					for (var i = 0; i < seq.Items.Count; i++)
					{
						var m = sizes[seq.Items[i]];
						Place(seq.Items[i], cx, y + size.Up - m.Up, sizes, tracks, boxes, counts);
						if (i < seq.Items.Count - 1)
							Line(tracks, cx + m.Width, lineY, cx + m.Width + HorizontalSeparation, lineY);
						cx += m.Width + HorizontalSeparation;
					}

					break;
				}
			case RailroadChoice choice:
				PlaceChoice(choice, x, y, size, sizes, tracks, boxes, counts);
				break;
			case RailroadOptional opt:
				{
					var inner = sizes[opt.Item];
					var elemX = x + (Arc * 2);
					var centerY = y + size.Up;
					Place(opt.Item, elemX, y + (Arc * 2), sizes, tracks, boxes, counts);
					Line(tracks, x, centerY, elemX, centerY);
					Line(tracks, elemX + inner.Width, centerY, x + size.Width, centerY);
					Bypass(tracks, x, y, size.Width, centerY);
					break;
				}
			case RailroadRepetition rep:
				{
					var inner = sizes[rep.Item];
					var elemX = x + (Arc * 2);
					var elemY = y + (rep.Min == 0 ? Arc * 2 : 0);
					var centerY = y + size.Up;
					var right = elemX + inner.Width;
					Place(rep.Item, elemX, elemY, sizes, tracks, boxes, counts);
					Line(tracks, x, centerY, elemX, centerY);
					Line(tracks, right, centerY, x + size.Width, centerY);

					var loopY = elemY + inner.Height + Arc;
					_ = tracks.Append("M ").Append(P(right, centerY));
					ArcTo(tracks, true, right + Arc, centerY + Arc);
					_ = tracks.Append("L ").Append(P(right + Arc, loopY));
					ArcTo(tracks, true, right, loopY + Arc);
					_ = tracks.Append("L ").Append(P(elemX, loopY + Arc));
					ArcTo(tracks, true, elemX - Arc, loopY);
					_ = tracks.Append("L ").Append(P(elemX - Arc, centerY + Arc));
					ArcTo(tracks, true, elemX, centerY);

					if (rep.Min == 0)
						Bypass(tracks, x, y, size.Width, centerY);
					if (CountText(rep) is { } count)
						counts.Add(new RailroadCountLayout(count, (elemX + right) / 2, loopY + Arc));
					break;
				}
		}
	}

	private static void PlaceChoice(
		RailroadChoice choice,
		double x,
		double y,
		Size size,
		Dictionary<RailroadExpression, Size> sizes,
		StringBuilder tracks,
		List<RailroadBoxLayout> boxes,
		List<RailroadCountLayout> counts
	)
	{
		var maxW = size.Width - (Arc * 4);
		var centerY = y + size.Up;
		var right = x + size.Width;
		var top = y;
		foreach (var alt in choice.Alternatives)
		{
			var m = sizes[alt];
			var elemX = x + (Arc * 2) + ((maxW - m.Width) / 2);
			var elemCy = top + m.Up;
			Place(alt, elemX, top, sizes, tracks, boxes, counts);

			// mermaid compares the centre lines exactly
			if (elemCy == centerY)
			{
				Line(tracks, x, centerY, elemX, elemCy);
				Line(tracks, elemX + m.Width, elemCy, right, centerY);
			}
			else
			{
				var below = elemCy > centerY;
				var dir = below ? 1 : -1;
				_ = tracks.Append("M ").Append(P(x, centerY));
				ArcTo(tracks, below, x + Arc, centerY + (dir * Arc));
				_ = tracks.Append("L ").Append(P(x + Arc, elemCy - (dir * Arc)));
				ArcTo(tracks, !below, x + (Arc * 2), elemCy);
				_ = tracks.Append("L ").Append(P(elemX, elemCy));

				_ = tracks.Append("M ").Append(P(elemX + m.Width, elemCy));
				_ = tracks.Append("L ").Append(P(right - (Arc * 2), elemCy));
				ArcTo(tracks, !below, right - Arc, elemCy - (dir * Arc));
				_ = tracks.Append("L ").Append(P(right - Arc, centerY + (dir * Arc)));
				ArcTo(tracks, below, right, centerY);
			}

			top += m.Height + VerticalSeparation;
		}
	}

	/// <summary>The path over the top of an optional or a zero-or-more repetition, from its left end to its right end.</summary>
	private static void Bypass(StringBuilder tracks, double x, double y, double width, double centerY)
	{
		var right = x + width;
		_ = tracks.Append("M ").Append(P(x, centerY));
		ArcTo(tracks, false, x + Arc, centerY - Arc);
		_ = tracks.Append("L ").Append(P(x + Arc, y + Arc));
		ArcTo(tracks, true, x + (Arc * 2), y);
		_ = tracks.Append("L ").Append(P(right - (Arc * 2), y));
		ArcTo(tracks, true, right - Arc, y + Arc);
		_ = tracks.Append("L ").Append(P(right - Arc, centerY - Arc));
		ArcTo(tracks, false, right, centerY);
	}

	private static void Line(StringBuilder tracks, double x1, double y1, double x2, double y2) =>
		tracks.Append("M ").Append(P(x1, y1)).Append("L ").Append(P(x2, y2));

	private static void ArcTo(StringBuilder tracks, bool sweep, double x, double y) =>
		tracks
			.Append("A ")
			.Append(DesignSystem.Num(Arc))
			.Append(' ')
			.Append(DesignSystem.Num(Arc))
			.Append(" 0 0 ")
			.Append(sweep ? '1' : '0')
			.Append(' ')
			.Append(P(x, y));

	private static string P(double x, double y) => DesignSystem.Num(x) + " " + DesignSystem.Num(y) + " ";
}
