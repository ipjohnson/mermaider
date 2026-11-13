using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Mermaider.Models;
using Mermaider.Text;

namespace Mermaider.Parsing;

/// <summary>
/// Parses <c>usecase-beta</c> diagrams with the statement rules of mermaid 12.3.0's use case grammar. The parser reads the
/// cleaned source rather than trimmed lines, because Markdown strings, metadata blocks, JSON objects and <c>accDescr</c>
/// blocks may span lines. Input mermaid refuses is refused with a <see cref="MermaidParseException"/> naming the line;
/// valid input the renderer does not draw is listed in <see cref="UsecaseDiagram.Unused"/>.
/// </summary>
internal static partial class UsecaseParser
{
	private const int TimeoutMs = 2000;

	internal static UsecaseDiagram Parse(string source) => new Reader(source).ParseDiagram();

	[GeneratedRegex(@"^usecase-beta(?:[ \t]|$)", RegexOptions.None, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"\W", RegexOptions.None, TimeoutMs)]
	private static partial Regex NonWordPattern();

	[GeneratedRegex(@"^(?:\d+(?:\.\d+)?|\.\d+)(?:px|em|rem|%)?$", RegexOptions.None, TimeoutMs)]
	private static partial Regex NumberPattern();

	internal static bool IsStrokeWidth(string value) => NumberPattern().IsMatch(value.Trim());

	/// <summary>Words the lexer reads as keywords, which therefore cannot be identifiers.</summary>
	private static readonly HashSet<string> Keywords =
	[
		"actor",
		"systemBoundary",
		"end",
		"direction",
		"TD",
		"TB",
		"BT",
		"LR",
		"RL",
		"note",
		"for",
		"json",
		"classDef",
		"class",
		"style",
		"true",
		"false",
	];

	private static readonly HashSet<string> PlantUmlWords =
	[
		with(StringComparer.OrdinalIgnoreCase),
		"allowmixing",
		"newpage",
		"package",
		"rectangle",
		"skinparam"
	];

	private enum Kind
	{
		Reference,
		Actor,
		UseCase,
	}

	private sealed class Element(string id, int line)
	{
		public string Id { get; } = id;
		public int Line { get; } = line;
		public Kind Kind { get; set; } = Kind.Reference;
		public UsecaseLabel? Label { get; set; }
		public UsecaseShape? Shape { get; set; }
		public string? Stereotype { get; set; }
		public string? Boundary { get; set; }
		public string? ActorType { get; set; }
		public string? Icon { get; set; }
		public bool? Business { get; set; }
	}

	private sealed record Edge(
		string? Id,
		string Source,
		string Target,
		UsecaseRelationshipType Type,
		UsecaseEnd Start,
		UsecaseEnd End,
		UsecaseLabel? Label,
		int MinLength,
		bool Markerless,
		int Line
	);

	private sealed record Metadata(List<(string Key, string Value, int Line)> Properties);

	private sealed record Name(string Id, UsecaseLabel? Label, UsecaseShape? Shape, int Line);

	private sealed class Reader(string text)
	{
		private readonly string _text = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n');
		private int _pos;

		private readonly Dictionary<string, Element> _elements = [with(StringComparer.Ordinal)];
		private readonly List<string> _order = [];
		private readonly Dictionary<string, (UsecaseLabel Label, UsecaseBoundaryType Type, List<string> Members, int Line)> _boundaries =
		[
			with(StringComparer.Ordinal)
		];
		private readonly List<string> _boundaryOrder = [];
		private readonly Dictionary<string, (List<UsecaseJsonRow> Rows, int Line)> _json = [with(StringComparer.Ordinal)];
		private readonly List<string> _jsonOrder = [];
		private readonly List<Edge> _edges = [];
		private readonly List<(string Target, UsecaseLabel Label, int Line)> _notes = [];
		private readonly List<(string Target, Metadata Metadata, int Line)> _assignments = [];
		private readonly Dictionary<string, Dictionary<string, string>> _classDefs = [with(StringComparer.Ordinal)];
		private readonly List<(string Id, string Class, int Line)> _classAssignments = [];
		private readonly List<(string Id, List<(string Property, string Value)> Styles, int Line)> _styles = [];
		private readonly List<UsecaseUnusedInput> _unused = [];
		private Direction _direction = Direction.LR;

		// ── statements ────────────────────────────────────────────────────

		public UsecaseDiagram ParseDiagram()
		{
			SkipBlankLinesAndComments();
			SkipHws();
			var headerEnd = LineEnd(_pos);
			var header = _text[_pos..headerEnd];
			if (!HeaderPattern().IsMatch(header))
				throw Error("expected the 'usecase-beta' header");
			_pos += "usecase-beta".Length;
			ExpectLineEnd();

			while (_pos < _text.Length)
			{
				SkipHws();
				if (_pos >= _text.Length)
					break;
				if (_text[_pos] == '\n')
				{
					_pos++;
					continue;
				}

				if (StartsWith("%%"))
				{
					_pos = LineEnd(_pos);
					continue;
				}

				Statement();
			}

			return Build();
		}

