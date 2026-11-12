using System;

namespace TableNote;

public sealed class ReattachScheduler
{
	public const int MaxAttempts = 4;

	public static readonly TimeSpan Interval = TimeSpan.FromSeconds(2L);

	public int Attempts { get; private set; }

	public bool Running { get; private set; }

	public static bool AllowsRetry(DesktopHostKind host)
	{
		return host == DesktopHostKind.None;
	}

	public void Start()
	{
		Attempts = 0;
		Running = true;
	}

	public void Stop()
	{
		Running = false;
	}

	public bool OnAttempt(bool attached)
	{
		if (!Running)
		{
			return false;
		}
		Attempts++;
		if (attached || Attempts >= 4)
		{
			Running = false;
			return false;
		}
		return true;
	}
}
