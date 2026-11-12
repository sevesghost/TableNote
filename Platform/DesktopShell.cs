using System;

namespace TableNote;

public static class DesktopShell
{
	public static IDesktopShell Create()
	{
		if (!OperatingSystem.IsWindows())
		{
			return new UnsupportedDesktopShell();
		}
		return new WindowsDesktopShell();
	}
}
