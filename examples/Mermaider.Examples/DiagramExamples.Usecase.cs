namespace Mermaider.Examples;

public static partial class DiagramExamples
{
	private static DiagramExample[] CreateUsecaseExamples() =>
		[
			new(
				"usecase-ordering",
				"Online Ordering",
				DiagramCategory.Usecase,
				"""
			usecase-beta
			direction LR
			actor Customer
			actor Staff("Order staff")@{ type: hollow }
			systemBoundary ordering["Ordering system"]@{ type: package }
			  Browse("Browse products")
			  Checkout("Checkout") <<Core>>
			  Payment("Process payment")
			  Review[Review order]
			end
			note for Checkout "Validates the cart before payment"
			Customer --> Browse
			Customer -- "places order" ---> Checkout
			Checkout ..> : include Payment
			Staff --> Review
			"""
			),
			new(
				"usecase-relationships",
				"Include, Extend and Generalization",
				DiagramCategory.Usecase,
				"""
			usecase-beta
			direction TB
			actor Admin
			actor User
			systemBoundary Account
			  Login("Sign in")
			  Reset("Reset password")
			  Audit[Audit log]
			end
			Admin --|> User
			User --> Login
			Reset ..> : extend Login
			Login ..> : include Audit
			Admin --o Audit
			"""
			),
		];
}
