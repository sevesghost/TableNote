using System.Diagnostics;
using System.Runtime.InteropServices;
using Avalonia.Threading;

namespace TableNote;

/// <summary>2.6.0: start-up and drawing records in tablenote.log. A window that is on screen but never drawn (it looks
/// see-through and ignores clicks) did not show up in the UI-thread watchdog, because the program itself kept running;
/// these lines say whether the editor was drawn and how long the start took.</summary>
public partial class MainWindow
{
    private static readonly Stopwatch SinceStart = Stopwatch.StartNew();
    private int _drawWatch;

    /// <summary>One line per start: version, how it was started, the saved mode, the system.</summary>
    private void LogStart()
    {
        var how = Program.StartHidden ? "sign-in start (tray)" : Program.ForceEdit ? "start --edit" : "start";
        AppLog.Write("start", $"TableNote {AppInfo.Version}: {how}, saved mode {_session.Doc.View}, " +
                              $"{RuntimeInformation.OSDescription}, {_session.Doc.Rows.Count} rows, window after {SinceStart.ElapsedMilliseconds} ms");
    }

    /// <summary>Notes the first frame (start-up) and logs when the window is shown but nothing is drawn within 5 s.</summary>
    private void WatchDrawing(string what, bool logFirstFrame)
    {
        var id = ++_drawWatch;
        var clock = Stopwatch.StartNew();
        var drawn = false;
        try
        {
            RequestAnimationFrame(_ =>
            {
                drawn = true;
                if (logFirstFrame) AppLog.Write("start", $"{what} drawn {clock.ElapsedMilliseconds} ms after it was shown");
            });
        }
        catch (Exception ex)
        {
            AppLog.Write("render", $"{what}: no frame callback ({ex.GetType().Name}: {ex.Message})");
            return;
        }
        DispatcherTimer.RunOnce(() =>
        {
            if (drawn || id != _drawWatch || !IsVisible) return;
            AppLog.Write("render", $"{what} is on screen but nothing was drawn for 5 s (view {_view}, activity {UiActivity.Current})");
        }, TimeSpan.FromSeconds(5));
    }
}
