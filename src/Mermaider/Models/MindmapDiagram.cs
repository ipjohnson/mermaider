namespace Mermaider.Models;

public sealed record MindmapDiagram
{
	public required MindmapNode Root { get; init; }
}

public sealed record MindmapNode
{
	public required string Label { get; init; }
	public MindmapShape Shape { get; init; } = MindmapShape.Rounded;

	/// <summary>The icon's class list from <c>::icon(...)</c>, such as <c>fa fa-book</c>. Null when the node has none.</summary>
	public string? Icon { get; init; }
	public required IReadOnlyList<MindmapNode> Children { get; init; }
}

public enum MindmapShape
{
	Default,
	Rounded,
	Square,
	Circle,
	Bang,
	Cloud,
	Hexagon,
}
