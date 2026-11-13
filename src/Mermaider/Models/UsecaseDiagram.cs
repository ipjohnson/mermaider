namespace Mermaider.Models;

/// <summary>UML use case diagram (<c>usecase-beta</c>): actors, use cases, system boundaries, notes, JSON tables and relationships.</summary>
public sealed record UsecaseDiagram
{
	public Direction Direction { get; init; } = Direction.LR;
	public required IReadOnlyList<UsecaseActor> Actors { get; init; }
	public required IReadOnlyList<UsecaseCase> UseCases { get; init; }
	public required IReadOnlyList<UsecaseBoundary> Boundaries { get; init; }
	public required IReadOnlyList<UsecaseRelationship> Relationships { get; init; }
	public required IReadOnlyList<UsecaseNote> Notes { get; init; }
	public required IReadOnlyList<UsecaseJsonTable> JsonTables { get; init; }

	/// <summary>Author styles per element or explicit edge id, after <c>classDef</c>, <c>class</c>, <c>:::</c> and <c>style</c> are applied in mermaid's order.</summary>
	public IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> Styles
	{
		get;
		init;
	} = new Dictionary<string, IReadOnlyDictionary<string, string>>();

	/// <summary>Valid input the renderer does not draw, such as edge animation, with its source line and the reason.</summary>
	public IReadOnlyList<UsecaseUnusedInput> Unused { get; init; } = [];
}

/// <summary>A label as written: <see cref="IsMarkdown"/> for a <c>"`…`"</c> string, whose markers are formatting.</summary>
public sealed record UsecaseLabel(string Text, bool IsMarkdown = false);

public enum UsecaseActorType
{
	Normal,
	Hollow,
	Awesome,
	Icon,
}

public sealed record UsecaseActor(
	string Id,
	UsecaseLabel Label,
	UsecaseActorType Type = UsecaseActorType.Normal,
	string? Icon = null,
	bool Business = false,
	string? Stereotype = null,
	string? Boundary = null
);

public enum UsecaseShape
{
	Ellipse,
	Rect,
}

public sealed record UsecaseCase(
	string Id,
	UsecaseLabel Label,
	UsecaseShape Shape = UsecaseShape.Ellipse,
	bool Business = false,
	string? Stereotype = null,
	string? Boundary = null
);

public enum UsecaseBoundaryType
{
	Rect,
	Package,
}

public sealed record UsecaseBoundary(string Id, UsecaseLabel Label, UsecaseBoundaryType Type, IReadOnlyList<string> Members);

public enum UsecaseRelationshipType
{
	Association,
	Include,
	Extend,
	Generalization,
}

/// <summary>What an end of a relationship draws.</summary>
public enum UsecaseEnd
{
	None,
	Arrow,
	Circle,
	Cross,
	Triangle,
}

public sealed record UsecaseRelationship(
	string? Id,
	string Source,
	string Target,
	UsecaseRelationshipType Type,
	UsecaseEnd StartEnd,
	UsecaseEnd EndEnd,
	UsecaseLabel? Label,
	int MinLength = 1
);

public sealed record UsecaseNote(string Id, string Target, UsecaseLabel Label);

/// <summary>A row of a JSON table: <paramref name="Key"/> is the leaf path (<c>items[0].name</c>), blank after the first value of a scalar array.</summary>
public sealed record UsecaseJsonRow(string Key, string Value);

public sealed record UsecaseJsonTable(string Id, IReadOnlyList<UsecaseJsonRow> Rows);

public sealed record UsecaseUnusedInput(int Line, string Text, string Reason);
