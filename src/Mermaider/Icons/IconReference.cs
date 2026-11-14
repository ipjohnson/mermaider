using System.Text.RegularExpressions;

namespace Mermaider.Icons;

/// <summary>
/// A FontAwesome-style icon reference: <c>fa:fa-book</c> in a flowchart label, or <c>fa fa-book</c> in a mindmap
/// <c>::icon(...)</c>. <see cref="Text"/> is the reference as mermaid.js writes it in its own source form
/// (<c>fa:fa-book</c>, <c>mdi:mdi-skull-outline</c>); <see cref="RegistryName"/> is the <see cref="IconRegistry"/> name
/// it draws, or null when the registry has none.
/// </summary>
internal readonly partial record struct IconReference(string Text, string Pack, string Name)
{
	/// <summary>mermaid.js's own pattern: <c>fa</c>, <c>fab</c>, <c>fak</c>, <c>fal</c>, <c>far</c> or <c>fas</c>, a colon, and <c>fa-</c> and a name.</summary>
	[GeneratedRegex(@"(fa[bklrs]?):fa-([\w-]+)", RegexOptions.None, matchTimeoutMilliseconds: 2000)]
	internal static partial Regex LabelPattern();

	/// <summary>True when <paramref name="label"/> holds at least one <c>fa:fa-name</c> reference.</summary>
	internal static bool InLabel(string? label) => label is not null && label.Contains(":fa-", StringComparison.Ordinal) && LabelPattern().IsMatch(label);

	internal static IconReference FromMatch(Match m) => new(m.Value, m.Groups[1].Value, m.Groups[2].Value);

	/// <summary>
	/// Reads a mindmap icon's class list, such as <c>fa fa-book</c> or <c>mdi mdi-skull-outline</c>. The first class is
	/// the pack and the second the name, without its <c>pack-</c> or <c>fa-</c> prefix; <c>fa-fw</c> is ignored.
	/// </summary>
	internal static IconReference FromClasses(string classes)
	{
		var parts = classes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Where(c => c != "fa-fw").Take(2).ToArray();
		if (parts.Length == 0)
			return new IconReference("", "", "");
		if (parts.Length == 1)
			return new IconReference(parts[0], "", parts[0]);
		var (pack, name) = (parts[0], parts[1]);
		if (name.StartsWith(pack + "-", StringComparison.OrdinalIgnoreCase))
			name = name[(pack.Length + 1)..];
		else if (name.StartsWith("fa-", StringComparison.OrdinalIgnoreCase))
			name = name[3..];
		return new IconReference(parts[0] + ":" + parts[1], pack, name);
	}

	/// <summary>
	/// The registry name to draw: <c>pack:name</c> when registered, else <c>fa:name</c> for any FontAwesome pack
	/// (<c>fas</c>, <c>far</c> and the rest share the <c>fa</c> placeholders), else the bare name. Null when none is registered.
	/// </summary>
	internal string? RegistryName
	{
		get
		{
			if (Name.Length == 0)
				return null;
			if (Pack.Length > 0 && IconRegistry.TryGet(Pack + ":" + Name, out _))
				return Pack + ":" + Name;
			if (Pack.StartsWith("fa", StringComparison.OrdinalIgnoreCase) && IconRegistry.TryGet("fa:" + Name, out _))
				return "fa:" + Name;
			return Pack.Length == 0 && IconRegistry.TryGet(Name, out _) ? Name : null;
		}
	}
}
