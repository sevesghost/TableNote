using System;
using System.Runtime.InteropServices;

namespace TableNote;

public static class WindowsTaskbar
{
	public static bool Hide(nint hwnd)
	{
		if (!OperatingSystem.IsWindows() || hwnd == 0)
		{
			return false;
		}
		int value = TaskbarPolicy.ExStyle((int)((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt64(), hideFromTaskbar: true);
		SetWindowLongPtr(hwnd, -20, new IntPtr(value));
		return TaskbarPolicy.IsHiddenFromTaskbarAndAltTab(((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt32());
	}

	/// <summary>v2.4.2: give the window its taskbar button back (edit mode). A visible window is
	/// hidden and re-shown without activation so Explorer picks up the new style.</summary>
	public static bool Show(nint hwnd)
	{
		if (!OperatingSystem.IsWindows() || hwnd == 0)
		{
			return false;
		}
		int current = (int)((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt64();
		int value = TaskbarPolicy.ExStyle(current, hideFromTaskbar: false);
		if (value != current)
		{
			SetWindowLongPtr(hwnd, -20, new IntPtr(value));
			if (IsWindowVisible(hwnd))
			{
				ShowWindow(hwnd, 0);
				ShowWindow(hwnd, 8);
			}
		}
		int now = ((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt32();
		return (now & 0x80) == 0 && (now & 0x40000) != 0;
	}

	[DllImport("user32.dll")]
	private static extern bool IsWindowVisible(nint hwnd);

	[DllImport("user32.dll")]
	private static extern bool ShowWindow(nint hwnd, int cmd);

	private static nint GetWindowLongPtr(nint hwnd, int index)
	{
		if (IntPtr.Size != 8)
		{
			return new IntPtr(GetWindowLong32(hwnd, index));
		}
		return GetWindowLongPtr64(hwnd, index);
	}

	private static nint SetWindowLongPtr(nint hwnd, int index, nint value)
	{
		if (IntPtr.Size != 8)
		{
			return new IntPtr(SetWindowLong32(hwnd, index, ((IntPtr)value).ToInt32()));
		}
		return SetWindowLongPtr64(hwnd, index, value);
	}

	[DllImport("user32.dll", EntryPoint = "GetWindowLong")]
	private static extern int GetWindowLong32(nint hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLong")]
	private static extern int SetWindowLong32(nint hwnd, int index, int value);

	[DllImport("user32.dll", EntryPoint = "GetWindowLongPtr")]
	private static extern nint GetWindowLongPtr64(nint hwnd, int index);

	[DllImport("user32.dll", EntryPoint = "SetWindowLongPtr")]
	private static extern nint SetWindowLongPtr64(nint hwnd, int index, nint value);
}
