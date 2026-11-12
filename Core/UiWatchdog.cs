using System.Diagnostics;
using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// Breadcrumb of what the UI thread is doing right now (set around risky calls) so a stall report says WHERE.
/// </summary>
public static class UiActivity
{
    private static volatile string _current = "idle";
    public static string Current => _current;

    public static IDisposable Scope(string what)
    {
        var prev = _current;
        _current = what;
        return new Restore(prev);
    }

    private sealed class Restore(string prev) : IDisposable
    {
        public void Dispose() => _current = prev;
    }
}

/// <summary>
/// UI-thread watchdog: a background thread posts a heartbeat to the dispatcher every 250 ms. If a heartbeat
/// is not processed within <see cref="StallThreshold"/>, the stall is written to tablenote.log once (with the
/// current <see cref="UiActivity"/>), and again with the total duration when the UI thread recovers.
/// </summary>
public static class UiWatchdog
{
    public static TimeSpan StallThreshold { get; set; } = TimeSpan.FromSeconds(2);
    public static TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(1);
    public static int StallsLogged => _stalls;

    private static Thread? _thread;
    private static volatile bool _stop;
    private static int _generation; // v2.3.1: Stop()+Start() must not leave the old thread running too
    private static int _stalls;
    private static long _postedAt;
    private static long _lastWaitTicks;   // Stopwatch ticks of the outstanding heartbeat, 0 = none
    private static readonly Stopwatch Clock = Stopwatch.StartNew();

    public static void Start()
    {
        if (_thread != null) return;
        _stop = false;
        var gen = Interlocked.Increment(ref _generation);
        _thread = new Thread(() => Loop(gen)) { IsBackground = true, Name = "TableNote UI watchdog", Priority = ThreadPriority.AboveNormal };
        _thread.Start();
    }

    public static void Stop()
    {
        _stop = true;
        _thread = null;
    }

    private static void Loop(int gen)
    {
        var reported = false;
        string? stalledIn = null;
        while (!_stop && gen == Volatile.Read(ref _generation))
        {
            // v2.3.1: 1 s instead of 250 ms — the 2 s stall threshold is still detected, but the UI thread is woken
            // 1×/s instead of 4×/s all day long (also in the tray / background mode).
            Thread.Sleep(HeartbeatInterval);
            var posted = Interlocked.Read(ref _postedAt);
            if (posted == 0)
            {
                if (reported)
                {
                    var s = Interlocked.Read(ref _lastWaitTicks) / (double)Stopwatch.Frequency;
                    AppLog.Write("watchdog", $"UI thread responsive again after {s:0.0} s (was busy in: {stalledIn})");
                    reported = false;
                }
                var stamp = Clock.ElapsedTicks;
                Interlocked.Exchange(ref _postedAt, stamp);
                try
                {
                    Dispatcher.UIThread.Post(() =>
                        Interlocked.Exchange(ref _lastWaitTicks, Clock.ElapsedTicks - Interlocked.Exchange(ref _postedAt, 0)),
                        DispatcherPriority.Send);
                }
                catch { Interlocked.Exchange(ref _postedAt, 0); }
                continue;
            }
            var age = TimeSpan.FromTicks((Clock.ElapsedTicks - posted) * TimeSpan.TicksPerSecond / Stopwatch.Frequency);
            if (!reported && age > StallThreshold)
            {
                reported = true;
                stalledIn = UiActivity.Current;
                Interlocked.Increment(ref _stalls);
                AppLog.Write("watchdog", $"UI thread not responding for {age.TotalSeconds:0.0} s — busy in: {stalledIn}");
            }
        }
    }
}
