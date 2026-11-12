using System.Runtime.InteropServices;

namespace TableNote;

/// <summary>Reminder sound. Always fire-and-forget on the thread pool — never blocks the UI thread.
/// v2.4.2: at most one sound call at a time and one per <see cref="MinGap"/> — several reminders arriving
/// in the same moment (e.g. 5 rows due in the same minute) give ONE sound, not a burst of parallel calls.</summary>
public static class NotificationSound
{
    public static bool Muted { get; set; }
    public static TimeSpan MinGap { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Test seam: replaces the system sound call.</summary>
    internal static Action? PlayOverride { get; set; }

    private static int _busy;
    private static long _lastAt = long.MinValue / 2;

    internal static void ResetForTests() { Interlocked.Exchange(ref _busy, 0); Interlocked.Exchange(ref _lastAt, long.MinValue / 2); }

    public static void Play()
    {
        if (Muted) return;
        var now = Environment.TickCount64;
        if (now - Interlocked.Read(ref _lastAt) < MinGap.TotalMilliseconds) return;
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return; // previous call still running (blocked) → skip
        Interlocked.Exchange(ref _lastAt, now);
        var core = PlayOverride;
        try
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (core != null) { core(); return; }
                    if (OperatingSystem.IsWindows()) { PlayWindows(); return; }
                    Console.Write("\a");
                }
                catch (Exception ex) { AppLog.Write("sound", "failed: " + ex.Message); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }
        catch (Exception ex) { Interlocked.Exchange(ref _busy, 0); AppLog.Write("sound", "could not start: " + ex.Message); }
    }

    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static void PlayWindows() => _ = MessageBeep(0x00000030); // asynchronous system sound

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint uType);
}
