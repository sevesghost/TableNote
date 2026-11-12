using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("NotifyTests")]

namespace TableNote;

public enum PopupCloseKind { None, Acknowledged, Snoozed, Done }

/// <summary>
/// ONE reminder window for the whole app. A new batch while it is open adds rows to the same window
/// (v2.2.1 opened a new window per batch). Each row: snooze / done / acknowledge; X snoozes what is left.
/// ShowActivated=false — never steals focus. Timers never re-enter and stop when the window closes.
/// </summary>
public static class ReminderPopup
{
    private sealed class State
    {
        public required Window Window;
        public required StackPanel List;
        public required Button CloseButton;
        public readonly Dictionary<string, DueTask> Pending = new(StringComparer.Ordinal);
        public readonly Dictionary<string, (Border Block, TextBlock Label)> Rows = new(StringComparer.Ordinal);
        public readonly List<Action> Closed = [];
        public PopupCloseKind Result = PopupCloseKind.None;
        public double Zoom = 1.0;
        public bool IsClosed;
        public Action<DueTask> Snooze = _ => { };
        public Action<DueTask> Done = _ => { };
        public Action<DueTask> Ack = _ => { };
        public Action<IReadOnlyList<DueTask>> SnoozeRemainingOnX = _ => { };
        public Func<DueTask, bool>? StillPending;
        public Action<string> Released = _ => { };
        public int SizedForCount = -1;
        public bool Discarded;
        public Action StopTimers = () => { }; // v2.4.2: could not be shown → release rows, never snooze/ack them
    }

    private static State? _current;

    /// <summary>UI zoom (settings.json uiScale / 100) applied to newly opened popups (Z8/Z9).</summary>
    public static double Zoom { get; set; } = 1.0;

    /// <summary>The open reminder window, if any (tests / diagnostics).</summary>
    public static Window? CurrentWindow => _current is { IsClosed: false } s ? s.Window : null;
    public static int OpenRowCount => _current is { IsClosed: false } s ? s.Pending.Count : 0;

    /// <summary>v2.4.2: the reminder window could not be created or shown (argument = number of tasks).
    /// MainWindow shows a status-bar message; the tasks are released and remind again on the next check.</summary>
    public static Action<int>? ShowFailed { get; set; }

    /// <summary>Test seam: work-area height (DIPs) used to size the window. Default reads the screen (may fail).</summary>
    internal static void ResetForTests() { _appClosing = false; _callbackDepth = 0; }

    internal static Func<Window, double?> WorkAreaHeight { get; set; } = DefaultWorkAreaHeight;

    /// <summary>v2.4.2: run one popup callback (save / snooze / done in MainWindow). An exception used to
    /// escape a button / Closed handler to the dispatcher and end the whole app; now it is logged and the
    /// remaining rows are still handled.</summary>
    private static void Safe(string what, Action action)
    {
        _callbackDepth++;
        try { action(); }
        catch (Exception ex) { AppLog.Write("popup", what + " failed: " + ex.GetType().Name + ": " + ex.Message); }
        finally { _callbackDepth--; }
    }

    private static int _callbackDepth;   // > 0 while a popup callback (Done → Rebuild → CheckNow …) runs
    private static bool _appClosing;     // CloseSilently() ran: drop deferred batches

