using System.Text;
using System.Text.RegularExpressions;
using Mermaider.Models;

namespace Mermaider.Parsing;

/// <summary>
/// Parses <c>zenuml</c> diagrams with the grammar of <c>@zenuml/core</c> (<c>src/g4/sequenceLexer.g4</c> and
/// <c>sequenceParser.g4</c>), the parser mermaid's <c>@mermaid-js/mermaid-zenuml</c> plugin draws with. Line breaks are
/// hidden, as in that grammar, so a statement may span lines; only a message's text after <c>:</c> and a title run to
/// the end of their line. Front matter, init directives and <c>%%</c> lines are blanked first, so line numbers are the
/// source's. Input the grammar refuses throws a <see cref="MermaidParseException"/> naming the line; valid input that is
/// not drawn is listed in <see cref="ZenUmlDiagram.Unused"/>.
/// </summary>
internal static partial class ZenUmlParser
{
	private const int TimeoutMs = 2000;

	internal const string DefaultStarter = "_STARTER_";

	[GeneratedRegex(@"^[ \t]*(?:%%|accTitle[ \t]*:|accDescr[ \t]*:)", RegexOptions.None, TimeoutMs)]
	private static partial Regex IgnoredLine();

	[GeneratedRegex(@"\*\*|__|\*|~~|`", RegexOptions.None, TimeoutMs)]
	private static partial Regex MarkdownMarker();

	[GeneratedRegex(@"\s+", RegexOptions.None, TimeoutMs)]
	private static partial Regex Whitespace();

	private static readonly HashSet<string> Keywords =
	[
		"true",
		"false",
		"nil",
		"null",
		"if",
		"else",
		"while",
		"for",
		"foreach",
		"forEach",
		"loop",
		"return",
		"new",
		"par",
		"group",
		"opt",
		"as",
		"try",
		"catch",
		"finally",
		"in",
	];

	private static readonly HashSet<string> Modifiers = ["const", "readonly", "static", "await"];

	private static readonly string[] Operators = ["<<", ">>", "->", "||", "&&", "==", "!=", ">=", "<="];

	internal static ZenUmlDiagram Parse(string source)
	{
		try
		{
			var (text, _) = DiagramPreprocessor.BlankConfig(source, "zenuml");
			var lines = text.Split('\n');
			for (var i = 0; i < lines.Length; i++)
			{
				if (IgnoredLine().IsMatch(lines[i]))
					lines[i] = "";
			}

			text = StripHeader(string.Join('\n', lines));
			var unused = new List<ZenUmlUnusedInput>();
			var tokens = Lex(text, unused);
			return new Reader(tokens, text, lines, unused).Read();
		}
		catch (RegexMatchTimeoutException ex)
		{
			throw new MermaidParseException($"Parsing timed out after {ex.MatchTimeout.TotalSeconds}s — input may contain pathological patterns.", ex);
		}
	}

	/// <summary>The markdown of a comment as text: markers removed, lines joined by a space.</summary>
	internal static string PlainComment(string comment) =>
		string.Join(' ', MarkdownMarker().Replace(comment, "").Split('\n').Select(l => l.Trim()).Where(l => l.Length > 0));

	private static MermaidParseException Error(int line, string message, string? near = null) =>
		new($"ZenUML, line {line}: {message}" + (near is { Length: > 0 } ? $" (near '{near}')" : ""));

	// mermaid's detector is /^\s*zenuml/ and its renderer removes that match, so the rest of the header line is source
	private static string StripHeader(string text)
	{
		var at = 0;
		while (at < text.Length && char.IsWhiteSpace(text[at]))
			at++;
		if (!text.AsSpan(at).StartsWith("zenuml", StringComparison.Ordinal))
		{
			var line = text.AsSpan(0, at).Count('\n') + 1;
			throw Error(line, "a ZenUML diagram starts with 'zenuml'");
		}

		return string.Concat(text.AsSpan(0, at), "      ", text.AsSpan(at + 6));
	}

	// ------------------------------------------------------------------
	// Lexer: sequenceLexer.g4. Spaces and line breaks are hidden, `//` comments ride on the next token, and the
	// modifiers const, readonly, static and await are dropped. The longest match wins, then the earlier rule.
	// ------------------------------------------------------------------

	private enum Kind
	{
		Title,
		Content,
		Str,
		Id,
		Keyword,
		Int,
		Float,
		Annotation,
		Starter,
		Return,
		Color,
		Divider,
		Op,
		Other,
		Eof
	}

