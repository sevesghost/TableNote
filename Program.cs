using Avalonia;
using Avalonia.Logging;

namespace TableNote;

internal static class Program
{
    private static SingleInstance? _instance;
    private static EditorSignal? _signal;

    public static bool ForceEdit { get; private set; }
    /// <summary>v2.3.1: live state of the "open again" pipe (was a value fixed at start-up).</summary>
    public static bool SignalReady => _signalStarted && (_signal?.IsListening ?? false);
    private static bool _signalStarted;
    /// <summary>Started by the login Run entry (--startup): stay in the tray, keep reminders running.</summary>
    public static bool StartHidden { get; private set; }
    public static event Action? EditRequested;

    [STAThread]
    public static void Main(string[] args)
    {
        DueDates.UseGregorianCalendar();
        ForceEdit = args.Any(a => string.Equals(a, "--edit", StringComparison.OrdinalIgnoreCase));
        StartHidden = !ForceEdit && args.Any(a => string.Equals(a, "--startup", StringComparison.OrdinalIgnoreCase));
        _instance = SingleInstance.TryAcquire(SingleInstance.DefaultLockPath());
        if (_instance == null)
        {
            if (!EditorSignal.TrySend())
                AppLog.Write("signal", "second start: could not reach the running TableNote (pipe not listening)");
            return;
        }

        CrashLog.Install(); // v2.4.2: unhandled exceptions go to tablenote.log (UI-thread ones no longer end the app)
        _signal = new EditorSignal(EditorSignal.DefaultPipeName);
        _signalStarted = _signal.TryStart(
            () => EditRequested?.Invoke(),
            () => EditRequested?.Invoke());

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        _signal.Dispose();
        GC.KeepAlive(_instance);
    }

    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace(LogEventLevel.Warning);
}
