namespace Mermaider.Models;

/// <summary>Visual shape of a node in a Mermaid diagram.</summary>
public enum NodeShape
{
	Rectangle,
	Rounded,
	Diamond,
	Stadium,
	Circle,
	Subroutine,
	DoubleCircle,
	Hexagon,
	Cylinder,
	Asymmetric,
	Trapezoid,
	TrapezoidAlt,

	/// <summary><c>[/text/]</c>, leaning right.</summary>
	Parallelogram,

	/// <summary><c>[\text\]</c>, leaning left.</summary>
	ParallelogramAlt,
	StateStart,
	StateEnd,
	ForkJoin,

	/// <summary><c>@{ shape: datastore }</c>: a store open at both ends.</summary>
	DataStore,

	/// <summary><c>@{ shape: folder }</c>.</summary>
	Folder,

	/// <summary><c>@{ shape: bucket }</c>.</summary>
	Bucket,

	/// <summary><c>@{ shape: console }</c>: a terminal window.</summary>
	Console,

	/// <summary><c>@{ shape: browser }</c>: a browser window.</summary>
	Browser,

	/// <summary><c>@{ shape: person }</c>: a figure above its label.</summary>
	Person,

	/// <summary><c>@{ shape: bang }</c>.</summary>
	Bang,

	/// <summary><c>@{ shape: cloud }</c>.</summary>
	Cloud,

	/// <summary><c>@{ shape: text }</c>: the label without a frame.</summary>
	Text,

	/// <summary><c>@{ shape: notch-rect }</c>: a card with its top left corner cut.</summary>
	NotchedRectangle,

	/// <summary><c>@{ shape: lin-rect }</c>: a rectangle with a lined left edge.</summary>
	LinedRectangle,

	/// <summary><c>@{ shape: hourglass }</c>: collate, drawn without its label.</summary>
	Hourglass,

	/// <summary><c>@{ shape: brace }</c>: a comment with a curly brace on its left.</summary>
	BraceLeft,

	/// <summary><c>@{ shape: brace-r }</c>: a comment with a curly brace on its right.</summary>
	BraceRight,

	/// <summary><c>@{ shape: braces }</c>: a comment between curly braces.</summary>
	Braces,

	/// <summary><c>@{ shape: bolt }</c>: a communication link, drawn without its label.</summary>
	LightningBolt,

	/// <summary><c>@{ shape: doc }</c>: a rectangle with a wavy bottom edge.</summary>
	Document,

	/// <summary><c>@{ shape: delay }</c>: a rectangle rounded on its right.</summary>
	Delay,

	/// <summary><c>@{ shape: h-cyl }</c>: a cylinder lying on its side.</summary>
	HorizontalCylinder,

	/// <summary><c>@{ shape: lin-cyl }</c>: a cylinder with a second rim line.</summary>
	LinedCylinder,

	/// <summary><c>@{ shape: curv-trap }</c>: a display.</summary>
	CurvedTrapezoid,

	/// <summary><c>@{ shape: div-rect }</c>: a rectangle with a band across its top.</summary>
	DividedRectangle,

	/// <summary><c>@{ shape: tri }</c>: a triangle pointing up.</summary>
	Triangle,

	/// <summary><c>@{ shape: win-pane }</c>: internal storage.</summary>
	WindowPane,

	/// <summary><c>@{ shape: f-circ }</c>: a junction dot, drawn without its label.</summary>
	FilledCircle,

	/// <summary><c>@{ shape: notch-pent }</c>: a loop limit.</summary>
	NotchedPentagon,

	/// <summary><c>@{ shape: flip-tri }</c>: a triangle pointing down.</summary>
	FlippedTriangle,

	/// <summary><c>@{ shape: sl-rect }</c>: manual input, with a sloped top edge.</summary>
	SlopedRectangle,

	/// <summary><c>@{ shape: docs }</c>: a stack of documents.</summary>
	StackedDocument,

	/// <summary><c>@{ shape: st-rect }</c>: a stack of rectangles.</summary>
	StackedRectangle,

	/// <summary><c>@{ shape: bow-rect }</c>: stored data.</summary>
	BowTieRectangle,

	/// <summary><c>@{ shape: cross-circ }</c>: a summary, drawn without its label.</summary>
	CrossedCircle,

	/// <summary><c>@{ shape: tag-doc }</c>: a document with a folded corner.</summary>
	TaggedDocument,

	/// <summary><c>@{ shape: tag-rect }</c>: a rectangle with a folded corner.</summary>
	TaggedRectangle,

	/// <summary><c>@{ shape: flag }</c>: paper tape, with wavy top and bottom edges.</summary>
	Flag,

	/// <summary><c>@{ shape: lin-doc }</c>: a document with a lined left edge.</summary>
	LinedDocument,

	/// <summary>A subgraph given <c>@{ view: collapsed }</c>, drawn as one box with its title.</summary>
	CollapsedGroup,

	/// <summary><c>@{ icon: "fa:user" }</c>: an icon from the <see cref="Icons.IconRegistry"/>, with its label above or below. See <see cref="MermaidNode.Asset"/>.</summary>
	Icon,

	/// <summary><c>@{ img: "data:image/png;base64,..." }</c>: an image, with its label above or below. See <see cref="MermaidNode.Asset"/>.</summary>
	Image
}
