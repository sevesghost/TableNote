namespace TableNote;

/// <summary>
/// Best-effort, SILENT Windows toast. Never runs on the UI thread: the texts are built by the caller,
/// then the toolkit call (COM/AUMID registration on first use can take seconds) runs on the thread pool.
/// Returns false only when a toast is impossible here (non-Windows / nothing to show).
/// </summary>
public static class WindowsToast
{
    /// <summary>v2.4.2: a toast still running after this long is treated as stuck — toasts are then turned
    /// off for this session (logged once). The reminder window and the sound still work.</summary>
    public static TimeSpan StuckAfter { get; set; } = TimeSpan.FromSeconds(20);

    /// <summary>Test seam: replaces the toolkit call (and the Windows check).</summary>
    internal static Action<string[]>? ShowCoreOverride { get; set; }

    private static int _busy;          // 1 while a toast call runs on the thread pool (at most one at a time)
    private static long _startedAt;    // Environment.TickCount64 of that call
    private static volatile bool _disabled;

    public static bool IsDisabled => _disabled;

    internal static void ResetForTests() { _disabled = false; Interlocked.Exchange(ref _busy, 0); }

    public static bool TryShow(IReadOnlyList<DueTask> tasks)
    {
        if (ShowCoreOverride == null && !OperatingSystem.IsWindows() || tasks.Count == 0 || _disabled) return false;
        if (Volatile.Read(ref _busy) == 1)
        {
            // v2.4.2: one toast at a time. Before, every batch / popup refresh queued another thread-pool
            // call; when the toolkit hung (first-use COM / shell registration), they piled up behind it.
            if (Environment.TickCount64 - Interlocked.Read(ref _startedAt) > StuckAfter.TotalMilliseconds && !_disabled)
            {
                _disabled = true;
                AppLog.Write("toast", $"no answer after {StuckAfter.TotalSeconds:0} s — toasts are off until TableNote restarts (the reminder window still shows)");
            }
            else AppLog.Write("toast", "previous toast still running — skipped");
            return false;
        }
        string[] lines;
        try
        {
            lines = tasks.Count == 1
                ? new[] { Localizer.T("popup.title_one"), tasks[0].Title, tasks[0].WhenLabel(DateTime.Now) }
                : new[] { Localizer.T("popup.title_many", tasks.Count),
                          string.Join(", ", tasks.Take(3).Select(t => t.Title)) + (tasks.Count > 3 ? "…" : "") };
        }
        catch (Exception ex) { AppLog.Write("toast", "text failed: " + ex.Message); return false; }
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return false;
        Interlocked.Exchange(ref _startedAt, Environment.TickCount64);
        var core = ShowCoreOverride ?? ShowCore;
        try
        {
            _ = Task.Run(() =>
            {
                var sw = System.Diagnostics.Stopwatch.StartNew();
                try { core(lines); }
                catch (Exception ex) { AppLog.Write("toast", "failed: " + ex.GetType().Name + " " + ex.Message); }
                finally { Interlocked.Exchange(ref _busy, 0); }
                if (sw.ElapsedMilliseconds > 1000) AppLog.Write("toast", $"slow: {sw.ElapsedMilliseconds} ms (off the UI thread)");
            });
        }
        catch (Exception ex) { Interlocked.Exchange(ref _busy, 0); AppLog.Write("toast", "could not start: " + ex.Message); return false; }
        return true;
    }

    private static void ShowCore(string[] lines)
    {
#if WINDOWS10_0_17763_0_OR_GREATER
        // Windows TFM build: call the toolkit directly (unpackaged app; Compat registers the AUMID).
        var b = new Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder();
        foreach (var l in lines) b.AddText(l);
        // Silent toast: the popup already plays the system sound once (avoid a double sound; mute applies).
        b.AddAudio(new Microsoft.Toolkit.Uwp.Notifications.ToastAudio { Silent = true });
        b.Show();
#else
        var builderType = Type.GetType("Microsoft.Toolkit.Uwp.Notifications.ToastContentBuilder, Microsoft.Toolkit.Uwp.Notifications");
        if (builderType == null) return;
        var builder = Activator.CreateInstance(builderType);
        if (builder == null) return;
        var addText = builderType.GetMethod("AddText", [typeof(string)]);
        foreach (var l in lines) addText?.Invoke(builder, [l]);
        var audioType = Type.GetType("Microsoft.Toolkit.Uwp.Notifications.ToastAudio, Microsoft.Toolkit.Uwp.Notifications");
        if (audioType != null)
        {
            var audio = Activator.CreateInstance(audioType);
            audioType.GetProperty("Silent")?.SetValue(audio, true);
            builderType.GetMethod("AddAudio", [audioType])?.Invoke(builder, [audio]);
        }
        var show = builderType.GetMethod("Show", Type.EmptyTypes)
                   ?? builderType.GetMethods().FirstOrDefault(m => m.Name == "Show" && m.GetParameters().Length <= 1);
        if (show == null) return;
        if (show.GetParameters().Length == 0) show.Invoke(builder, null);
        else show.Invoke(builder, [null!]);
#endif
    }
}
