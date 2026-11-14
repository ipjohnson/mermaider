using AwesomeAssertions;
using Nullean.Snapshots;

namespace Mermaider.Tests.Snapshots;

public class ZenUmlSpecTests
{
	[Test]
	public void Async_messages_with_declared_participants() =>
		MermaidRenderer
			.RenderSvg(
				"""
			zenuml
			title Greeting
			@Actor Alice
			J as John
			Alice->J: Hello John, how are you?
			J->Alice: Great!
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Sync_calls_creations_and_returns() =>
		MermaidRenderer
			.RenderSvg(
				"""
			zenuml
			Client->A.method() {
			  b = new B(id)
			  B.load() {
			    if(cached) {
			      return x1
			    }
			  }
			  return x2
			}
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);

	[Test]
	public void Fragments_comments_groups_and_a_divider() =>
		MermaidRenderer
			.RenderSvg(
				"""
			zenuml
			group Services { API BookingService }
			// start the booking
			Consumer->API: Book something
			try {
			  API->BookingService: Start booking process
			} catch {
			  API->Consumer: show failure
			} finally {
			  API->BookingService: rollback status
			}
			== retries ==
			while(attempts < 3) {
			  API->Consumer: still trying
			  par {
			    API->BookingService: ping
			    API->Consumer: wait
			  }
			}
			"""
			)
			.Should()
			.MatchSnapshot(SvgSnapshotFormat.Instance);
}
