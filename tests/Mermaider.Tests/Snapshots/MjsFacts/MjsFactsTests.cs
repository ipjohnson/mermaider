using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Mermaider.Tests.Snapshots.MjsFacts;

/// <summary>
/// Diffs Mermaider's facts (<see cref="MermaiderFacts"/>) with the committed mermaid.js facts for every
/// <see cref="MjsCorpus"/> case. A difference must be listed in <c>mjs-facts-baseline.txt</c> under a reason; a listed
/// difference that no longer occurs fails too, until its line is removed. Refresh the mermaid.js facts with
/// <c>mise run mjs-facts</c>; nothing here needs Node.
/// </summary>
public class MjsFactsTests
{
	private const string Accepted = "accept\tyes";

	public static IEnumerable<string> Cases() => MjsCorpus.All.Select(c => c.Name);

	[Test]
	[MethodDataSource(nameof(Cases))]
	public void Facts_match_mermaid_js(string name)
	{
		var mjs = MjsFactsFile.Read(name);
		var pinned = MjsFactsFile.PinnedMermaidVersion();
		mjs
			.Version
			.Should()
			.Be(pinned, $"{name}.mjs.facts.txt was made by mermaid {mjs.Version} but package-lock.json pins mermaid {pinned}; run `mise run mjs-facts`");
		var source = MjsCorpus.Get(name).Source;
		mjs
			.SourceHash
			.Should()
			.Be(MjsCorpus.SourceHash(source), $"the source of '{name}' changed since its mermaid.js facts were made; run `mise run mjs-facts`");

		var differences = Differences(mjs.Facts, MermaiderFacts.For(source));
		var baselined = MjsFactsBaseline.Load().Entries.Where(e => e.Case == name).Select(e => e.Difference).ToList();
		Unexplained(name, differences, baselined).Should().BeEmpty();
	}

	/// <summary>The differences the baseline does not list, and the baseline lines whose difference no longer occurs.</summary>
	internal static string Unexplained(string name, IReadOnlyList<string> differences, IReadOnlyList<string> baselined)
	{
		var unexpected = Subtract(differences, baselined);
		var vanished = Subtract(baselined, differences);
		var report = new StringBuilder();
		if (unexpected.Count > 0)
			_ = report
				.AppendLine("New differences from mermaid.js (fix them, or add them to mjs-facts-baseline.txt under a reason):")
				.AppendJoin('\n', unexpected.Select(d => $"{name} {d}"))
				.AppendLine();
		if (vanished.Count > 0)
			_ = report
				.AppendLine("Baselined differences that no longer occur (remove these lines from mjs-facts-baseline.txt):")
				.AppendJoin('\n', vanished.Select(d => $"{name} {d}"))
				.AppendLine();
		return report.ToString();
	}

	[Test]
	public void Every_case_has_facts_and_every_facts_file_a_case()
	{
		var cases = Cases().ToHashSet();
		var files = Directory
			.EnumerateFiles(MjsCorpus.FactsDir, "*" + MjsFactsFile.Suffix)
			.Select(f => Path.GetFileName(f)[..^MjsFactsFile.Suffix.Length])
			.ToHashSet();
		cases.Except(files).Should().BeEmpty("every corpus case needs mermaid.js facts; run `mise run mjs-facts`");
		files.Except(cases).Should().BeEmpty("a facts file without a corpus case is stale; run `mise run mjs-facts`");
	}

	[Test]
	public void Every_baseline_line_names_a_case_and_has_a_reason()
	{
		var baseline = MjsFactsBaseline.Load();
		baseline.Problems.Should().BeEmpty();
		var cases = Cases().ToHashSet();
		baseline.Entries.Where(e => !cases.Contains(e.Case)).Select(e => e.Line).Should().BeEmpty("a baseline line must name a corpus case");
	}

	[Test]
	public void A_new_difference_and_a_vanished_one_both_fail()
	{
		string[] mjs = ["accept\tyes", "node\tA\tStart", "edge\tA\tB\tnone\tarrow\tsolid"];
		string[] mer = ["accept\tyes", "node\tA\tstart", "edge\tA\tB\tnone\tarrow\tsolid"];
		var differences = Differences(mjs, mer);
		differences.Should().Equal("+ node | A | start", "- node | A | Start");

		Unexplained("x", differences, differences).Should().BeEmpty();
		var unlisted = Unexplained("x", differences, ["- node | A | Start"]);
		unlisted.Should().Contain("New differences").And.Contain("x + node | A | start").And.NotContain("no longer occur");
		var vanished = Unexplained("x", differences, [.. differences, "- edge | B | A"]);
		vanished.Should().Contain("no longer occur").And.Contain("x - edge | B | A").And.NotContain("New differences");
	}

