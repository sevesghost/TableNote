using System.Diagnostics;
using System.Globalization;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("StartTests")]

namespace TableNote;

/// <summary>
/// 2.6.1: how long each step of a start took, counted from the moment Windows started the process (so the time the
/// .exe needs before any TableNote code runs is included). One line in tablenote.log per start, e.g.
/// "start timeline: code 210 ms · splash 480 ms · table built 690 ms · editor drawn 930 ms".
/// </summary>
public static class StartClock
{
    private static readonly object Gate = new();
    private static readonly List<(string Step, long Ms)> Steps = [];
    private static DateTime? _processStart;
    private static bool _reported;

    /// <summary>Called first thing in Main: remembers when the process was started.</summary>
    public static void Begin()
    {
        try { _processStart = Process.GetCurrentProcess().StartTime; }
        catch { _processStart = null; }
        Mark("code");
    }

    /// <summary>Milliseconds since the process started (since <see cref="Begin"/> when the start time is unknown).</summary>
    public static long Now()
    {
        if (_processStart is { } start)
        {
            var ms = (long)(DateTime.Now - start).TotalMilliseconds;
            if (ms >= 0) return ms;
        }
        return (long)Fallback.Elapsed.TotalMilliseconds;
    }

    private static readonly Stopwatch Fallback = Stopwatch.StartNew();

    /// <summary>Records that <paramref name="step"/> is done now. Only the first mark of a step counts.</summary>
    public static void Mark(string step)
    {
        var ms = Now();
        lock (Gate)
        {
            if (Steps.Exists(s => s.Step == step)) return;
            Steps.Add((step, ms));
        }
    }

    /// <summary>Milliseconds at which <paramref name="step"/> was marked, or null.</summary>
    public static long? At(string step)
    {
        lock (Gate)
        {
            foreach (var s in Steps)
                if (s.Step == step) return s.Ms;
        }
        return null;
    }

    /// <summary>"code 210 ms · splash 480 ms · …" in the order the steps happened.</summary>
    public static string Timeline()
    {
        lock (Gate)
            return string.Join(" · ", Steps.Select(s => s.Step + " " + s.Ms.ToString("N0", CultureInfo.InvariantCulture) + " ms"));
    }

    /// <summary>Writes the timeline once, when the editor has drawn its first frame (later calls do nothing).</summary>
    public static void ReportOnce()
    {
        lock (Gate)
        {
            if (_reported) return;
            _reported = true;
        }
        AppLog.Write("start", "start timeline (ms after the process started): " + Timeline());
    }

    /// <summary>Tests: start counting again.</summary>
    internal static void Reset()
    {
        lock (Gate)
        {
            Steps.Clear();
            _reported = false;
        }
    }
}
