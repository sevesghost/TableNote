namespace TableNote;

/// <summary>
/// The editor window's layer as saved in note.json ("desktop", "floating", "window"). Up to 2.5.1 "desktop" could
/// attach the window to the desktop in background mode; since 2.6.0 the table goes on the wallpaper instead, and
/// "desktop" and "window" are both a normal window. The saved values are unchanged so old files read the same.
/// </summary>
public static class DesktopPlacement
{
	public static bool EffectiveTopmost(DesktopLayerKind layer)
	{
		return layer == DesktopLayerKind.Floating;
	}

	public static DesktopLayerKind ParseLayer(string? value)
	{
		string? text = value?.Trim().ToLowerInvariant();
		if (!(text == "window"))
		{
			if (text == "floating")
			{
				return DesktopLayerKind.Floating;
			}
			return DesktopLayerKind.Desktop;
		}
		return DesktopLayerKind.Window;
	}

	public static string FormatLayer(DesktopLayerKind layer)
	{
		return layer switch
		{
			DesktopLayerKind.Window => "window",
			DesktopLayerKind.Floating => "floating",
			_ => "desktop",
		};
	}
}
