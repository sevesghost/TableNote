using System;
using System.Runtime.InteropServices;

namespace TableNote;

public static class ClickThrough
{
	private static class WindowsClickThrough
	{
		private const int GwlExStyle = -20;

		private const int WsExTransparent = 32;

		private const int WsExLayered = 524288;

		private const uint LwaAlpha = 2u;

		// v2.3.1: remember whether WE added WS_EX_LAYERED, so Disable can take it off again
		// (the editor stayed a layered window after the first Background mode).
		private static readonly System.Collections.Generic.HashSet<nint> AddedLayered = new System.Collections.Generic.HashSet<nint>();

		public static bool Enable(nint hwnd)
		{
			long num = ((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt64();
			if ((num & WsExLayered) == 0) AddedLayered.Add(hwnd);
			num |= 0x80020;
			SetWindowLongPtr(hwnd, -20, new IntPtr(num));
			return SetLayeredWindowAttributes(hwnd, 0u, byte.MaxValue, 2u);
		}

		public static void Disable(nint hwnd)
		{
			long num = ((IntPtr)GetWindowLongPtr(hwnd, -20)).ToInt64();
			num &= -33;
			if (AddedLayered.Remove(hwnd)) num &= ~(long)WsExLayered;
			SetWindowLongPtr(hwnd, -20, new IntPtr(num));
		}

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

		[DllImport("user32.dll")]
		private static extern bool SetLayeredWindowAttributes(nint hwnd, uint colorKey, byte alpha, uint flags);
	}

	private static class LinuxClickThrough
	{
		private const int ShapeInput = 2;

		private const int ShapeSet = 0;

		public static bool TryEnable(nint hwnd)
		{
			nint num = XOpenDisplay(0);
			if (num == 0)
			{
				return false;
			}
			try
			{
				XShapeCombineRectangles(num, hwnd, 2, 0, 0, 0, 0, 0, 0);
				XFlush(num);
				return true;
			}
			finally
			{
				XCloseDisplay(num);
			}
		}

		public static void Disable(nint hwnd)
		{
			nint num = XOpenDisplay(0);
			if (num == 0)
			{
				return;
			}
			try
			{
				XShapeCombineMask(num, hwnd, 2, 0, 0, 0, 0);
				XFlush(num);
			}
			finally
			{
				XCloseDisplay(num);
			}
		}

		[DllImport("libX11.so.6")]
		private static extern nint XOpenDisplay(nint name);

		[DllImport("libX11.so.6")]
		private static extern int XCloseDisplay(nint display);

		[DllImport("libX11.so.6")]
		private static extern int XFlush(nint display);

		[DllImport("libXext.so.6")]
		private static extern void XShapeCombineRectangles(nint display, nint window, int destKind, int xOff, int yOff, nint rectangles, int count, int op, int ordering);

		[DllImport("libXext.so.6")]
		private static extern void XShapeCombineMask(nint display, nint window, int destKind, int xOff, int yOff, nint pixmap, int op);
	}

	public static bool Enable(nint hwnd)
	{
		if (hwnd == 0)
		{
			return false;
		}
		try
		{
			if (OperatingSystem.IsWindows())
			{
				return WindowsClickThrough.Enable(hwnd);
			}
			if (OperatingSystem.IsLinux())
			{
				return LinuxClickThrough.TryEnable(hwnd);
			}
			return false;
		}
		catch (Exception)
		{
			return false;
		}
	}

	public static void Disable(nint hwnd)
	{
		if (hwnd == 0)
		{
			return;
		}
		try
		{
			if (OperatingSystem.IsWindows())
			{
				WindowsClickThrough.Disable(hwnd);
			}
			else if (OperatingSystem.IsLinux())
			{
				LinuxClickThrough.Disable(hwnd);
			}
		}
		catch (Exception)
		{
		}
	}
}