    public static void ShowBatch(
        IReadOnlyList<DueTask> tasks,
        Action<DueTask> snoozeOne,
        Action<DueTask> doneOne,
        Action<DueTask> acknowledgeOne,
        Action<IReadOnlyList<DueTask>> snoozeRemainingOnX,
        Action closed,
        Func<DueTask, bool>? stillPending = null,
        Action<string>? released = null)
    {
        if (tasks.Count == 0 || _appClosing) return;
        if (_callbackDepth > 0)
        {
            // v2.4.2: re-entry from a popup button / close handler (e.g. Done → ApplyDone → CheckNow).
            // Don't change the window while its own handler is still running — add the batch right after it.
            Dispatcher.UIThread.Post(() =>
            {
                if (_appClosing) { foreach (var t in tasks) { var id = t.RowId; try { released?.Invoke(id); } catch { /* ignore */ } } return; }
                ShowBatch(tasks, snoozeOne, doneOne, acknowledgeOne, snoozeRemainingOnX, closed, stillPending, released);
            });
            return;
        }
        using var scope = UiActivity.Scope("reminder popup: show");
        // Thread pool, never block. Several batches in the same moment (e.g. 5 rows due in the same minute that
        // arrive through several checks) play ONE sound and run at most one toast at a time (v2.4.2).
        Safe("sound", NotificationSound.Play);
        Safe("toast", () => WindowsToast.TryShow(tasks));

        State? s = null;
        try
        {
            s = _current is { IsClosed: false } open ? open : Create();
            s.Snooze = snoozeOne; s.Done = doneOne; s.Ack = acknowledgeOne;
            s.SnoozeRemainingOnX = snoozeRemainingOnX; s.StillPending = stillPending;
            if (released != null) s.Released = released;
            s.Closed.Add(closed);
            foreach (var t in tasks) AddOrReplace(s, t);
            UpdateChrome(s);
            if (!s.Window.IsVisible)
            {
                var shown = s;
                Dispatcher.UIThread.Post(() => ShowWindow(shown));
            }
        }
        catch (Exception ex)
        {
            AppLog.Write("popup", $"show failed ({tasks.Count} tasks): {ex.GetType().Name}: {ex.Message}");
            if (s != null) Discard(s);
            // Rows that never reached the window (or a window that was never made) go back to the service.
            foreach (var t in tasks)
            {
                var id = t.RowId;
                Safe("release", () => (released ?? s?.Released)?.Invoke(id));
            }
            Safe("show-failed notice", () => ShowFailed?.Invoke(tasks.Count));
        }
    }

    private static void ShowWindow(State s)
    {
        if (s.IsClosed) return;
        try { s.Window.Show(); }
        catch (Exception ex)
        {
            AppLog.Write("popup", "window show failed: " + ex.GetType().Name + ": " + ex.Message);
            var n = s.Pending.Count;
            Discard(s);
            Safe("show-failed notice", () => ShowFailed?.Invoke(n));
        }
    }

    /// <summary>Drop a window that could not be shown: release its rows (they remind again), no snooze / ack.</summary>
    private static void Discard(State s)
    {
        s.Discarded = true;
        OnClosed(s);
        try { s.Window.Close(); } catch { /* never shown / already gone */ }
    }

    private static State Create()
    {
        var window = new Window
        {
            Width = 480,
            MinWidth = 360,
            MinHeight = 200,
            Topmost = true,
            CanResize = true,
            ShowInTaskbar = true,
            ShowActivated = false,
            WindowStartupLocation = WindowStartupLocation.CenterScreen,
            Background = new SolidColorBrush(Color.Parse("#FFF6C2")),
            FontFamily = AppFonts.Thai
        };
        var list = new StackPanel { Spacing = 10 };
        var closeBtn = new Button
        {
            HorizontalAlignment = HorizontalAlignment.Right,
            Margin = new Thickness(0, 8, 0, 0),
            FontFamily = AppFonts.Thai
        };
        var s = new State { Window = window, List = list, CloseButton = closeBtn };
        _current = s;

        closeBtn.Click += (_, _) =>
        {
            s.Result = PopupCloseKind.Acknowledged;
            foreach (var t in s.Pending.Values.ToList()) Safe("acknowledge", () => s.Ack(t));
            s.Pending.Clear();
            Safe("close", window.Close);
        };

        // Live labels: an advance entry left open past its due turns into the on-time reminder (one sound, silent toast).
        // Lateness is NOT recomputed here — it was decided when the reminder was created.
        var refresh = new DispatcherTimer { Interval = TimeSpan.FromSeconds(15) };
        refresh.Tick += (_, _) =>
        {
            refresh.Stop();
            try { Refresh(s); }
            catch (Exception ex) { AppLog.Write("popup", "refresh failed: " + ex.Message); }
            finally { if (!s.IsClosed) refresh.Start(); }
        };
        // Done / deleted / due edited elsewhere → drop the row within ~5 s.
        var watch = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        watch.Tick += (_, _) =>
        {
            watch.Stop();
            try { Watch(s); }
            catch (Exception ex) { AppLog.Write("popup", "watch failed: " + ex.Message); }
            finally { if (!s.IsClosed) watch.Start(); }
        };

        s.StopTimers = () => { watch.Stop(); refresh.Stop(); };
        window.Closed += (_, _) => OnClosed(s);

        s.Zoom = Zoom;
        window.Content = new ScrollViewer
        {
            Content = new LayoutTransformControl
            {
                LayoutTransform = Math.Abs(Zoom - 1) < 0.001 ? null : new ScaleTransform(Zoom, Zoom),
                Child = new StackPanel { Margin = new Thickness(16), Children = { list, closeBtn } }
            }
        };
        refresh.Start();
        watch.Start();
        return s;
    }

