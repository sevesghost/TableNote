using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Avalonia;
using SkiaSharp;
using TableNote;

public delegate void Check(bool ok, string name, string detail = "");

/// <summary>A desktop with monitors and pictures, recording what TableNote asks Windows to do.</summary>
sealed class FakeDesktop : IWallpaperHost
{
    public List<WallpaperMonitor> Monitors = [];
    public WallpaperFit Fit = WallpaperFit.Fill;
    public uint Color = 0x00804020;
    public bool PerMonitor = true, Slideshow, Spotlight, Policy, FailSet, FailRead;
    public List<string> Items = [];
    public uint Options, Tick;
    public readonly List<string> Calls = [];
    public Action? BeforeSet;

    public bool Supported => true;

    public WallpaperState? Read(out string? detail)
    {
        detail = FailRead ? "fake read failure" : null;
        if (FailRead) return null;
        return new WallpaperState
        {
            Monitors = Monitors.ToList(), Fit = Fit, BackgroundColor = Color, PerMonitor = PerMonitor,
            Slideshow = Slideshow, SlideshowItems = Items.ToList(), SlideshowOptions = Options, SlideshowTick = Tick,
            Spotlight = Spotlight, PolicyLocked = Policy
        };
    }

    public bool SetPicture(string? monitorId, string path, out string? detail)
    {
        Calls.Add($"set {monitorId ?? "*"} {Path.GetFileName(path)}");
        detail = null;
        if (FailSet) { detail = "nope"; return false; }
        if (monitorId != null && Monitors.All(m => m.Id != monitorId)) { detail = "no such monitor"; return false; }
        BeforeSet?.Invoke();
        for (var i = 0; i < Monitors.Count; i++)
            if (monitorId == null || Monitors[i].Id == monitorId) Monitors[i] = Monitors[i] with { Picture = path };
        Slideshow = false; // Windows: a picture ends the slideshow / spotlight
        Spotlight = false;
        return true;
    }

    public bool ClearPicture(out string? detail)
    {
        Calls.Add("clear");
        detail = null;
        for (var i = 0; i < Monitors.Count; i++) Monitors[i] = Monitors[i] with { Picture = "" };
        return true;
    }

    public bool SetSlideshow(IReadOnlyList<string> items, uint options, uint tick, out string? detail)
    {
        Calls.Add($"slideshow {string.Join(",", items.Select(Path.GetFileName))} {options} {tick}");
        detail = null;
        Slideshow = true;
        return true;
    }
}

static class Pictures
{
    /// <summary>A real PNG or JPEG: left half red, right half blue.</summary>
    public static string Halves(string dir, string name, int width, int height)
    {
        using var bmp = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bmp))
        {
            using var red = new SKPaint { Color = new SKColor(220, 20, 20) };
            using var blue = new SKPaint { Color = new SKColor(20, 20, 220) };
            canvas.DrawRect(0, 0, width / 2f, height, red);
            canvas.DrawRect(width / 2f, 0, width - width / 2f, height, blue);
        }
        var path = Path.Combine(dir, name);
        var format = name.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ? SKEncodedImageFormat.Png : SKEncodedImageFormat.Jpeg;
        using var data = bmp.Encode(format, 95);
        File.WriteAllBytes(path, data.ToArray());
        return path;
    }

    public static SKBitmap Decode(byte[] bytes) => SKBitmap.Decode(bytes) ?? throw new InvalidOperationException("not a picture");

    public static bool Near(SKColor c, byte r, byte g, byte b, int tolerance = 40) =>
        Math.Abs(c.Red - r) <= tolerance && Math.Abs(c.Green - g) <= tolerance && Math.Abs(c.Blue - b) <= tolerance;

    public static bool IsRed(SKColor c) => Near(c, 220, 20, 20);
    public static bool IsBlue(SKColor c) => Near(c, 20, 20, 220);
}

/// <summary>
/// A fake IDesktopWallpaper (and IShellItemArray / IShellItem) built from unmanaged function pointers, laid out in
/// the documented vtable order. Every slot records its own name, so a call through the wrong slot shows up.
/// </summary>
static unsafe class FakeCom
{
    public static readonly List<string> Calls = [];
    public static string? LastMonitor, LastPath;
    public static nint Desk, Array, Item0, Item1;