		private void Statement()
		{
			var line = Line;
			if (TryAccessibility())
				return;

			var word = PeekWord();
			switch (word)
			{
				case "direction":
					_pos += word.Length;
					SkipHws();
					var dir = PeekWord();
					_direction = dir switch
					{
						"TD" or "TB" => Direction.TD,
						"BT" => Direction.BT,
						"LR" => Direction.LR,
						"RL" => Direction.RL,
						_ => throw Error("expected TD, TB, BT, LR or RL after 'direction'"),
					};
					_pos += dir.Length;
					ExpectLineEnd();
					return;
				case "actor":
					ActorStatement(null, allowRelation: true);
					return;
				case "systemBoundary":
					BoundaryStatement();
					return;
				case "end":
					throw Error("'end' without a systemBoundary");
				case "note":
					NoteStatement();
					return;
				case "json" when IsJsonStart():
					JsonStatement();
					return;
				case "classDef":
					ClassDefStatement();
					return;
				case "class":
					ClassStatement();
					return;
				case "style":
					StyleStatement();
					return;
			}

			if (word.Length > 0 && PlantUmlWords.Contains(word) && !Keywords.Contains(word))
				throw Error($"'{word}' is PlantUML syntax that use case diagrams do not support");

			if (TryMetadataAssignment(line))
				return;

			var source = EntityName(null, declaring: true);
			SkipHws();
			if (!AtLineEnd())
				RelationTail(source, line);
			ExpectLineEnd();
		}

		private bool TryAccessibility()
		{
			var rest = _text.AsSpan(_pos);
			if (rest.StartsWith("accTitle") && rest["accTitle".Length..].TrimStart(" \t").StartsWith(":"))
			{
				_pos = LineEnd(_pos);
				return true;
			}

			if (!rest.StartsWith("accDescr"))
				return false;
			var after = rest["accDescr".Length..].TrimStart(" \t");
			if (after.StartsWith(":"))
			{
				_pos = LineEnd(_pos);
				return true;
			}

			if (!after.StartsWith("{"))
				return false;
			var close = _text.IndexOf('}', _pos);
			if (close < 0)
				throw Error("accDescr block is not closed with '}'");
			_pos = close + 1;
			ExpectLineEnd();
			return true;
		}

		private void ActorStatement(string? boundary, bool allowRelation)
		{
			var line = Line;
			_pos += "actor".Length;
			RequireHws();
			var first = ActorItem(boundary);
			SkipHws();
			if (Peek(','))
			{
				while (Peek(','))
				{
					_pos++;
					SkipHws();
					_ = ActorItem(boundary);
					SkipHws();
				}
			}
			else if (!AtLineEnd())
			{
				if (!allowRelation)
					throw Error("a system boundary holds only actor and use case declarations");
				RelationTail(first, line);
			}

			ExpectLineEnd();
		}

		private string ActorItem(string? boundary)
		{
			var line = Line;
			string id;
			UsecaseLabel? label = null;
			if (TryString(out var quoted))
			{
				id = GeneratedId(quoted);
				label = quoted;
			}
			else
			{
				id = Identifier();
				SkipHws();
				if (Peek('('))
				{
					_pos++;
					label = NodeLabel(')');
					Expect(')');
				}
			}

			var element = Declare(id, line);
			if (element.Kind == Kind.UseCase && element.Shape is not null)
				throw Error($"'{id}' is declared as an actor and as a use case");
			element.Kind = Kind.Actor;
			SetLabel(element, label);
			SetBoundary(element, boundary);
			Suffixes(element, line);
			return id;
		}

		/// <summary>An actor or use case reference or declaration, with its optional metadata, stereotype and classes.</summary>
		private string EntityName(string? boundary, bool declaring)
		{
			var name = ReadName(allowParens: true);
			var element = Declare(name.Id, name.Line);
			if (name.Shape is { } shape)
			{
				if (element.Kind == Kind.Actor)
					throw Error($"'{name.Id}' is declared as an actor and as a use case", name.Line);
				if (element.Shape is { } existing && existing != shape)
					throw Error($"Use case '{name.Id}' has conflicting shapes", name.Line);
				element.Shape = shape;
				element.Kind = Kind.UseCase;
			}

			SetLabel(element, name.Label);
			if (boundary is not null)
			{
				if (element.Kind == Kind.Reference)
					element.Kind = Kind.UseCase;
				SetBoundary(element, boundary);
			}

			var hadSuffix = Suffixes(element, name.Line);
			if (declaring && (name.Label is not null || hadSuffix) && element.Kind == Kind.Reference)
				element.Kind = Kind.UseCase;
			return name.Id;
		}

		private Name ReadName(bool allowParens)
		{
			var line = Line;
			if (TryString(out var quoted))
				return new Name(GeneratedId(quoted), quoted, null, line);

			var id = Identifier();
			SkipHws();
			if (Peek('(') && allowParens)
			{
				_pos++;
				var label = NodeLabel(')');
				Expect(')');
				return new Name(id, label, UsecaseShape.Ellipse, line);
			}

			if (Peek('['))
			{
				_pos++;
				var label = NodeLabel(']');
				Expect(']');
				return new Name(id, label, UsecaseShape.Rect, line);
			}

			return new Name(id, null, null, line);
		}

		/// <summary>Metadata, then a stereotype, then <c>:::</c> classes, in that order. True when any is present.</summary>
		private bool Suffixes(Element element, int line)
		{
			var any = false;
			SkipHws();
			if (StartsWith("@{"))
			{
				any = true;
				ApplyMetadata(element, ReadMetadata(), line);
				SkipHws();
			}

			if (StartsWith("<<"))
			{
				any = true;
				_pos += 2;
				var end = _text.IndexOf(">>", _pos, StringComparison.Ordinal);
				if (end < 0 || _text.AsSpan(_pos, end - _pos).Contains('\n'))
					throw Error("stereotype is not closed with '>>'");
				var stereotype = _text[_pos..end].Trim();
				if (stereotype.Length == 0)
					throw Error("stereotype is empty");
				if (element.Stereotype is { } existing && existing != stereotype)
					throw Error($"Element '{element.Id}' has conflicting stereotypes");
				element.Stereotype = stereotype;
				_pos = end + 2;
				SkipHws();
			}

			if (StartsWith(":::"))
			{
				any = true;
				foreach (var cls in ClassSuffix())
					_classAssignments.Add((element.Id, cls, line));
			}

			return any;
		}

