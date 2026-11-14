using System.Runtime.CompilerServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Mermaider.Examples;

namespace Mermaider.Tests.Snapshots.MjsFacts;

/// <summary>
/// The sources compared with mermaid.js: the gallery examples of every diagram type, the sources of the flowchart, state,
/// class, sequence, ER and architecture spec tests, the committed repro files in <c>Cases/</c>, and the examples of
/// mermaid's syntax docs in <c>Docs/</c> (scripts/mermaid-docs-corpus.py writes them; <c>Docs/SOURCE.txt</c> names the
/// docs commit).
/// </summary>
internal static partial class MjsCorpus
{
	private const int TimeoutMs = 2000;

	public sealed record Case(string Name, string Source);

	private static string Here([CallerFilePath] string path = "") => Path.GetDirectoryName(path)!;

	public static string CasesDir => Path.Combine(Here(), "Cases");

	public static string DocsDir => Path.Combine(Here(), "Docs");

	/// <summary>The mermaid.js facts sit beside the layout skeleton references they share case names with.</summary>
	public static string FactsDir => Path.GetFullPath(Path.Combine(Here(), "..", "LayoutSkeleton", "Reference"));

	public static string BaselinePath => Path.Combine(Here(), "mjs-facts-baseline.txt");

	public static string RepositoryRoot
	{
		get
		{
			var dir = new DirectoryInfo(Here());
			while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "package-lock.json")))
				dir = dir.Parent;
			return dir?.FullName ?? throw new InvalidOperationException("package-lock.json not found above " + Here());
		}
	}

	private static readonly Lazy<IReadOnlyList<Case>> s_all = new(Load);

	public static IReadOnlyList<Case> All => s_all.Value;

	public static Case Get(string name) => All.Single(c => c.Name == name);

	/// <summary>The hash scripts/mjs-facts.mjs writes into each facts file: sha256 of the LF-normalised source.</summary>
	public static string SourceHash(string source) =>
		Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(source.Replace("\r\n", "\n", StringComparison.Ordinal))))[..16];

	private static readonly string[] s_specFiles =
	[
		"FlowchartSpecTests",
		"StateSpecTests",
		"ClassSpecTests",
		"SequenceSpecTests",
		"ErSpecTests",
		"ArchitectureSpecTests",
	];

	private static IReadOnlyList<Case> Load()
	{
		var cases = new List<Case>();
		cases.AddRange(DiagramExamples.All.Where(e => IsCompared(e.Source)).Select(e => new Case(e.Slug, e.Source)));

		foreach (var file in s_specFiles)
		{
			var prefix = "spec-" + Kebab(file[..^"SpecTests".Length]) + "-";
			var code = File.ReadAllText(Path.Combine(Here(), "..", file + ".cs"));
			foreach (var (method, source) in RawStringSources(code))
				if (IsCompared(source))
					cases.Add(new Case(prefix + Kebab(method), source));
		}

		foreach (var dir in new[] { CasesDir, DocsDir })
			cases.AddRange(
				Directory
					.EnumerateFiles(dir, "*.mmd")
					.Order(StringComparer.Ordinal)
					.Select(f => new Case(Path.GetFileNameWithoutExtension(f), File.ReadAllText(f)))
			);

		var duplicate = cases.GroupBy(c => c.Name).FirstOrDefault(g => g.Count() > 1);
		return duplicate is not null ? throw new InvalidOperationException($"Two corpus cases are named '{duplicate.Key}'") : cases;
	}

	/// <summary>Sources of a diagram type Mermaider draws; the header is the first line that is not front matter or a comment.</summary>
	public static bool IsCompared(string source)
	{
		var inFrontMatter = false;
		foreach (var raw in source.Split('\n'))
		{
			var line = raw.Trim();
			if (line == "---")
			{
				inFrontMatter = !inFrontMatter;
				continue;
			}

			if (inFrontMatter || line.Length == 0 || line.StartsWith("%%", StringComparison.Ordinal))
				continue;
			return HeaderPattern().IsMatch(line);
		}

		return false;
	}

	/// <summary>Each test method's first raw string literal, dedented by its closing quotes as C# does.</summary>
	private static IEnumerable<(string Method, string Source)> RawStringSources(string code)
	{
		foreach (Match m in SpecMethodPattern().Matches(code))
		{
			var indent = m.Groups["indent"].Value;
			var lines = m.Groups["body"].Value.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n');
			var dedented = lines.Select(l => l.StartsWith(indent, StringComparison.Ordinal) ? l[indent.Length..] : l.TrimStart());
			yield return (m.Groups["method"].Value, string.Join('\n', dedented));
		}
	}

	private static string Kebab(string name) =>
		KebabBoundaryPattern().Replace(name, "-$1").Replace('_', '-').ToLowerInvariant().Replace("--", "-", StringComparison.Ordinal);

	[GeneratedRegex(@"^(flowchart|graph|stateDiagram(-v2)?|classDiagram(-v2)?|sequenceDiagram|erDiagram|pie|quadrantChart|timeline|gitGraph|radar-beta|treemap-beta|venn-beta|mindmap|gantt|journey|C4(Context|Container|Component|Dynamic|Deployment)|sankey(-beta)?|xychart(-beta)?|requirementDiagram|packet(-beta)?|kanban|architecture-beta|block(-beta)?|treeView(-beta)?|usecase-beta)\b", RegexOptions.None, TimeoutMs)]
	private static partial Regex HeaderPattern();

	[GeneratedRegex(@"public\s+(?:async\s+Task|void|Task)\s+(?<method>\w+)\(\)(?:(?!public\s)[\s\S])*?""""""\r?\n(?<body>[\s\S]*?)\r?\n(?<indent>[ \t]*)""""""", RegexOptions.None, TimeoutMs)]
	private static partial Regex SpecMethodPattern();

	[GeneratedRegex(@"(?<=[a-z0-9])([A-Z])", RegexOptions.None, TimeoutMs)]
	private static partial Regex KebabBoundaryPattern();
}
