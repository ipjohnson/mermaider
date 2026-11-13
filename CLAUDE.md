# Claude Configuration

See AGENTS.md for project conventions and architecture.

## Key Design Decisions

- All regex patterns use `[GeneratedRegex]` with `matchTimeoutMilliseconds: 2000`
- SVG rendering uses `ObjectPool<StringBuilder>` — never allocate a new StringBuilder per render
- Text metrics operate on `ReadOnlySpan<char>` and use `SearchValues<char>` for SIMD character lookups
- Models use `FrozenDictionary` for immutable lookup tables after parsing
- Layout uses built-in Sugiyama engine (layered algorithm with rectilinear edge routing); optional MSAGL via `Mermaider.Layout.Msagl`
- Theming uses CSS custom properties with `color-mix()` fallbacks embedded in SVG
- Three style presets (`DiagramStyle.Quiet` default / `Blueprint` / `Tonal`, knobs in `Theming/StyleSpec.cs`) plus
  `Gradient`, `Tint` (0.5–1.5) and `Elevation` (0–2) inputs, normalised by `DesignInputs.From`. Presets change paint only,
  never layout
- Every colour comes from a colour family (`Theming/ColorFamily.cs`: clusters `p<i>`, series, neutral `n`, accent `a`,
  roles `s f w i`; stages Top/Bot/Flat/Band/Soft/Tint/Edge/Stroke/Ink); every text is a `TypeRole`. Renderers draw with
  the shared components in `Rendering/DesignSystem*.cs` (`DesignSystem.For(context)` … `ds.Close(sb)`) — no literal hex,
  ad-hoc `color-mix` ratios or font sizes. See `DESIGN.md`
- Architecture diagrams (`architecture-beta`) use a bespoke directional-grid layout (`Layout/ArchitectureLayout.cs`), not Sugiyama — edges carry explicit L/R/T/B sides, which don't map onto a layered graph
- Icons (architecture services/groups) resolve via `Mermaider.Icons.IconRegistry`: a small built-in set (Mermaid defaults + curated AWS/GCP/Azure + full Elastic, all original placeholder glyphs, not official trademarked logos) plus user registrations via `IconRegistry.Register`. Icons render as `<image href="data:image/svg+xml;base64,...">`; `SvgSanitizer` has one narrow, regex-anchored exception permitting `href` only on `<image>` with a base64 `data:image/svg+xml|png` value — icons are also sanitized once at registration time

## Testing

Run tests with: `dotnet test` (Mermaider.Tests and Sugiyama.Tests), or one project with `dotnet run --project tests/Mermaider.Tests/Mermaider.Tests.csproj`

Tests use TUnit (source-generated test runner), AwesomeAssertions and CsCheck.

290 tests cover: flowchart parser (17), sequence parser (12), class parser (13), ER parser (7),
SVG renderer (7), sequence renderer (12), class renderer (7), ER renderer (9), text metrics (1),
golden file snapshots (14 — all diagram types, themes, transparency),
spec coverage snapshots (flowchart 22, sequence 20, class 13, ER 11, state 12).
(Counts above predate several diagram types added since; run the suite for the current total —
538 as of the `architecture-beta` addition.) Architecture diagrams add: parser tests, renderer
tests, `IconRegistry` tests (built-ins, vendor packs, custom registration, malicious-icon
stripping), sanitizer tests for the scoped `<image>` href exception, and spec-coverage snapshots
(groups, nested groups, junction routing, arrow directions, vendor icon packs, the reported
k8s/ECH diagram) under `Snapshots/ArchitectureSpecTests.cs`.

`MjsFactsTests` (`tests/Mermaider.Tests/Snapshots/MjsFacts/`) diffs Mermaider's facts with committed mermaid.js facts
(`Snapshots/LayoutSkeleton/Reference/*.mjs.facts.txt`) for the gallery examples of all 24 diagram types, the flowchart,
state, class, sequence, ER and architecture spec-test sources, the repros in `MjsFacts/Cases/`, and the examples of
mermaid's syntax docs in `MjsFacts/Docs/` (`scripts/mermaid-docs-corpus.py <mermaid checkout>` writes them for the types
Mermaider draws; `Docs/SOURCE.txt` names the docs commit). Every case records
acceptance and whether anything is drawn. Each type adds its structure (labels, groups, edge ends, markers, node shapes in
mermaid's short names, participant types, icons, chart data) and which of its labels the drawing shows; each extractor
in `scripts/mjs-facts.mjs` says which facts come from mermaid's model and which from its DOM, and `MermaiderFacts`
mirrors it. CI needs no Node. Accepted differences live in `mjs-facts-baseline.txt` under a reason or an issue; a new
difference and a vanished one both fail.
`mise run mjs-facts` (or `scripts/mjs-facts.sh`) re-renders the facts with the mermaid version `package-lock.json` pins,
in UTC on a page clock fixed at 2000-01-01, so two runs write the same files.

`OverlapLintTests` (`Snapshots/LayoutSkeleton/OverlapLint.cs`) checks the class and sequence cases of that corpus for labels
on labels, labels on class or actor boxes, notes on anything, intersecting sibling groups and boxes outside the canvas.
Mermaider's boxes come from its SVG, mermaid.js's from the `bbox` lines the facts runner records. An overlap counts
only when mermaid.js does not have it too, and the counts per case must equal `overlap-lint-baseline.txt`.

## Benchmarks

Run benchmarks with: `dotnet run -c Release --project tests/Mermaider.Benchmarks/Mermaider.Benchmarks.csproj`

## CLI

Test with: `printf 'graph TD\n  A-->B' | dotnet run --project src/Mermaider.Cli/Mermaider.Cli.csproj`
(add `-- --style blueprint --no-gradient --tint 0.8 --elevation 2` to exercise the design inputs)
