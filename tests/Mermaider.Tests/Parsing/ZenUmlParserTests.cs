using AwesomeAssertions;
using Mermaider.Models;
using Mermaider.Parsing;

namespace Mermaider.Tests.Parsing;

public class ZenUmlParserTests
{
	[Test]
	public void Parses_participants_messages_and_nesting()
	{
		var diagram = ZenUmlParser.Parse(
			"""
			zenuml
			title Orders
			@Actor Client
			S as "Order Service"
			Client->S.place(item, qty) {
			  id = Store.save(order)
			  S->Mailer: confirm
			  return id
			}
			"""
		);

		diagram.Title.Should().Be("Orders");
		diagram
			.Participants
			.Should()
			.Equal(
				new ZenUmlParticipant("Client", "Client", "Actor", null),
				new ZenUmlParticipant("S", "Order Service", null, null),
				new ZenUmlParticipant("Store", "Store", null, null),
				new ZenUmlParticipant("Mailer", "Mailer", null, null)
			);
		var place = diagram.Statements.Should().ContainSingle().Which.Should().BeOfType<ZenUmlMessage>().Which;
		place.Kind.Should().Be(ZenUmlMessageKind.Sync);
		(place.From, place.To, place.Label).Should().Be(("Client", "S", "place(item,qty)"));
		place.Body.Should().HaveCount(3);
		place.Body[0].Should().BeEquivalentTo(new { Kind = ZenUmlMessageKind.Sync, From = "S", To = "Store", Label = "save(order)", Assignee = "id" });
		place.Body[1].Should().BeEquivalentTo(new { Kind = ZenUmlMessageKind.Async, From = "S", To = "Mailer", Label = "confirm" });
		place.Body[2].Should().BeEquivalentTo(new { Kind = ZenUmlMessageKind.Return, From = "S", To = "Client", Label = "id" });
	}

	[Test]
	public void A_call_without_a_source_comes_from_the_starter()
	{
		var diagram = ZenUmlParser.Parse("zenuml\nA.method()\nnew B(1)");

		diagram.Participants[0].Should().Be(new ZenUmlParticipant(ZenUmlParser.DefaultStarter, "", null, null, IsStarter: true));
		diagram
			.Statements
			.Cast<ZenUmlMessage>()
			.Select(m => (m.Kind, m.From, m.To, m.Label))
			.Should()
			.Equal((ZenUmlMessageKind.Sync, "_STARTER_", "A", "method()"), (ZenUmlMessageKind.Create, "_STARTER_", "B", "«1»"));

		ZenUmlParser.Parse("zenuml\n@Starter(User)\nA.m()").Participants.Select(p => p.Id).Should().Equal("User", "A");
	}

	[Test]
	public void Parses_fragments_with_their_sections()
	{
		var diagram = ZenUmlParser.Parse(
			"""
			zenuml
			// check first
			if(x > 1) {
			  A->B: one
			} else if(y) {
			  A->B: two
			} else {
			  A->B: three
			}
			try {
			  A.m()
			} catch(Timeout e) {
			  A->B: retry
			} finally {
			  A->B: done
			}
			"""
		);

		var alt = diagram.Statements[0].Should().BeOfType<ZenUmlFragment>().Which;
		(alt.Kind, alt.Condition, alt.Comment).Should().Be((ZenUmlFragmentKind.Alt, "x > 1", "check first"));
		alt.Sections.Select(s => s.Label).Should().Equal("", "y", "else");
		var tcf = diagram.Statements[1].Should().BeOfType<ZenUmlFragment>().Which;
		tcf.Kind.Should().Be(ZenUmlFragmentKind.Try);
		tcf.Sections.Select(s => s.Label).Should().Equal("", "catch(Timeout e)", "finally");
	}

	[Test]
	public void Input_that_is_not_drawn_is_listed_as_unused()
	{
		var diagram = ZenUmlParser.Parse(
			"""
			zenuml
			@Database DB
			<<Repo>> R 200 #ff0000
			// a comment on a participant
			A
			await A.load()
			opt
			x =
			"""
		);

		diagram
			.Unused
			.Select(u => (u.Line, u.Text, u.Reason))
			.Should()
			.Equal(
				(2, "@Database", "the icon is not drawn; the participant is drawn as a box"),
				(3, "200", "a participant width is not drawn"),
				(3, "#ff0000", "a participant colour is not drawn"),
				(3, "<<Repo>>", "a stereotype is not drawn"),
				(4, "// a comment on a participant", "ZenUML draws a comment only above a message or a fragment"),
				(6, "await", "the modifier is not drawn"),
				(7, "opt", "a fragment without a message is not drawn"),
				(8, "x =", "an assignment without a message is not drawn")
			);
	}

	[Test]
	public void The_documented_examples_leave_only_an_icon_and_a_participant_comment_unused()
	{
		var unused = Directory
			.GetFiles(Mermaider.Tests.Snapshots.MjsFacts.MjsCorpus.DocsDir, "docs-zenuml-*.mmd")
			.Order(StringComparer.Ordinal)
			.SelectMany(
				f => ZenUmlParser.Parse(File.ReadAllText(f)).Unused.Select(u => $"{Path.GetFileNameWithoutExtension(f)}:{u.Line} {u.Text} | {u.Reason}")
			)
			.ToList();

		unused.Should().Equal(
			"docs-zenuml-003:4 @Database | the icon is not drawn; the participant is drawn as a box",
			"docs-zenuml-011:2 // a comment on a participant will not be rendered | ZenUML draws a comment only above a message or a fragment"
		);
	}

	[Test]
	[Arguments("zenuml\nA.m() {\n  B.n()\n", "ZenUML, line 4: expected '}' before the end of the diagram")]
	[Arguments("zenuml\nA->B: x\n}", "ZenUML, line 3: expected a statement, not '}' (near '}')")]
	[Arguments("zenuml\nelse { A->B: x }", "ZenUML, line 2: expected a statement, not 'else' (near 'else { A->B: x }')")]
	[Arguments("zenuml\nif(x { A->B: x }", "ZenUML, line 2: '(' is never closed")]
	public void Input_the_grammar_refuses_throws_with_the_line(string source, string message)
	{
		var act = () => ZenUmlParser.Parse(source);
		act.Should().Throw<MermaidParseException>().WithMessage(message);
	}
}
