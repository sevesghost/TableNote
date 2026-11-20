using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using Avalonia;
using Microsoft.Win32;

namespace TableNote;

/// <summary>
/// Windows wallpaper through IDesktopWallpaper (Windows 8+), one picture per monitor. When that object can't be
/// created, SystemParametersInfo sets one picture for every monitor. Slideshow and spotlight state come from the
/// same object and from HKCU\...\Explorer\Wallpapers\BackgroundType (0 picture, 1 colour, 2 slideshow, 3 spotlight).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed unsafe class WindowsWallpaperHost : IWallpaperHost
{
    private static readonly Guid ClsidDesktopWallpaper = new("C2CF3110-460E-4fc1-B9D0-8A1C0C9CC4BD");
    private static readonly Guid IidDesktopWallpaper = new("B92B56A9-8B55-4E14-9A89-0199BBB6F93B");
    private static readonly Guid IidShellItem = new("43826D1E-E718-42EE-BC55-A1E261C37BFE");
    private static readonly Guid IidShellItemArray = new("B63EA76D-1F85-456F-A19C-48159EFA858B");
    private const uint ClsctxAll = 0x17;
    private const int CoENotInitialized = unchecked((int)0x800401F0);
    private const uint CoinitApartmentThreaded = 0x2;
    private const uint SpiGetDeskWallpaper = 0x0073;
    private const uint SpiSetDeskWallpaper = 0x0014;
    private const uint SpifUpdateIniFile = 0x1, SpifSendChange = 0x2;
    private const int ColorDesktop = 1;

    public bool Supported => true;

    public WallpaperState? Read(out string? detail)
    {
        var type = BackgroundType();
        var spotlight = type == 3 || (type < 0 && SpotlightEnabled());
        var policy = PolicyLocked();
        var p = Create(out detail);
        if (p == 0) return ReadClassic(type, spotlight, policy, ref detail);
        try
        {
            var com = new DesktopWallpaperCom(p);
            var monitors = new List<WallpaperMonitor>();
            if (com.GetMonitorDevicePathCount(out var count) >= 0)
            {
                for (uint i = 0; i < count && i < 16; i++)
                {
                    if (com.GetMonitorDevicePathAt(i, out var id) < 0 || id.Length == 0) continue;
                    // A monitor that was connected once but is not now has no rectangle: skip it.
                    if (com.GetMonitorRect(id, out var rect) != 0 || rect.Width <= 0 || rect.Height <= 0) continue;
                    // Settings → "Solid color": no picture, even if Windows still remembers the last one.
                    var picture = type != 1 && com.GetWallpaper(id, out var path) >= 0 ? path : "";
                    monitors.Add(new WallpaperMonitor(id, rect, picture));
                }
            }
            if (monitors.Count == 0)
            {
                detail = "IDesktopWallpaper listed no connected monitor";
                return ReadClassic(type, spotlight, policy, ref detail);
            }
            var fit = com.GetPosition(out var position) >= 0 && position is >= 0 and <= 5 ? (WallpaperFit)position : WallpaperFit.Fill;
            var color = com.GetBackgroundColor(out var c) >= 0 ? c : GetSysColor(ColorDesktop);
            // BackgroundType is what Settings shows. Without it: "slideshows enabled" + "a slideshow is configured"
            // (DSS_SLIDESHOW alone may stay set after switching back to a picture).
            var slideshow = type >= 0
                ? type == 2
                : com.GetStatus(out var status) >= 0 && (status & DesktopWallpaperCom.DssRunning) == DesktopWallpaperCom.DssRunning;
            List<string> items = [];
            uint options = 0, tick = 0;
            if (slideshow)
            {
                if (com.GetSlideshow(out var array) >= 0 && array != 0)
                {
                    try { items = DesktopWallpaperCom.ItemPaths(array); }
                    finally { DesktopWallpaperCom.Release(array); }
                }
                if (com.GetSlideshowOptions(out var o, out var t) >= 0) { options = o; tick = t; }
            }
            return new WallpaperState
            {
                Monitors = monitors, Fit = fit, BackgroundColor = color, PerMonitor = true,
                Slideshow = slideshow, SlideshowItems = items, SlideshowOptions = options, SlideshowTick = tick,
                Spotlight = spotlight, PolicyLocked = policy
            };
        }
        catch (Exception ex)
        {
            detail = "IDesktopWallpaper read failed: " + ex.Message;
            return null;
        }
        finally { DesktopWallpaperCom.Release(p); }
    }

    public bool SetPicture(string? monitorId, string path, out string? detail)
    {
        var p = Create(out detail);
        if (p != 0)
        {
            try
            {
                var hr = new DesktopWallpaperCom(p).SetWallpaper(monitorId, path);
                if (hr >= 0) { detail = null; return true; }
                detail = $"IDesktopWallpaper.SetWallpaper 0x{hr:X8}";
            }
            catch (Exception ex) { detail = "IDesktopWallpaper.SetWallpaper: " + ex.Message; }
            finally { DesktopWallpaperCom.Release(p); }
            if (monitorId != null) return false; // one monitor only works through IDesktopWallpaper
        }
        if (SystemParametersInfo(SpiSetDeskWallpaper, 0, path, SpifUpdateIniFile | SpifSendChange)) { detail = null; return true; }
        detail = (detail == null ? "" : detail + "; ") + "SystemParametersInfo error " + Marshal.GetLastWin32Error();
        return false;
    }

    public bool ClearPicture(out string? detail)
    {
        detail = null;
        if (SystemParametersInfo(SpiSetDeskWallpaper, 0, "", SpifUpdateIniFile | SpifSendChange)) return true;
        detail = "SystemParametersInfo (no picture) error " + Marshal.GetLastWin32Error();
        return false;
    }

    public bool SetSlideshow(IReadOnlyList<string> items, uint options, uint tick, out string? detail)
    {
        var p = Create(out detail);
        if (p == 0) return false;
        nint array = 0;
        try
        {
            array = CreateItemArray(items, out detail);
            if (array == 0) return false;
            var com = new DesktopWallpaperCom(p);
            var hr = com.SetSlideshow(array);
            if (hr < 0) { detail = $"IDesktopWallpaper.SetSlideshow 0x{hr:X8}"; return false; }
            com.SetSlideshowOptions(options, tick); // shuffle and interval; the slideshow runs without them too
            detail = null;
            return true;
        }
        catch (Exception ex)
        {
            detail = "IDesktopWallpaper.SetSlideshow: " + ex.Message;
            return false;
        }
        finally
        {
            if (array != 0) DesktopWallpaperCom.Release(array);
            DesktopWallpaperCom.Release(p);
        }
    }

    private static nint Create(out string? detail)
    {
        detail = null;
        var clsid = ClsidDesktopWallpaper;
        var iid = IidDesktopWallpaper;
        var hr = CoCreateInstance(ref clsid, 0, ClsctxAll, ref iid, out var p);
        if (hr == CoENotInitialized)
        {
            CoInitializeEx(0, CoinitApartmentThreaded);
            hr = CoCreateInstance(ref clsid, 0, ClsctxAll, ref iid, out p);
        }
        if (hr >= 0 && p != 0) return p;
        detail = $"IDesktopWallpaper unavailable (0x{hr:X8})";
        return 0;
    }

    private static WallpaperState ReadClassic(int type, bool spotlight, bool policy, ref string? detail)
    {
        var buffer = stackalloc char[1024];
        var path = type != 1 && SystemParametersInfoGet(SpiGetDeskWallpaper, 1024, buffer, 0) ? new string(buffer) : "";
        return new WallpaperState
        {
            Monitors = [new WallpaperMonitor("", default, path)],
            Fit = ClassicFit(), BackgroundColor = GetSysColor(ColorDesktop), PerMonitor = false,
            Slideshow = type == 2, Spotlight = spotlight, PolicyLocked = policy
        };
    }

    private static nint CreateItemArray(IReadOnlyList<string> items, out string? detail)
    {
        detail = null;
        if (items.Count == 0) { detail = "no slideshow items"; return 0; }
        if (items.Count == 1)
        {
            var iidItem = IidShellItem;
            var hr = SHCreateItemFromParsingName(items[0], 0, ref iidItem, out var item);
            if (hr < 0 || item == 0) { detail = $"SHCreateItemFromParsingName 0x{hr:X8}"; return 0; }
            try
            {
                var iidArray = IidShellItemArray;
                hr = SHCreateShellItemArrayFromShellItem(item, ref iidArray, out var array);
                if (hr >= 0 && array != 0) return array;
                detail = $"SHCreateShellItemArrayFromShellItem 0x{hr:X8}";
                return 0;
            }
            finally { DesktopWallpaperCom.Release(item); }
        }
        var pidls = new nint[items.Count];
        var n = 0;
        try
        {
            foreach (var path in items)
                if (SHParseDisplayName(path, 0, out var pidl, 0, out _) >= 0 && pidl != 0) pidls[n++] = pidl;
            if (n == 0) { detail = "none of the slideshow items exists any more"; return 0; }
            fixed (nint* list = pidls)
            {
                var hr = SHCreateShellItemArrayFromIDLists((uint)n, list, out var array);
                if (hr >= 0 && array != 0) return array;
                detail = $"SHCreateShellItemArrayFromIDLists 0x{hr:X8}";
                return 0;
            }
        }
        finally
        {
            for (var i = 0; i < n; i++) Marshal.FreeCoTaskMem(pidls[i]); // ILFree
        }
    }

    private static int BackgroundType()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Explorer\Wallpapers");
            return key?.GetValue("BackgroundType") is int v ? v : -1;
        }
        catch { return -1; }
    }

    private static bool SpotlightEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\DesktopSpotlight\Settings");
            return key?.GetValue("EnabledState") is int v && v == 1;
        }
        catch { return false; }
    }

    /// <summary>The organization fixed the wallpaper (Group Policy "Desktop Wallpaper" / "Prevent changing desktop background").</summary>
    private static bool PolicyLocked()
    {
        foreach (var root in new[] { Registry.CurrentUser, Registry.LocalMachine })
        {
            try
            {
                using (var system = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\System"))
                    if (system?.GetValue("Wallpaper") is string s && s.Trim().Length > 0) return true;
                using (var active = root.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Policies\ActiveDesktop"))
                    if (active?.GetValue("NoChangingWallPaper") is int v && v != 0) return true;
            }
            catch { /* unreadable policy key: not locked */ }
        }
        return false;
    }

    private static WallpaperFit ClassicFit()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Control Panel\Desktop");
            var style = (key?.GetValue("WallpaperStyle") as string)?.Trim();
            var tile = (key?.GetValue("TileWallpaper") as string)?.Trim();
            return style switch
            {
                "2" => WallpaperFit.Stretch,
                "6" => WallpaperFit.Fit,
                "10" => WallpaperFit.Fill,
                "22" => WallpaperFit.Span,
                _ => tile == "1" ? WallpaperFit.Tile : WallpaperFit.Center
            };
        }
        catch { return WallpaperFit.Fill; }
    }

    [DllImport("ole32.dll")] private static extern int CoCreateInstance(ref Guid clsid, nint outer, uint context, ref Guid iid, out nint ppv);
    [DllImport("ole32.dll")] private static extern int CoInitializeEx(nint reserved, uint coinit);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHCreateItemFromParsingName(string path, nint bindContext, ref Guid iid, out nint item);
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromShellItem(nint item, ref Guid iid, out nint array);
    [DllImport("shell32.dll")] private static extern int SHCreateShellItemArrayFromIDLists(uint count, nint* pidls, out nint array);
    [DllImport("shell32.dll", CharSet = CharSet.Unicode)] private static extern int SHParseDisplayName(string name, nint bindContext, out nint pidl, uint attributesIn, out uint attributesOut);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfo(uint action, uint param, string value, uint winIni);
    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true, EntryPoint = "SystemParametersInfoW")]
    private static extern bool SystemParametersInfoGet(uint action, uint size, char* buffer, uint winIni);
    [DllImport("user32.dll")] private static extern uint GetSysColor(int index);
}