	private sealed class Token(Kind kind, string text, int line, int start, int end)
	{
		public Kind Kind { get; } = kind;
		public string Text { get; } = text;
		public int Line { get; } = line;
		public int Start { get; } = start;
		public int End { get; } = end;
		public string? Comment { get; set; }
		public int CommentLine { get; set; }
		public bool CommentUsed { get; set; }

		public bool Is(string op) => (Kind is Kind.Op or Kind.Keyword) && Text == op;
	}

	private static List<Token> Lex(string text, List<ZenUmlUnusedInput> unused)
	{
		var tokens = new List<Token>();
		var line = 1;
		var i = 0;
		var lineStart = true;
		var comment = new List<string>();
		var commentLine = 0;

		void Add(Kind kind, string value, int start, int end)
		{
			var t = new Token(kind, value, line, start, end);
			if (comment.Count > 0)
			{
				t.Comment = string.Join('\n', comment);
				t.CommentLine = commentLine;
				comment.Clear();
			}
			tokens.Add(t);
		}

		string RestOfLine(int from, out int end)
		{
			end = from;
			while (end < text.Length && text[end] is not '\n' and not '\r')
				end++;
			return text[from..end];
		}

		while (i < text.Length)
		{
			var c = text[i];
			if (c == '\n')
			{
				line++;
				i++;
				lineStart = true;
				continue;
			}
			if (c is ' ' or '\t' or '\r')
			{
				i++;
				continue;
			}

			var start = i;
			if (lineStart && c == '=' && i + 1 < text.Length && text[i + 1] == '=')
			{
				var body = RestOfLine(i, out i);
				Add(Kind.Divider, body.Trim().Trim('=').Trim(), start, i);
				lineStart = false;
				continue;
			}
			lineStart = false;

			if (c == '/' && i + 1 < text.Length && text[i + 1] == '/')
			{
				var body = RestOfLine(i + 2, out i);
				if (comment.Count == 0)
					commentLine = line;
				comment.Add(body.Trim());
				continue;
			}

			if (c == ':')
			{
				Add(Kind.Op, ":", start, i + 1);
				var content = RestOfLine(i + 1, out i);
				if (content.Trim().Length > 0)
					Add(Kind.Content, content.Trim(), start + 1, i);
				continue;
			}

			if (c == '"')
			{
				var sb = new StringBuilder();
				i++;
				while (i < text.Length && text[i] is not '\n' and not '\r')
				{
					if (text[i] == '"')
					{
						if (i + 1 < text.Length && text[i + 1] == '"')
						{
							_ = sb.Append('"');
							i += 2;
							continue;
						}
						i++;
						break;
					}
					_ = sb.Append(text[i]);
					i++;
				}
				Add(Kind.Str, sb.ToString(), start, i);
				continue;
			}

			if (c == '@')
			{
				i++;
				while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
					i++;
				var word = text[start..i];
				var kind = word switch
				{
					"@Starter" or "@starter" => Kind.Starter,
					"@Return" or "@return" or "@Reply" or "@reply" => Kind.Return,
					_ => Kind.Annotation,
				};
				Add(kind, word[1..], start, i);
				continue;
			}

			if (c == '#' && i + 1 < text.Length && char.IsAsciiHexDigit(text[i + 1]))
			{
				i++;
				while (i < text.Length && char.IsAsciiHexDigit(text[i]))
					i++;
				Add(Kind.Color, text[start..i], start, i);
				continue;
			}

			if (char.IsAsciiDigit(c) || (c == '.' && i + 1 < text.Length && char.IsAsciiDigit(text[i + 1])))
			{
				while (i < text.Length && char.IsAsciiDigit(text[i]))
					i++;
				var isFloat = i < text.Length && text[i] == '.';
				if (isFloat)
				{
					i++;
					while (i < text.Length && char.IsAsciiDigit(text[i]))
						i++;
				}
				Add(isFloat ? Kind.Float : Kind.Int, text[start..i], start, i);
				continue;
			}

			if (char.IsAsciiLetter(c) || c == '_')
			{
				while (i < text.Length && (char.IsAsciiLetterOrDigit(text[i]) || text[i] == '_'))
					i++;
				var word = text[start..i];
				if (word == "title" && tokens.Count == 0)
				{
					var title = RestOfLine(i, out i);
					Add(Kind.Title, title.Trim(), start, i);
					continue;
				}
				if (Modifiers.Contains(word))
				{
					unused.Add(new ZenUmlUnusedInput(line, word, "the modifier is not drawn"));
					continue;
				}
				Add(Keywords.Contains(word) ? Kind.Keyword : Kind.Id, word, start, i);
				continue;
			}

			var op = Array.Find(Operators, o => text.AsSpan(i).StartsWith(o, StringComparison.Ordinal));
			if (op is not null)
			{
				i += op.Length;
				Add(Kind.Op, op, start, i);
				continue;
			}

			if (">=<+-*/%^!;,(){}.".Contains(c, StringComparison.Ordinal))
			{
				i++;
				Add(Kind.Op, c.ToString(), start, i);
				continue;
			}

			i++;
			Add(Kind.Other, c.ToString(), start, i);
		}

		Add(Kind.Eof, "", text.Length, text.Length);
		return tokens;
	}