		private List<string> ClassSuffix()
		{
			_pos += 3;
			var classes = new List<string> { Identifier() };
			SkipHws();
			while (Peek(','))
			{
				_pos++;
				SkipHws();
				classes.Add(Identifier());
				SkipHws();
			}

			return classes;
		}

		private void RelationTail(string source, int line)
		{
			string? edgeId = null;
			var save = _pos;
			if (TryIdentifier(out var maybeId))
			{
				SkipHws();
				if (Peek('@') && !StartsWith("@{"))
				{
					_pos++;
					edgeId = maybeId;
					SkipHws();
				}
				else
				{
					_pos = save;
				}
			}

			var edge = Arrow(source, edgeId, line);
			SkipHws();
			var target = EntityName(null, declaring: false);
			_edges.Add(edge with { Target = target });
		}

		private Edge Arrow(string source, string? id, int line)
		{
			Edge Make(UsecaseRelationshipType type, UsecaseEnd start, UsecaseEnd end, UsecaseLabel? label = null, int minLength = 1, bool markerless = false) =>
				new(id, source, "", type, start, end, label, minLength, markerless, line);

			if (StartsWith("..>"))
			{
				_pos += 3;
				SkipHws();
				Expect(':');
				SkipHws();
				var word = PeekWord();
				var type = word.ToLowerInvariant() switch
				{
					"include" => UsecaseRelationshipType.Include,
					"extend" => UsecaseRelationshipType.Extend,
					_ => throw Error("expected 'include' or 'extend' after '..> :'"),
				};
				_pos += word.Length;
				return Make(type, UsecaseEnd.None, UsecaseEnd.Arrow);
			}

			if (StartsWith("--|>"))
			{
				_pos += 4;
				return Make(UsecaseRelationshipType.Generalization, UsecaseEnd.None, UsecaseEnd.Triangle);
			}

			if (StartsWith("<--"))
			{
				var dashes = CountDashes(_pos + 1);
				_pos += 1 + dashes;
				if (dashes == 2 && TryLabelledRight(out var label, out var closing, ["--"]))
					return Make(UsecaseRelationshipType.Association, UsecaseEnd.Arrow, UsecaseEnd.None, label, MinLength(closing.Length));
				return Make(UsecaseRelationshipType.Association, UsecaseEnd.Arrow, UsecaseEnd.None, null, MinLength(dashes));
			}

			if (StartsWith("o--") || StartsWith("x--"))
			{
				var start = _text[_pos] == 'o' ? UsecaseEnd.Circle : UsecaseEnd.Cross;
				_pos += 3;
				if (TryLabelledRight(out var label, out _, ["--"]))
					return Make(UsecaseRelationshipType.Association, start, UsecaseEnd.None, label);
				return Make(UsecaseRelationshipType.Association, start, UsecaseEnd.None);
			}

			if (StartsWith("--o") || StartsWith("--x"))
			{
				var end = _text[_pos + 2] == 'o' ? UsecaseEnd.Circle : UsecaseEnd.Cross;
				_pos += 3;
				return Make(UsecaseRelationshipType.Association, UsecaseEnd.None, end);
			}

			if (StartsWith("--"))
			{
				var dashes = CountDashes(_pos);
				_pos += dashes;
				if (Peek('>'))
				{
					_pos++;
					return Make(UsecaseRelationshipType.Association, UsecaseEnd.None, UsecaseEnd.Arrow, null, MinLength(dashes));
				}

				if (dashes == 2 && TryLabelledRight(out var label, out var closing, ["-->", "--o", "--x", "--"]))
				{
					var end = closing[^1] switch
					{
						'>' => UsecaseEnd.Arrow,
						'o' => UsecaseEnd.Circle,
						'x' => UsecaseEnd.Cross,
						_ => UsecaseEnd.None,
					};
					var length = end is UsecaseEnd.Circle or UsecaseEnd.Cross ? 1 : MinLength(closing.TrimEnd('>').Length);
					return Make(UsecaseRelationshipType.Association, UsecaseEnd.None, end, label, length, end == UsecaseEnd.None);
				}

				return Make(UsecaseRelationshipType.Association, UsecaseEnd.None, UsecaseEnd.None, null, MinLength(dashes), markerless: true);
			}

			throw Error("expected a relationship operator such as '-->', '--', '..> : include' or '--|>'");
		}

		private static int MinLength(int dashes) => Math.Max(1, dashes - 1);

		private int CountDashes(int at)
		{
			var n = 0;
			while (at + n < _text.Length && _text[at + n] == '-')
				n++;
			return n;
		}

		/// <summary>
		/// After an operator that may open a labelled association, reads the label and the operator closing it (one of
		/// <paramref name="closers"/>, with any number of extra dashes) when both are on this line; otherwise moves nothing.
		/// </summary>
		private bool TryLabelledRight(out UsecaseLabel label, out string closing, string[] closers)
		{
			var save = _pos;
			label = new UsecaseLabel("");
			closing = "";
			SkipHws();
			if (AtLineEnd())
			{
				_pos = save;
				return false;
			}

			UsecaseLabel? read;
			if (TryString(out var quoted))
			{
				read = quoted;
			}
			else
			{
				var start = _pos;
				var end = ScanLabel(start, stopAtDashes: true);
				var text = _text[start..end].Trim();
				if (text.Length == 0)
				{
					_pos = save;
					return false;
				}

				_pos = end;
				read = new UsecaseLabel(MultilineUtils.DecodeEntities(text));
			}

			SkipHws();
			if (!StartsWith("--"))
			{
				_pos = save;
				return false;
			}

			var dashes = CountDashes(_pos);
			var after = _pos + dashes < _text.Length ? _text[_pos + dashes] : '\n';
			string candidate;
			if (after == '>' && closers.Contains("-->"))
				candidate = new string('-', dashes) + ">";
			else if (dashes == 2 && after is 'o' or 'x' && closers.Contains("--" + after))
				candidate = "--" + after;
			else if (closers.Contains("--"))
				candidate = new string('-', dashes);
			else
				candidate = "";

			if (candidate.Length == 0)
			{
				_pos = save;
				return false;
			}

			_pos += candidate.Length;
			label = read;
			closing = candidate;
			return true;
		}