	/// <summary>Writes every case's source to <c>MJS_CORPUS_OUT</c> as <c>&lt;case&gt;.mmd</c>, for scripts/mjs-facts.sh.</summary>
	[Test]
	public void Export_corpus()
	{
		var outDir = Environment.GetEnvironmentVariable("MJS_CORPUS_OUT");
		if (string.IsNullOrEmpty(outDir))
			return;
		_ = Directory.CreateDirectory(outDir);
		foreach (var stale in Directory.EnumerateFiles(outDir, "*.mmd"))
			File.Delete(stale);
		foreach (var c in MjsCorpus.All)
			File.WriteAllText(Path.Combine(outDir, c.Name + ".mmd"), c.Source, new UTF8Encoding(false));
	}

	/// <summary>
	/// Writes every current difference to <c>MJS_FACTS_REPORT/differences.txt</c> in baseline form, and a side-by-side page
	/// <c>review.html</c> of the cases that differ, using the mermaid.js SVGs scripts/mjs-facts.sh leaves in
	/// <c>tests/Mermaider.Tests/Snapshots/Reference/mermaidjs</c>.
	/// </summary>
	[Test]
	public void Write_difference_report()
	{
		var outDir = Environment.GetEnvironmentVariable("MJS_FACTS_REPORT");
		if (string.IsNullOrEmpty(outDir))
			return;
		_ = Directory.CreateDirectory(outDir);
		var text = new StringBuilder();
		var html = new StringBuilder(
			"<!doctype html><meta charset=utf-8><title>Mermaider vs mermaid.js</title><style>body{font:14px system-ui}section{margin:2em 0}.row{display:flex;gap:1em}.row>div{flex:1;border:1px solid #ccc;padding:.5em;overflow:auto}pre{white-space:pre-wrap}</style>\n"
		);
		var svgDir = Path.Combine(MjsCorpus.FactsDir, "..", "..", "Reference", "mermaidjs");
		foreach (var c in MjsCorpus.All)
		{
			var mjs = MjsFactsFile.Read(c.Name);
			var differences = Differences(mjs.Facts, MermaiderFacts.For(c.Source));
			if (differences.Count == 0)
				continue;
			foreach (var d in differences)
				_ = text.Append(c.Name).Append(' ').AppendLine(d);
			var mjsSvgPath = Path.Combine(svgDir, c.Name + ".svg");
			var mjsSvg = File.Exists(mjsSvgPath) ? File.ReadAllText(mjsSvgPath) : "<p>(no mermaid.js SVG)</p>";
			string merSvg;
			try
			{
				merSvg = MermaidRenderer.RenderSvg(c.Source);
			}
			catch (Exception e)
			{
				merSvg = "<p>" + System.Net.WebUtility.HtmlEncode(e.Message) + "</p>";
			}

			_ = html
				.Append("<section><h2>")
				.Append(c.Name)
				.Append("</h2><pre>")
				.Append(System.Net.WebUtility.HtmlEncode(string.Join('\n', differences)))
				.Append("</pre><div class=row><div><h3>mermaid.js</h3>")
				.Append(mjsSvg)
				.Append("</div><div><h3>Mermaider</h3>")
				.Append(merSvg)
				.Append("</div></div></section>\n");
		}

		File.WriteAllText(Path.Combine(outDir, "differences.txt"), text.ToString());
		File.WriteAllText(Path.Combine(outDir, "review.html"), html.ToString());
	}

	/// <summary>"- fact" is in mermaid.js only, "+ fact" in Mermaider only; a fact's fields are shown joined by " | ".</summary>
	internal static List<string> Differences(IReadOnlyList<string> mjs, IReadOnlyList<string> mermaider)
	{
		static string Show(string fact) => fact.Replace("\t", " | ", StringComparison.Ordinal);
		// When one engine refuses the source, what the other draws is not compared.
		if (mjs.Contains(Accepted) != mermaider.Contains(Accepted))
		{
			mjs = [.. mjs.Where(f => f.StartsWith("accept\t", StringComparison.Ordinal))];
			mermaider = [.. mermaider.Where(f => f.StartsWith("accept\t", StringComparison.Ordinal))];
		}

		var missing = Subtract(mjs, mermaider).Select(f => "- " + Show(f));
		var extra = Subtract(mermaider, mjs).Select(f => "+ " + Show(f));
		return [.. missing.Concat(extra).Order(StringComparer.Ordinal)];
	}

