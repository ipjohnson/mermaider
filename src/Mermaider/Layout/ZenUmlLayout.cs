using Mermaider.Models;
using Mermaider.Parsing;
using Mermaider.Rendering;

namespace Mermaider.Layout;

/// <summary>
/// Lays out a ZenUML diagram as a sequence diagram. Sync calls are solid messages with a filled head that activate their
/// target until their last nested message; async messages have an open head; creations and returns are dashed. Each
/// assignment adds a return to the caller. Fragments become frames, comments become notes over the next message's
/// participants and dividers become notes across every lifeline.
/// </summary>
internal static class ZenUmlLayout
{
	internal static PositionedSequenceDiagram Layout(ZenUmlDiagram diagram)
	{
		var (sequence, ends) = ToSequence(diagram);
		var top = diagram.Title is { Length: > 0 } ? SequenceSvgRenderer.TitleBand : 0;
		var positioned = SequenceLayout.Layout(sequence, top, ends, notesBelowFrames: true);
		return diagram.Title is { Length: > 0 } title
			? positioned with { Width = Math.Max(positioned.Width, SequenceSvgRenderer.TitleWidth(title)) }
			: positioned;
	}

	internal static (SequenceDiagram Sequence, IReadOnlyList<(string ActorId, int AfterIndex)> ActivationEnds) ToSequence(ZenUmlDiagram diagram)
	{
		var builder = new Builder();
		builder.Emit(diagram.Statements);

		var actors = diagram
			.Participants
			.Select(p => new SequenceActor(p.Id, p.Label, IsPerson(p) ? SequenceActorType.Actor : SequenceActorType.Participant))
			.ToList();
		if (actors.Count > 0)
		{
			foreach (var (afterIndex, text) in builder.Dividers)
				builder.Notes.Add(new SequenceNote([actors[0].Id, actors[^1].Id], text, SequenceNotePosition.Over, afterIndex));
		}

		var sequence = new SequenceDiagram
		{
			Actors = actors,
			Messages = builder.Messages,
			Blocks = builder.Blocks,
			Notes = builder.Notes,
			Boxes = [.. diagram.Groups.Where(g => g.ParticipantIds.Count > 0).Select(g => new SequenceBox(null, g.Name, g.ParticipantIds))],
			Creates = builder.Creates,
		};
		return (sequence, builder.Ends);
	}

	/// <summary>The starter and <c>@Actor</c> participants are drawn with a person glyph.</summary>
	internal static bool IsPerson(ZenUmlParticipant p) => p.IsStarter || string.Equals(p.Annotation, "Actor", StringComparison.OrdinalIgnoreCase);

	private sealed class Builder
	{
		public List<SequenceMessage> Messages { get; } = [];
		public List<SequenceBlock> Blocks { get; } = [];
		public List<SequenceNote> Notes { get; } = [];
		public List<SequenceCreate> Creates { get; } = [];
		public List<(string ActorId, int AfterIndex)> Ends { get; } = [];
		public List<(int AfterIndex, string Text)> Dividers { get; } = [];

		public void Emit(IReadOnlyList<ZenUmlStatement> statements)
		{
			foreach (var s in statements)
				Emit(s);
		}

		private void Emit(ZenUmlStatement statement)
		{
			var first = Messages.Count;
			switch (statement)
			{
				case ZenUmlMessage m:
					EmitMessage(m);
					break;
				case ZenUmlFragment f:
					EmitFragment(f);
					break;
				case ZenUmlDivider d:
					Dividers.Add((first - 1, d.Text));
					break;
			}

			if (statement.Comment is { } comment && Messages.Count > first)
			{
				var m = Messages[first];
				string[] over = m.From == m.To ? [m.From] : [m.From, m.To];
				Notes.Add(new SequenceNote(over, ZenUmlParser.PlainComment(comment), SequenceNotePosition.Over, first - 1));
			}
		}

		private void EmitMessage(ZenUmlMessage m)
		{
			switch (m.Kind)
			{
				case ZenUmlMessageKind.Async:
					Messages.Add(new SequenceMessage(m.From, m.To, m.Label, SequenceLineStyle.Solid, SequenceArrowHead.Open));
					return;
				case ZenUmlMessageKind.Return:
					Messages.Add(new SequenceMessage(m.From, m.To, m.Label, SequenceLineStyle.Dashed, SequenceArrowHead.Open));
					return;
			}

			var create = m.Kind == ZenUmlMessageKind.Create;
			Messages.Add(
				new SequenceMessage(
					m.From,
					m.To,
					m.Label,
					create ? SequenceLineStyle.Dashed : SequenceLineStyle.Solid,
					create ? SequenceArrowHead.Open : SequenceArrowHead.Filled,
					Activate: true
				)
			);
			if (create)
				Creates.Add(new SequenceCreate(m.To, Messages.Count - 1));
			Emit(m.Body);
			if (m.Assignee is { } assignee)
				Messages.Add(new SequenceMessage(m.To, m.From, assignee, SequenceLineStyle.Dashed, SequenceArrowHead.Open));
			Ends.Add((m.To, Messages.Count - 1));
		}

		private void EmitFragment(ZenUmlFragment f)
		{
			var start = Messages.Count;
			var dividers = new List<SequenceBlockDivider>();
			for (var i = 0; i < f.Sections.Count; i++)
			{
				var section = f.Sections[i];
				if (i > 0 && Messages.Count > start)
					dividers.Add(new SequenceBlockDivider(Messages.Count, section.Label));
				if (f.Kind != ZenUmlFragmentKind.Par)
				{
					Emit(section.Statements);
					continue;
				}

				// ZenUML separates every statement of a par
				foreach (var s in section.Statements)
				{
					if (Messages.Count > start && ZenUmlParser.HasMessage([s]))
						dividers.Add(new SequenceBlockDivider(Messages.Count, ""));
					Emit(s);
				}
			}

			// a divider for a section without a message would sit on the next section's first message
			dividers = [.. dividers.Where(d => d.Index < Messages.Count).GroupBy(d => d.Index).Select(g => g.Last())];
			if (Messages.Count == start)
				return;
			Blocks.Add(new SequenceBlock
			{
				Type = f.Kind switch
				{
					ZenUmlFragmentKind.Loop => SequenceBlockType.Loop,
					ZenUmlFragmentKind.Alt => SequenceBlockType.Alt,
					ZenUmlFragmentKind.Opt => SequenceBlockType.Opt,
					ZenUmlFragmentKind.Par => SequenceBlockType.Par,
					_ => SequenceBlockType.Try,
				},
				Label = f.Condition,
				StartIndex = start,
				EndIndex = Messages.Count - 1,
				Dividers = dividers,
			});
		}
	}
}