		private void BoundaryStatement()
		{
			var line = Line;
			_pos += "systemBoundary".Length;
			RequireHws();
			var name = ReadName(allowParens: true);
			var id = name.Id;
			if (_elements.ContainsKey(id) || _json.ContainsKey(id))
				throw Error($"'{id}' is already used by another element", line);
			var label = name.Label ?? new UsecaseLabel(id);
			var type = UsecaseBoundaryType.Rect;
			if (_boundaries.TryGetValue(id, out var existing))
			{
				if (name.Label is not null && existing.Label != label)
					throw Error($"Boundary '{id}' has conflicting titles", line);
				type = existing.Type;
			}
			else
			{
				_boundaryOrder.Add(id);
			}

			_boundaries[id] = (existing.Label ?? label, type, existing.Members ?? [], line);
			SkipHws();
			if (StartsWith("@{"))
			{
				_assignments.Add((id, ReadMetadata(), line));
				SkipHws();
			}

			if (StartsWith(":::"))
			{
				foreach (var cls in ClassSuffix())
					_classAssignments.Add((id, cls, line));
			}

			ExpectLineEnd();

			while (true)
			{
				SkipHws();
				if (_pos >= _text.Length)
					throw Error($"systemBoundary '{id}' is not closed with 'end'", line);
				if (_text[_pos] == '\n')
				{
					_pos++;
					continue;
				}

				if (StartsWith("%%"))
				{
					_pos = LineEnd(_pos);
					continue;
				}

				var word = PeekWord();
				if (word == "end")
				{
					_pos += 3;
					ExpectLineEnd();
					return;
				}

				if (word == "actor")
				{
					ActorStatement(id, allowRelation: false);
					continue;
				}

				if (word is "systemBoundary")
					throw Error("system boundaries cannot be nested");
				if (Keywords.Contains(word))
					throw Error("a system boundary holds only actor and use case declarations");

				_ = EntityName(id, declaring: true);
				SkipHws();
				if (!AtLineEnd())
					throw Error("a system boundary holds only actor and use case declarations");
				ExpectLineEnd();
			}
		}

		private void NoteStatement()
		{
			var line = Line;
			_pos += "note".Length;
			RequireHws();
			if (PeekWord() != "for")
				throw Error("expected 'note for <target> <text>'");
			_pos += 3;
			RequireHws();
			var target = Identifier();
			RequireHws();
			UsecaseLabel label;
			if (TryString(out var quoted))
			{
				label = quoted;
			}
			else
			{
				var start = _pos;
				var end = ScanLabel(start, stopAtDashes: true);
				var text = _text[start..end].Trim();
				if (text.Length == 0)
					throw Error("a note needs text");
				_pos = end;
				label = new UsecaseLabel(MultilineUtils.DecodeEntities(text));
			}

			_notes.Add((target, label, line));
			ExpectLineEnd();
		}

		private bool IsJsonStart()
		{
			var i = _pos + 4;
			if (i >= _text.Length || _text[i] is not (' ' or '\t'))
				return false;
			while (i < _text.Length && _text[i] is ' ' or '\t')
				i++;
			var idStart = i;
			while (i < _text.Length && IsWordChar(_text[i]))
				i++;
			if (i == idStart)
				return false;
			while (i < _text.Length && _text[i] is ' ' or '\t')
				i++;
			if (i >= _text.Length || _text[i] != '@')
				return false;
			i++;
			while (i < _text.Length && _text[i] is ' ' or '\t')
				i++;
			return i < _text.Length && _text[i] == '{';
		}

		private void JsonStatement()
		{
			var line = Line;
			_pos += 4;
			SkipHws();
			var idStart = _pos;
			while (_pos < _text.Length && IsWordChar(_text[_pos]))
				_pos++;
			var id = _text[idStart.._pos];
			SkipHws();
			Expect('@');
			SkipHws();
			var start = _pos;
			var end = JsonObjectEnd(start);
			var body = _text[start..end];
			_pos = end;
			if (_elements.ContainsKey(id) || _boundaries.ContainsKey(id) || _json.ContainsKey(id))
				throw Error($"'{id}' is already used by another element", line);
			_json[id] = (JsonRows(body, line), line);
			_jsonOrder.Add(id);
			SkipHws();
			if (StartsWith(":::"))
			{
				foreach (var cls in ClassSuffix())
					_classAssignments.Add((id, cls, line));
			}

			ExpectLineEnd();
		}

		private int JsonObjectEnd(int start)
		{
			var depth = 0;
			var quoted = false;
			var escaped = false;
			for (var i = start; i < _text.Length; i++)
			{
				var c = _text[i];
				if (quoted)
				{
					if (escaped)
						escaped = false;
					else if (c == '\\')
						escaped = true;
					else if (c == '"')
						quoted = false;
					continue;
				}

				if (c == '"')
					quoted = true;
				else if (c == '{')
					depth++;
				else if (c == '}' && --depth == 0)
					return i + 1;
			}

			throw Error("JSON object is not closed");
		}

