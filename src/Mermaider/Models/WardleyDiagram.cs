namespace Mermaider.Models;

/// <summary>
/// Wardley map (<c>wardley-beta</c>): components placed by visibility and evolution, their dependencies, evolution
/// trends, pipelines, notes, numbered annotations and forces. Coordinates are percent (0–100) as mermaid keeps them:
/// a source value up to 1 is a fraction, a larger one is already a percent.
/// </summary>
public sealed record WardleyDiagram
{
	public string? Title { get; init; }

	/// <summary>Canvas size from <c>size [width, height]</c>, or null for the default.</summary>
	public WardleySize? Size { get; init; }

	/// <summary>The evolution axis stages from <c>evolution A -> B -> …</c>, or empty for Genesis, Custom Built, Product, Commodity.</summary>
	public IReadOnlyList<string> Stages { get; init; } = [];

	/// <summary>Stage end positions (0–1) from <c>Stage@0.25</c>; used only when there is one per stage.</summary>
	public IReadOnlyList<double> StageBoundaries { get; init; } = [];

	public required IReadOnlyList<WardleyNode> Nodes { get; init; }
	public required IReadOnlyList<WardleyLink> Links { get; init; }
	public required IReadOnlyList<WardleyTrend> Trends { get; init; }
	public required IReadOnlyList<WardleyPipeline> Pipelines { get; init; }
	public required IReadOnlyList<WardleyNote> Notes { get; init; }
	public required IReadOnlyList<WardleyAnnotation> Annotations { get; init; }

	/// <summary>Where the list of annotation texts is drawn, from the first <c>annotations [x, y]</c>.</summary>
	public WardleyPoint? AnnotationsBox { get; init; }

	public required IReadOnlyList<WardleyForce> Forces { get; init; }

	/// <summary>Valid input the renderer does not draw, with its source line and the reason.</summary>
	public IReadOnlyList<WardleyUnusedInput> Unused { get; init; } = [];
}

public sealed record WardleySize(int Width, int Height);

/// <summary>A position in percent: <see cref="Evolution"/> left to right, <see cref="Visibility"/> bottom to top.</summary>
public sealed record WardleyPoint(double Evolution, double Visibility);

public enum WardleyNodeKind
{
	Component,
	Anchor,
	PipelineComponent,
}

public enum WardleySourcing
{
	None,
	Build,
	Buy,
	Outsource,
	Market,
}

/// <summary>
/// A component, anchor or pipeline component. A pipeline component's <see cref="Id"/> is <c>Parent_Name</c>, as in
/// mermaid; links find it by its label.
/// </summary>
public sealed record WardleyNode(
	string Id,
	string Label,
	WardleyNodeKind Kind,
	WardleyPoint Position,
	double? LabelOffsetX = null,
	double? LabelOffsetY = null,
	bool Inertia = false,
	WardleySourcing Sourcing = WardleySourcing.None,
	bool IsPipelineParent = false
);

public enum WardleyFlow
{
	None,
	Forward,
	Backward,
	Bidirectional,
}

public sealed record WardleyLink(string Source, string Target, bool Dashed = false, string? Label = null, WardleyFlow Flow = WardleyFlow.None);

/// <summary>An <c>evolve</c> arrow from a node to <see cref="TargetEvolution"/> at the node's visibility.</summary>
public sealed record WardleyTrend(string NodeId, double TargetEvolution);

public sealed record WardleyPipeline(string NodeId, IReadOnlyList<string> ComponentIds);

public sealed record WardleyNote(string Text, WardleyPoint Position);

public sealed record WardleyAnnotation(int Number, WardleyPoint Position, string Text);

/// <summary>An accelerator (right arrow) or, with <see cref="Deaccelerator"/>, a deaccelerator (left arrow).</summary>
public sealed record WardleyForce(string Name, WardleyPoint Position, bool Deaccelerator);

/// <summary>Input mermaid accepts that is not drawn: the 1-based source line, the text and why.</summary>
public sealed record WardleyUnusedInput(int Line, string Text, string Reason);