	// ------------------------------------------------------------------
	// Parser: sequenceParser.g4. prog is `title? head? block`, where head declares participants, groups and the starter.
	// ------------------------------------------------------------------

	private sealed class Reader(List<Token> tokens, string text, string[] lines, List<ZenUmlUnusedInput> unused)
	{
		private readonly List<string> _order = [];
		private readonly Dictionary<string, ZenUmlParticipant> _participants = [];
		private readonly List<ZenUmlGroup> _groups = [];
		private int _pos;
		private string _starter = DefaultStarter;
		private bool _starterUsed;

		private Token Peek(int ahead = 0) => tokens[Math.Min(_pos + ahead, tokens.Count - 1)];

		private Token Next() => tokens[Math.Min(_pos++, tokens.Count - 1)];

		private bool Accept(string op)
		{
			if (!Peek().Is(op))
				return false;
			_pos++;
			return true;
		}

		private MermaidParseException Unexpected(string expected)
		{
			var t = Peek();
			if (t.Kind == Kind.Eof)
				return Error(t.Line, $"expected {expected} before the end of the diagram");
			var near = t.Line - 1 < lines.Length ? lines[t.Line - 1].Trim() : t.Text;
			return Error(t.Line, $"expected {expected}, not '{t.Text}'", near.Length > 40 ? near[..40] + "…" : near);
		}

		private void Unused(Token from, string reason)
		{
			var end = Peek(-1);
			var text = Source(from, end.Start >= from.Start ? end : from);
			unused.Add(new ZenUmlUnusedInput(from.Line, text, reason));
		}

		private string Source(Token from, Token to) => Whitespace().Replace(text[from.Start..Math.Max(from.End, to.End)], " ").Trim();

		private static bool IsName(Token t) => t.Kind is Kind.Id or Kind.Str;

		internal ZenUmlDiagram Read()
		{
			string? title = null;
			if (Peek().Kind == Kind.Title)
				title = Next().Text;

			ReadHead();
			var statements = ReadBlock(_starter, _starter, topLevel: true);
			if (Peek().Kind != Kind.Eof)
				throw Unexpected("a statement");

			foreach (var t in tokens)
			{
				if (t.Comment is not null && !t.CommentUsed)
					unused.Add(
						new ZenUmlUnusedInput(
							t.CommentLine,
							"// " + t.Comment.Replace("\n", " // ", StringComparison.Ordinal),
							"ZenUML draws a comment only above a message or a fragment"
						)
					);
			}

			var participants = _order.Select(id => _participants[id]).ToList();
			if (_starterUsed && !_participants.ContainsKey(_starter))
				participants.Insert(
					0,
					new ZenUmlParticipant(_starter, _starter == DefaultStarter ? "" : _starter, null, null, IsStarter: _starter == DefaultStarter)
				);

			ReportEmpty(statements);
			return new ZenUmlDiagram
			{
				Title = title,
				Participants = participants,
				Starter = _starter,
				Groups = _groups,
				Statements = statements,
				Unused = [.. unused.OrderBy(u => u.Line)],
			};
		}

		// ---- head --------------------------------------------------------

		private void ReadHead()
		{
			while (true)
			{
				var t = Peek();
				if (t.Is("group"))
				{
					ReadGroup();
				}
				else if (t.Kind == Kind.Starter)
				{
					_ = Next();
					if (Accept("("))
					{
						if (IsName(Peek()))
							_starter = Next().Text;
						if (!Accept(")"))
							throw Unexpected("')'");
					}
				}
				else if (t.Kind == Kind.Annotation || t.Is("<<") || (IsName(t) && IsDeclaration()))
				{
					_ = ReadParticipant();
				}
				else
				{
					return;
				}
			}
		}

