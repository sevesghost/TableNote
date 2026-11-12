namespace TableNote;

public static class BackgroundTransition
{
	public readonly record struct Attempt(bool RoutesReady, bool ClickThroughApplied, bool Faulted);

	public static bool Entered(Attempt attempt)
	{
		if (attempt.RoutesReady && attempt.ClickThroughApplied)
		{
			return !attempt.Faulted;
		}
		return false;
	}

	public static (ViewMode Mode, string? Notice, bool PersistBackground) Request(RecoveryRoutes routes, bool clickThroughApplied, bool taskbarHidden, bool desktopAttached, bool faulted)
	{
		if (!RecoveryGate.CanEnter(routes))
		{
			return (Mode: ViewMode.Edit, Notice: RecoveryGate.BlockReason(routes), PersistBackground: false);
		}
		if (!taskbarHidden)
		{
			return (Mode: ViewMode.Edit, Notice: RecoveryGate.TaskbarHideFailed, PersistBackground: false);
		}
		if (!desktopAttached)
		{
			return (Mode: ViewMode.Edit, Notice: RecoveryGate.NotAttachedStaysEditor, PersistBackground: false);
		}
		if (!Entered(new Attempt(RoutesReady: true, clickThroughApplied, faulted)))
		{
			return (Mode: ViewMode.Edit, Notice: RecoveryGate.PartialRollback, PersistBackground: false);
		}
		return (Mode: ViewMode.Background, Notice: null, PersistBackground: true);
	}
}