    private static void OnClosed(State s)
    {
        if (s.IsClosed) return; // Discard() and the Closed event may both get here
        s.IsClosed = true;
        Safe("stop timers", s.StopTimers);
        if (ReferenceEquals(_current, s)) _current = null;
        // Only X / system close leaves Result == None with pending rows.
        if (!s.Discarded && s.Result == PopupCloseKind.None && s.Pending.Count > 0)
        {
            var remaining = s.Pending.Values.ToList();
            Safe("snooze on X", () => s.SnoozeRemainingOnX(remaining));
        }
        foreach (var c in s.Closed.ToList()) Safe("closed", c);
        foreach (var id in s.Pending.Keys.ToList()) Safe("release", () => s.Released(id)); // v2.3.1: every row left
    }

    private static double? DefaultWorkAreaHeight(Window w)
    {
        var screens = w.Screens;
        if (screens == null) return null;
        var screen = screens.ScreenFromWindow(w) ?? screens.Primary;
        if (screen == null) return null;
        var scaling = screen.Scaling > 0 ? screen.Scaling : 1;
        return screen.WorkingArea.Height / scaling;
    }

    private static void UpdateChrome(State s)
    {
        var n = s.Pending.Count;
        s.Window.Title = n == 1 ? Localizer.T("popup.title_one") : Localizer.T("popup.title_many", n);
        s.CloseButton.Content = n > 1 ? Localizer.T("popup.close_all") : Localizer.T("popup.close_one");
        // v2.3.1: size only when the number of rows changed — the 5 s watch used to reset a size the
        // user had dragged, and re-measured the whole window every tick.
        if (s.SizedForCount == n) return;
        s.SizedForCount = n;
        // Height grows with rows and zoom, but never beyond the work area (Z9); the content scrolls.
        // v2.4.2: the window is not shown yet here; reading its screen can fail on some systems → default.
        double maxH = 700;
        try { if (WorkAreaHeight(s.Window) is { } h && h > 0 && !double.IsNaN(h)) maxH = h; }
        catch (Exception ex) { AppLog.Write("popup", "screen size unavailable: " + ex.GetType().Name + ": " + ex.Message); }
        s.Window.Height = Math.Min(Math.Clamp((160 + n * 88) * s.Zoom, 220, 560 * s.Zoom), Math.Max(220, maxH - 40));
        s.Window.Width = Math.Min(480 * s.Zoom, 960);
    }

    private static string LabelText(DueTask t, DateTime now) => (t.Offset > 0 ? "⏰ " : "🔔 ") + t.WhenLabel(now);
    private static IBrush LabelBrush(DueTask t) => new SolidColorBrush(Color.Parse(t.Offset > 0 ? "#1F5FA8" : "#B03A1A"));