		// a name opens a statement when what follows it can only continue one
		private bool IsDeclaration()
		{
			var next = Peek(1);
			if (next.Kind == Kind.Op && next.Text is "->" or "." or ":" or "=" or "(" or "{" or ";" or "-" or ",")
				return false;
			return !(IsName(next) && Peek(2).Is("="));
		}

		private void ReadGroup()
		{
			var start = Next();
			var name = IsName(Peek()) ? Next().Text : "";
			if (!Accept("{"))
			{
				Unused(start, "a group without braces holds no participants");
				return;
			}

			var members = new List<string>();
			while (!Peek().Is("}"))
			{
				var t = Peek();
				if (t.Kind == Kind.Eof)
					throw Unexpected("'}'");
				if (t.Kind != Kind.Annotation && !t.Is("<<") && !IsName(t))
					throw Unexpected("a participant or '}'");
				if (ReadParticipant() is { } id)
					members.Add(id);
			}
			_ = Next();
			_groups.Add(new ZenUmlGroup(name, members));
		}

		private string? ReadParticipant()
		{
			var start = Peek();
			string? annotation = null;
			string? stereotype = null;
			if (Peek().Kind == Kind.Annotation)
				annotation = Next().Text;
			if (Peek().Is("<<") || Peek().Is("<"))
			{
				_ = Next();
				if (IsName(Peek()))
					stereotype = Next().Text;
				_ = Accept(">>") || Accept(">");
			}

			if (!IsName(Peek()))
			{
				Unused(start, "an annotator or stereotype without a name declares no participant");
				return null;
			}

			var id = Next().Text;
			if (Peek().Kind == Kind.Int)
				Unused(Next(), "a participant width is not drawn");
			var label = id;
			if (Accept("as") && IsName(Peek()))
				label = Next().Text;
			if (Peek().Kind == Kind.Color)
				Unused(Next(), "a participant colour is not drawn");

			if (stereotype is not null)
				unused.Add(new ZenUmlUnusedInput(start.Line, "<<" + stereotype + ">>", "a stereotype is not drawn"));
			if (annotation is not null && !annotation.Equals("Actor", StringComparison.OrdinalIgnoreCase))
				unused.Add(new ZenUmlUnusedInput(start.Line, "@" + annotation, "the icon is not drawn; the participant is drawn as a box"));

			if (!_participants.ContainsKey(id))
				_order.Add(id);
			_participants[id] = new ZenUmlParticipant(id, label, annotation, stereotype);
			return id;
		}

		private void Use(string id)
		{
			if (id == _starter)
			{
				_starterUsed = true;
				return;
			}
			if (_participants.ContainsKey(id))
				return;
			_order.Add(id);
			_participants[id] = new ZenUmlParticipant(id, id, null, null);
		}

		// ---- statements ----------------------------------------------------

		private List<ZenUmlStatement> ReadBlock(string context, string caller, bool topLevel = false)
		{
			var list = new List<ZenUmlStatement>();
			while (Peek().Kind != Kind.Eof && !Peek().Is("}"))
			{
				if (ReadStatement(context, caller) is { } s)
					list.Add(s);
			}
			if (!topLevel && !Accept("}"))
				throw Unexpected("'}'");
			return list;
		}

		private List<ZenUmlStatement> ReadBraceBlock(string context, string caller) => Accept("{") ? ReadBlock(context, caller) : throw Unexpected("'{'");

		private string? TakeComment(Token t)
		{
			if (t.Comment is null)
				return null;
			t.CommentUsed = true;
			return t.Comment;
		}

