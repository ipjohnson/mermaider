using System.Collections.Frozen;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// The shape names <c>@{ shape: ... }</c> accepts: the short name, aliases and internal aliases of every shape in
/// mermaid 12.3.0's <c>shapesDefs</c>, the table its flowchart docs list.
/// </summary>
internal static class FlowchartShapeNames
{
	private static readonly (NodeShape Shape, string[] Names)[] Table =
	[
		(NodeShape.Rectangle, ["rect", "proc", "process", "rectangle", "squareRect"]),
		(NodeShape.Rounded, ["rounded", "event", "roundedRect"]),
		(NodeShape.Stadium, ["stadium", "terminal", "pill"]),
		(NodeShape.Subroutine, ["fr-rect", "subprocess", "subproc", "framed-rectangle", "subroutine"]),
		(NodeShape.Cylinder, ["cyl", "db", "database", "cylinder"]),
		(NodeShape.DataStore, ["datastore", "data-store"]),
		(NodeShape.Folder, ["folder", "directory"]),
		(NodeShape.Bucket, ["bucket"]),
		(NodeShape.Console, ["console"]),
		(NodeShape.Browser, ["browser"]),
		(NodeShape.Person, ["person"]),
		(NodeShape.Circle, ["circle", "circ"]),
		(NodeShape.Bang, ["bang"]),
		(NodeShape.Cloud, ["cloud"]),
		(NodeShape.Diamond, ["diam", "decision", "diamond", "question"]),
		(NodeShape.Hexagon, ["hex", "hexagon", "prepare"]),
		(NodeShape.Parallelogram, ["lean-r", "lean-right", "in-out", "lean_right"]),
		(NodeShape.ParallelogramAlt, ["lean-l", "lean-left", "out-in", "lean_left"]),
		(NodeShape.Trapezoid, ["trap-b", "priority", "trapezoid-bottom", "trapezoid"]),
		(NodeShape.TrapezoidAlt, ["trap-t", "manual", "trapezoid-top", "inv-trapezoid", "inv_trapezoid"]),
		(NodeShape.DoubleCircle, ["dbl-circ", "double-circle", "doublecircle"]),
		(NodeShape.Text, ["text"]),
		(NodeShape.NotchedRectangle, ["notch-rect", "card", "notched-rectangle"]),
		(NodeShape.LinedRectangle, ["lin-rect", "lined-rectangle", "lined-process", "lin-proc", "shaded-process", "shadedProcess"]),
		(NodeShape.StateStart, ["sm-circ", "start", "small-circle", "stateStart"]),
		(NodeShape.StateEnd, ["fr-circ", "stop", "framed-circle", "stateEnd"]),
		(NodeShape.ForkJoin, ["fork", "join", "forkJoin"]),
		(NodeShape.Hourglass, ["hourglass", "collate"]),
		(NodeShape.BraceLeft, ["brace", "comment", "brace-l"]),
		(NodeShape.BraceRight, ["brace-r"]),
		(NodeShape.Braces, ["braces"]),
		(NodeShape.LightningBolt, ["bolt", "com-link", "lightning-bolt"]),
		(NodeShape.Document, ["doc", "document"]),
		(NodeShape.Delay, ["delay", "half-rounded-rectangle"]),
		(NodeShape.HorizontalCylinder, ["h-cyl", "das", "horizontal-cylinder"]),
		(NodeShape.LinedCylinder, ["lin-cyl", "disk", "lined-cylinder"]),
		(NodeShape.CurvedTrapezoid, ["curv-trap", "curved-trapezoid", "display"]),
		(NodeShape.DividedRectangle, ["div-rect", "div-proc", "divided-rectangle", "divided-process"]),
		(NodeShape.Triangle, ["tri", "extract", "triangle"]),
		(NodeShape.WindowPane, ["win-pane", "internal-storage", "window-pane"]),
		(NodeShape.FilledCircle, ["f-circ", "junction", "filled-circle"]),
		(NodeShape.NotchedPentagon, ["notch-pent", "loop-limit", "notched-pentagon"]),
		(NodeShape.FlippedTriangle, ["flip-tri", "manual-file", "flipped-triangle"]),
		(NodeShape.SlopedRectangle, ["sl-rect", "manual-input", "sloped-rectangle"]),
		(NodeShape.StackedDocument, ["docs", "documents", "st-doc", "stacked-document"]),
		(NodeShape.StackedRectangle, ["st-rect", "procs", "processes", "stacked-rectangle"]),
		(NodeShape.BowTieRectangle, ["bow-rect", "stored-data", "bow-tie-rectangle"]),
		(NodeShape.CrossedCircle, ["cross-circ", "summary", "crossed-circle"]),
		(NodeShape.TaggedDocument, ["tag-doc", "tagged-document"]),
		(NodeShape.TaggedRectangle, ["tag-rect", "tagged-rectangle", "tag-proc", "tagged-process"]),
		(NodeShape.Flag, ["flag", "paper-tape"]),
		(NodeShape.Asymmetric, ["odd", "rect_left_inv_arrow"]),
		(NodeShape.LinedDocument, ["lin-doc", "lined-document"]),
	];

	private static readonly FrozenDictionary<string, NodeShape> ByName = Table.SelectMany(e => e.Names.Select(n => (Name: n, e.Shape))).ToFrozenDictionary(
		e => e.Name,
		e => e.Shape,
		StringComparer.Ordinal
	);

	/// <summary>Every accepted name, for tests and the gallery.</summary>
	internal static IEnumerable<string> All => Table.SelectMany(e => e.Names);

	/// <summary>The short name of each shape, in the order of mermaid's table.</summary>
	internal static IEnumerable<(string ShortName, NodeShape Shape)> ShortNames => Table.Select(e => (e.Names[0], e.Shape));

	internal static bool TryGet(string name, out NodeShape shape) => ByName.TryGetValue(name, out shape);

	/// <summary>Shapes mermaid draws without their label: markers rather than boxes.</summary>
	internal static bool DrawsLabel(NodeShape shape) =>
		shape is not (NodeShape.StateStart or NodeShape.StateEnd or NodeShape.ForkJoin or NodeShape.Hourglass or NodeShape.LightningBolt or NodeShape.FilledCircle or NodeShape.CrossedCircle);
}
