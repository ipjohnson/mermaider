using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class UsecaseSpecTests
{
	[Test]
	public void Actors_and_a_boundary() =>
		MermaidRenderer
			.RenderSvg(
				"""
			usecase-beta
			direction LR
			actor Customer
			actor Support
			systemBoundary Storefront
			  Browse("Browse catalogue")
			  Checkout("Checkout")
			end
			Customer --> Browse
			Customer --> Checkout
			Support --> Checkout
			Checkout ..> : include Browse
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Actor_variants_and_business_elements() =>
		MermaidRenderer
			.RenderSvg(
				"""
			usecase-beta
			actor Normal("Normal actor")
			actor Hollow("Hollow actor")@{ type: hollow, business: true } <<Employee>>
			actor Awesome("Awesome actor")@{ type: awesome }
			Quote("Prepare quote")@{ business: true } <<Core>>
			Normal --> Quote
			Hollow --> Quote
			Awesome --> Quote
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Association_markers() =>
		MermaidRenderer
			.RenderSvg(
				"""
			usecase-beta
			actor User
			actor Support
			User --> Start
			Start <-- Support
			Start -- Finish
			User --o Start
			User --x Finish
			Finish x-- Support
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Package_boundary_note_and_json_table() =>
		MermaidRenderer
			.RenderSvg(
				"""
			usecase-beta
			direction TB
			systemBoundary sb1["Payment service"]@{ type: package }
			  actor Clerk("Payment clerk")
			  Authorize("Authorize payment")
			end
			json Payment@{ "amount": 19.95, "tags": ["card", "eu"] }
			note for Authorize "Requires an active session"
			Clerk --> Authorize
			Authorize --> Payment
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
