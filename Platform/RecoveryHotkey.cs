namespace TableNote;

public static class RecoveryHotkey
{
	public const int Id = 17732;

	public const uint Modifiers = 6u;

	public const uint KeyE = 69u;

	public const uint WmHotkey = 786u;

	public static bool CountsAsRecovery(bool registerSucceeded)
	{
		return registerSucceeded;
	}

	public static string? FailureNotice(bool registerSucceeded, bool collided)
	{
		if (!registerSucceeded)
		{
			if (!collided)
			{
				return RecoveryGate.HotkeyUnavailable;
			}
			return RecoveryGate.HotkeyCollision;
		}
		return null;
	}
}
