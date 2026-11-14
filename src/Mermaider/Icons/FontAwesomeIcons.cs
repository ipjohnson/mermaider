namespace Mermaider.Icons;

/// <summary>
/// Placeholder glyphs for commonly used FontAwesome names, registered as <c>fa:&lt;name&gt;</c> (for example
/// <c>fa:book</c>). They are original line pictograms on a 24 × 24 grid, not FontAwesome's artwork. A label's
/// <c>fa:fa-book</c>, <c>fas:fa-book</c> or mindmap <c>::icon(fa fa-book)</c> resolves to them through
/// <see cref="IconReference"/>.
/// </summary>
internal static class FontAwesomeIcons
{
	/// <summary>Glyph markup by FontAwesome name, drawn in the stroke the wrapping <c>&lt;svg&gt;</c> sets.</summary>
	private static readonly (string Name, string Glyph)[] Glyphs =
	[
		("user", """<circle cx="12" cy="8" r="4"/><path d="M4 21c0-4.4 3.6-7 8-7s8 2.6 8 7"/>"""),
		("users", """<circle cx="9" cy="8" r="3.5"/><path d="M2 20c0-3.9 3.1-6 7-6s7 2.1 7 6"/><path d="M15.5 4.6a3.5 3.5 0 0 1 0 6.8M18 14.4c2.4.7 4 2.6 4 5.6"/>"""),
		("house", """<path d="M3 11.5 12 4l9 7.5"/><path d="M5.5 9.5V20h13V9.5"/><path d="M10 20v-6h4v6"/>"""),
		("book", """<path d="M4 4.5A1.5 1.5 0 0 1 5.5 3H20v15H5.5A1.5 1.5 0 0 0 4 19.5z"/><path d="M4 19.5A1.5 1.5 0 0 0 5.5 21H20v-3"/><path d="M8 7h8"/>"""),
		("film", """<rect x="3" y="4" width="18" height="16" rx="2"/><path d="M7 4v16M17 4v16M3 8h4M3 12h4M3 16h4M17 8h4M17 12h4M17 16h4"/>"""),
		("music", """<path d="M9 18V5l11-2v13"/><circle cx="6.5" cy="18" r="2.5"/><circle cx="17.5" cy="16" r="2.5"/>"""),
		("twitter", """<path d="M4 15c3 3 9 4 13-1s2-9 2-9l2-2-3 .5C16 2 12 3 12 7c-3 0-6-1.5-8-4 0 0-2 5 3 8-1.5 0-3-.5-3-.5 0 2 1.5 3.5 3.5 4z"/>"""),
		("ban", """<circle cx="12" cy="12" r="9"/><path d="m5.6 5.6 12.8 12.8"/>"""),
		("spinner", """<path d="M12 3v3M12 18v3M3 12h3M18 12h3M5.6 5.6l2.1 2.1M16.3 16.3l2.1 2.1M5.6 18.4l2.1-2.1M16.3 7.7l2.1-2.1"/>"""),
		("camera-retro", """<rect x="3" y="7" width="18" height="13" rx="2"/><path d="M8 7l1.5-3h5L16 7"/><circle cx="12" cy="13.5" r="3.5"/><path d="M5.5 10h2"/>"""),
		("camera", """<rect x="3" y="7" width="18" height="13" rx="2"/><path d="M8 7l1.5-3h5L16 7"/><circle cx="12" cy="13.5" r="3.5"/>"""),
		("car", """<path d="M3 16v-3l2-5h14l2 5v3z"/><path d="M3 13h18"/><circle cx="7" cy="17.5" r="1.8"/><circle cx="17" cy="17.5" r="1.8"/>"""),
		("truck", """<path d="M2 6h12v10H2zM14 10h4l3 3v3h-7"/><circle cx="6" cy="17.5" r="1.8"/><circle cx="17" cy="17.5" r="1.8"/>"""),
		("check", """<path d="m4 12.5 5 5L20 6.5"/>"""),
		("xmark", """<path d="M6 6l12 12M18 6 6 18"/>"""),
		("plus", """<path d="M12 5v14M5 12h14"/>"""),
		("minus", """<path d="M5 12h14"/>"""),
		("star", """<path d="m12 3 2.7 5.6 6.1.8-4.5 4.2 1.1 6.1L12 16.8l-5.4 2.9 1.1-6.1-4.5-4.2 6.1-.8z"/>"""),
		("heart", """<path d="M12 20s-8-4.7-8-10.5A4.5 4.5 0 0 1 12 7a4.5 4.5 0 0 1 8 2.5C20 15.3 12 20 12 20z"/>"""),
		("gear", """<circle cx="12" cy="12" r="3"/><path d="M12 2.5v3M12 18.5v3M2.5 12h3M18.5 12h3M5.3 5.3l2.1 2.1M16.6 16.6l2.1 2.1M5.3 18.7l2.1-2.1M16.6 7.4l2.1-2.1"/><circle cx="12" cy="12" r="6.5"/>"""),
		("envelope", """<rect x="3" y="5" width="18" height="14" rx="2"/><path d="m3.5 6 8.5 7 8.5-7"/>"""),
		("phone", """<path d="M5 3h3.5l1.5 4.5-2.2 1.5a11 11 0 0 0 7.2 7.2l1.5-2.2L21 15.5V19a2 2 0 0 1-2 2A16 16 0 0 1 3 5a2 2 0 0 1 2-2z"/>"""),
		("lock", """<rect x="4.5" y="10.5" width="15" height="10.5" rx="2"/><path d="M8 10.5V7.5a4 4 0 0 1 8 0v3"/>"""),
		("unlock", """<rect x="4.5" y="10.5" width="15" height="10.5" rx="2"/><path d="M8 10.5V7.5a4 4 0 0 1 7.7-1.5"/>"""),
		("key", """<circle cx="7.5" cy="15.5" r="4.5"/><path d="m10.7 12.3 9.3-9.3M16 7l3 3M13.5 9.5l2 2"/>"""),
		("magnifying-glass", """<circle cx="10.5" cy="10.5" r="6.5"/><path d="m15.5 15.5 5 5"/>"""),
		("file", """<path d="M6 3h8l5 5v13H6z"/><path d="M14 3v5h5"/>"""),
		("folder", """<path d="M3 6.5A1.5 1.5 0 0 1 4.5 5H9l2 2.5h8.5A1.5 1.5 0 0 1 21 9v9.5a1.5 1.5 0 0 1-1.5 1.5h-15A1.5 1.5 0 0 1 3 18.5z"/>"""),
		("cloud", """<path d="M7 19a4.5 4.5 0 0 1-.6-9A6 6 0 0 1 18 9.5a4.8 4.8 0 0 1-.5 9.5z"/>"""),
		("database", """<ellipse cx="12" cy="5.5" rx="7.5" ry="2.5"/><path d="M4.5 5.5v13c0 1.4 3.4 2.5 7.5 2.5s7.5-1.1 7.5-2.5v-13M4.5 12c0 1.4 3.4 2.5 7.5 2.5s7.5-1.1 7.5-2.5"/>"""),
		("server", """<rect x="3.5" y="4" width="17" height="7" rx="1.5"/><rect x="3.5" y="13" width="17" height="7" rx="1.5"/><path d="M7 7.5h1M7 16.5h1"/>"""),
		("code", """<path d="m8 7-5 5 5 5M16 7l5 5-5 5M13.5 4l-3 16"/>"""),
		("bug", """<rect x="7" y="7" width="10" height="13" rx="5"/><path d="M12 11v9M7 12H3M21 12h-4M7 17l-3 2M17 17l3 2M8.5 7.5 6 4.5M15.5 7.5 18 4.5"/>"""),
		("globe", """<circle cx="12" cy="12" r="9"/><path d="M3 12h18M12 3c2.5 2.5 3.5 6 3.5 9s-1 6.5-3.5 9M12 3c-2.5 2.5-3.5 6-3.5 9s1 6.5 3.5 9"/>"""),
		("link", """<path d="M10 14a4 4 0 0 0 5.7 0l3-3a4 4 0 0 0-5.7-5.7l-1 1"/><path d="M14 10a4 4 0 0 0-5.7 0l-3 3a4 4 0 0 0 5.7 5.7l1-1"/>"""),
		("bell", """<path d="M6 16V11a6 6 0 0 1 12 0v5l2 2H4z"/><path d="M10 20.5a2 2 0 0 0 4 0"/>"""),
		("calendar", """<rect x="3.5" y="5" width="17" height="15.5" rx="2"/><path d="M3.5 10h17M8 3v4M16 3v4"/>"""),
		("clock", """<circle cx="12" cy="12" r="9"/><path d="M12 7v5l3.5 2"/>"""),
		("comment", """<path d="M4 5h16v11H9l-5 4z"/>"""),
		("download", """<path d="M12 3v12M7 10l5 5 5-5M4 20h16"/>"""),
		("upload", """<path d="M12 15V3M7 8l5-5 5 5M4 20h16"/>"""),
		("circle-info", """<circle cx="12" cy="12" r="9"/><path d="M12 11v6M12 7.5v.5"/>"""),
		("circle-question", """<circle cx="12" cy="12" r="9"/><path d="M9.5 9.5a2.5 2.5 0 1 1 3.5 2.3c-.7.3-1 .9-1 1.7M12 17v.5"/>"""),
		("triangle-exclamation", """<path d="M12 3.5 21.5 20h-19z"/><path d="M12 10v4.5M12 17v.5"/>"""),
		("play", """<path d="M7 4.5v15l12-7.5z"/>"""),
		("pause", """<path d="M8 5v14M16 5v14"/>"""),
		("stop", """<rect x="5.5" y="5.5" width="13" height="13" rx="1.5"/>"""),
		("trash", """<path d="M4 7h16M9.5 7V4.5h5V7M6 7l1 13.5h10L18 7M10 11v6M14 11v6"/>"""),
		("pen", """<path d="M4 20l1-4.5L16 4.5l3.5 3.5L8.5 19z"/><path d="m13.5 7 3.5 3.5"/>"""),
		("arrow-right", """<path d="M4 12h16M14 6l6 6-6 6"/>"""),
		("arrow-left", """<path d="M20 12H4M10 6l-6 6 6 6"/>"""),
		("arrow-up", """<path d="M12 20V4M6 10l6-6 6 6"/>"""),
		("arrow-down", """<path d="M12 4v16M6 14l6 6 6-6"/>"""),
		("flag", """<path d="M5 21V4M5 4h12l-2.5 4.5L17 13H5"/>"""),
		("shield", """<path d="M12 3 4.5 6v5.5c0 4.7 3.2 8 7.5 9.5 4.3-1.5 7.5-4.8 7.5-9.5V6z"/>"""),
		("rocket", """<path d="M12 2.5c3.5 2.5 5 6.5 4.5 11.5h-9C7 9 8.5 5 12 2.5z"/><circle cx="12" cy="9" r="1.8"/><path d="M7.5 14 4.5 17.5h4M16.5 14l3 3.5h-4M10 18l2 3.5 2-3.5"/>"""),
		("lightbulb", """<path d="M9 17.5h6M10 21h4"/><path d="M9 17.5c0-2.5-3-4-3-7.5a6 6 0 0 1 12 0c0 3.5-3 5-3 7.5"/>"""),
		("chart-bar", """<path d="M4 20h16M7 20v-7M12 20V5M17 20v-10"/>"""),
		("desktop", """<rect x="3" y="4" width="18" height="12" rx="1.5"/><path d="M12 16v4M8 20h8"/>"""),
		("laptop", """<rect x="5" y="5" width="14" height="10" rx="1.5"/><path d="M2.5 19h19l-2-4h-15z"/>"""),
		("mobile", """<rect x="7" y="3" width="10" height="18" rx="2"/><path d="M11 17.5h2"/>"""),
		("wifi", """<path d="M2.5 9a14 14 0 0 1 19 0M5.5 12.5a9.5 9.5 0 0 1 13 0M8.8 16a5 5 0 0 1 6.4 0M12 19.5v.5"/>"""),
		("image", """<rect x="3" y="4.5" width="18" height="15" rx="2"/><circle cx="8.5" cy="9.5" r="1.8"/><path d="m3.5 18 5.5-5.5 4 4 2.5-2.5 5 5"/>"""),
		("bolt", """<path d="M13.5 2.5 5 13.5h6l-1 8 8.5-11h-6z"/>"""),
		("power-off", """<path d="M12 3v8"/><path d="M7 6.3a7.5 7.5 0 1 0 10 0"/>"""),
		("sitemap", """<rect x="9" y="3" width="6" height="4.5" rx="1"/><rect x="2.5" y="16.5" width="6" height="4.5" rx="1"/><rect x="15.5" y="16.5" width="6" height="4.5" rx="1"/><path d="M12 7.5V12M5.5 16.5V12h13v4.5"/>"""),
		("tag", """<path d="M3 3h8.5l9.5 9.5-8.5 8.5L3 11.5z"/><circle cx="7.5" cy="7.5" r="1.5"/>"""),
		("cart-shopping", """<path d="M2.5 3.5h3l2.5 12h11l2-8.5H6.5"/><circle cx="9.5" cy="19.5" r="1.5"/><circle cx="17" cy="19.5" r="1.5"/>"""),
		("building", """<rect x="5" y="3" width="14" height="18" rx="1"/><path d="M9 7h1M14 7h1M9 11h1M14 11h1M9 15h1M14 15h1M10.5 21v-3h3v3"/>"""),
		("location-dot", """<path d="M12 21s-7-6.5-7-11.5a7 7 0 0 1 14 0C19 14.5 12 21 12 21z"/><circle cx="12" cy="9.5" r="2.5"/>"""),
		("paper-plane", """<path d="M21 3 3 10.5l7 2.5 2.5 7z"/><path d="m10 13 11-10"/>"""),
	];