/// <summary>
/// IDesktopWallpaper through its vtable: plain function-pointer calls, no COM interop marshalling, so the trimmed exe
/// needs nothing extra and the tests run the same code against a fake object. Slot numbers follow shobjidl_core.h
/// (IUnknown takes 0–2). Strings that come back are CoTaskMem and freed here.
/// </summary>
public sealed unsafe class DesktopWallpaperCom(nint instance)
{
    public const int DssEnabled = 0x1, DssSlideshow = 0x2;
    /// <summary>DSS_ENABLED | DSS_SLIDESHOW: slideshows are on and one is configured.</summary>
    public const int DssRunning = DssEnabled | DssSlideshow;
    public const uint SigdnFileSysPath = 0x80058000;
    private const int SetWallpaperSlot = 3, GetWallpaperSlot = 4, GetMonitorDevicePathAtSlot = 5, GetMonitorDevicePathCountSlot = 6,
        GetMonitorRectSlot = 7, GetBackgroundColorSlot = 9, GetPositionSlot = 11, SetSlideshowSlot = 12, GetSlideshowSlot = 13,
        SetSlideshowOptionsSlot = 14, GetSlideshowOptionsSlot = 15, GetStatusSlot = 17;
    // IShellItemArray: 7 GetCount, 8 GetItemAt. IShellItem: 5 GetDisplayName.
    private const int ArrayGetCountSlot = 7, ArrayGetItemAtSlot = 8, ItemGetDisplayNameSlot = 5;

