using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class WardleySpecTests
{
	[Test]
	public void Value_chain_with_evolution_and_a_note() =>
		MermaidRenderer
			.RenderSvg(
				"""
			wardley-beta
			title Tea Shop Value Chain
			anchor Business [0.95, 0.63]
			component Cup of Tea [0.79, 0.61]
			component Tea [0.63, 0.81]
			component Hot Water [0.52, 0.80]
			component Kettle [0.43, 0.35]
			component Power [0.10, 0.70]
			Business -> Cup of Tea
			Cup of Tea -> Tea
			Cup of Tea -> Hot Water
			Hot Water -> Kettle
			Kettle -> Power
			evolve Kettle 0.62
			evolve Power 0.89
			note "Standardising power allows Kettles to evolve faster" [0.30, 0.49]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Sourcing_inertia_and_link_flows() =>
		MermaidRenderer
			.RenderSvg(
				"""
			wardley-beta
			title Sourcing
			anchor Customer [0.80, 0.95]
			component Custom App [0.45, 0.85] (build)
			component Off-the-shelf Tool [0.85, 0.65] (buy)
			component Managed Service [0.60, 0.40] (outsource) (inertia)
			component Cloud Platform [0.95, 0.25] (market)
			Customer -> Custom App
			Custom App +> Off-the-shelf Tool
			Custom App +<> Managed Service
			Off-the-shelf Tool +'hosted on'> Cloud Platform
			Managed Service -.-> Cloud Platform; runs on
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Pipeline_and_custom_stage_widths() =>
		MermaidRenderer
			.RenderSvg(
				"""
			wardley-beta
			title Storage
			evolution Genesis@0.2 -> Custom@0.4 -> Product@0.75 -> Commodity@1.0
			component App [0.80, 0.70]
			component Database [0.40, 0.60]
			pipeline Database {
			  component "File System" [0.25]
			  component "SQL DB" [0.50]
			  component "Cloud DB" [0.85] label [-20, 20]
			}
			App -> Database
			App -> SQL DB
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Annotations_and_forces() =>
		MermaidRenderer
			.RenderSvg(
				"""
			wardley-beta
			title Forces
			evolution Genesis / Concept -> Custom / Emerging -> Product / Converging -> Commodity / Accepted
			component API [0.60, 0.70]
			component Cache [0.50, 0.55]
			component Database [0.40, 0.40]
			API -> Cache
			Cache -> Database
			annotations [0.95, 0.05]
			annotation 1,[0.60, 0.65] "Critical component"
			annotation 2,[0.50, 0.50] "Performance layer"
			accelerator "AI Adoption" [0.55, 0.25]
			deaccelerator "Legacy Constraints" [0.15, 0.75]
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
