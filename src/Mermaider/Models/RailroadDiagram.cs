namespace Mermaider.Models;

/// <summary>
/// A railroad (syntax) diagram: grammar rules drawn one below the other. The four notations mermaid takes
/// (<c>railroad-beta</c>, <c>railroad-ebnf-beta</c>, <c>railroad-abnf-beta</c> and <c>railroad-peg-beta</c>) all parse
/// into this model, as they do into mermaid's railroad db.
/// </summary>
public sealed record RailroadDiagram
{
	public required RailroadNotation Notation { get; init; }
	public string? Title { get; init; }
	public required IReadOnlyList<RailroadRule> Rules { get; init; }

	/// <summary>Valid input the renderer does not draw, with its source line and the reason.</summary>
	public IReadOnlyList<RailroadUnusedInput> Unused { get; init; } = [];
}

public enum RailroadNotation
{
	/// <summary><c>railroad-beta</c>: mermaid's constructors, such as <c>sequence(a, b)</c>.</summary>
	Ir,
	Ebnf,
	Abnf,
	Peg,
}

/// <summary>A rule and its definition. A name may repeat; every rule is drawn, in source order.</summary>
public sealed record RailroadRule(string Name, RailroadExpression Definition, int Line);

/// <summary>A node of a rule's definition. Sequences and choices of one item are never built: the item stands alone.</summary>
public abstract record RailroadExpression;

/// <summary>A literal, drawn as a rounded box.</summary>
public sealed record RailroadTerminal(string Value) : RailroadExpression;

/// <summary>A reference to a rule, drawn as a square box.</summary>
public sealed record RailroadNonTerminal(string Name) : RailroadExpression;

/// <summary>An EBNF special sequence, a PEG predicate or any-character, drawn as a dashed box.</summary>
public sealed record RailroadSpecial(string Text) : RailroadExpression;

public sealed record RailroadSequence(IReadOnlyList<RailroadExpression> Items) : RailroadExpression;

public sealed record RailroadChoice(IReadOnlyList<RailroadExpression> Alternatives) : RailroadExpression;

public sealed record RailroadOptional(RailroadExpression Item) : RailroadExpression;

/// <summary>A loop. <see cref="Max"/> is null when unbounded; a minimum of 0 adds a bypass above the item.</summary>
public sealed record RailroadRepetition(RailroadExpression Item, int Min, int? Max) : RailroadExpression;

/// <summary>Input mermaid accepts that is not drawn: the 1-based source line, the text and why.</summary>
public sealed record RailroadUnusedInput(int Line, string Text, string Reason);