    public static void Build()
    {
        Desk = Make(
        [
            (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release,
            (nint)(delegate* unmanaged[Stdcall]<nint, char*, char*, int>)&SetWallpaper,
            (nint)(delegate* unmanaged[Stdcall]<nint, char*, char**, int>)&GetWallpaper,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint, char**, int>)&GetMonitorDevicePathAt,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint*, int>)&GetMonitorDevicePathCount,
            (nint)(delegate* unmanaged[Stdcall]<nint, char*, DesktopWallpaperCom.NativeRect*, int>)&GetMonitorRect,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint, int>)&SetBackgroundColor,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint*, int>)&GetBackgroundColor,
            (nint)(delegate* unmanaged[Stdcall]<nint, int, int>)&SetPosition,
            (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetPosition,
            (nint)(delegate* unmanaged[Stdcall]<nint, nint, int>)&SetSlideshow,
            (nint)(delegate* unmanaged[Stdcall]<nint, nint*, int>)&GetSlideshow,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint, uint, int>)&SetSlideshowOptions,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint*, uint*, int>)&GetSlideshowOptions,
            (nint)(delegate* unmanaged[Stdcall]<nint, char*, int, int>)&AdvanceSlideshow,
            (nint)(delegate* unmanaged[Stdcall]<nint, int*, int>)&GetStatus,
            (nint)(delegate* unmanaged[Stdcall]<nint, int, int>)&Enable,
        ]);
        Array = Make(
        [
            (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ArrayBindToHandler,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ArrayGetPropertyStore,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ArrayGetPropertyDescriptionList,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ArrayGetAttributes,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint*, int>)&ArrayGetCount,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint, nint*, int>)&ArrayGetItemAt,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ArrayEnumItems,
        ]);
        nint[] item =
        [
            (nint)(delegate* unmanaged[Stdcall]<nint, Guid*, nint*, int>)&QueryInterface,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&AddRef,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint>)&Release,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ItemBindToHandler,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ItemGetParent,
            (nint)(delegate* unmanaged[Stdcall]<nint, uint, char**, int>)&ItemGetDisplayName,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ItemGetAttributes,
            (nint)(delegate* unmanaged[Stdcall]<nint, int>)&ItemCompare,
        ];
        Item0 = Make(item);
        Item1 = Make(item);
    }

    private static nint Make(nint[] slots)
    {
        var table = (nint*)NativeMemory.Alloc((nuint)(slots.Length * sizeof(nint)));
        for (var i = 0; i < slots.Length; i++) table[i] = slots[i];
        var obj = (nint*)NativeMemory.Alloc((nuint)sizeof(nint));
        *obj = (nint)table;
        return (nint)obj;
    }

    private static string Name(nint self) => self == Desk ? "desk" : self == Array ? "array" : self == Item0 ? "item0" : self == Item1 ? "item1" : "?";
    private static char* Str(string s) => (char*)Marshal.StringToCoTaskMemUni(s);

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int QueryInterface(nint self, Guid* iid, nint* ppv) { Calls.Add("QueryInterface"); *ppv = 0; return unchecked((int)0x80004002); }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint AddRef(nint self) { Calls.Add("AddRef:" + Name(self)); return 2; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static uint Release(nint self) { Calls.Add("Release:" + Name(self)); return 0; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int SetWallpaper(nint self, char* monitor, char* path)
    {
        Calls.Add("SetWallpaper");
        LastMonitor = monitor == null ? null : new string(monitor);
        LastPath = new string(path);
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetWallpaper(nint self, char* monitor, char** result)
    {
        Calls.Add("GetWallpaper");
        *result = Str(@"C:\pics\" + (monitor == null ? "all" : new string(monitor)) + ".jpg");
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetMonitorDevicePathAt(nint self, uint index, char** result) { Calls.Add("GetMonitorDevicePathAt"); *result = Str("MON-" + index); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetMonitorDevicePathCount(nint self, uint* count) { Calls.Add("GetMonitorDevicePathCount"); *count = 3; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetMonitorRect(nint self, char* monitor, DesktopWallpaperCom.NativeRect* rect)
    {
        Calls.Add("GetMonitorRECT");
        var id = new string(monitor);
        if (id == "MON-2") { *rect = default; return 1; } // S_FALSE: known, not connected
        var k = id == "MON-0" ? 0 : 1;
        rect->Left = k * 1920; rect->Top = 0; rect->Right = k * 1920 + 1920; rect->Bottom = 1080;
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int SetBackgroundColor(nint self, uint color) { Calls.Add("SetBackgroundColor"); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetBackgroundColor(nint self, uint* color) { Calls.Add("GetBackgroundColor"); *color = 0x00112233; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int SetPosition(nint self, int position) { Calls.Add("SetPosition"); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetPosition(nint self, int* position) { Calls.Add("GetPosition"); *position = 3; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int SetSlideshow(nint self, nint items) { Calls.Add("SetSlideshow:" + Name(items)); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetSlideshow(nint self, nint* items) { Calls.Add("GetSlideshow"); *items = Array; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int SetSlideshowOptions(nint self, uint options, uint tick) { Calls.Add($"SetSlideshowOptions:{options}:{tick}"); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetSlideshowOptions(nint self, uint* options, uint* tick) { Calls.Add("GetSlideshowOptions"); *options = 1; *tick = 900000; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int AdvanceSlideshow(nint self, char* monitor, int direction) { Calls.Add("AdvanceSlideshow"); return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int GetStatus(nint self, int* state) { Calls.Add("GetStatus"); *state = 3; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int Enable(nint self, int enable) { Calls.Add("Enable"); return 0; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ArrayBindToHandler(nint self) { Calls.Add("Array.BindToHandler"); return -1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ArrayGetPropertyStore(nint self) { Calls.Add("Array.GetPropertyStore"); return -1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ArrayGetPropertyDescriptionList(nint self) { Calls.Add("Array.GetPropertyDescriptionList"); return -1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ArrayGetAttributes(nint self) { Calls.Add("Array.GetAttributes"); return -1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ArrayGetCount(nint self, uint* count) { Calls.Add("Array.GetCount"); *count = 2; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ArrayGetItemAt(nint self, uint index, nint* item) { Calls.Add("Array.GetItemAt"); *item = index == 0 ? Item0 : Item1; return 0; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ArrayEnumItems(nint self) { Calls.Add("Array.EnumItems"); return -1; }

    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ItemBindToHandler(nint self) { Calls.Add("Item.BindToHandler"); return -1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ItemGetParent(nint self) { Calls.Add("Item.GetParent"); return -1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])]
    private static int ItemGetDisplayName(nint self, uint sigdn, char** name)
    {
        Calls.Add("Item.GetDisplayName:" + sigdn.ToString("X8"));
        *name = Str(self == Item0 ? @"C:\Slides" : @"D:\More pictures");
        return 0;
    }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ItemGetAttributes(nint self) { Calls.Add("Item.GetAttributes"); return -1; }
    [UnmanagedCallersOnly(CallConvs = [typeof(CallConvStdcall)])] private static int ItemCompare(nint self) { Calls.Add("Item.Compare"); return -1; }
}
