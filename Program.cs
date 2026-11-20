using Avalonia;
using Avalonia.Logging;
using Avalonia.Media;

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
        if (_instance == null && OperatingSystem.IsWindows())
            _instance = TakeOverFromOtherCopy(); // 2.6.0: a copy from another file (an older version) is running
        if (_instance == null)
        {
            if (!EditorSignal.TrySend())
                AppLog.Write("signal", "second start: could not reach the running TableNote (pipe not listening)");
            return;
        }

        CrashLog.Install(); // v2.4.2: unhandled exceptions go to tablenote.log (UI-thread ones no longer end the app)
        // Old print files. Off the startup path so a large temp folder does not delay the window.
        Task.Run(static () => ViewPrintFiles.CleanupExpired());
        _signal = new EditorSignal(EditorSignal.DefaultPipeName);
        _signalStarted = _signal.TryStart(
            () => EditRequested?.Invoke(),
            () => EditRequested?.Invoke());

        BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        _signal.Dispose();
        GC.KeepAlive(_instance);
    }

    /// <summary>Another TableNote holds the lock. If it runs from another file (e.g. the old version is still in the tray),
    /// ask whether to close it and start this one; otherwise (or on No) it is just brought forward, as before.</summary>
    [System.Runtime.Versioning.SupportedOSPlatform("windows")]
    private static SingleInstance? TakeOverFromOtherCopy()
    {
        try
        {
            var other = OtherInstance.Pick(Environment.ProcessPath, OtherInstance.Find());
            if (other == null) return null;
            Localizer.SetLanguage(AppSettings.Load(NoteStore.DefaultPath()).Language);
            AppLog.Write("start", $"another TableNote is running: {other.Version} from {other.Path}");
            if (!OtherInstance.Ask(other)) return null;
            if (!OtherInstance.Stop(other, TimeSpan.FromSeconds(5))) return null;
            for (var i = 0; i < 20; i++)
            {
                if (SingleInstance.TryAcquire(SingleInstance.DefaultLockPath()) is { } taken)
                {
                    AppLog.Write("start", $"closed TableNote {other.Version}; starting {AppInfo.Version} from {Environment.ProcessPath}");
                    return taken;
                }
                Thread.Sleep(150);
            }
            AppLog.Write("start", "the other TableNote was closed but its lock did not come free");
        }
        catch (Exception ex) { AppLog.Write("start", "could not take over from the other TableNote: " + ex.GetType().Name + ": " + ex.Message); }
        return null;
    }

    // 2.6.0: the bundled font (Noto Sans Thai with Noto Sans Latin and symbols merged in, Assets/Fonts) is the
    // default for every control, so Thai, digits and Latin share one design. It replaces the Inter package (about 1 MB).
    public static AppBuilder BuildAvaloniaApp() =>
        AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .With(AppFonts.ManagerOptions)
            .LogToTrace(LogEventLevel.Warning);
}