		private ZenUmlStatement? ReadStatement(string context, string caller)
		{
			var t = Peek();
			switch (t.Kind)
			{
				case Kind.Divider:
					_ = Next();
					return new ZenUmlDivider(t.Line, null, t.Text);
				case Kind.Return:
					{
						_ = Next();
						var comment = TakeComment(t);
						if (ReadAsync(context, Peek()) is not { } message)
						{
							Unused(t, "@return without a message is not drawn");
							return null;
						}
						return message with { Line = t.Line, Comment = comment, Kind = ZenUmlMessageKind.Return };
					}
				case Kind.Other:
					_ = Next();
					unused.Add(new ZenUmlUnusedInput(t.Line, t.Text, "an unknown character is not drawn"));
					return null;
				case Kind.Op when t.Is(";"):
					_ = Next();
					return null;
				case Kind.Keyword:
					return t.Text switch
					{
						"if" => ReadAlt(context, caller),
						"while" or "for" or "foreach" or "forEach" or "loop" => ReadLoop(context, caller),
						"par" or "opt" => ReadParOpt(context, caller),
						"try" => ReadTry(context, caller),
						"return" => ReadReturn(context, caller),
						"new" => ReadCreation(context, null, t),
						_ => throw Unexpected("a statement"),
					};
				case Kind.Id or Kind.Str:
					return ReadMessage(context, t);
				default:
					throw Unexpected("a statement");
			}
		}

		private ZenUmlFragment ReadAlt(string context, string caller)
		{
			var start = Next();
			var comment = TakeComment(start);
			var condition = ReadParExpr();
			var sections = new List<ZenUmlSection> { new("", ReadBraceBlock(context, caller)) };
			while (Peek().Is("else"))
			{
				_ = Next();
				if (Accept("if"))
				{
					var c = ReadParExpr();
					sections.Add(new ZenUmlSection(c, ReadBraceBlock(context, caller)));
				}
				else
				{
					sections.Add(new ZenUmlSection("else", ReadBraceBlock(context, caller)));
					break;
				}
			}
			return new ZenUmlFragment(start.Line, comment, ZenUmlFragmentKind.Alt, condition, sections);
		}

		private ZenUmlFragment ReadLoop(string context, string caller)
		{
			var start = Next();
			var comment = TakeComment(start);
			var condition = Peek().Is("(") ? ReadParExpr() : "";
			var body = Peek().Is("{") ? ReadBraceBlock(context, caller) : [];
			return new ZenUmlFragment(start.Line, comment, ZenUmlFragmentKind.Loop, condition, [new ZenUmlSection("", body)]);
		}

		private ZenUmlFragment ReadParOpt(string context, string caller)
		{
			var start = Next();
			var comment = TakeComment(start);
			var body = Peek().Is("{") ? ReadBraceBlock(context, caller) : [];
			var kind = start.Text == "par" ? ZenUmlFragmentKind.Par : ZenUmlFragmentKind.Opt;
			return new ZenUmlFragment(start.Line, comment, kind, "", [new ZenUmlSection("", body)]);
		}

		private ZenUmlFragment ReadTry(string context, string caller)
		{
			var start = Next();
			var comment = TakeComment(start);
			var sections = new List<ZenUmlSection> { new("", ReadBraceBlock(context, caller)) };
			while (Peek().Is("catch"))
			{
				_ = Next();
				var label = Peek().Is("(") ? "catch(" + ReadParenthesised() + ")" : "catch";
				sections.Add(new ZenUmlSection(label, ReadBraceBlock(context, caller)));
			}
			if (Accept("finally"))
				sections.Add(new ZenUmlSection("finally", ReadBraceBlock(context, caller)));
			return new ZenUmlFragment(start.Line, comment, ZenUmlFragmentKind.Try, "", sections);
		}

		private ZenUmlMessage ReadReturn(string context, string caller)
		{
			var start = Next();
			var comment = TakeComment(start);
			// a returned string is labelled without its quotes
			var label = Peek().Kind == Kind.Str && !IsExpressionContinuation(Peek(1)) ? Next().Text : StartsExpression(Peek()) ? ReadExpression() : "";
			_ = Accept(";");
			Use(context);
			Use(caller);
			return new ZenUmlMessage(start.Line, comment, ZenUmlMessageKind.Return, context, caller, label, null, []);
		}

		// parExpr: `(condition)`, where an unclosed `(` or `()` is allowed too
		private string ReadParExpr() => Peek().Is("(") ? ReadParenthesised() : throw Unexpected("'('");

		private string ReadParenthesised()
		{
			var open = Next();
			var depth = 1;
			var first = Peek();
			while (true)
			{
				var t = Peek();
				if (t.Kind == Kind.Eof)
					throw Error(open.Line, "'(' is never closed");
				if (t.Is("("))
					depth++;
				else if (t.Is(")") && --depth == 0)
					break;
				_ = Next();
			}
			var close = Next();
			return ReferenceEquals(first, close) ? "" : Text(first, Peek(-2));
		}

		private string Text(Token from, Token to) => Source(from, to);

