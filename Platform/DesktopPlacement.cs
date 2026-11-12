namespace TableNote;

public static class DesktopPlacement
{
	// v2.4.2: notices are resolved in the UI language when shown (were Thai consts).
	public static string FallbackNotice => Localizer.T("desktop.fallback");

	public static string DesktopWorkerNotice => Localizer.T("desktop.worker");

	public static string DesktopProgmanNotice => Localizer.T("desktop.progman");

	public static string BottomFallbackNotice => Localizer.T("desktop.bottom_fallback");

	public static string FloatingNotice => Localizer.T("desktop.floating");

	public static DesktopDecision Decide(DesktopAttachResult result)
	{
		if (!result.Attached)
		{
			return new DesktopDecision(ClaimsDesktop: false, Topmost: false, FallbackNotice);
		}
		return result.Host switch
		{
			DesktopHostKind.WorkerWBehindIcons => new DesktopDecision(ClaimsDesktop: true, Topmost: false, DesktopWorkerNotice), 
			DesktopHostKind.BottomFallback => new DesktopDecision(ClaimsDesktop: true, Topmost: false, BottomFallbackNotice),
			DesktopHostKind.IconHost => new DesktopDecision(ClaimsDesktop: true, Topmost: false, Localizer.T("desktop.icon_host")),
			DesktopHostKind.Progman => new DesktopDecision(ClaimsDesktop: true, Topmost: false, DesktopProgmanNotice), 
			_ => new DesktopDecision(ClaimsDesktop: false, Topmost: false, FallbackNotice), 
		};
	}

	public static bool EffectiveTopmost(DesktopLayerKind layer)
	{
		return layer == DesktopLayerKind.Floating;
	}

	public static DesktopLayerKind ParseLayer(string? value)
	{
		string text = value?.Trim().ToLowerInvariant();
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
