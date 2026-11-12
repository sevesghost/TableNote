using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Text;

namespace TableNote;

/// <summary>
/// Windows desktop (wallpaper-layer) attach, robust on Windows 10/11.
///
/// Attach: send Progman 0x052C (spawns the WorkerW behind the icons), EnumWindows to find the window holding
/// SHELLDLL_DefView, then try hosts in order (<see cref="DesktopHostLocator.Candidates"/>). For each host:
/// switch our HWND from WS_POPUP to WS_CHILD, SetParent, verify with GetAncestor(GA_PARENT) (GetParent returns
/// the OWNER for popups, which made 2.2.x always report failure), and move the window to the same screen spot
/// in the parent's client coordinates (physical pixels; the app is Per-Monitor-V2 DPI aware).
///
/// Fallback (<see cref="EnterBottomFallback"/>): no parent; WS_EX_TOOLWINDOW (no taskbar button), kept at
/// HWND_BOTTOM; <see cref="KeepBottomFallbackAlive"/> re-shows it with SWP_NOACTIVATE after Win+D hides/minimizes it.
///
/// Every step is written to tablenote.log ("desktop: …").
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class WindowsDesktopShell : IDesktopShell
{
    private static class Native
    {
        public delegate bool EnumWindowsProc(nint hwnd, nint lParam);

        public const int GwlStyle = -16;
        public const int GwlExStyle = -20;
        public const long WsChild = 0x40000000L;
        public const long WsPopup = 0x80000000L;
        public const long WsExToolWindow = 0x00000080L;
        public const long WsExAppWindow = 0x00040000L;
        public const uint GaParent = 1;
        public const uint SwpNoSize = 0x0001, SwpNoMove = 0x0002, SwpNoZOrder = 0x0004, SwpNoActivate = 0x0010,
            SwpFrameChanged = 0x0020, SwpShowWindow = 0x0040, SwpNoOwnerZOrder = 0x0200;
        public const int SwShowNoActivate = 4;
        public static readonly nint HwndBottom = 1;
        public static readonly nint HwndNotTopmost = -2;

        [StructLayout(LayoutKind.Sequential)]
        public struct Rect { public int Left, Top, Right, Bottom; }

        [StructLayout(LayoutKind.Sequential)]
        public struct Point { public int X, Y; }

        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindow(string? cls, string? name);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern nint FindWindowEx(nint parent, nint after, string? cls, string? name);
        [DllImport("user32.dll")] public static extern bool EnumWindows(EnumWindowsProc proc, nint lParam);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern int GetClassName(nint hwnd, StringBuilder buf, int max);
        [DllImport("user32.dll", SetLastError = true)] public static extern nint SetParent(nint child, nint parent);
        [DllImport("user32.dll")] public static extern nint GetAncestor(nint hwnd, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool SetWindowPos(nint hwnd, nint after, int x, int y, int cx, int cy, uint flags);
        [DllImport("user32.dll", SetLastError = true)] public static extern bool GetWindowRect(nint hwnd, out Rect rect);
        [DllImport("user32.dll")] public static extern int MapWindowPoints(nint from, nint to, ref Point pt, uint count);
        [DllImport("user32.dll")] public static extern bool IsWindow(nint hwnd);
        [DllImport("user32.dll")] public static extern bool IsWindowVisible(nint hwnd);
        [DllImport("user32.dll")] public static extern bool IsIconic(nint hwnd);
        [DllImport("user32.dll")] public static extern bool ShowWindow(nint hwnd, int cmd);
        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern nint SendMessageTimeout(nint hwnd, uint msg, nint wParam, nint lParam, uint flags, uint timeout, out nint result);
        [DllImport("user32.dll", CharSet = CharSet.Unicode)] public static extern uint RegisterWindowMessage(string s);
        [DllImport("user32.dll")] public static extern bool IsHungAppWindow(nint hwnd);
        public const uint SmtoAbortIfHung = 0x0002;
        [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW", SetLastError = true)] public static extern nint GetWindowLongPtr(nint hwnd, int index);
        [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW", SetLastError = true)] public static extern nint SetWindowLongPtr(nint hwnd, int index, nint value);
    }

    // Saved so Detach can restore the exact top-level window.
    private long? _savedStyle;
    private Native.Rect? _savedScreenRect;
    private nint _attachedHost;
    private long? _savedExStyleForFallback;

    public bool InBottomFallback { get; private set; }

    private static void Log(string msg) => AppLog.Write("desktop", msg);

    public DesktopAttachResult TryAttach(nint hwnd)
    {
        if (hwnd == 0)
        {
            Log("attach: no HWND (TryGetPlatformHandle returned null)");
            return new DesktopAttachResult(false, DesktopHostKind.None, "no window handle");
        }
        // v2.3.1: leave the bottom fallback only once a real host is known (below), not before every try.
        if (_attachedHost != 0 && Native.IsWindow(_attachedHost) && Native.GetAncestor(hwnd, Native.GaParent) == _attachedHost)
            return new DesktopAttachResult(true, DesktopHostKind.WorkerWBehindIcons, "already attached");

        // NO cross-process SendMessage on the UI thread (v2.2.1 sent 0x052C to Progman here with SMTO_NORMAL; while
        // Explorer was busy this froze the UI thread — main window AND reminder popup — see ReadMe "Not Responding").
        // The 0x052C nudge + window scan run on the thread pool (PrepareHostsAsync); here we only read the result.
        var prepared = _prepared;
        List<ShellNode> nodes;
        if (prepared != null && DateTime.UtcNow - prepared.Value.At < TimeSpan.FromSeconds(30) && prepared.Value.Nodes.All(n => (n.ClassName != "WorkerW" && n.ClassName != "Progman") || Native.IsWindow(n.Id)))
            nodes = prepared.Value.Nodes;
        else
        {
            _ = PrepareHostsAsync(); // refresh in the background; callers retry (retry timer / background watch)
            using (UiActivity.Scope("desktop: EnumWindows snapshot"))
                nodes = CaptureSnapshot(); // local, never blocks on another process
            if (prepared == null)
            {
                Log("attach: Progman not nudged yet (0x052C runs in the background) — will retry");
                return new DesktopAttachResult(false, DesktopHostKind.None, "desktop (Explorer) is still starting; will retry automatically");
            }
        }
        var candidates = DesktopHostLocator.Candidates(nodes);
        // v2.3.1: Windows 11 24H2+ — the WorkerW behind the icons is a CHILD of Progman (not top-level),
        // so EnumWindows never saw it and we fell back to IconHost/Progman (icons over the table) or the fallback.
        if (!candidates.Any(c => c.Kind == DesktopHostKind.WorkerWBehindIcons))
        {
            var progman = nodes.FirstOrDefault(n => n.ClassName == "Progman");
            if (progman != null)
            {
                var childWorker = Native.FindWindowEx(progman.Id, 0, "WorkerW", null);
                if (childWorker != 0)
                    candidates.Insert(0, new DesktopHostLocator.Choice(DesktopHostKind.WorkerWBehindIcons, childWorker));
            }
        }
        Log($"attach: step 3 snapshot {nodes.Count} top-level windows, icon host = " +
            (nodes.FirstOrDefault(n => n.HasDesktopIcons)?.ClassName ?? "none") +
            ", candidates = [" + string.Join(", ", candidates.Select(c => $"{c.Kind}:0x{c.Handle:X}")) + "]");
        if (candidates.Count == 0)
            return new DesktopAttachResult(false, DesktopHostKind.None, "no desktop host found");

        // v2.3.1: without a valid rect we would park (and later restore) the window as 1×1 at 0,0.
        if (!Native.GetWindowRect(hwnd, out var screen) || screen.Right - screen.Left <= 0 || screen.Bottom - screen.Top <= 0)
        {
            Log($"attach: GetWindowRect failed/empty (err {Marshal.GetLastWin32Error()}) — not attaching");
            return new DesktopAttachResult(false, DesktopHostKind.None, "could not read the window position");
        }
        if (InBottomFallback) ExitBottomFallback(hwnd);
        _savedScreenRect ??= screen;
        var oldStyle = (long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle);
        _savedStyle ??= oldStyle;

        foreach (var c in candidates)
        {
            // SetParent into a window of a hung Explorer thread would hang us too (input queues get attached).
            if (Native.IsHungAppWindow(c.Handle)) { Log($"attach: skip {c.Kind} 0x{c.Handle:X} — Explorer window not responding"); continue; }
            bool ok;
            using (UiActivity.Scope($"desktop: SetParent → {c.Kind}"))
                ok = TryAttachTo(hwnd, c, screen, oldStyle);
            if (ok) 
            {
                _attachedHost = c.Handle;
                Log($"attach: OK under {c.Kind} 0x{c.Handle:X}");
                return new DesktopAttachResult(true, c.Kind is DesktopHostKind.IconHost ? DesktopHostKind.WorkerWBehindIcons : c.Kind, c.Kind.ToString());
            }
        }
        // Undo style change if nothing worked.
        Native.SetWindowLongPtr(hwnd, Native.GwlStyle, (nint)oldStyle);
        Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpFrameChanged);
        Log("attach: all hosts failed");
        return new DesktopAttachResult(false, DesktopHostKind.None, "SetParent failed for every host");
    }

    private static bool TryAttachTo(nint hwnd, DesktopHostLocator.Choice c, Native.Rect screen, long oldStyle)
    {
        // WS_CHILD so the window really becomes a child (needed for correct GetAncestor/coordinates/redraw).
        var childStyle = (oldStyle & ~Native.WsPopup) | Native.WsChild;
        Native.SetWindowLongPtr(hwnd, Native.GwlStyle, (nint)childStyle);
        var prev = Native.SetParent(hwnd, c.Handle);
        var err = Marshal.GetLastWin32Error();
        var parent = Native.GetAncestor(hwnd, Native.GaParent);
        if (parent != c.Handle)
        {
            Log($"attach: step 4 SetParent({c.Kind} 0x{c.Handle:X}) failed: returned 0x{prev:X}, err {err}, GA_PARENT = 0x{parent:X}");
            Native.SetParent(hwnd, 0);
            Native.SetWindowLongPtr(hwnd, Native.GwlStyle, (nint)oldStyle);
            return false;
        }
        // Same on-screen spot, expressed in the parent's client coordinates (WorkerW may start at a negative
        // virtual-screen origin on multi-monitor setups).
        var pt = new Native.Point { X = screen.Left, Y = screen.Top };
        Native.MapWindowPoints(0, c.Handle, ref pt, 1);
        var w = Math.Max(1, screen.Right - screen.Left);
        var h = Math.Max(1, screen.Bottom - screen.Top);
        // Bottom of the host's children: under SHELLDLL_DefView when the host also holds the icons.
        if (!Native.SetWindowPos(hwnd, Native.HwndBottom, pt.X, pt.Y, w, h,
                Native.SwpNoActivate | Native.SwpShowWindow | Native.SwpFrameChanged))
            Log($"attach: step 5 SetWindowPos in parent failed (err {Marshal.GetLastWin32Error()}) — kept parent");
        return true;
    }

    public void Detach(nint hwnd)
    {
        if (hwnd == 0) return;
        if (InBottomFallback) ExitBottomFallback(hwnd);
        var parent = Native.GetAncestor(hwnd, Native.GaParent);
        var wasChild = _attachedHost != 0 || ((long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle) & Native.WsChild) != 0;
        if (!wasChild) { _savedStyle = null; _savedScreenRect = null; return; }

        Native.GetWindowRect(hwnd, out var nowScreen);
        Native.SetParent(hwnd, 0);
        var style = _savedStyle ?? (((long)Native.GetWindowLongPtr(hwnd, Native.GwlStyle) & ~Native.WsChild) | Native.WsPopup);
        Native.SetWindowLongPtr(hwnd, Native.GwlStyle, (nint)style);
        var r = _savedScreenRect ?? nowScreen;
        Native.SetWindowPos(hwnd, Native.HwndNotTopmost, r.Left, r.Top, Math.Max(1, r.Right - r.Left), Math.Max(1, r.Bottom - r.Top),
            Native.SwpNoActivate | Native.SwpFrameChanged);
        Log($"detach: from 0x{parent:X}");
        _attachedHost = 0;
        _savedStyle = null;
        _savedScreenRect = null;
    }

    /// <summary>Fallback that survives Win+D: tool window (no taskbar button) at HWND_BOTTOM, re-shown by a timer.</summary>
    public bool EnterBottomFallback(nint hwnd)
    {
        if (hwnd == 0) return false;
        var ex = (long)Native.GetWindowLongPtr(hwnd, Native.GwlExStyle);
        _savedExStyleForFallback ??= ex;
        Native.SetWindowLongPtr(hwnd, Native.GwlExStyle, (nint)((ex | Native.WsExToolWindow) & ~Native.WsExAppWindow));
        var ok = Native.SetWindowPos(hwnd, Native.HwndBottom, 0, 0, 0, 0,
            Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpShowWindow | Native.SwpFrameChanged);
        InBottomFallback = true;
        Log($"fallback: bottom tool window {(ok ? "on" : $"SetWindowPos failed (err {Marshal.GetLastWin32Error()})")}");
        return true;
    }

    /// <summary>Call ~every second while in fallback: undo Win+D (minimize/hide) and stay at the bottom.</summary>
    public void KeepBottomFallbackAlive(nint hwnd)
    {
        if (!InBottomFallback || hwnd == 0) return;
        if (Native.IsIconic(hwnd) || !Native.IsWindowVisible(hwnd))
        {
            Native.ShowWindow(hwnd, Native.SwShowNoActivate);
            Log("fallback: restored after show-desktop/minimize");
        }
        Native.SetWindowPos(hwnd, Native.HwndBottom, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoActivate | Native.SwpNoOwnerZOrder);
    }

    public void ExitBottomFallback(nint hwnd)
    {
        if (!InBottomFallback) return;
        InBottomFallback = false;
        if (_savedExStyleForFallback is long ex)
        {
            // Keep WS_EX_TOOLWINDOW if it was there before (the app hides its taskbar button that way).
            Native.SetWindowLongPtr(hwnd, Native.GwlExStyle, (nint)ex);
            // v2.3.1: SWP_NOZORDER — HWND_NOTOPMOST here lifted the table above other windows (flash).
            Native.SetWindowPos(hwnd, 0, 0, 0, 0, 0, Native.SwpNoMove | Native.SwpNoSize | Native.SwpNoZOrder | Native.SwpNoActivate | Native.SwpFrameChanged);
        }
        _savedExStyleForFallback = null;
        Log("fallback: off");
    }

    /// <summary>True while our parent host still exists (Explorer restart destroys WorkerW).</summary>
    public Task PrepareAsync() => PrepareHostsAsync();

    public bool HostAlive() => _attachedHost != 0 && Native.IsWindow(_attachedHost);

    private static volatile StrongBox<(DateTime At, List<ShellNode> Nodes)>? _preparedBox;
    private static (DateTime At, List<ShellNode> Nodes)? _prepared => _preparedBox?.Value;
    private static int _preparing;

    /// <summary>
    /// Thread-pool only: FindWindow(Progman) → SendMessageTimeout(0x052C, SMTO_ABORTIFHUNG, 1000 ms) → EnumWindows.
    /// One run at a time; result cached for <see cref="TryAttach"/>.
    /// </summary>
    public static Task PrepareHostsAsync()
    {
        if (Interlocked.Exchange(ref _preparing, 1) == 1) return Task.CompletedTask;
        return Task.Run(() =>
        {
            try
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                var progman = Native.FindWindow("Progman", null);
                if (progman == 0) { Log("prepare: step 1 FindWindow(Progman) = 0 — Explorer not running/ready"); return; }
                if (Native.IsHungAppWindow(progman)) { Log("prepare: Progman is not responding — skip 0x052C"); }
                else
                {
                    // 0x052C: ask Progman to spawn a WorkerW between the wallpaper and the icons. Two variants are used in the wild.
                    var r1 = Native.SendMessageTimeout(progman, 0x052C, 0xD, 0x1, Native.SmtoAbortIfHung, 1000, out _);
                    var r2 = r1 != 0 ? Native.SendMessageTimeout(progman, 0x052C, 0, 0, Native.SmtoAbortIfHung, 1000, out _) : 0;
                    if (r1 == 0) Log($"prepare: step 2 SendMessageTimeout(Progman, 0x052C) timed out/aborted (err {Marshal.GetLastWin32Error()}, continuing)");
                }
                var nodes = CaptureSnapshot();
                _preparedBox = new StrongBox<(DateTime, List<ShellNode>)>((DateTime.UtcNow, nodes));
                Log($"prepare: done in {sw.ElapsedMilliseconds} ms, {nodes.Count} windows");
            }
            catch (Exception ex) { Log("prepare: failed " + ex.Message); }
            finally { Interlocked.Exchange(ref _preparing, 0); }
        });
    }

    public static List<ShellNode> CaptureSnapshot()
    {
        var nodes = new List<ShellNode>();
        Native.EnumWindows((hwnd, _) =>
        {
            var sb = new StringBuilder(128);
            Native.GetClassName(hwnd, sb, sb.Capacity);
            nodes.Add(new ShellNode
            {
                ClassName = sb.ToString(),
                Id = hwnd,
                HasDesktopIcons = Native.FindWindowEx(hwnd, 0, "SHELLDLL_DefView", null) != 0
            });
            return true;
        }, 0);
        return nodes;
    }

    public static uint TaskbarCreatedMessage() => Native.RegisterWindowMessage("TaskbarCreated");
}
