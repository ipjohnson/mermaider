namespace Mermaider.Examples;

public static partial class DiagramExamples
{
	private static DiagramExample[] CreateWardleyExamples() =>
		[
			new(
				"wardley-platform",
				"Platform Strategy",
				DiagramCategory.Wardley,
				"""
			wardley-beta
			title Software Platform Strategy
			evolution Genesis@0.25 -> Custom@0.5 -> Product@0.75 -> Commodity@1.0
			anchor Customer [0.92, 0.72]
			component Mobile App [0.80, 0.55] (build)
			component API Gateway [0.65, 0.68] (buy)
			component Auth Service [0.52, 0.80] (outsource)
			component Database [0.40, 0.45] (buy) (inertia)
			component Compute [0.18, 0.92] (market)
			Customer -> Mobile App
			Mobile App -> API Gateway
			API Gateway -> Auth Service
			API Gateway -> Database
			Database -> Compute
			evolve Database 0.78
			accelerator "Cloud native" [0.25, 0.55]
			note "Migrate to a managed database" [0.30, 0.30]
			"""
			),
			new(
				"wardley-pipeline",
				"Pipeline and Flows",
				DiagramCategory.Wardley,
				"""
			wardley-beta
			title Data Platform
			anchor Analyst [0.95, 0.60]
			component Dashboards [0.80, 0.55]
			component Storage [0.45, 0.60]
			pipeline Storage {
			  component "File Store" [0.30]
			  component "Warehouse" [0.60]
			  component "Lakehouse" [0.80]
			}
			Analyst -> Dashboards
			Dashboards +'queries'> Warehouse
			Dashboards -.-> Storage; reads
			annotations [0.20, 0.05]
			annotation 1,[0.84, 0.50] "Self-service reporting"
			annotation 2,[0.36, 0.60] "Consolidate storage"
			"""
			),
		];
}