		private List<UsecaseJsonRow> JsonRows(string body, int line)
		{
			JsonDocument doc;
			try
			{
				doc = JsonDocument.Parse(body, new JsonDocumentOptions { MaxDepth = 64 });
			}
			catch (JsonException e)
			{
				throw Error("invalid JSON object: " + e.Message, line);
			}

			using (doc)
			{
				var rows = new List<UsecaseJsonRow>();
				Flatten(doc.RootElement, "", rows);
				return rows;
			}
		}

		private static void Flatten(JsonElement value, string path, List<UsecaseJsonRow> rows)
		{
			switch (value.ValueKind)
			{
				case JsonValueKind.Object:
					{
						// a repeated property keeps its first position and its last value
						var order = new List<string>();
						var values = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
						foreach (var p in value.EnumerateObject())
						{
							if (!values.ContainsKey(p.Name))
								order.Add(p.Name);
							values[p.Name] = p.Value;
						}

						if (order.Count == 0)
						{
							rows.Add(new UsecaseJsonRow(path, "{}"));
							return;
						}

						foreach (var key in order)
							Flatten(values[key], path.Length == 0 ? key : path + "." + key, rows);
						return;
					}
				case JsonValueKind.Array:
					{
						var items = value.EnumerateArray().ToList();
						if (items.Count == 0)
						{
							rows.Add(new UsecaseJsonRow(path, "[]"));
							return;
						}

						if (items.All(i => i.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)))
						{
							for (var i = 0; i < items.Count; i++)
								rows.Add(new UsecaseJsonRow(i == 0 ? path : "", Scalar(items[i])));
							return;
						}

						for (var i = 0; i < items.Count; i++)
							Flatten(items[i], path + "[" + i.ToString(System.Globalization.CultureInfo.InvariantCulture) + "]", rows);
						return;
					}
				default:
					rows.Add(new UsecaseJsonRow(path, Scalar(value)));
					return;
			}
		}

		private static string Scalar(JsonElement value) => value.ValueKind switch
		{
			JsonValueKind.String => value.GetString() ?? "",
			JsonValueKind.Null => "null",
			JsonValueKind.True => "true",
			JsonValueKind.False => "false",
			_ => value.GetRawText(),
		};

		private void ClassDefStatement()
		{
			_pos += "classDef".Length;
			RequireHws();
			var names = IdentifierList();
			RequireHws();
			var styles = Styles();
			foreach (var name in names)
			{
				if (!_classDefs.TryGetValue(name, out var def))
					_classDefs[name] = def = [with(StringComparer.Ordinal)];
				foreach (var (property, value) in styles)
					def[property] = value;
			}

			ExpectLineEnd();
		}

		private void ClassStatement()
		{
			var line = Line;
			_pos += "class".Length;
			RequireHws();
			var targets = IdentifierList();
			RequireHws();
			var classes = IdentifierList();
			foreach (var target in targets)
			{
				foreach (var cls in classes)
					_classAssignments.Add((target, cls, line));
			}

			ExpectLineEnd();
		}

		private void StyleStatement()
		{
			var line = Line;
			_pos += "style".Length;
			RequireHws();
			var target = Identifier();
			RequireHws();
			_styles.Add((target, Styles(), line));
			ExpectLineEnd();
		}

		private List<string> IdentifierList()
		{
			var list = new List<string> { Identifier() };
			while (true)
			{
				var save = _pos;
				SkipHws();
				if (!Peek(','))
				{
					_pos = save;
					return list;
				}

				_pos++;
				SkipHws();
				list.Add(Identifier());
			}
		}

		/// <summary>Comma-separated <c>property:value</c> declarations; <c>\,</c> is a literal comma, a semicolon is refused.</summary>
		private List<(string Property, string Value)> Styles()
		{
			var end = LineEnd(_pos);
			var raw = _text[_pos..end];
			if (raw.Contains(';'))
				throw Error("style declarations are separated by commas; semicolons are not allowed");
			_pos = end;
			var result = new List<(string, string)>();
			var current = new StringBuilder();
			for (var i = 0; i <= raw.Length; i++)
			{
				if (i < raw.Length && raw[i] == '\\' && i + 1 < raw.Length && raw[i + 1] == ',')
				{
					_ = current.Append(',');
					i++;
					continue;
				}

				if (i < raw.Length && raw[i] != ',')
				{
					_ = current.Append(raw[i]);
					continue;
				}

				var item = current.ToString().Trim();
				_ = current.Clear();
				var colon = item.IndexOf(':');
				if (colon <= 0 || colon == item.Length - 1)
					throw Error($"expected 'property:value', found '{item}'");
				result.Add((item[..colon].Trim(), item[(colon + 1)..].Trim()));
			}

			return result;
		}

		private bool TryMetadataAssignment(int line)
		{
			var save = _pos;
			string target;
			if (TryString(out var quoted))
				target = GeneratedId(quoted);
			else if (TryIdentifier(out var id))
				target = id;
			else
				return false;

			if (!StartsWith("@{"))
			{
				_pos = save;
				return false;
			}

			_assignments.Add((target, ReadMetadata(), line));
			ExpectLineEnd();
			return true;
		}

		private Metadata ReadMetadata()
		{
			_pos += 2;
			var properties = new List<(string, string, int)>();
			var expectSeparator = false;
			while (true)
			{
				SkipHws();
				if (_pos >= _text.Length)
					throw Error("metadata is not closed with '}'");
				var c = _text[_pos];
				if (c == '\n')
				{
					_pos++;
					expectSeparator = false;
					continue;
				}

				if (c == '}')
				{
					_pos++;
					return new Metadata(properties);
				}

				if (c == ',')
				{
					_pos++;
					expectSeparator = false;
					continue;
				}

				if (expectSeparator)
					throw Error("metadata properties are separated by commas or new lines");

				var line = Line;
				var key = TryString(out var quotedKey) ? quotedKey.Text : RawWord();
				SkipHws();
				Expect(':');
				SkipHws();
				var value = TryString(out var quotedValue) ? quotedValue.Text : RawWord();
				properties.Add((key, value, line));
				expectSeparator = true;
			}
		}

