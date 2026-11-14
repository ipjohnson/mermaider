namespace Mermaider.Examples;

public static partial class DiagramExamples
{
	private static DiagramExample[] CreateZenUmlExamples() =>
		[
			new(
				"zenuml-checkout",
				"Checkout",
				DiagramCategory.ZenUml,
				"""
			zenuml
			title Checkout
			@Actor Customer
			Shop as "Web Shop"
			Customer->Shop.checkout(cart) {
			  // reserve before charging
			  ok = Inventory.reserve(items)
			  if(ok) {
			    receipt = Payments.charge(card, total)
			    Shop->Mailer: send receipt
			  } else {
			    return "out of stock"
			  }
			}
			"""
			),
			new(
				"zenuml-booking",
				"Booking with Retries",
				DiagramCategory.ZenUml,
				"""
			zenuml
			title Booking
			group Backend { API BookingService }
			Client->API.book(slot) {
			  booking = new Booking(slot)
			  try {
			    API->Audit: start attempts
			    while(attempts < 3) {
			      BookingService.confirm(booking)
			    }
			  } catch(Timeout e) {
			    API->Client: retry later
			  } finally {
			    API->Audit: log attempt
			  }
			  par {
			    API->Client: confirmed
			    API->Calendar: block slot
			  }
			}
			"""
			),
		];
}