	/// <summary>Older or alternative FontAwesome names and the glyph each draws.</summary>
	private static readonly (string Alias, string Name)[] Aliases =
	[
		("home", "house"),
		("times", "xmark"),
		("close", "xmark"),
		("remove", "xmark"),
		("cog", "gear"),
		("search", "magnifying-glass"),
		("edit", "pen"),
		("pencil", "pen"),
		("info-circle", "circle-info"),
		("question-circle", "circle-question"),
		("exclamation-triangle", "triangle-exclamation"),
		("warning", "triangle-exclamation"),
		("trash-can", "trash"),
		("shopping-cart", "cart-shopping"),
		("map-marker", "location-dot"),
		("bar-chart", "chart-bar"),
		("envelope-o", "envelope"),
		("file-o", "file"),
		("folder-o", "folder"),
		("send", "paper-plane"),
	];

	/// <summary>Every glyph and alias as a registry name (<c>fa:&lt;name&gt;</c>) and its SVG.</summary>
	internal static IEnumerable<(string Name, string Svg)> All()
	{
		var byName = Glyphs.ToDictionary(g => g.Name, g => g.Glyph, StringComparer.Ordinal);
		foreach (var (name, glyph) in Glyphs)
			yield return ("fa:" + name, Svg(glyph));
		foreach (var (alias, name) in Aliases)
			yield return ("fa:" + alias, Svg(byName[name]));
	}

	private static string Svg(string glyph) =>
		$"""
		<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 24 24" fill="none" stroke="#64748b" stroke-width="2" stroke-linecap="round" stroke-linejoin="round">
		{glyph}
		</svg>
		""";
}