		private string RawWord()
		{
			var start = _pos;
			while (_pos < _text.Length && IsWordChar(_text[_pos]))
				_pos++;
			return _pos == start ? throw Error("expected a metadata key or value") : _text[start.._pos];
		}

		private void ApplyMetadata(Element element, Metadata metadata, int line) => _assignments.Add((element.Id, metadata, line));

		// ── labels and names ──────────────────────────────────────────────

		private UsecaseLabel NodeLabel(char closer)
		{
			SkipHws();
			if (TryString(out var quoted))
			{
				SkipHws();
				return quoted;
			}

			var start = _pos;
			var end = ScanLabel(start, stopAtDashes: true);
			if (end >= _text.Length || _text[end] != closer)
				throw Error($"expected '{closer}' to close the label");
			var text = _text[start..end].Trim();
			if (text.Length == 0)
				throw Error("a label cannot be empty");
			_pos = end;
			return new UsecaseLabel(MultilineUtils.DecodeEntities(text));
		}

		/// <summary>
		/// The end of an unquoted label starting at <paramref name="start"/>: the first delimiter, quote or reserved sequence
		/// (<c>--</c>, <c>..&gt;</c>, <c>:::</c>, <c>@{</c>, <c>&lt;&lt;</c>) or the end of the line.
		/// </summary>
		private int ScanLabel(int start, bool stopAtDashes)
		{
			var i = start;
			while (i < _text.Length)
			{
				var c = _text[i];
				if (c is '\n' or '(' or ')' or '[' or ']' or '{' or '}' or '"' or '\'')
					return i;
				var rest = _text.AsSpan(i);
				if (
					(stopAtDashes && rest.StartsWith("--")) || rest.StartsWith("<--") || rest.StartsWith("..>") || rest.StartsWith(":::") || rest.StartsWith(
						"@{"
					) || rest.StartsWith("<<")
				)
					return i;
				// o-- and x-- are operators only where a token starts
				if (c is 'o' or 'x' && rest.StartsWith(c == 'o' ? "o--" : "x--") && (i == start || !IsWordChar(_text[i - 1])))
					return i;
				i++;
			}

			return i;
		}

		private bool TryString(out UsecaseLabel label)
		{
			label = new UsecaseLabel("");
			if (_pos >= _text.Length)
				return false;
			if (StartsWith("\"`"))
			{
				var close = _text.IndexOf("`\"", _pos + 2, StringComparison.Ordinal);
				if (close < 0)
					throw Error("Markdown string is not closed with '`\"'");
				label = new UsecaseLabel(MultilineUtils.DecodeEntities(_text[(_pos + 2)..close]), IsMarkdown: true);
				_pos = close + 2;
				return true;
			}

			var q = _text[_pos];
			if (q is not ('"' or '\''))
				return false;
			var end = _pos + 1;
			while (end < _text.Length && _text[end] != q && _text[end] != '\n')
				end++;
			if (end >= _text.Length || _text[end] != q)
				throw Error("string is not closed");
			label = new UsecaseLabel(MultilineUtils.DecodeEntities(_text[(_pos + 1)..end]));
			_pos = end + 1;
			return true;
		}

		private static string GeneratedId(UsecaseLabel label) => NonWordPattern().Replace(label.Text, "_");

		private string Identifier() => TryIdentifier(out var id) ? id : throw Error("expected an identifier");

		private bool TryIdentifier(out string id)
		{
			var start = _pos;
			var end = start;
			while (end < _text.Length && IsWordChar(_text[end]))
				end++;
			id = _text[start..end];
			if (
				id.Length == 0 || Keywords.Contains(id) || id.Equals("include", StringComparison.OrdinalIgnoreCase) || id.Equals(
					"extend",
					StringComparison.OrdinalIgnoreCase
				)
			)
				return false;
			_pos = end;
			return true;
		}

		private string PeekWord()
		{
			var end = _pos;
			while (end < _text.Length && IsWordChar(_text[end]))
				end++;
			return _text[_pos..end];
		}

		private static bool IsWordChar(char c) => char.IsAsciiLetterOrDigit(c) || c == '_';

		// ── model ─────────────────────────────────────────────────────────

		private Element Declare(string id, int line)
		{
			if (_boundaries.ContainsKey(id) || _json.ContainsKey(id))
				return new Element(id, line) { Kind = Kind.Reference };
			if (!_elements.TryGetValue(id, out var element))
			{
				_elements[id] = element = new Element(id, line);
				_order.Add(id);
			}

			return element;
		}

		private void SetLabel(Element element, UsecaseLabel? label)
		{
			if (label is null)
				return;
			if (element.Label is { } existing && existing != label)
				throw Error($"ID '{element.Id}' has conflicting labels");
			element.Label = label;
		}

		private void SetBoundary(Element element, string? boundary)
		{
			if (boundary is null)
				return;
			if (element.Boundary is { } existing && existing != boundary)
				throw Error($"Element '{element.Id}' belongs to more than one system boundary");
			if (element.Boundary is null)
				_boundaries[boundary].Members.Add(element.Id);
			element.Boundary = boundary;
		}