		// ---- messages ------------------------------------------------------

		private ZenUmlStatement? ReadMessage(string context, Token start)
		{
			var comment = TakeComment(start);
			string? assignee = null;
			if (IsAssignment())
			{
				var first = Peek();
				var names = new List<Token> { Next() };
				while (Peek().Is(",") || IsName(Peek()))
				{
					if (Accept(","))
						continue;
					names.Add(Next());
				}
				_ = Next();
				// `Type a = …` gives the variable a type, which is not drawn
				assignee = names.Count == 2 && !Text(first, names[^1]).Contains(',', StringComparison.Ordinal) ? names[1].Text : Text(first, names[^1]);
				if (Peek().Is("new"))
					return ReadCreation(context, assignee, start) is { } created ? created with { Comment = comment } : null;
				if (!IsName(Peek()))
				{
					Unused(start, "an assignment without a message is not drawn");
					return null;
				}
			}

			var name = Next();
			if (Peek().Is("->"))
			{
				_ = Next();
				if (!IsName(Peek()))
				{
					Unused(start, "a message without a target is not drawn");
					return null;
				}
				var to = Next();
				if (Peek().Is("."))
				{
					_ = Next();
					return ReadSync(name.Text, to.Text, assignee, start, comment);
				}
				return Async(start, comment, name.Text, to.Text);
			}

			if (Peek().Is(":"))
				return Async(start, comment, context, name.Text);

			if (Peek().Is("-"))
			{
				_ = Next();
				if (!IsName(Peek()))
				{
					Unused(start, "a message without a target is not drawn");
					return null;
				}
				return Async(start, comment, name.Text, Next().Text);
			}

			if (Peek().Is("."))
			{
				_ = Next();
				return ReadSync(context, name.Text, assignee, start, comment);
			}

			_pos--;
			return ReadSync(context, context, assignee, start, comment);
		}

		private bool IsAssignment()
		{
			var i = 0;
			if (!IsName(Peek(i)))
				return false;
			i++;
			if (IsName(Peek(i)) && Peek(i + 1).Is("="))
				return true;
			while (Peek(i).Is(",") && IsName(Peek(i + 1)))
				i += 2;
			return Peek(i).Is("=");
		}

		private ZenUmlMessage? ReadAsync(string context, Token start)
		{
			if (!IsName(Peek()))
				return null;
			var name = Next();
			if (Accept("->"))
				return IsName(Peek()) ? Async(start, null, name.Text, Next().Text) : null;
			if (Peek().Is(":"))
				return Async(start, null, context, name.Text);
			return null;
		}

		private ZenUmlMessage Async(Token start, string? comment, string from, string to)
		{
			var label = "";
			if (Accept(":") && Peek().Kind == Kind.Content)
				label = Next().Text;
			Use(from);
			Use(to);
			return new ZenUmlMessage(start.Line, comment, ZenUmlMessageKind.Async, from, to, label, null, []);
		}

		private ZenUmlMessage? ReadSync(string from, string to, string? assignee, Token start, string? comment)
		{
			if (!IsName(Peek()))
			{
				Unused(start, "a message without a method is not drawn");
				return null;
			}

			var label = new StringBuilder();
			while (true)
			{
				_ = label.Append(Next().Text);
				if (Peek().Is("("))
					_ = label.Append('(').Append(ReadParameters()).Append(')');
				if (!Peek().Is(".") || !IsName(Peek(1)))
					break;
				_ = label.Append(Next().Text);
			}

			Use(from);
			Use(to);
			var body = Peek().Is("{") ? ReadBraceBlock(to, from) : [];
			_ = Accept(";");
			return new ZenUmlMessage(start.Line, comment, ZenUmlMessageKind.Sync, from, to, label.ToString(), assignee, body);
		}

		private ZenUmlMessage? ReadCreation(string context, string? assignee, Token start)
		{
			var comment = TakeComment(start);
			_ = Next();
			if (!IsName(Peek()))
			{
				Unused(start, "'new' without a participant is not drawn");
				return null;
			}

			var construct = Next().Text;
			var parameters = Peek().Is("(") ? ReadParameters() : "";
			var id = assignee is null ? construct : assignee + ":" + construct;
			Use(context);
			Use(id);
			var label = parameters.Length > 0 ? "«" + parameters + "»" : "«create»";
			var body = Peek().Is("{") ? ReadBraceBlock(id, context) : [];
			_ = Accept(";");
			return new ZenUmlMessage(start.Line, comment, ZenUmlMessageKind.Create, context, id, label, assignee, body);
		}

