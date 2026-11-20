using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace TableNote;

/// <summary>
/// 2.6.0: starting TableNote while a copy from ANOTHER file is running (an older version still in the tray, say) used to
/// bring that running copy forward and quietly exit, so the new version seemed to start but the old one came up.
/// Now the new copy asks whether to close the running one and start itself instead. Both use the same note.json and
/// settings. A second start of the same file still just brings the running editor forward.
/// </summary>
public static class OtherInstance
{
    public sealed record Running(int ProcessId, string Path, string Version);

    /// <summary>The running copy to offer closing: one started from another file than <paramref name="myPath"/>.</summary>
    public static Running? Pick(string? myPath, IEnumerable<Running> running) =>
        string.IsNullOrEmpty(myPath) ? null : running.FirstOrDefault(r => !SamePath(r.Path, myPath));

    public static bool SamePath(string a, string b)
    {
        try { return string.Equals(System.IO.Path.GetFullPath(a), System.IO.Path.GetFullPath(b), StringComparison.OrdinalIgnoreCase); }
        catch { return false; }
    }

    public static string Question(Running other) =>
        Localizer.T("instance.other_running", other.Version, System.IO.Path.GetDirectoryName(other.Path) ?? other.Path, AppInfo.Version);

    /// <summary>Other TableNote processes of this user (by process name), with their file and version.</summary>
    [SupportedOSPlatform("windows")]
    public static List<Running> Find()
    {
        var list = new List<Running>();
        using var me = Process.GetCurrentProcess();
        foreach (var p in Process.GetProcessesByName(me.ProcessName))
        {
            using (p)
            {
                if (p.Id == me.Id) continue;
                string? path = null;
                try { path = p.MainModule?.FileName; }
                catch { /* another user's or an elevated process: not ours to close */ }
                if (string.IsNullOrEmpty(path)) continue;
                var version = "?";
                try { version = FileVersionInfo.GetVersionInfo(path).ProductVersion ?? "?"; }
                catch { /* keep "?" */ }
                list.Add(new Running(p.Id, path, version));
            }
        }
        return list;
    }

    /// <summary>Yes/No box in front of everything (there is no window yet).</summary>
    [SupportedOSPlatform("windows")]
    public static bool Ask(Running other) =>
        MessageBoxW(0, Question(other), Localizer.T("app.title"), MbYesNo | MbIconQuestion | MbTopmost | MbSetForeground) == IdYes;

    /// <summary>Ends the other copy and waits for it. Its last edits were saved by its autosave (1.5 s after typing).</summary>
    [SupportedOSPlatform("windows")]
    public static bool Stop(Running other, TimeSpan wait)
    {
        try
        {
            using var p = Process.GetProcessById(other.ProcessId);
            p.Kill();
            return p.WaitForExit(wait);
        }
        catch (ArgumentException) { return true; } // already gone
        catch (Exception ex)
        {
            AppLog.Write("start", "could not close the other TableNote: " + ex.GetType().Name + ": " + ex.Message);
            return false;
        }
    }

    private const uint MbYesNo = 0x4, MbIconQuestion = 0x20, MbTopmost = 0x40000, MbSetForeground = 0x10000;
    private const int IdYes = 6;

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int MessageBoxW(nint hwnd, string text, string caption, uint type);
}