		private UsecaseDiagram Build()
		{
			// endpoints that are neither boundaries nor JSON tables
			var explicitEdges = new Dictionary<string, int>(StringComparer.Ordinal);
			for (var i = 0; i < _edges.Count; i++)
			{
				var e = _edges[i];
				foreach (var end in new[] { e.Source, e.Target })
				{
					if (_boundaries.ContainsKey(end))
						throw Error($"a relationship cannot end on the system boundary '{end}'", e.Line);
				}

				if (e.Id is { } id)
				{
					if (_elements.ContainsKey(id) || _boundaries.ContainsKey(id) || _json.ContainsKey(id) || !explicitEdges.TryAdd(id, i))
						throw Error($"edge ID '{id}' is already used", e.Line);
				}
			}

			foreach (var (target, metadata, line) in _assignments)
				Assign(target, metadata, line, explicitEdges);

			var actors = new List<UsecaseActor>();
			var useCases = new List<UsecaseCase>();
			foreach (var id in _order)
			{
				var e = _elements[id];
				var label = e.Label ?? new UsecaseLabel(id);
				if (e.Kind == Kind.Actor)
				{
					var type = e.Icon is not null
						? UsecaseActorType.Icon
						: e.ActorType switch
						{
							"hollow" => UsecaseActorType.Hollow,
							"awesome" => UsecaseActorType.Awesome,
							_ => UsecaseActorType.Normal,
						};
					if (e.Icon is not null && e.ActorType is { } t && t != "normal")
						throw Error($"Actor '{id}' cannot combine icon with type '{t}'", e.Line);
					if (e.Business == true && type is UsecaseActorType.Icon or UsecaseActorType.Awesome)
						throw Error($"Business actor '{id}' must use normal or hollow geometry", e.Line);
					actors.Add(new UsecaseActor(id, label, type, e.Icon, e.Business == true, e.Stereotype, e.Boundary));
				}
				else
				{
					var shape = e.Shape ?? UsecaseShape.Ellipse;
					if (shape == UsecaseShape.Rect && e.Business == true)
						throw Error($"Rectangular use case '{id}' cannot be a business use case", e.Line);
					useCases.Add(new UsecaseCase(id, label, shape, e.Business == true, e.Stereotype, e.Boundary));
				}
			}

			Kind KindOf(string id) => _elements.TryGetValue(id, out var e) ? (e.Kind == Kind.Actor ? Kind.Actor : Kind.UseCase) : Kind.Reference;

			var relationships = new List<UsecaseRelationship>();
			var anonymous = 0;
			foreach (var e in _edges)
			{
				var sourceJson = _json.ContainsKey(e.Source);
				var targetJson = _json.ContainsKey(e.Target);
				var (sk, tk) = (KindOf(e.Source), KindOf(e.Target));
				if (
					e.Type is UsecaseRelationshipType.Include or UsecaseRelationshipType.Extend &&
					(sourceJson || targetJson || sk != Kind.UseCase || tk != Kind.UseCase)
				)
					throw Error($"{e.Type.ToString().ToLowerInvariant()} relationship requires use-case endpoints", e.Line);
				if (e.Type == UsecaseRelationshipType.Generalization && (sourceJson || targetJson || sk != tk))
					throw Error("Generalization requires actor-to-actor or use-case-to-use-case endpoints", e.Line);
				if ((sourceJson || targetJson) && (e.Start is UsecaseEnd.Circle or UsecaseEnd.Cross || e.End is UsecaseEnd.Circle or UsecaseEnd.Cross))
					throw Error($"JSON relationship '{e.Source}' to '{e.Target}' permits only point, reversed-point, or markerless solid association", e.Line);
				var label = e.Type switch
				{
					UsecaseRelationshipType.Include => new UsecaseLabel("«include»"),
					UsecaseRelationshipType.Extend => new UsecaseLabel("«extend»"),
					_ => e.Label,
				};
				relationships.Add(new UsecaseRelationship(e.Id ?? ("edge-" + anonymous++), e.Source, e.Target, e.Type, e.Start, e.End, label, e.MinLength));
			}

			var notes = new List<UsecaseNote>();
			foreach (var (target, label, line) in _notes)
			{
				if (!_elements.ContainsKey(target))
					throw Error(
						_json.ContainsKey(target) || _boundaries.ContainsKey(target)
							? $"Note target '{target}' must be an actor or a use case"
							: $"Note target '{target}' is unresolved",
						line
					);
				notes.Add(new UsecaseNote("note-" + notes.Count, target, label));
			}

			return new UsecaseDiagram
			{
				Direction = _direction,
				Actors = actors,
				UseCases = useCases,
				Boundaries = _boundaryOrder.Select(
					id => new UsecaseBoundary(id, _boundaries[id].Label, _boundaries[id].Type, _boundaries[id].Members)
				).ToList(),
				Relationships = relationships,
				Notes = notes,
				JsonTables = _jsonOrder.Select(id => new UsecaseJsonTable(id, _json[id].Rows)).ToList(),
				Styles = ResolveStyles(explicitEdges),
				Unused = _unused,
			};
		}