		// parameters: each one's source text, joined by commas without spaces, as ZenUML labels a call
		private string ReadParameters()
		{
			var open = Next();
			var parts = new List<string>();
			var depth = 0;
			Token? first = null;
			while (true)
			{
				var t = Peek();
				if (t.Kind == Kind.Eof)
					throw Error(open.Line, "'(' is never closed");
				if (depth == 0 && (t.Is(")") || t.Is(",")))
				{
					if (first is not null)
						parts.Add(Text(first, Peek(-1)));
					first = null;
					_ = Next();
					if (t.Is(")"))
						break;
					continue;
				}
				if (t.Is("("))
					depth++;
				else if (t.Is(")"))
					depth--;
				first ??= t;
				_ = Next();
			}
			return string.Join(',', parts);
		}

		// ---- expressions (for `return`) ------------------------------------

		private static bool StartsExpression(Token t) =>
			t.Kind is Kind.Id or Kind.Str or Kind.Int or Kind.Float || t.Is("-") || t.Is("!") || t.Is("(") || t.Is("new") || t.Is("true") || t.Is(
				"false"
			) || t.Is("nil") || t.Is("null");

		private static bool IsExpressionContinuation(Token t) => t.Is("(") || t.Is(".") || (t.Kind == Kind.Op && BinaryOperators.Contains(t.Text));

		private static readonly HashSet<string> BinaryOperators = ["*", "/", "%", "+", "-", "<=", ">=", "<", ">", "==", "!=", "&&", "||"];

		private string ReadExpression()
		{
			var first = Peek();
			ReadOperand();
			while (Peek().Kind == Kind.Op && BinaryOperators.Contains(Peek().Text) && StartsExpression(Peek(1)))
			{
				_ = Next();
				ReadOperand();
			}
			return Text(first, Peek(-1));
		}

		private void ReadOperand()
		{
			while (Accept("-") || Accept("!"))
			{ }
			var t = Next();
			if (t.Is("("))
			{
				_pos--;
				_ = ReadParenthesised();
				return;
			}
			if (t.Is("new"))
			{
				if (IsName(Peek()))
					_ = Next();
				if (Peek().Is("("))
					_ = ReadParameters();
				return;
			}
			if (!IsName(t))
				return;
			while (true)
			{
				if (Peek().Is("("))
					_ = ReadParameters();
				else if (Peek().Is(".") && IsName(Peek(1)))
					_pos += 2;
				else
					return;
			}
		}

		// ---- what is not drawn ---------------------------------------------

		private void ReportEmpty(IReadOnlyList<ZenUmlStatement> statements)
		{
			foreach (var s in statements)
			{
				switch (s)
				{
					case ZenUmlMessage m:
						ReportEmpty(m.Body);
						break;
					case ZenUmlFragment f:
						if (!f.Sections.Any(sec => HasMessage(sec.Statements)))
						{
							unused.Add(new ZenUmlUnusedInput(f.Line, f.Kind.ToString().ToLowerInvariant(), "a fragment without a message is not drawn"));
							break;
						}
						for (var i = 1; i < f.Sections.Count; i++)
						{
							if (!HasMessage(f.Sections[i].Statements))
								unused.Add(new ZenUmlUnusedInput(f.Line, f.Sections[i].Label, "a section without a message is not drawn"));
						}
						foreach (var sec in f.Sections)
							ReportEmpty(sec.Statements);
						break;
				}
			}
		}
	}

	/// <summary>The number of statements, nested ones included, for the element limit.</summary>
	internal static int CountStatements(IReadOnlyList<ZenUmlStatement> statements) =>
		statements.Sum(
			s => 1 + s switch
			{
				ZenUmlMessage m => CountStatements(m.Body),
				ZenUmlFragment f => f.Sections.Sum(sec => CountStatements(sec.Statements)),
				_ => 0,
			}
		);

	/// <summary>Whether any of <paramref name="statements"/>, or a statement nested in one, draws a message.</summary>
	internal static bool HasMessage(IReadOnlyList<ZenUmlStatement> statements) =>
		statements.Any(
			s =>
				s switch
				{
					ZenUmlMessage => true,
					ZenUmlFragment f => f.Sections.Any(sec => HasMessage(sec.Statements)),
					_ => false,
				}
		);
}
