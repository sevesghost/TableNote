using Avalonia.Threading;

namespace TableNote;

/// <summary>
/// v2.4.2: last-chance exception logging to tablenote.log. Before, an exception that escaped a UI handler
/// (e.g. a reminder-window button) or a background task ended TableNote with nothing in the log.
/// - UI thread (Avalonia dispatcher): logged and marked handled, so the app keeps running.
/// - Unobserved task exceptions: logged and marked observed.
/// - Any other unhandled exception (non-UI thread): logged; .NET still ends the process.
/// </summary>
public static class CrashLog
{
    private static int _installed;
    private static int _uiCount;
    public const int MaxUiLogs = 50; // a handler that throws on every frame must not flood the log

    public static void Install()
    {
        if (Interlocked.Exchange(ref _installed, 1) == 1) return;
        AppDomain.CurrentDomain.UnhandledException += (_, e) => OnUnhandled(e.ExceptionObject, e.IsTerminating);
        TaskScheduler.UnobservedTaskException += (_, e) => { OnUnobserved(e.Exception); e.SetObserved(); };
        try
        {
            Dispatcher.UIThread.UnhandledException += (_, e) => e.Handled = OnUiException(e.Exception);
        }
        catch (Exception ex) { AppLog.Write("crash", "UI exception hook unavailable: " + ex.Message); }
    }

    public static void OnUnhandled(object? error, bool terminating) =>
        AppLog.Write("crash", (terminating ? "fatal (app ends): " : "unhandled: ") + Describe(error as Exception, error?.ToString()));

    public static void OnUnobserved(Exception? error) =>
        AppLog.Write("crash", "background task: " + Describe(error, null));

    /// <summary>Returns true = handled (keep running).</summary>
    public static bool OnUiException(Exception error)
    {
        var n = Interlocked.Increment(ref _uiCount);
        if (n <= MaxUiLogs)
            AppLog.Write("crash", $"UI (kept running, busy in: {UiActivity.Current}): " + Describe(error, null));
        return true;
    }

    private static string Describe(Exception? ex, string? fallback)
    {
        if (ex == null) return fallback ?? "(unknown)";
        var root = ex is AggregateException { InnerExceptions.Count: 1 } a ? a.InnerExceptions[0] : ex;
        var where = root.StackTrace?.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "";
        return $"{root.GetType().Name}: {root.Message} {where}".Trim();
    }
}