		private void Assign(string target, Metadata metadata, int line, Dictionary<string, int> explicitEdges)
		{
			Exception Invalid(string kind, string key, int at) => Error($"Metadata property '{key}' is invalid for {kind} '{target}'", at);

			if (_boundaries.TryGetValue(target, out var boundary))
			{
				foreach (var (key, value, at) in metadata.Properties)
				{
					if (key != "type")
						throw Invalid("boundary", key, at);
					var type = value switch
					{
						"rect" => UsecaseBoundaryType.Rect,
						"package" => UsecaseBoundaryType.Package,
						_ => throw Error($"Boundary type '{value}' must be rect or package", at),
					};
					_boundaries[target] = boundary = (boundary.Label, type, boundary.Members, boundary.Line);
				}

				return;
			}

			if (_json.ContainsKey(target))
			{
				if (metadata.Properties.Count > 0)
					throw Invalid("json", metadata.Properties[0].Key, metadata.Properties[0].Line);
				return;
			}

			if (explicitEdges.ContainsKey(target))
			{
				foreach (var (key, value, at) in metadata.Properties)
				{
					var valid = (key, value) switch
					{
						("animate", "true" or "false") => true,
						("animation", "fast" or "slow") => true,
						_ => false,
					};
					if (!valid)
						throw Invalid("edge", key, at);
					_unused.Add(new UsecaseUnusedInput(at, $"{key}: {value}", $"edge '{target}': animation is not drawn"));
				}

				return;
			}

			if (!_elements.TryGetValue(target, out var element))
				throw Error($"Metadata target '{target}' is unresolved", line);

			foreach (var (key, value, at) in metadata.Properties)
			{
				var isActor = element.Kind == Kind.Actor;
				switch (key)
				{
					case "business":
						var business = value switch
						{
							"true" => true,
							"false" => false,
							_ => throw Error($"'business' must be true or false", at),
						};
						if (element.Business is { } existing && existing != business)
							throw Error($"'{target}' has conflicting business metadata", at);
						element.Business = business;
						break;
					case "type" when isActor:
						if (value is not ("normal" or "hollow" or "awesome"))
							throw Error($"Actor type '{value}' must be normal, hollow or awesome", at);
						if (element.ActorType is { } type && type != value)
							throw Error($"Actor '{target}' has conflicting type metadata", at);
						element.ActorType = value;
						break;
					case "icon" when isActor:
						if (element.Icon is { } icon && icon != value)
							throw Error($"Actor '{target}' has conflicting icon metadata", at);
						element.Icon = value;
						break;
					default:
						throw Invalid(isActor ? "actor" : "usecase", key, at);
				}
			}
		}

		/// <summary>The <c>default</c> class, then the assigned classes in order, then <c>style</c> declarations; later wins per property.</summary>
		private Dictionary<string, IReadOnlyDictionary<string, string>> ResolveStyles(Dictionary<string, int> explicitEdges)
		{
			bool Known(string id) => _elements.ContainsKey(id) || _boundaries.ContainsKey(id) || _json.ContainsKey(id) || explicitEdges.ContainsKey(id);

			var classes = new Dictionary<string, List<string>>(StringComparer.Ordinal);
			foreach (var (id, cls, line) in _classAssignments)
			{
				if (!Known(id))
					throw Error($"Class/style target '{id}' is unresolved or anonymous", line);
				if (!classes.TryGetValue(id, out var list))
					classes[id] = list = [];
				list.Add(cls);
			}

			foreach (var (id, _, line) in _styles)
			{
				if (!Known(id))
					throw Error($"Class/style target '{id}' is unresolved or anonymous", line);
			}

			var result = new Dictionary<string, IReadOnlyDictionary<string, string>>(StringComparer.Ordinal);
			var ids = _order.Concat(_boundaryOrder).Concat(_jsonOrder).Concat(explicitEdges.Keys);
			foreach (var id in ids)
			{
				var merged = new Dictionary<string, string>(StringComparer.Ordinal);
				if (!explicitEdges.ContainsKey(id) && _classDefs.TryGetValue("default", out var def))
				{
					foreach (var (k, v) in def)
						merged[k] = v;
				}

				foreach (var cls in classes.GetValueOrDefault(id) ?? [])
				{
					if (_classDefs.TryGetValue(cls, out var c))
					{
						foreach (var (k, v) in c)
							merged[k] = v;
					}
				}

				foreach (var (target, styles, _) in _styles)
				{
					if (target != id)
						continue;
					foreach (var (k, v) in styles)
						merged[k] = v;
				}

				if (merged.Count > 0)
					result[id] = merged;
			}

			return result;
		}

		// ── cursor ────────────────────────────────────────────────────────

		private int Line => LineOf(_pos);

		private int LineOf(int pos)
		{
			var line = 1;
			for (var i = 0; i < pos && i < _text.Length; i++)
			{
				if (_text[i] == '\n')
					line++;
			}

			return line;
		}

		private int LineEnd(int from)
		{
			var i = _text.IndexOf('\n', from);
			return i < 0 ? _text.Length : i;
		}

		private bool StartsWith(string s) => _text.AsSpan(_pos).StartsWith(s);

		private bool Peek(char c) => _pos < _text.Length && _text[_pos] == c;

		private bool AtLineEnd() => _pos >= _text.Length || _text[_pos] == '\n';

		private void SkipHws()
		{
			while (_pos < _text.Length && _text[_pos] is ' ' or '\t')
				_pos++;
		}

		private void RequireHws()
		{
			if (_pos >= _text.Length || _text[_pos] is not (' ' or '\t'))
				throw Error("expected a space");
			SkipHws();
		}

		private void SkipBlankLinesAndComments()
		{
			while (_pos < _text.Length)
			{
				var end = LineEnd(_pos);
				var line = _text.AsSpan(_pos, end - _pos).Trim();
				if (line.Length > 0 && !line.StartsWith("%%"))
					return;
				_pos = Math.Min(_text.Length, end + 1);
			}
		}

		private void Expect(char c)
		{
			if (!Peek(c))
				throw Error($"expected '{c}'");
			_pos++;
		}

		private void ExpectLineEnd()
		{
			SkipHws();
			if (_pos >= _text.Length)
				return;
			if (_text[_pos] != '\n')
				throw Error("unexpected text after a complete statement");
			_pos++;
		}

		private MermaidParseException Error(string message, int? line = null)
		{
			var at = line ?? Line;
			var end = LineEnd(_pos);
			var near = _pos < _text.Length && line is null ? _text[_pos..Math.Min(end, _pos + 30)] : "";
			return new MermaidParseException($"Use case diagram, line {at}: {message}" + (near.Length > 0 ? $" (near '{near}')" : ""));
		}
	}
}