	/// <summary>Multiset difference: each item of <paramref name="b"/> cancels one equal item of <paramref name="a"/>.</summary>
	internal static List<string> Subtract(IReadOnlyList<string> a, IReadOnlyList<string> b)
	{
		var counts = b.GroupBy(x => x).ToDictionary(g => g.Key, g => g.Count());
		var result = new List<string>();
		foreach (var x in a)
		{
			if (counts.TryGetValue(x, out var n) && n > 0)
				counts[x] = n - 1;
			else
				result.Add(x);
		}

		return result;
	}
}

/// <summary>
/// A committed mermaid.js facts file. <see cref="Geometry"/> holds its <c>canvas</c> and <c>bbox</c> lines, which
/// <see cref="LayoutSkeleton.OverlapLint"/> reads; <see cref="Facts"/> holds the rest, which MjsFactsTests diffs.
/// </summary>
internal sealed record MjsFactsFile(string Version, string SourceHash, IReadOnlyList<string> Facts, IReadOnlyList<string> Geometry)
{
	public const string Suffix = ".mjs.facts.txt";

	public static MjsFactsFile Read(string name)
	{
		var path = Path.Combine(MjsCorpus.FactsDir, name + Suffix);
		if (!File.Exists(path))
			throw new FileNotFoundException($"No mermaid.js facts for '{name}'; run `mise run mjs-facts`", path);
		string? version = null, hash = null;
		var facts = new List<string>();
		var geometry = new List<string>();
		foreach (var line in File.ReadAllLines(path))
		{
			if (line.StartsWith("# mermaid ", StringComparison.Ordinal))
				version = line["# mermaid ".Length..].Trim();
			else if (line.StartsWith("# source ", StringComparison.Ordinal))
				hash = line["# source ".Length..].Trim();
			else if (line.StartsWith("bbox\t", StringComparison.Ordinal) || line.StartsWith("canvas\t", StringComparison.Ordinal))
				geometry.Add(line);
			else if (line.Length > 0 && !line.StartsWith('#'))
				facts.Add(line);
		}

		return new MjsFactsFile(version ?? "(none)", hash ?? "(none)", facts, geometry);
	}

	/// <summary>The mermaid version package-lock.json pins, which scripts/mjs-facts.mjs renders with.</summary>
	public static string PinnedMermaidVersion()
	{
		using var lockFile = JsonDocument.Parse(File.ReadAllText(Path.Combine(MjsCorpus.RepositoryRoot, "package-lock.json")));
		return lockFile.RootElement.GetProperty("packages").GetProperty("node_modules/mermaid").GetProperty("version").GetString()!;
	}
}

/// <summary>
/// mjs-facts-baseline.txt: a <c># reason</c> line, then the differences it explains as <c>&lt;case&gt; &lt;-|+&gt; &lt;fact&gt;</c>,
/// one per line, until a blank line. Every entry needs a reason above it.
/// </summary>
internal sealed record MjsFactsBaseline(IReadOnlyList<MjsFactsBaseline.Entry> Entries, IReadOnlyList<string> Problems)
{
	public sealed record Entry(string Case, string Difference, string Reason, string Line);

	public static MjsFactsBaseline Load()
	{
		var entries = new List<Entry>();
		var problems = new List<string>();
		if (!File.Exists(MjsCorpus.BaselinePath))
			return new MjsFactsBaseline(entries, problems);
		string? reason = null;
		var afterEntry = false;
		var lineNumber = 0;
		foreach (var line in File.ReadAllLines(MjsCorpus.BaselinePath))
		{
			lineNumber++;
			if (line.Trim().Length == 0)
			{
				reason = null;
				continue;
			}

			if (line.StartsWith('#'))
			{
				reason = reason is null || afterEntry ? line[1..].Trim() : reason + " " + line[1..].Trim();
				afterEntry = false;
				continue;
			}

			afterEntry = true;

			var space = line.IndexOf(' ', StringComparison.Ordinal);
			var difference = space < 0 ? string.Empty : line[(space + 1)..];
			if (space < 0 || !(difference.StartsWith("- ", StringComparison.Ordinal) || difference.StartsWith("+ ", StringComparison.Ordinal)))
				problems.Add($"line {lineNumber}: expected '<case> <-|+> <fact>': {line}");
			else if (string.IsNullOrEmpty(reason))
				problems.Add($"line {lineNumber}: no '# reason' line above it: {line}");
			else
				entries.Add(new Entry(line[..space], difference, reason, line));
		}

		return new MjsFactsBaseline(entries, problems);
	}
}
