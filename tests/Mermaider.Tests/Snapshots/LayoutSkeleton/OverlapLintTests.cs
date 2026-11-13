using System.Runtime.CompilerServices;
using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Tests.Snapshots.MjsFacts;

namespace Mermaider.Tests.Snapshots.LayoutSkeleton;

/// <summary>
/// Ratcheted overlap lint (see <see cref="OverlapLint"/>) for every class and sequence case of <see cref="MjsCorpus"/>.
/// A finding counts only when mermaid.js, measured by scripts/mjs-facts.mjs, does not have it too. The counts per case
/// and check must equal <c>overlap-lint-baseline.txt</c>: a new finding fails, and so does a fixed one until its line is
/// lowered or removed. Run with <c>OVERLAP_LINT_OUT=&lt;dir&gt;</c> to write the current counts and a report of each finding.
/// </summary>
public class OverlapLintTests
{
	public static IEnumerable<string> Cases() => MjsCorpus.All.Where(c => IsLinted(c.Source)).Select(c => c.Name);

	private static bool IsLinted(string source)
	{
		var (cleaned, _) = DiagramPreprocessor.Process(source);
		return DiagramDetector.Detect(cleaned.AsSpan()) is DiagramType.Class or DiagramType.Sequence;
	}

	/// <summary>Mermaider's findings that mermaid.js does not share. A source either engine refuses has none.</summary>
	internal static List<OverlapLint.Finding> Findings(string name)
	{
		var mjs = MjsFactsFile.Read(name);
		if (!mjs.Facts.Contains("accept\tyes"))
			return [];
		string svg;
		try
		{
			svg = MermaidRenderer.RenderSvg(MjsCorpus.Get(name).Source);
		}
		catch (Exception e) when (e is MermaidParseException or OperationCanceledException)
		{
			return [];
		}

		var mermaider = OverlapLint.Run(OverlapLint.FromMermaiderSvg(svg));
		return OverlapLint.MermaiderOnly(mermaider, OverlapLint.Run(OverlapLint.FromMjs(mjs.Geometry)));
	}

	[Test]
	[MethodDataSource(nameof(Cases))]
	public void Overlaps_match_the_baseline(string name)
	{
		var findings = Findings(name);
		var baseline = OverlapBaseline.Load();
		var problems = new List<string>();
		foreach (var check in OverlapLint.AllChecks)
		{
			var found = findings.Where(f => f.Check == check).ToList();
			var allowed = baseline.Counts.GetValueOrDefault((name, check), 0);
			if (found.Count > allowed)
				problems.Add($"{name} {check}: {found.Count} found, {allowed} in overlap-lint-baseline.txt: {string.Join("; ", found.Select(f => f.Key))}");
			else if (found.Count < allowed)
				problems.Add($"{name} {check}: {found.Count} found, {allowed} in overlap-lint-baseline.txt; lower the count or remove the line");
		}

		problems.Should().BeEmpty();
	}

	[Test]
	public void Every_baseline_line_names_a_case_and_has_a_reason()
	{
		var baseline = OverlapBaseline.Load();
		baseline.Problems.Should().BeEmpty();
		var cases = Cases().ToHashSet();
		baseline.Counts.Keys.Where(k => !cases.Contains(k.Case)).Should().BeEmpty("a baseline line must name a class or sequence corpus case");
		baseline.Counts.Keys.Where(k => !OverlapLint.AllChecks.Contains(k.Check)).Should().BeEmpty("a baseline line must name a check");
	}

