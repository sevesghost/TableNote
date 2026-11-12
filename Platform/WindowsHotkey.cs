using System;
using System.Runtime.InteropServices;

namespace TableNote;

public static class WindowsHotkey
{
	public static bool TryRegister(nint hwnd, out bool collided)
	{
		collided = false;
		if (!OperatingSystem.IsWindows() || hwnd == 0)
		{
			return false;
		}
		if (RegisterHotKey(hwnd, 17732, 6u, 69u))
		{
			return true;
		}
		collided = Marshal.GetLastWin32Error() == 1409;
		return false;
	}

	public static void Unregister(nint hwnd)
	{
		if (OperatingSystem.IsWindows() && hwnd != 0)
		{
			UnregisterHotKey(hwnd, 17732);
		}
	}

	[DllImport("user32.dll", SetLastError = true)]
	private static extern bool RegisterHotKey(nint hwnd, int id, uint modifiers, uint key);

	[DllImport("user32.dll")]
	private static extern bool UnregisterHotKey(nint hwnd, int id);
}
