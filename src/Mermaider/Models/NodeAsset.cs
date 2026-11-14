namespace Mermaider.Models;

/// <summary>What an icon or image node draws above or below its label.</summary>
public enum NodeAssetKind
{
	/// <summary><c>@{ icon: "..." }</c>: a name resolved through <see cref="Icons.IconRegistry"/>.</summary>
	Icon,

	/// <summary><c>@{ img: "..." }</c>: an image URL. Only base64 <c>data:image/svg+xml</c> and <c>data:image/png</c> URIs are drawn.</summary>
	Image
}

/// <summary>The frame an icon node draws behind its icon, from <c>form</c>.</summary>
public enum IconForm
{
	/// <summary>No frame: the icon alone.</summary>
	None,

	/// <summary><c>form: "square"</c>.</summary>
	Square,

	/// <summary><c>form: "circle"</c>.</summary>
	Circle,

	/// <summary><c>form: "rounded"</c>.</summary>
	Rounded
}

/// <summary>The icon or image of a flowchart node given with <c>@{ icon: ... }</c> or <c>@{ img: ... }</c>.</summary>
/// <param name="Kind">Icon or image.</param>
/// <param name="Source">The icon name, such as <c>fa:user</c>, or the image URL as written in the source.</param>
public sealed record NodeAsset(NodeAssetKind Kind, string Source)
{
	/// <summary>The frame behind an icon, from <c>form</c>. Images have none.</summary>
	public IconForm Form { get; init; }

	/// <summary>True for <c>pos: "t"</c>, which puts the label above the icon or image. Default: below.</summary>
	public bool LabelOnTop { get; init; }

	/// <summary>Width from <c>w</c>, in px.</summary>
	public double? Width { get; init; }

	/// <summary>Height from <c>h</c>, in px.</summary>
	public double? Height { get; init; }

	/// <summary>True for <c>constraint: "on"</c>, which keeps an image's aspect ratio.</summary>
	public bool KeepAspectRatio { get; init; }
}