	[Test]
	public void Each_check_finds_its_overlap_and_mermaid_js_findings_are_not_counted()
	{
		OverlapLint.Box[] boxes =
		[
			new(OverlapLint.Node, "A", 0, 0, 100, 40),
			new(OverlapLint.Label, "holds", 90, 30, 140, 50),
			new(OverlapLint.Label, "many", 130, 45, 170, 65),
			new(OverlapLint.Note, "n", 160, 60, 200, 90),
			new(OverlapLint.Group, "Execution", 0, 100, 300, 300),
			new(OverlapLint.Group, "Containers", 0, 150, 300, 250),
			new(OverlapLint.Group, "Inner", 10, 160, 50, 200, "Containers"),
			new(OverlapLint.Label, "edge", 290, 0, 320, 20),
		];
		var found = OverlapLint.Run(new OverlapLint.Geometry(new OverlapLint.Box("canvas", "", 0, 0, 300, 300), boxes));
		found.Should().BeEquivalentTo([
			new OverlapLint.Finding(OverlapLint.Canvas, "label 'edge'"),
			new OverlapLint.Finding(OverlapLint.LabelOverNode, "label 'holds' / node 'A'"),
			new OverlapLint.Finding(OverlapLint.LabelOverLabel, "label 'holds' / label 'many'"),
			new OverlapLint.Finding(OverlapLint.NoteOverlap, "label 'many' / note 'n'"),
			new OverlapLint.Finding(OverlapLint.GroupOverlap, "group 'Containers' / group 'Execution'"),
		]);

		var mjs = new[] { new OverlapLint.Finding(OverlapLint.LabelOverNode, "label 'holds' / node 'A'") };
		OverlapLint.MermaiderOnly(found, mjs).Should().HaveCount(4).And.NotContain(mjs);
	}

	[Test]
	public void The_namespace_repro_is_found()
	{
		var findings = Findings("cs-namespaces");
		findings.Should().Contain(new OverlapLint.Finding(OverlapLint.GroupOverlap, "group 'Containers' / group 'Execution'"));
	}

	[Test]
	public void Write_baseline_and_report()
	{
		var outDir = Environment.GetEnvironmentVariable("OVERLAP_LINT_OUT");
		if (string.IsNullOrEmpty(outDir))
			return;

		_ = Directory.CreateDirectory(outDir);
		var counts = new List<string>();
		var report = new List<string> { "# Overlap lint report", string.Empty };
		foreach (var name in Cases())
		{
			var findings = Findings(name);
			foreach (var check in OverlapLint.AllChecks)
			{
				var n = findings.Count(f => f.Check == check);
				if (n > 0)
					counts.Add($"{name} {check} {n}");
			}

			if (findings.Count > 0)
			{
				report.Add($"## {name} ({findings.Count})");
				report.AddRange(findings.Select(f => $"- **{f.Check}**: {f.Key}"));
				report.Add(string.Empty);
			}
		}

		File.WriteAllLines(Path.Combine(outDir, "overlap-lint-baseline.txt"), counts);
		File.WriteAllLines(Path.Combine(outDir, "overlap-lint-report.md"), report);
	}
}

/// <summary>
/// overlap-lint-baseline.txt: a <c># reason</c> line, then <c>&lt;case&gt; &lt;check&gt; &lt;count&gt;</c> lines it explains,
/// until a blank line.
/// </summary>
internal sealed record OverlapBaseline(IReadOnlyDictionary<(string Case, string Check), int> Counts, IReadOnlyList<string> Problems)
{
	private static string BaselinePath([CallerFilePath] string here = "") => Path.Combine(Path.GetDirectoryName(here)!, "overlap-lint-baseline.txt");

	public static OverlapBaseline Load()
	{
		var counts = new Dictionary<(string, string), int>();
		var problems = new List<string>();
		var path = BaselinePath();
		if (!File.Exists(path))
			return new OverlapBaseline(counts, problems);
		var hasReason = false;
		var lineNumber = 0;
		foreach (var line in File.ReadAllLines(path))
		{
			lineNumber++;
			if (line.Trim().Length == 0)
			{
				hasReason = false;
				continue;
			}

			if (line.StartsWith('#'))
			{
				hasReason = true;
				continue;
			}

			var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
			if (parts.Length != 3 || !int.TryParse(parts[2], out var n) || n <= 0)
				problems.Add($"line {lineNumber}: expected '<case> <check> <count>': {line}");
			else if (!hasReason)
				problems.Add($"line {lineNumber}: no '# reason' line above it: {line}");
			else if (!counts.TryAdd((parts[0], parts[1]), n))
				problems.Add($"line {lineNumber}: a second line for {parts[0]} {parts[1]}");
		}

		return new OverlapBaseline(counts, problems);
	}
}