    [StructLayout(LayoutKind.Sequential)]
    public struct NativeRect { public int Left, Top, Right, Bottom; }

    private static void* Slot(nint obj, int index) => (*(void***)obj)[index];

    public int SetWallpaper(string? monitorId, string path)
    {
        fixed (char* m = monitorId)
        fixed (char* w = path)
            return ((delegate* unmanaged[Stdcall]<nint, char*, char*, int>)Slot(instance, SetWallpaperSlot))(instance, m, w);
    }

    public int GetWallpaper(string? monitorId, out string path)
    {
        char* result = null;
        int hr;
        fixed (char* m = monitorId)
            hr = ((delegate* unmanaged[Stdcall]<nint, char*, char**, int>)Slot(instance, GetWallpaperSlot))(instance, m, &result);
        path = TakeString(result);
        return hr;
    }

    public int GetMonitorDevicePathAt(uint index, out string id)
    {
        char* result = null;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)Slot(instance, GetMonitorDevicePathAtSlot))(instance, index, &result);
        id = TakeString(result);
        return hr;
    }

    public int GetMonitorDevicePathCount(out uint count)
    {
        uint n = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(instance, GetMonitorDevicePathCountSlot))(instance, &n);
        count = n;
        return hr;
    }

    public int GetMonitorRect(string monitorId, out PixelRect rect)
    {
        NativeRect r = default;
        int hr;
        fixed (char* m = monitorId)
            hr = ((delegate* unmanaged[Stdcall]<nint, char*, NativeRect*, int>)Slot(instance, GetMonitorRectSlot))(instance, m, &r);
        rect = new PixelRect(r.Left, r.Top, Math.Max(0, r.Right - r.Left), Math.Max(0, r.Bottom - r.Top));
        return hr;
    }

    public int GetBackgroundColor(out uint color)
    {
        uint c = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(instance, GetBackgroundColorSlot))(instance, &c);
        color = c;
        return hr;
    }

    public int GetPosition(out int position)
    {
        int p = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(instance, GetPositionSlot))(instance, &p);
        position = p;
        return hr;
    }

    public int GetStatus(out int state)
    {
        int s = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(instance, GetStatusSlot))(instance, &s);
        state = s;
        return hr;
    }

    /// <summary>IShellItemArray of the slideshow; the caller releases it.</summary>
    public int GetSlideshow(out nint items)
    {
        nint a = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, nint*, int>)Slot(instance, GetSlideshowSlot))(instance, &a);
        items = a;
        return hr;
    }

    public int SetSlideshow(nint items) =>
        ((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(instance, SetSlideshowSlot))(instance, items);

    public int GetSlideshowOptions(out uint options, out uint tick)
    {
        uint o = 0, t = 0;
        var hr = ((delegate* unmanaged[Stdcall]<nint, uint*, uint*, int>)Slot(instance, GetSlideshowOptionsSlot))(instance, &o, &t);
        options = o;
        tick = t;
        return hr;
    }

    public int SetSlideshowOptions(uint options, uint tick) =>
        ((delegate* unmanaged[Stdcall]<nint, uint, uint, int>)Slot(instance, SetSlideshowOptionsSlot))(instance, options, tick);

    public static uint Release(nint obj) =>
        obj == 0 ? 0 : ((delegate* unmanaged[Stdcall]<nint, uint>)Slot(obj, 2))(obj);

    /// <summary>File-system paths of an IShellItemArray (a slideshow folder, or its pictures).</summary>
    public static List<string> ItemPaths(nint array)
    {
        var list = new List<string>();
        uint n = 0;
        if (((delegate* unmanaged[Stdcall]<nint, uint*, int>)Slot(array, ArrayGetCountSlot))(array, &n) < 0) return list;
        for (uint i = 0; i < n && i < 1000; i++)
        {
            nint item = 0;
            if (((delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)Slot(array, ArrayGetItemAtSlot))(array, i, &item) < 0 || item == 0)
                continue;
            try
            {
                char* name = null;
                if (((delegate* unmanaged[Stdcall]<nint, uint, char**, int>)Slot(item, ItemGetDisplayNameSlot))(item, SigdnFileSysPath, &name) >= 0)
                {
                    var path = TakeString(name);
                    if (path.Length > 0) list.Add(path);
                }
            }
            finally { Release(item); }
        }
        return list;
    }

    private static string TakeString(char* s)
    {
        if (s == null) return "";
        try { return new string(s); }
        finally { Marshal.FreeCoTaskMem((nint)s); }
    }
}