    private static void AddOrReplace(State s, DueTask task)
    {
        if (s.Rows.TryGetValue(task.RowId, out var existing))
        {
            // Same task again (e.g. a later fire): update in place, no second row.
            s.Pending[task.RowId] = task;
            existing.Label.Text = LabelText(task, DateTime.Now);
            existing.Label.Foreground = LabelBrush(task);
            return;
        }
        s.Pending[task.RowId] = task;
        var rowId = task.RowId;
        var block = new Border
        {
            BorderBrush = new SolidColorBrush(Color.Parse("#E4D08A")),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10),
            Background = new SolidColorBrush(Color.Parse("#FFFDF6")),
            DataContext = rowId
        };
        var title = new TextBlock
        {
            Text = task.Title, FontWeight = FontWeight.Bold, FontSize = 15,
            TextWrapping = TextWrapping.Wrap, Foreground = new SolidColorBrush(Color.Parse("#3B3222"))
        };
        var due = new TextBlock
        {
            Text = LabelText(task, DateTime.Now), FontSize = 13, FontWeight = FontWeight.SemiBold,
            Foreground = LabelBrush(task), Margin = new Thickness(0, 2, 0, 2)
        };
        var detail = new TextBlock
        {
            Text = task.Detail, FontSize = 12, TextWrapping = TextWrapping.Wrap,
            IsVisible = !string.IsNullOrWhiteSpace(task.Detail)
        };
        var snoozeBtn = new Button { Content = Localizer.T("popup.snooze"), MinWidth = 110, Margin = new Thickness(0, 0, 8, 0), FontFamily = AppFonts.Thai };
        var doneBtn = new Button { Content = Localizer.T("popup.done"), MinWidth = 90, Margin = new Thickness(0, 0, 8, 0), FontFamily = AppFonts.Thai };
        var ackBtn = new Button { Content = Localizer.T("popup.ack"), MinWidth = 90, FontFamily = AppFonts.Thai };
        ToolTip.SetTip(ackBtn, Localizer.T("popup.ack_tip"));
        var rowBtns = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 6, 0, 0), Children = { snoozeBtn, doneBtn, ackBtn } };

        void Finish(PopupCloseKind kind, Func<Action<DueTask>> apply)
        {
            if (!s.Pending.TryGetValue(rowId, out var current)) return;
            Safe(kind.ToString(), () => apply()(current));
            Remove(s, rowId);
            if (s.Pending.Count == 0)
            {
                s.Result = kind; // all rows handled by buttons — not an X close
                Safe("close", s.Window.Close);
            }
            else UpdateChrome(s);
        }
        snoozeBtn.Click += (_, _) => Finish(PopupCloseKind.Snoozed, () => s.Snooze);
        doneBtn.Click += (_, _) => Finish(PopupCloseKind.Done, () => s.Done);
        ackBtn.Click += (_, _) => Finish(PopupCloseKind.Acknowledged, () => s.Ack);

        block.Child = new StackPanel { Children = { title, due, detail, rowBtns } };
        s.List.Children.Add(block);
        s.Rows[rowId] = (block, due);
    }

    private static void Remove(State s, string rowId)
    {
        s.Pending.Remove(rowId);
        if (s.Rows.Remove(rowId, out var r)) s.List.Children.Remove(r.Block);
        // v2.3.1: release THIS task to the reminder service right away. Before, a task snoozed / edited
        // while other tasks kept the popup open stayed "in popup" and its next reminder never fired until close.
        try { s.Released(rowId); } catch (Exception ex) { AppLog.Write("popup", "release failed: " + ex.Message); }
    }

    /// <summary>v2.3.1: app quits → close the popup without snoozing / acknowledging anything, so the
    /// open tasks remind again on the next start (no callbacks into a closed main window afterwards).</summary>
    public static void CloseSilently()
    {
        _appClosing = true;
        if (_current is not { IsClosed: false } s) return;
        s.Result = PopupCloseKind.Acknowledged; // not None → the X-close "snooze remaining" is skipped
        s.Snooze = _ => { }; s.Done = _ => { }; s.Ack = _ => { };
        try { s.Window.Close(); } catch { /* ignore */ }
    }

    private static void Refresh(State s)
    {
        var now = DateTime.Now;
        var becameDue = new List<DueTask>();
        foreach (var t in s.Pending.Values.ToList())
        {
            var cur = t;
            if (cur.Offset > 0 && now >= cur.Due)
            {
                cur = cur with { Offset = 0, FireAt = cur.Due, Late = false }; // fired on time, in this popup
                s.Pending[cur.RowId] = cur;
                becameDue.Add(cur);
            }
            if (s.Rows.TryGetValue(cur.RowId, out var r))
            {
                r.Label.Text = LabelText(cur, now);
                r.Label.Foreground = LabelBrush(cur);
            }
        }
        if (becameDue.Count > 0)
        {
            NotificationSound.Play();
            // v2.3.1: toast only the tasks that just became due (not every on-time task already shown).
            _ = WindowsToast.TryShow(becameDue); // silent toast
        }
    }

    private static void Watch(State s)
    {
        if (s.StillPending is not { } still) return;
        foreach (var t in s.Pending.Values.ToList())
            if (!still(t)) Remove(s, t.RowId);
        if (s.Pending.Count == 0) { s.Result = PopupCloseKind.Done; s.Window.Close(); }
        else UpdateChrome(s); // cheap now: resizes only when the row count changed
    }
}
