namespace Mermaider.Models;

/// <summary>
/// ZenUML sequence diagram (<c>zenuml</c>): participants, then a tree of statements. Sync calls and creations hold the
/// statements nested in their braces, and fragments hold their sections.
/// </summary>
public sealed record ZenUmlDiagram
{
	public string? Title { get; init; }

	/// <summary>Participants in drawing order. The starter is first when a message comes from it.</summary>
	public required IReadOnlyList<ZenUmlParticipant> Participants { get; init; }

	/// <summary>The id of the participant that top-level calls come from: <c>@Starter(X)</c>, or <c>_STARTER_</c>.</summary>
	public required string Starter { get; init; }

	public IReadOnlyList<ZenUmlGroup> Groups { get; init; } = [];

	public required IReadOnlyList<ZenUmlStatement> Statements { get; init; }

	/// <summary>Valid input the renderer does not draw, with its source line and the reason.</summary>
	public IReadOnlyList<ZenUmlUnusedInput> Unused { get; init; } = [];
}

/// <summary>
/// A participant. <see cref="Annotation"/> is the annotator without its <c>@</c>, such as <c>Actor</c> or
/// <c>Database</c>. <see cref="IsStarter"/> marks the implicit <c>_STARTER_</c> participant.
/// </summary>
public sealed record ZenUmlParticipant(string Id, string Label, string? Annotation, string? Stereotype, bool IsStarter = false);

public sealed record ZenUmlGroup(string Name, IReadOnlyList<string> ParticipantIds);

/// <summary>A statement with its 1-based source line and the <c>//</c> comment written above it, if any.</summary>
public abstract record ZenUmlStatement(int Line, string? Comment);

public enum ZenUmlMessageKind
{
	Sync,
	Async,
	Create,
	Return
}

/// <summary>
/// A message. A sync call or creation with an <see cref="Assignee"/> returns it to <see cref="From"/>, and its
/// <see cref="Body"/> holds the statements in its braces, sent from <see cref="To"/>.
/// </summary>
public sealed record ZenUmlMessage(
	int Line,
	string? Comment,
	ZenUmlMessageKind Kind,
	string From,
	string To,
	string Label,
	string? Assignee,
	IReadOnlyList<ZenUmlStatement> Body
) : ZenUmlStatement(Line, Comment);

public enum ZenUmlFragmentKind
{
	Loop,
	Alt,
	Opt,
	Par,
	Try
}

/// <summary>
/// A fragment. <see cref="Condition"/> is the source text in the parentheses of <c>if</c> or <c>while</c>. The first
/// section is the body; each later one is an <c>else if</c>, <c>else</c>, <c>catch</c> or <c>finally</c> with its label.
/// </summary>
public sealed record ZenUmlFragment(
	int Line,
	string? Comment,
	ZenUmlFragmentKind Kind,
	string Condition,
	IReadOnlyList<ZenUmlSection> Sections
) : ZenUmlStatement(Line, Comment);

public sealed record ZenUmlSection(string Label, IReadOnlyList<ZenUmlStatement> Statements);

/// <summary>A divider, <c>== text ==</c>, drawn across every lifeline.</summary>
public sealed record ZenUmlDivider(int Line, string? Comment, string Text) : ZenUmlStatement(Line, Comment);

/// <summary>Input ZenUML accepts that is not drawn: the 1-based source line, the text and why.</summary>
public sealed record ZenUmlUnusedInput(int Line, string Text, string Reason);
