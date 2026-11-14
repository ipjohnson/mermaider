using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Rendering;

internal static class NodeSizing
{
	private const double MaxContentWidth = 220;

	/// <summary>A wrapped node label's size, each <c>fa:fa-name</c> reference measured as its inline icon.</summary>
	internal static MultilineMetrics MeasureLabel(string wrapped) =>
		TextMetrics.MeasureMultiline(InlineIcons.ForMeasure(wrapped).AsSpan(), RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);

	private static double Width(string text) =>
		TextMetrics.MeasureTextWidth(InlineIcons.ForMeasure(text), RenderConstants.FontSizes.NodeLabel, RenderConstants.FontWeights.NodeLabel);

	internal static string WrapLabel(string label)
	{
		if (!label.Contains('\n') && Width(label) <= MaxContentWidth)
			return label;

		var lines = label.Split('\n');
		var result = new List<string>();
		foreach (var line in lines)
		{
			var lineW = Width(line);
			if (lineW <= MaxContentWidth)
			{
				result.Add(line);
				continue;
			}

			var words = line.Split(' ');
			var current = "";
			foreach (var word in words)
			{
				var candidate = current.Length == 0 ? word : current + " " + word;
				var candidateW = Width(candidate);
				if (candidateW > MaxContentWidth && current.Length > 0)
				{
					result.Add(current);
					current = word;
				}
				else
				{
					current = candidate;
				}
			}
			if (current.Length > 0)
				result.Add(current);
		}
		return string.Join('\n', result);
	}

	/// <param name="label">The node's label.</param>
	/// <param name="shape">The node's shape.</param>
	/// <param name="acrossFlow">True in a left-to-right or right-to-left flowchart, where a fork bar stands upright.</param>
	/// <param name="asset">The icon or image of an icon or image node.</param>
	internal static (double Width, double Height) Estimate(string label, NodeShape shape, bool acrossFlow = false, NodeAsset? asset = null)
	{
		if (asset is not null)
			return NodeAssets.Size(asset, label);
		if (shape == NodeShape.ForkJoin && acrossFlow)
			return (8, 120);
		if (FlowchartShapes.FixedSize(shape) is { } fixedSize)
			return fixedSize;

		label = WrapLabel(label);
		var metrics = MeasureLabel(label);

		var width = metrics.Width + (RenderConstants.NodePadding.Horizontal * 2);
		var height = metrics.Height + (RenderConstants.NodePadding.Vertical * 2);

		switch (shape)
		{
			case NodeShape.Diamond when label.Length == 0:
				// a label-less diamond is a state-diagram choice pseudo-state: a small marker, not a decision box
				return (32, 32);
			case NodeShape.Diamond:
				{
					var side = Math.Max(width, height) + RenderConstants.NodePadding.DiamondExtra;
					width = side;
					height = side;
					break;
				}
			case NodeShape.Circle or NodeShape.DoubleCircle:
				{
					var diameter = Math.Ceiling(Math.Sqrt((width * width) + (height * height))) + 8;
					width = shape == NodeShape.DoubleCircle ? diameter + 12 : diameter;
					height = width;
					break;
				}
			case NodeShape.Hexagon or NodeShape.Trapezoid or NodeShape.TrapezoidAlt or NodeShape.Parallelogram or NodeShape.ParallelogramAlt:
				width += RenderConstants.NodePadding.Horizontal;
				break;
			case NodeShape.Asymmetric:
				width += 12;
				break;
			case NodeShape.Cylinder:
				height += 14;
				break;
			case NodeShape.StateStart or NodeShape.StateEnd:
				return (24, 24);
			case NodeShape.ForkJoin:
				return (120, 8);
			default:
				(width, height) = FlowchartShapes.Grow(shape, width, height);
				break;
		}

		width = Math.Max(width, 64);
		height = Math.Max(height, 40);

		return (width, height);
	}
}
