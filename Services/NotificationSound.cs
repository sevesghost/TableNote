using System.Runtime.InteropServices;
using System.Runtime.Versioning;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("SoundTests")]

namespace TableNote;

/// <summary>Reminder sound. Always fire-and-forget on the thread pool — never blocks the UI thread.
/// v2.4.2: at most one sound call at a time and one per <see cref="MinGap"/> — several reminders arriving
/// in the same moment (e.g. 5 rows due in the same minute) give ONE sound, not a burst of parallel calls.
/// 2.6.0: the sound and its volume come from settings.json (<see cref="ReminderSounds"/>); the .wav is scaled to the
/// volume in memory and played with PlaySound (no change to the Windows volume mixer).</summary>
public static class NotificationSound
{
    public static bool Muted { get; set; }
    public static TimeSpan MinGap { get; set; } = TimeSpan.FromSeconds(2);
    /// <summary>2.6.0: settings.json "reminderSound".</summary>
    public static string SoundId { get; set; } = ReminderSounds.Default;
    /// <summary>2.6.0: settings.json "reminderVolume", 0–100.</summary>
    public static int Volume { get; set; } = ReminderSounds.DefaultVolume;

    /// <summary>Test seam: replaces the system sound call.</summary>
    internal static Action? PlayOverride { get; set; }
    /// <summary>Test seam: replaces the "Test" button's sound (id, volume).</summary>
    internal static Action<string, int>? PreviewOverride { get; set; }

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
        var id = SoundId;
        var volume = Volume;
        try
        {
            _ = Task.Run(() =>
            {
                try
                {
                    if (core != null) { core(); return; }
                    if (OperatingSystem.IsWindows()) { PlayWindows(id, volume); return; }
                    Console.Write("\a");
                }
                catch (Exception ex) { AppLog.Write("sound", "failed: " + ex.Message); }
                finally { Interlocked.Exchange(ref _busy, 0); }
            });
        }
        catch (Exception ex) { Interlocked.Exchange(ref _busy, 0); AppLog.Write("sound", "could not start: " + ex.Message); }
    }

    /// <summary>The sound dialog's "Test": plays now, also when muted; a test still playing stops first.</summary>
    public static void Preview(string id, int volume)
    {
        if (PreviewOverride is { } fake) { fake(id, volume); return; }
        _ = Task.Run(() =>
        {
            try
            {
                if (!OperatingSystem.IsWindows()) { Console.Write("\a"); return; }
                Stop();
                PlayWindows(id, volume);
            }
            catch (Exception ex) { AppLog.Write("sound", "test failed: " + ex.Message); }
        });
    }

    [SupportedOSPlatform("windows")]
    private static void Stop() => PlaySoundFile(null, 0, 0);

    [SupportedOSPlatform("windows")]
    private static void PlayWindows(string id, int volume)
    {
        if (volume <= 0) return;
        var path = ReminderSounds.Resolve(id);
        if (path == null) { _ = MessageBeep(0x00000030); return; } // no file: the system warning sound at its own volume
        if (Prepared(path, volume) is { } bytes && PlaySound(bytes, 0, SndMemory | SndNoDefault)) return;
        if (!PlaySoundFile(path, 0, SndFilename | SndNoDefault)) _ = MessageBeep(0x00000030); // not scalable: as it is
    }

    private static readonly object CacheLock = new();
    private static (string Key, byte[] Bytes)? _cache;

    /// <summary>The file scaled to the volume (cached for the last file and volume); null if it can't be scaled.</summary>
    private static byte[]? Prepared(string path, int volume)
    {
        var info = new FileInfo(path);
        if (!info.Exists || info.Length > 32 * 1024 * 1024) return null;
        var key = path + "|" + info.LastWriteTimeUtc.Ticks + "|" + info.Length + "|" + volume;
        lock (CacheLock)
            if (_cache is { } c && c.Key == key) return c.Bytes;
        var bytes = ReminderSounds.Scaled(File.ReadAllBytes(path), volume);
        if (bytes != null) lock (CacheLock) _cache = (key, bytes);
        return bytes;
    }

    private const uint SndNoDefault = 0x0002, SndMemory = 0x0004, SndFilename = 0x00020000; // SND_SYNC = 0

    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool MessageBeep(uint uType);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW", CharSet = CharSet.Unicode)]
    private static extern bool PlaySoundFile(string? sound, nint module, uint flags);

    [DllImport("winmm.dll", EntryPoint = "PlaySoundW")]
    private static extern bool PlaySound(byte[] sound, nint module, uint flags);
}
