namespace TableNote;

public static class TaskbarPolicy
{
	public const int ToolWindow = 128;

	public const int AppWindow = 262144;

	public static bool MayHideFromTaskbar(RecoveryRoutes routes)
	{
		if (routes.TrayReady)
		{
			return routes.RelaunchSignalReady;
		}
		return false;
	}

	public static int ExStyle(int current, bool hideFromTaskbar)
	{
		if (!hideFromTaskbar)
		{
			return (current & -129) | 0x40000;
		}
		return (current | 0x80) & -262145;
	}

	public static bool IsHiddenFromTaskbarAndAltTab(int exStyle)
	{
		if ((exStyle & 0x80) != 0)
		{
			return (exStyle & 0x40000) == 0;
		}
		return false;
	}
}
