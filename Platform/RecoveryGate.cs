using System;

namespace TableNote;

public static class RecoveryGate
{
	// v2.4.2: notices are resolved in the UI language when shown (were Thai consts).
	public static string TrayUnavailable => Localizer.T("recovery.tray_unavailable");

	public static string SignalUnavailable => Localizer.T("recovery.signal_unavailable");

	public static string ClickThroughFailed => Localizer.T("recovery.click_through_failed");

	public static string PartialRollback => Localizer.T("recovery.partial_rollback");

	public static string ForcedEdit => Localizer.T("recovery.forced_edit");

	public static string PersistedUnsafe => Localizer.T("recovery.persisted_unsafe");

	public static string TrayLost => Localizer.T("recovery.tray_lost");

	public static string HotkeyCollision => Localizer.T("recovery.hotkey_collision");

	public static string HotkeyUnavailable => Localizer.T("recovery.hotkey_unavailable");

	public static string TaskbarHideFailed => Localizer.T("recovery.taskbar_hide_failed");

	public static string NotAttachedStaysEditor => Localizer.T("recovery.not_attached");

	public static bool CanEnter(RecoveryRoutes routes)
	{
		if (routes.TrayReady)
		{
			return routes.RelaunchSignalReady;
		}
		return false;
	}

	public static string? BlockReason(RecoveryRoutes routes)
	{
		if (!routes.TrayReady)
		{
			return TrayUnavailable;
		}
		if (!routes.RelaunchSignalReady)
		{
			return SignalUnavailable;
		}
		return null;
	}

	public static ViewMode ResolveStartup(ViewMode persisted, bool forceEdit, RecoveryRoutes routes)
	{
		if (forceEdit || persisted != ViewMode.Background)
		{
			return ViewMode.Edit;
		}
		if (!CanEnter(routes))
		{
			return ViewMode.Edit;
		}
		return ViewMode.Background;
	}

	public static string? StartupNotice(ViewMode persisted, bool forceEdit, RecoveryRoutes routes)
	{
		if (forceEdit && persisted == ViewMode.Background)
		{
			return ForcedEdit;
		}
		if (persisted == ViewMode.Background && !CanEnter(routes))
		{
			return PersistedUnsafe;
		}
		return null;
	}

	public static ViewMode AfterShellRestart(ViewMode current, RecoveryRoutes routesAfter)
	{
		if (current != ViewMode.Background)
		{
			return current;
		}
		if (!CanEnter(routesAfter))
		{
			return ViewMode.Edit;
		}
		return ViewMode.Background;
	}

	public static ViewMode Parse(string? value)
	{
		if (!string.Equals(value?.Trim(), "background", StringComparison.OrdinalIgnoreCase))
		{
			return ViewMode.Edit;
		}
		return ViewMode.Background;
	}

	public static string Format(ViewMode mode)
	{
		if (mode != ViewMode.Background)
		{
			return "edit";
		}
		return "background";
	}
}
